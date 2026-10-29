using System;
using System.Threading;
using System.Windows.Forms;

namespace Afterimage
{
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
#if DESKTOP_ASSISTANT_TEST
            return AssistantTest.Run(System.IO.Path.Combine(AppContext.BaseDirectory, "assistant-test"));
#else
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args);
            if (args.Length > 1 && args[0] == "--validate") return Validation.Run(args[1]);
            if (args.Length > 1 && args[0] == "--island-test") return IslandTest.Run(args[1]);
            if (args.Length > 1 && args[0] == "--assistant-test") return AssistantTest.Run(args[1]);
            if (args.Length > 0 && args[0] == "--quit") return InstanceChannel.SendAsync("quit").GetAwaiter().GetResult() ? 0 : 1;
            bool first;
            // Keep the original mutex so the earlier unnamed build cannot record concurrently.
            using (Mutex mutex = new Mutex(true, @"Local\WorkflowRecorder-Portable-v1", out first)) {
                if (!first) { InstanceChannel.SendAsync("show").GetAwaiter().GetResult(); return 0; }
                try {
                    using var main = new MainForm();
                    using var channel = new InstanceChannel(command => { if (!main.IsDisposed && main.IsHandleCreated) main.BeginInvoke((Action)(() => { if (command == "quit") main.ExitApp(); else main.OpenCompanion(); })); });
                    main.FormClosed += (_, _) => Application.ExitThread();
                    main.StartInterface(); Application.Run();
                } catch (Exception e) { MessageBox.Show(e.Message, "Afterimage", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            return 0;
#endif
        }
    }
}
