using System;
using System.Runtime.InteropServices;

namespace ScreenBreak
{
    /// <summary>
    /// Idle detection that also counts game controllers, because XInput pads
    /// don't update GetLastInputInfo and gamers would otherwise look "away".
    /// </summary>
    internal static class InputMonitor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetState(int index, out XINPUT_STATE state);

        private static readonly bool[] connected = new bool[4];
        private static readonly long[] lastSignature = new long[4];
        private static DateTime nextScan = DateTime.MinValue;
        private static DateTime lastPadInput = DateTime.MinValue;
        private static bool xinputAvailable = true;

        public static double IdleSeconds()
        {
            PollControllers();
            double pad = (DateTime.UtcNow - lastPadInput).TotalSeconds;
            return Math.Min(Native.SystemIdleSeconds(), pad);
        }

        private static void PollControllers()
        {
            if (!xinputAvailable) return;
            // Querying empty slots is slow, so only rescan for new pads every 10 s.
            bool rescan = DateTime.UtcNow >= nextScan;
            if (rescan) nextScan = DateTime.UtcNow.AddSeconds(10);
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    if (!connected[i] && !rescan) continue;
                    bool wasConnected = connected[i];
                    XINPUT_STATE st;
                    connected[i] = XInputGetState(i, out st) == 0;
                    if (!connected[i]) continue;

                    // Coarse signature so stick drift / trigger noise doesn't count as activity.
                    long sig = st.wButtons
                        | (long)(st.bLeftTrigger / 64) << 16
                        | (long)(st.bRightTrigger / 64) << 18
                        | (long)(st.sThumbLX / 8192 + 4) << 20
                        | (long)(st.sThumbLY / 8192 + 4) << 24
                        | (long)(st.sThumbRX / 8192 + 4) << 28
                        | (long)(st.sThumbRY / 8192 + 4) << 32;
                    if (wasConnected && sig != lastSignature[i]) lastPadInput = DateTime.UtcNow;
                    lastSignature[i] = sig;
                }
            }
            catch (DllNotFoundException) { xinputAvailable = false; }
            catch (EntryPointNotFoundException) { xinputAvailable = false; }
        }
    }
}
