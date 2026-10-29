using System;
using System.Threading;
using System.Windows.Forms;

namespace Afterimage
{
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args);
            if (args.Length > 1 && args[0] == "--validate") return Validation.Run(args[1]);
            bool first;
            // Keep the original mutex so the earlier unnamed build cannot record concurrently.
            using (Mutex mutex = new Mutex(true, @"Local\WorkflowRecorder-Portable-v1", out first)) {
                if (!first) { MessageBox.Show("Already running in the system tray.", "Afterimage"); return 0; }
                try { Application.Run(new MainForm()); } catch (Exception e) { MessageBox.Show(e.Message, "Afterimage", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            return 0;
        }
    }
}
