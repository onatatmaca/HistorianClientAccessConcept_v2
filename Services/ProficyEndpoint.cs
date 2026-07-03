using Proficy.Historian.ClientAccess.API;
using Proficy.Historian.ClientAccess.API.Internal;
using System;
using System.IdentityModel.Policy;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Security;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Makes the Proficy ClientAccess API usable with IP addresses and custom ports.
    ///
    /// Port: the API builds its net.tcp URI from the public static
    /// <see cref="HistorianAddress.TcpPort"/> (default 13000, or the `TcpPortNumber`
    /// appSetting). It is process-global, so callers set it immediately before each
    /// Connect (connections are opened sequentially).
    ///
    /// IP: connecting by IP normally fails WCF's DNS-identity check — the server's
    /// certificate claims its hostname ("provided DNS claim 'TESTSV1'"), the client
    /// expects the URI host (the IP), and `CertificateValidationMode` does not bypass
    /// that comparison. <see cref="PrepareForIp"/> therefore prebuilds the channel
    /// factory exactly the way <c>ServerConnection.Connect()</c> would (verified
    /// against the decompiled 1.6.1.0 assembly), but swaps in an identity verifier
    /// that skips ONLY the name comparison. TLS encryption and the configured
    /// certificate validation mode still apply, and hostname connections do not go
    /// through this path at all — they keep the full vendor-stock identity check.
    /// </summary>
    public static class ProficyEndpoint
    {
        /// <summary>The baseline port (honours a `TcpPortNumber` appSetting), captured
        /// before any per-connection override touches the static.</summary>
        public static readonly int DefaultTcpPort = HistorianAddress.TcpPort;

        /// <summary>Sets the port used by the NEXT ServerConnection.Connect() call.</summary>
        public static void SetPortForNextConnect(int? port)
        {
            HistorianAddress.TcpPort = port ?? DefaultTcpPort;
        }

        /// <summary>
        /// Pre-creates the WCF channel factory for <paramref name="sc"/> with a lenient
        /// DNS-identity verifier so a later Connect() accepts an IP address. Must be
        /// called after SetPortForNextConnect and before sc.Connect().
        /// Throws NotSupportedException if the installed ClientAccess assembly's
        /// internals no longer match (caller falls back to hostname-only connects).
        /// </summary>
        public static void PrepareForIp(ServerConnection sc, ConnectionProperties props)
        {
            // Same binding choice as ServerConnection.Connect(): username set →
            // TransportWithMessageCredential, else Windows transport auth.
            NetTcpBinding raw = props.Username != null
                ? (NetTcpBinding)new UsernameAuthenticationBinding()
                : new WindowsAuthenticationBinding();
            ApplyProperties(raw, props);

            // The identity verifiers live on the security binding elements — replace
            // them with one that skips the DNS-name comparison (everything else,
            // including the TLS handshake and cert validation mode, is untouched).
            var custom = new CustomBinding(raw);
            foreach (BindingElement el in custom.Elements)
            {
                var ssl = el as SslStreamSecurityBindingElement;
                if (ssl != null) ssl.IdentityVerifier = LenientIdentityVerifier.Instance;
                var sec = el as SecurityBindingElement;
                if (sec != null) sec.LocalClientSettings.IdentityVerifier = LenientIdentityVerifier.Instance;
            }

            string url = props.Username != null
                ? HistorianAddress.UsernameAuthNetTcp(props.ServerHostName)
                : HistorianAddress.WindowsAuthNetTcp(props.ServerHostName);

            var factory = new DuplexChannelFactory<IHistorian>(sc, custom, new EndpointAddress(url));
            factory.Credentials.ServiceCertificate.Authentication.CertificateValidationMode =
                (X509CertificateValidationMode)(int)props.ServerCertificateValidationMode;
            if (props.Username != null)
            {
                factory.Credentials.UserName.UserName = props.Username;
                factory.Credentials.UserName.Password = props.Password;
            }

            // Replicate the endpoint behaviors Connect() would add. PagedOperation is
            // internal → the one reflection property-set; everything else is public.
            factory.Endpoint.Behaviors.Add(new VersionHeaderBehavior(sc));
            foreach (OperationDescription op in factory.Endpoint.Contract.Operations)
            {
                foreach (IOperationBehavior ob in op.Behaviors)
                {
                    if (ob.GetType().Name == "PagedOperation")
                    {
                        PropertyInfo pi = ob.GetType().GetProperty("MaxReceivedMessageSize");
                        if (pi != null) pi.SetValue(ob, raw.MaxReceivedMessageSize, null);
                    }
                }
                var dcs = op.Behaviors.Find<DataContractSerializerOperationBehavior>();
                if (dcs != null) dcs.MaxItemsInObjectGraph = props.MaxItemsInObjectGraph;
            }

            FieldInfo field = typeof(ServerConnection).GetField(
                "factory", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new NotSupportedException(
                    "This ClientAccess API version does not support IP connections " +
                    "through this tool — connect with the server's hostname instead.");
            }
            field.SetValue(sc, factory);
        }

        /// <summary>Mirror of the private ServerConnection.ConfigureBinding.</summary>
        private static void ApplyProperties(NetTcpBinding binding, ConnectionProperties p)
        {
            binding.MaxReceivedMessageSize = p.MaxReceivedMessageSize;
            binding.OpenTimeout = p.OpenTimeout;
            binding.CloseTimeout = p.CloseTimeout;
            binding.SendTimeout = p.SendTimeout;
            binding.ReceiveTimeout = p.ReceiveTimeout;
            binding.ReliableSession.InactivityTimeout = p.InactivityTimeout;
            binding.ReliableSession.Enabled = p.ReliableSessionEnabled;
            binding.ReliableSession.Ordered = p.ReliableSessionEnabled;
            binding.ReaderQuotas.MaxDepth = p.XmlReaderQuotaMaxDepth;
            binding.ReaderQuotas.MaxStringContentLength = p.XmlReaderQuotaMaxStringContentLength;
            binding.ReaderQuotas.MaxArrayLength = p.XmlReaderQuotaMaxArrayLength;
            binding.ReaderQuotas.MaxBytesPerRead = p.XmlReaderQuotaMaxBytesPerRead;
            binding.ReaderQuotas.MaxNameTableCharCount = p.XmlReaderQuotaMaxNameTableCharCount;
        }

        /// <summary>Accepts whatever name the server presents; the certificate itself
        /// is still validated per ServerCertificateValidationMode.</summary>
        private sealed class LenientIdentityVerifier : IdentityVerifier
        {
            public static readonly LenientIdentityVerifier Instance = new LenientIdentityVerifier();

            public override bool CheckAccess(EndpointIdentity identity, AuthorizationContext authContext)
                => true;

            public override bool TryGetIdentity(EndpointAddress reference, out EndpointIdentity identity)
                => CreateDefault().TryGetIdentity(reference, out identity);
        }
    }
}
