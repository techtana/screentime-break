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
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\ScreenBreak.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("ScreenBreak is already running. Look for the green clock icon in the tray.", "ScreenBreak");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool preview = Array.IndexOf(args, "--preview") >= 0;
                Application.Run(new TrayApp(preview));
            }
        }
    }
}
