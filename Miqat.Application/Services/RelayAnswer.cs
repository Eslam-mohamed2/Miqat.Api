using System.Text.Json;
using Miqat.Application.Modules;

namespace Miqat.Application.Services
{
    /// <summary>
    /// Reads the answer of a hosted relay (TURN) service. The services agree on
    /// the content (the entries of RTCConfiguration.iceServers) but not on the
    /// wrapping, so every form in use is accepted:
    /// a list of servers; { "iceServers": [ ... ] }; { "iceServers": { ... } };
    /// and "urls" as one address or a list.
    /// </summary>
    public static class RelayAnswer
    {
        public static List<IceServerDto> Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("iceServers", out var inner))
                root = inner;

            var entries = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                JsonValueKind.Object => new List<JsonElement> { root },
                _ => new List<JsonElement>()
            };

            var servers = new List<IceServerDto>();
            foreach (var entry in entries)
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("urls", out var urls)) continue;

                var addresses = (urls.ValueKind == JsonValueKind.Array
                        ? urls.EnumerateArray().Where(u => u.ValueKind == JsonValueKind.String).Select(u => u.GetString())
                        : new[] { urls.ValueKind == JsonValueKind.String ? urls.GetString() : null })
                    .Where(IsUsable)
                    .Select(u => u!)
                    .ToList();
                if (addresses.Count == 0) continue;

                servers.Add(new IceServerDto
                {
                    Urls = addresses,
                    Username = Text(entry, "username"),
                    Credential = Text(entry, "credential")
                });
            }

            return servers;
        }

        /// <summary>
        /// A stun:, turn: or turns: address a browser will really try. Port 53
        /// is left out: browsers refuse it, and an entry they refuse only
        /// slows the start of a call.
        /// </summary>
        private static bool IsUsable(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!url.StartsWith("stun:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase)) return false;

            var address = url.Split('?')[0];
            return !address.EndsWith(":53", StringComparison.Ordinal);
        }

        private static string? Text(JsonElement entry, string name) =>
            entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
