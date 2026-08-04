using System;
using System.Linq;
using System.Windows.Forms;

namespace HistorianSyncTool
{
    static class Program
    {
        /// <summary>
        /// True when started with <c>--demo</c>: the app runs against a generated in-memory
        /// pair of servers instead of a real Historian. Used for screenshot verification and
        /// for showing the tool without any server. A banner makes it obvious on screen.
        /// </summary>
        public static bool DemoMode { get; private set; }

        [STAThread]
        static void Main(string[] args)
        {
            DemoMode = args != null && args.Any(a =>
                string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/demo", StringComparison.OrdinalIgnoreCase));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Forms.MainForm());
        }
    }
}
