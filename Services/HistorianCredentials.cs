using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;
using HistorianSyncTool.Properties;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// The Historian login used for both servers.
    ///
    /// It used to live ONLY in <c>HistorianSyncTool.exe.config</c>, which meant a tester who
    /// received the hand-out zip could not connect to a server that requires a login at all:
    /// the packaged config deliberately ships without credentials, so the server answered
    /// "the server has rejected the client credentials" and the only fix was to hand-edit XML.
    ///
    /// Order of preference:
    ///   1. What the user typed in this session (or saved earlier on this PC)
    ///   2. <c>HistorianUsername</c> / <c>HistorianPassword</c> from app.config — kept so
    ///      existing installations and unattended machines keep working unchanged
    ///   3. Nothing, i.e. the Windows session, which is correct when the tool runs on the
    ///      Historian box itself
    ///
    /// The password is stored with DPAPI under the CURRENT WINDOWS USER, so the stored blob is
    /// useless on another machine or to another user, and it never touches the repo or the
    /// hand-out zip. It is still a stored password: only saved when the user asks for it.
    /// </summary>
    public static class HistorianCredentials
    {
        /// <summary>Entropy mixed into the DPAPI blob so another application's stored secret
        /// can never be decrypted with ours by accident.</summary>
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HistorianSyncTool.credentials.v1");

        public static string Username { get; private set; }
        public static string Password { get; private set; }

        /// <summary>True when a login will be sent (rather than the Windows session).</summary>
        public static bool HasLogin => !string.IsNullOrEmpty(Username);

        /// <summary>Where the current values came from — shown in the dialog so the user is
        /// never guessing which of the two sources is in effect.</summary>
        public static string Source { get; private set; } = "none";

        /// <summary>Call once at startup, before the first connect.</summary>
        public static void Load()
        {
            string savedUser = Settings.Default.HistorianUser;
            if (!string.IsNullOrEmpty(savedUser))
            {
                Username = savedUser;
                Password = Unprotect(Settings.Default.HistorianPasswordEnc);
                Source = "saved";
                return;
            }

            string cfgUser = ConfigurationManager.AppSettings["HistorianUsername"];
            if (!string.IsNullOrEmpty(cfgUser))
            {
                Username = cfgUser;
                Password = ConfigurationManager.AppSettings["HistorianPassword"] ?? "";
                Source = "config";
                return;
            }

            Username = null;
            Password = null;
            Source = "none";
        }

        /// <summary>
        /// Use these credentials for the next connect. <paramref name="remember"/> stores them
        /// for this Windows user; otherwise they live only until the program closes.
        /// </summary>
        public static void Set(string username, string password, bool remember)
        {
            Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
            Password = Username == null ? null : (password ?? "");
            Source = Username == null ? "none" : (remember ? "saved" : "session");

            if (remember && Username != null)
            {
                Settings.Default.HistorianUser = Username;
                Settings.Default.HistorianPasswordEnc = Protect(Password);
            }
            else
            {
                // Explicitly clear, so "do not remember" also forgets what was stored before.
                Settings.Default.HistorianUser = "";
                Settings.Default.HistorianPasswordEnc = "";
            }
            Settings.Default.Save();
        }

        /// <summary>Applies the current login to a connection, if there is one.</summary>
        public static void ApplyTo(Proficy.Historian.ClientAccess.API.ConnectionProperties props)
        {
            if (props == null || !HasLogin) return;
            props.Username = Username;
            props.Password = Password ?? "";
        }

        /// <summary>True if this exception looks like a rejected login rather than a network
        /// problem, so the caller can offer the login dialog instead of a generic error.</summary>
        public static bool LooksLikeAuthFailure(Exception ex)
        {
            if (ex == null) return false;
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                string m = e.Message ?? "";
                if (m.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("username", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("authentic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("logon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] enc = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch (CryptographicException ex)
            {
                System.Diagnostics.Trace.TraceError("Could not protect the password: " + ex);
                return "";
            }
        }

        private static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                byte[] plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                // Copied to another machine or another Windows user: the blob cannot be read.
                // Not fatal - the user is simply asked for the password again.
                System.Diagnostics.Trace.TraceWarning("Stored password could not be read: " + ex.Message);
                return "";
            }
        }
    }
}
