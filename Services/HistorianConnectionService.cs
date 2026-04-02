using Proficy.Historian.ClientAccess.API;
using System;

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

        public void ConnectPrimary(string hostname)
        {
            DisconnectPrimary();
            _primary = new ServerConnection(new ConnectionProperties { ServerHostName = hostname });
            _primary.Connect();
        }

        public void ConnectSecondary(string hostname)
        {
            DisconnectSecondary();
            _secondary = new ServerConnection(new ConnectionProperties { ServerHostName = hostname });
            _secondary.Connect();
        }

        public void DisconnectPrimary()
        {
            if (_primary != null)
            {
                try { if (_primary.IsConnected()) _primary.Disconnect(); } catch { }
                _primary = null;
            }
        }

        public void DisconnectSecondary()
        {
            if (_secondary != null)
            {
                try { if (_secondary.IsConnected()) _secondary.Disconnect(); } catch { }
                _secondary = null;
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
