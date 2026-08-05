using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("HistorianSyncTool.Tests")]

[assembly: AssemblyTitle("Historian Sync Tool")]
[assembly: AssemblyDescription("GE Proficy Historian synchronization and gap analysis tool")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("HistorianSyncTool")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: ComVisible(false)]
// Keep counting UP, always. User-scoped settings live in a per-version folder and
// Settings.Upgrade() imports only from a STRICTLY LOWER version, so a version that ever goes
// backwards silently loses every saved setting with no way to recover it.
// See Settings.UpgradeFromPreviousVersion().
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
[assembly: AssemblyInformationalVersion("2.1")]
