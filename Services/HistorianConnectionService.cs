using Proficy.Historian.ClientAccess.API;
using System;
using System.Configuration;
using System.Diagnostics;

namespace HistorianSyncTool.Services
{
    public class HistorianConnectionService : IDisposable
    {
        /// <summary>
        /// Connection properties shared by both servers. An optional login can be set in
        /// app.config (HistorianUsername/HistorianPassword) for remote servers that reject
        /// empty usernames; left empty, the Windows session is used (the normal case when
        /// the tool runs on the Historian box itself).
        /// </summary>
        private static ConnectionProperties BuildProperties(string hostname)
        {
            var props = new ConnectionProperties { ServerHostName = hostname };
            // One place decides the login: what the user entered, else app.config, else the
            // Windows session. Reading app.config directly here meant a tester who unzipped the
            // hand-out package could not connect to a server requiring a login at all, because
            // the packaged config ships (deliberately) without credentials.
            HistorianCredentials.ApplyTo(props);
            props.ServerCertificateValidationMode = CertificateValidationMode.None;
            return props;
        }
        private ServerConnection _primary;
        private ServerConnection _secondary;
        private bool _disposed;

        /// <summary>
        /// Offline demo (<c>--demo</c>): both sides report connected and hand out two distinct
        /// <see cref="ServerConnection"/> objects that were NEVER connected — constructing one
        /// opens no socket (verified). They exist only so <see cref="DemoDataService"/> can tell
        /// the two sides apart by reference; every call against them is served from memory.
        /// </summary>
        public bool DemoMode { get; private set; }

        public bool IsPrimaryConnected => DemoMode || (_primary != null && _primary.IsConnected());
        public bool IsSecondaryConnected => DemoMode || (_secondary != null && _secondary.IsConnected());

        public ServerConnection Primary => _primary;
        public ServerConnection Secondary => _secondary;

        public string PrimaryHostname { get; private set; }
        public string SecondaryHostname { get; private set; }

        /// <summary>
        /// Switches this service into offline demo mode. Creates the two sentinel connections
        /// WITHOUT calling Connect(), so no network traffic of any kind is produced.
        /// </summary>
        public void EnableDemoMode(string primaryHost, string secondaryHost)
        {
            DisconnectPrimary();
            DisconnectSecondary();
            var props = new ConnectionProperties { ServerHostName = "DEMO" };
            _primary   = new ServerConnection(props);   // never connected — sentinel only
            _secondary = new ServerConnection(props);
            PrimaryHostname   = primaryHost;
            SecondaryHostname = secondaryHost;
            DemoMode = true;
        }

        public void ConnectPrimary(string hostInput)
        {
            if (DemoMode) return;   // demo sessions never open a real connection
            if (string.IsNullOrWhiteSpace(hostInput))
                throw new ArgumentException("Hostname cannot be empty.", nameof(hostInput));
            DisconnectPrimary();
            _primary = Open(hostInput);
            PrimaryHostname = hostInput.Trim();
        }

        public void ConnectSecondary(string hostInput)
        {
            if (DemoMode) return;   // demo sessions never open a real connection
            if (string.IsNullOrWhiteSpace(hostInput))
                throw new ArgumentException("Hostname cannot be empty.", nameof(hostInput));
            DisconnectSecondary();
            _secondary = Open(hostInput);
            SecondaryHostname = hostInput.Trim();
        }

        /// <summary>
        /// Opens one connection from free-text input: "host", "host:port", "ip" or
        /// "ip:port". The port applies to this connect only (connections are opened
        /// sequentially); IP inputs go through ProficyEndpoint.PrepareForIp, which
        /// relaxes ONLY the WCF DNS-identity comparison — hostname connects keep the
        /// full vendor-stock security path.
        /// </summary>
        private static ServerConnection Open(string hostInput)
        {
            var (host, port) = HostInputParser.Parse(hostInput);
            if (host.Length == 0)
                throw new ArgumentException("Hostname cannot be empty.", nameof(hostInput));

            ProficyEndpoint.SetPortForNextConnect(port);
            var props = BuildProperties(host);
            var conn = new ServerConnection(props);
            if (HostInputParser.IsIpAddress(host))
                ProficyEndpoint.PrepareForIp(conn, props);
            conn.Connect();
            return conn;
        }

        public void DisconnectPrimary()
        {
            if (_primary != null)
            {
                try { if (_primary.IsConnected()) _primary.Disconnect(); }
                catch (Exception ex) { Trace.TraceWarning("Error disconnecting primary: " + ex.Message); }
                _primary = null;
                PrimaryHostname = null;
            }
        }

        public void DisconnectSecondary()
        {
            if (_secondary != null)
            {
                try { if (_secondary.IsConnected()) _secondary.Disconnect(); }
                catch (Exception ex) { Trace.TraceWarning("Error disconnecting secondary: " + ex.Message); }
                _secondary = null;
                SecondaryHostname = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            DisconnectPrimary();
            DisconnectSecondary();
            _disposed = true;
        }
    }
}
