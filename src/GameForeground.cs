using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TeyvatGuide
{
    internal sealed class GameForeground
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder path, ref int length);

        private int lastPid;
        private bool lastWasGame;

        public IntPtr GameWindow()
        {
            IntPtr window = GetForegroundWindow();
            int pid;
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out pid) == 0 || IsIconic(window)) return IntPtr.Zero;
            if (pid != lastPid) {
                lastPid = pid;
                lastWasGame = IsGameProcess(pid);
            }
            return lastWasGame ? window : IntPtr.Zero;
        }

        private static bool IsGameProcess(int pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (handle != IntPtr.Zero) try {
                var path = new StringBuilder(1024);
                int length = path.Capacity;
                if (QueryFullProcessImageName(handle, 0, path, ref length))
                    return GameLauncher.IsClientName(Path.GetFileName(path.ToString()));
            } finally { CloseHandle(handle); }
            try { using (var process = Process.GetProcessById(pid)) return GameLauncher.IsClientName(process.ProcessName + ".exe"); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return false;
        }
    }
}
