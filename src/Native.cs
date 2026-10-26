using System;
using System.Runtime.InteropServices;

namespace ScreenBreak
{
    internal static class Native
    {
        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x8000000;

        public const int WM_NCHITTEST = 0x84;
        public const int HTTRANSPARENT = -1;

        public const uint SWP_NOSIZE = 0x1;
        public const uint SWP_NOMOVE = 0x2;
        public const uint SWP_NOACTIVATE = 0x10;
        public const uint SWP_SHOWWINDOW = 0x40;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        /// <summary>Seconds since the last keyboard/mouse input anywhere in the session.</summary>
        public static double SystemIdleSeconds()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            if (!GetLastInputInfo(ref info)) return 0;
            uint now = unchecked((uint)Environment.TickCount);
            return unchecked(now - info.dwTime) / 1000.0;
        }

        [DllImport("powrprof.dll")]
        private static extern uint CallNtPowerInformation(int level, IntPtr input, int inputSize, out uint output, int outputSize);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        /// <summary>
        /// True while something is being watched without input: an app asks Windows to keep the
        /// display on (video players and browsers do while playing), or a full-screen app,
        /// game or presentation is in front.
        /// </summary>
        public static bool IsWatching()
        {
            const int SystemExecutionState = 16;
            const uint ES_DISPLAY_REQUIRED = 0x2;
            const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;
            try
            {
                uint state;
                if (CallNtPowerInformation(SystemExecutionState, IntPtr.Zero, 0, out state, sizeof(uint)) == 0 &&
                    (state & ES_DISPLAY_REQUIRED) != 0) return true;
                int quns;
                if (SHQueryUserNotificationState(out quns) == 0 &&
                    (quns == QUNS_BUSY || quns == QUNS_RUNNING_D3D_FULL_SCREEN || quns == QUNS_PRESENTATION_MODE)) return true;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint flags);

        /// <summary>
        /// Load DLLs only from System32, never from the exe's folder or the current directory,
        /// so a DLL planted next to the exe (e.g. in Downloads) can't be loaded.
        /// </summary>
        public static void HardenDllSearch()
        {
            const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;
            try { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32); }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Windows 11 rounded corners for a borderless window (no-op on Windows 10).</summary>
        public static void RoundCorners(IntPtr hwnd)
        {
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
            int pref = DWMWCP_ROUND;
            try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int processId);
        public const int ASFW_ANY = -1;

        public static void BringToTop(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
