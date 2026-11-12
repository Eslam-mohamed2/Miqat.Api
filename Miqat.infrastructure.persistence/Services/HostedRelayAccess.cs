using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Services;

namespace Miqat.infrastructure.persistence.Services
{
    /// <summary>
    /// Asks a hosted relay (TURN) service for the servers and passwords to give
    /// the browsers, and keeps the answer for a while so a call does not wait
    /// on that service.
    /// <para>
    /// Settings: Calls:Relay:Url, and Calls:Relay:Token when the service wants
    /// a key in the Authorization header. With a token the address is called
    /// with POST and the wanted lifetime (Cloudflare); without one with GET
    /// (services that take the key inside the address). Neither is ever logged.
    /// </para>
    /// </summary>
    public class HostedRelayAccess : IRelayAccess
    {
        /// <summary>How long one answer is handed out. Well under the lifetime of the passwords in it.</summary>
        private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(30);

        /// <summary>After a failure the service is left alone for a moment, so a call does not wait on it again.</summary>
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static IReadOnlyList<IceServerDto> _kept = Array.Empty<IceServerDto>();
        private static DateTime _keptUntil = DateTime.MinValue;
        private static DateTime _passwordsEnd = DateTime.MinValue;

        private readonly HttpClient _http;
        private readonly RelaySettings _settings;
        private readonly ILogger<HostedRelayAccess> _logger;

        public HostedRelayAccess(HttpClient http, CallSettings settings, ILogger<HostedRelayAccess> logger)
        {
            _http = http;
            _settings = settings.Relay;
            _logger = logger;
        }

        public async Task<IReadOnlyList<IceServerDto>> GetServersAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.Url)) return Array.Empty<IceServerDto>();
            if (DateTime.UtcNow < _keptUntil) return _kept;

            await Gate.WaitAsync(cancellationToken);
            try
            {
                if (DateTime.UtcNow < _keptUntil) return _kept;

                try
                {
                    _kept = await AskAsync(cancellationToken);
                    _keptUntil = DateTime.UtcNow.Add(KeepFor);
                    _passwordsEnd = DateTime.UtcNow.AddSeconds(_settings.TtlSeconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // The message only: the address may carry the key of the service.
                    var reason = ex is HttpRequestException { StatusCode: not null } refused
                        ? $"HTTP {(int)refused.StatusCode.Value}"
                        : ex.GetType().Name;
                    _logger.LogWarning("The call relay service did not answer ({Reason}). Calls go on without a relay.", reason);

                    // The last answer stays good while its passwords are; after that, nothing.
                    if (DateTime.UtcNow >= _passwordsEnd) _kept = Array.Empty<IceServerDto>();
                    _keptUntil = DateTime.UtcNow.Add(RetryAfter);
                }

                return _kept;
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<IReadOnlyList<IceServerDto>> AskAsync(CancellationToken cancellationToken)
        {
            var withToken = !string.IsNullOrWhiteSpace(_settings.Token);
            using var request = new HttpRequestMessage(withToken ? HttpMethod.Post : HttpMethod.Get, _settings.Url);

            if (withToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Token);
                request.Content = new StringContent($"{{\"ttl\":{_settings.TtlSeconds}}}", Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var servers = RelayAnswer.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (servers.Count == 0)
                throw new InvalidOperationException("The relay service answered without any server.");

            return servers;
        }
    }
}
