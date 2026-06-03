using Proficy.Historian.ClientAccess.API;
using System;
using System.Diagnostics;

namespace HistorianSyncTool.Services
{
    public class HistorianConnectionService : IDisposable
    {
        private ServerConnection _primary;
        private ServerConnection _secondary;
        private bool _disposed;

        public bool IsPrimaryConnected => _primary != null && _primary.IsConnected();
        public bool IsSecondaryConnected => _secondary != null && _secondary.IsConnected();

        public ServerConnection Primary => _primary;
        public ServerConnection Secondary => _secondary;

        public string PrimaryHostname { get; private set; }
        public string SecondaryHostname { get; private set; }

        public void ConnectPrimary(string hostname)
        {
            if (string.IsNullOrWhiteSpace(hostname))
                throw new ArgumentException("Hostname cannot be empty.", nameof(hostname));
            DisconnectPrimary();
            _primary = new ServerConnection(new ConnectionProperties { ServerHostName = hostname });
            _primary.Connect();
            PrimaryHostname = hostname;
        }

        public void ConnectSecondary(string hostname)
        {
            if (string.IsNullOrWhiteSpace(hostname))
                throw new ArgumentException("Hostname cannot be empty.", nameof(hostname));
            DisconnectSecondary();
            _secondary = new ServerConnection(new ConnectionProperties { ServerHostName = hostname });
            _secondary.Connect();
            SecondaryHostname = hostname;
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
