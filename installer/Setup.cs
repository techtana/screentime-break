// ScreenBreak installer / uninstaller. ScreenBreak.exe is embedded as a resource.
//
//   ScreenBreak-Setup.exe               install (or upgrade) with a small dialog
//   ScreenBreak-Setup.exe /quiet        install silently (start with Windows on)
//   uninstall.exe /uninstall [/quiet]   remove; /quiet keeps settings unless /purge is given
//
// Installs per user to %LOCALAPPDATA%\Programs\ScreenBreak, so no admin rights are needed.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ScreenBreak Setup")]
[assembly: AssemblyProduct("ScreenBreak")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace ScreenBreakSetup
{
    internal static class Setup
    {
        private const string AppName = "ScreenBreak";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenBreak";

        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ScreenBreak");
        private static readonly string AppExe = Path.Combine(InstallDir, "ScreenBreak.exe");
        private static readonly string UninstallExe = Path.Combine(InstallDir, "uninstall.exe");
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenBreak");
        private static readonly string StartMenuLink = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ScreenBreak.lnk");
        private static readonly string DesktopLink = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ScreenBreak.lnk");

        private static string Version
        {
            get { return ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute))).InformationalVersion; }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            bool quiet = Has(args, "/quiet") || Has(args, "/S");
            Application.EnableVisualStyles();
            try
            {
                if (Has(args, "/uninstall")) return Uninstall(quiet, Has(args, "/purge"));
                if (quiet)
                {
                    Install(true, false, false);
                    return 0;
                }
                Application.Run(new InstallForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show("Setup failed:\n" + ex.Message, "ScreenBreak Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool Has(string[] args, string flag)
        {
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsInstalled { get { return File.Exists(AppExe); } }

        public static bool AutostartWanted
        {
            get
            {
                if (!IsInstalled) return true;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppName) != null;
            }
        }

        public static void Install(bool autostart, bool desktopShortcut, bool launch)
        {
            StopRunningApp();
            Directory.CreateDirectory(InstallDir);

            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenBreak.exe"))
            using (FileStream fs = File.Create(AppExe))
                payload.CopyTo(fs);

            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, UninstallExe, true);

            // The app syncs the Run key from its own settings at startup, so record the choice there too.
            SetAutostartSetting(autostart);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (autostart) k.SetValue(AppName, "\"" + AppExe + "\"");
                else k.DeleteValue(AppName, false);
            }

            CreateShortcut(StartMenuLink);
            if (desktopShortcut) CreateShortcut(DesktopLink);

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppName);
                k.SetValue("DisplayIcon", AppExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + UninstallExe + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + UninstallExe + "\" /uninstall /quiet");
                k.SetValue("EstimatedSize", (int)((new FileInfo(AppExe).Length + new FileInfo(UninstallExe).Length) / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            if (launch) Process.Start(new ProcessStartInfo(AppExe) { WorkingDirectory = InstallDir });
        }

        private static int Uninstall(bool quiet, bool purge)
        {
            if (!quiet && MessageBox.Show("Remove ScreenBreak from this PC?", "Uninstall ScreenBreak",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;

            StopRunningApp();
            DeleteFile(StartMenuLink);
            DeleteFile(DesktopLink);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null) k.DeleteValue(AppName, false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            bool removeData = purge || (!quiet && Directory.Exists(DataDir) && MessageBox.Show(
                "Also delete your ScreenBreak settings and screen-time history?", "Uninstall ScreenBreak",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
            if (removeData && Directory.Exists(DataDir)) Directory.Delete(DataDir, true);

            DeleteFile(AppExe);
            string self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            if (self.StartsWith(InstallDir + "\\", StringComparison.OrdinalIgnoreCase))
            {
                // A running exe can't delete itself: let a hidden cmd remove the folder once we've exited.
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rd /s /q \"" + InstallDir + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetTempPath()
                });
            }
            else if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);

            if (!quiet) MessageBox.Show("ScreenBreak was removed.", "Uninstall ScreenBreak", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        private static void StopRunningApp()
        {
            foreach (Process p in Process.GetProcessesByName(AppName))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception) { }
            }
        }

        private static void SetAutostartSetting(bool on)
        {
            Directory.CreateDirectory(DataDir);
            string path = Path.Combine(DataDir, "settings.ini");
            string line = "StartWithWindows=" + on;
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "# ScreenBreak settings\r\n" + line + "\r\n");
                return;
            }
            string[] lines = File.ReadAllLines(path);
            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("StartWithWindows=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = line;
                    found = true;
                }
            }
            File.WriteAllLines(path, lines);
            if (!found) File.AppendAllText(path, line + "\r\n");
        }

        private static void CreateShortcut(string linkPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                Type t = link.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { AppExe });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { InstallDir });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Screen-time break reminder" });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { AppExe + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.FinalReleaseComObject(link);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public static string InstallLocation { get { return InstallDir; } }
        public static string AppVersion { get { return Version; } }
    }

    internal class InstallForm : Form
    {
        private readonly CheckBox autostart, desktop, launch;

        public InstallForm()
        {
            bool upgrade = Setup.IsInstalled;
            Text = "ScreenBreak Setup";
            Font = new Font("Segoe UI", 9f);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(18);

            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(18, 14) };
            panel.Controls.Add(new Label
            {
                Text = (upgrade ? "Update ScreenBreak to " : "Install ScreenBreak ") + Setup.AppVersion,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                AutoSize = true
            });
            panel.Controls.Add(new Label
            {
                Text = "A gentle screen-time break reminder that lives in the tray.\n\n" +
                       "Installs for your Windows account only (no admin rights needed) to:\n" + Setup.InstallLocation,
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Margin = new Padding(3, 8, 3, 12)
            });
            autostart = new CheckBox { Text = "Start ScreenBreak when I sign in to Windows", Checked = Setup.AutostartWanted, AutoSize = true };
            desktop = new CheckBox { Text = "Add a desktop shortcut", AutoSize = true };
            launch = new CheckBox { Text = "Start ScreenBreak when setup finishes", Checked = true, AutoSize = true };
            panel.Controls.Add(autostart);
            panel.Controls.Add(desktop);
            panel.Controls.Add(launch);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 16, 0, 0) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var install = new Button { Text = upgrade ? "Update" : "Install", AutoSize = true };
            install.Click += OnInstall;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(install);
            panel.Controls.Add(buttons);
            Controls.Add(panel);
            AcceptButton = install;
            CancelButton = cancel;
        }

        private void OnInstall(object sender, EventArgs e)
        {
            UseWaitCursor = true;
            Enabled = false;
            try
            {
                Setup.Install(autostart.Checked, desktop.Checked, launch.Checked);
                MessageBox.Show(this,
                    "ScreenBreak is installed.\n\nLook for the green clock icon in the tray (it may be under the ^ arrow). " +
                    "Right-click it for settings. To remove it, use Settings > Apps > Installed apps.",
                    "ScreenBreak Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                Enabled = true;
                UseWaitCursor = false;
                MessageBox.Show(this, "Setup failed:\n" + ex.Message, "ScreenBreak Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
