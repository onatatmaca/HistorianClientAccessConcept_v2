using System;
using System.Net;
using System.Net.Sockets;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Parses the server fields' free-text input: "hostname", "hostname:port",
    /// "10.0.0.5" or "10.0.0.5:14000". Pure logic — no Proficy or UI dependencies.
    /// </summary>
    public static class HostInputParser
    {
        /// <summary>
        /// Splits an optional ":port" suffix off the input. Returns the bare host and
        /// the port (null when none given). Throws on a malformed port so the caller
        /// can surface a clear message instead of a cryptic connect error.
        /// </summary>
        public static (string Host, int? Port) Parse(string input)
        {
            string s = (input ?? "").Trim();
            if (s.Length == 0) return ("", null);

            int colon = s.LastIndexOf(':');
            // More than one colon would be an IPv6 literal — not supported by the
            // Historian WCF address format we target; treat the input as a plain host.
            if (colon < 0 || colon != s.IndexOf(':')) return (s, null);

            string hostPart = s.Substring(0, colon).Trim();
            string portPart = s.Substring(colon + 1).Trim();
            int port;
            if (hostPart.Length == 0 || !int.TryParse(portPart, out port)
                || port < 1 || port > 65535)
            {
                throw new ArgumentException(
                    $"'{input}' is not a valid server address — use \"hostname\", " +
                    "\"hostname:port\", \"ip\" or \"ip:port\" (port 1–65535).");
            }
            return (hostPart, port);
        }

        /// <summary>True when the (already port-less) host is an IPv4 address literal.</summary>
        public static bool IsIpAddress(string host)
        {
            IPAddress ip;
            return IPAddress.TryParse(host ?? "", out ip)
                && ip.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
