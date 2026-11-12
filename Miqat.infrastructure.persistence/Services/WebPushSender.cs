using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Services
{
    /// <summary>
    /// Web Push: the standard way to reach a browser that is not showing the
    /// app. The message is encrypted for that one browser (RFC 8291,
    /// "aes128gcm" - the form every current browser accepts, Safari included),
    /// signed with the key of this server (VAPID, RFC 8292) and handed to the
    /// push service of the browser maker.
    /// <para>
    /// Settings: Push:PublicKey and Push:PrivateKey (a VAPID key pair; without
    /// them nothing is sent), Push:Subject (a mailto: or https: address the
    /// push services can contact), Push:AllowedHosts (replaces the list of
    /// known push services) and Push:AllowInsecureEndpoints (plain http, for
    /// local testing only).
    /// </para>
    /// </summary>
    public class WebPushSender : IPushSender
    {
        /// <summary>The push services of the browsers that support Web Push.</summary>
        private static readonly string[] KnownPushHosts =
        {
            "fcm.googleapis.com",
            "push.services.mozilla.com",
            "notify.windows.com",
            "push.apple.com"
        };

        private const int RecordSize = 4096;      // one record: every push service accepts it
        private const int MaxPayloadBytes = 3000; // leaves room for the encryption overhead
        private const int PointLength = 65;       // an uncompressed P-256 public key
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);
        private const int SecondsToKeep = 24 * 60 * 60; // a device that is offline gets it for a day

        private readonly HttpClient _http;
        private readonly ILogger<WebPushSender> _logger;
        private readonly ECDsa? _signingKey;
        private readonly string _subject;
        private readonly string[] _allowedHosts;
        private readonly bool _allowInsecure;

        public WebPushSender(HttpClient http, IConfiguration configuration, ILogger<WebPushSender> logger)
        {
            _http = http;
            _logger = logger;
            _subject = Blank(configuration["Push:Subject"]) ?? "mailto:support@miqat.app";

            var configured = configuration.GetSection("Push:AllowedHosts").Get<string[]>();
            _allowedHosts = configured is { Length: > 0 } ? configured : KnownPushHosts;
            _allowInsecure = configuration.GetValue<bool>("Push:AllowInsecureEndpoints");

            PublicKey = Blank(configuration["Push:PublicKey"]);
            var privateKey = Blank(configuration["Push:PrivateKey"]);
            if (PublicKey == null || privateKey == null) return;

            try
            {
                _signingKey = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    // Some tools drop leading zero bytes of the private number.
                    D = Concat(new byte[Math.Max(0, 32 - Base64Url.Decode(privateKey).Length)], Base64Url.Decode(privateKey)),
                    Q = ToPoint(Base64Url.Decode(PublicKey))
                });
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
            {
                // A wrong key must not take the whole API down: device
                // notifications stay off and the reason is in the log.
                _logger.LogError(ex, "Push:PublicKey / Push:PrivateKey are not a valid VAPID key pair; device notifications are off.");
                PublicKey = null;
            }
        }

        public bool IsConfigured => _signingKey != null;

        public string? PublicKey { get; }

        public bool IsAllowedEndpoint(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address)) return false;

            var schemeOk = address.Scheme == Uri.UriSchemeHttps || (_allowInsecure && address.Scheme == Uri.UriSchemeHttp);

            return schemeOk && _allowedHosts.Any(host =>
                address.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                address.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> SendAsync(Device device, string payloadJson)
        {
            // Checked again at send time: the allowed list may have been
            // tightened since the device registered.
            if (_signingKey == null || !IsAllowedEndpoint(device.Endpoint)) return true;

            var endpoint = new Uri(device.Endpoint);
            var payload = Encoding.UTF8.GetBytes(payloadJson);
            if (payload.Length > MaxPayloadBytes)
            {
                _logger.LogWarning("A push message of {Bytes} bytes is too large to send; skipped.", payload.Length);
                return true;
            }

            byte[] body;
            try
            {
                body = Encrypt(payload, Base64Url.Decode(device.P256dh), Base64Url.Decode(device.Auth));
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
            {
                // Keys that are not keys can never be sent to: the registration is useless.
                _logger.LogInformation(ex, "Device {DeviceId} has unusable push keys; forgetting the device.", device.Id);
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("TTL", SecondsToKeep.ToString());
            request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={SignToken(endpoint)}, k={PublicKey}");

            using var response = await _http.SendAsync(request);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // The browser unsubscribed or was uninstalled.
                _logger.LogInformation("Push subscription {DeviceId} is gone; forgetting the device.", device.Id);
                return false;
            }

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("The push service answered {Status} for device {DeviceId}.", (int)response.StatusCode, device.Id);

            return true;
        }

        /// <summary>
        /// "This request comes from the server with this public key": a short
        /// token for the push service the message goes to, signed with ES256.
        /// </summary>
        private string SignToken(Uri endpoint)
        {
            var header = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" }));
            var claims = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                aud = endpoint.GetLeftPart(UriPartial.Authority),
                exp = DateTimeOffset.UtcNow.Add(TokenLifetime).ToUnixTimeSeconds(),
                sub = _subject
            }));

            var unsigned = $"{header}.{claims}";
            var signature = _signingKey!.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256);
            return $"{unsigned}.{Base64Url.Encode(signature)}";
        }

        /// <summary>
        /// RFC 8291: a fresh key pair per message, a shared secret with the key
        /// of the browser, and from it the AES-128-GCM key and nonce. The result
        /// is one record: salt, record size, the fresh public key, then the
        /// encrypted payload.
        /// </summary>
        private static byte[] Encrypt(byte[] payload, byte[] browserPublicKey, byte[] authSecret)
        {
            using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            using var browser = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = ToPoint(browserPublicKey)
            });

            var senderPublicKey = FromPoint(sender.ExportParameters(includePrivateParameters: false).Q);
            var sharedSecret = sender.DeriveRawSecretAgreement(browser.PublicKey);

            var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), browserPublicKey, senderPublicKey);
            var inputKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);

            var salt = RandomNumberGenerator.GetBytes(16);
            var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKey, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
            var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKey, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

            // The payload ends with 0x02: "this is the last record".
            var plaintext = Concat(payload, new byte[] { 2 });
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(key, tag.Length))
                aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var recordSize = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(recordSize, RecordSize);

            return Concat(salt, recordSize, new[] { (byte)senderPublicKey.Length }, senderPublicKey, ciphertext, tag);
        }

        private static ECPoint ToPoint(byte[] uncompressed)
        {
            if (uncompressed.Length != PointLength || uncompressed[0] != 4)
                throw new FormatException("A public key must be an uncompressed P-256 point (65 bytes).");

            return new ECPoint { X = uncompressed[1..33], Y = uncompressed[33..] };
        }

        private static byte[] FromPoint(ECPoint point) => Concat(new byte[] { 4 }, point.X!, point.Y!);

        private static byte[] Concat(params byte[][] parts)
        {
            var all = new byte[parts.Sum(p => p.Length)];
            var offset = 0;
            foreach (var part in parts)
            {
                part.CopyTo(all, offset);
                offset += part.Length;
            }

            return all;
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Base64 as browsers write keys: "-" and "_" instead of "+" and "/", no padding.</summary>
        private static class Base64Url
        {
            public static string Encode(byte[] bytes) =>
                Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            public static byte[] Decode(string text)
            {
                var standard = text.Trim().Replace('-', '+').Replace('_', '/');
                return Convert.FromBase64String(standard.PadRight(standard.Length + (4 - standard.Length % 4) % 4, '='));
            }
        }
    }
}
