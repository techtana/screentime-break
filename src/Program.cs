using System;
using System.Threading;
using System.Windows.Forms;

namespace ScreenBreak
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.HardenDllSearch();

            // Log unexpected errors and keep running instead of showing the .NET crash dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => Log.Error("UI thread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => Log.Error("Fatal", e.ExceptionObject as Exception ?? new Exception("unknown"));

            const string ShowSignalName = @"Local\ScreenBreak.ShowStatus";
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\ScreenBreak.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running: ask that copy to show its status panel, and let it take focus.
                    EventWaitHandle running;
                    if (EventWaitHandle.TryOpenExisting(ShowSignalName, out running))
                    {
                        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                        running.Set();
                        running.Dispose();
                    }
                    else MessageBox.Show("ScreenBreak is already running. Look for the green clock icon in the tray.", "ScreenBreak");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool preview = Array.IndexOf(args, "--preview") >= 0;
                using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName))
                    Application.Run(new TrayApp(preview, showSignal));
            }
        }
    }
}
