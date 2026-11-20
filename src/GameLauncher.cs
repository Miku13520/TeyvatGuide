using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace TeyvatGuide
{
    internal static class GameLauncher
    {
        private static readonly string[] ClientFiles = { "YuanShen.exe", "GenshinImpact.exe" };

        public static bool IsGameExecutable(string path)
        {
            if (!File.Exists(path) || Path.GetPathRoot(path).Length <= 2) return false;
            return IsClientName(Path.GetFileName(path));
        }

        internal static bool IsClientName(string name) { return Array.Exists(ClientFiles, file => file.Equals(name, StringComparison.OrdinalIgnoreCase)); }

        internal static string FromLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return null;
            string path = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')).Replace('/', '\\');
            if (IsGameExecutable(path)) return Path.GetFullPath(path);
            if (!Directory.Exists(path) || Path.GetPathRoot(path).Length <= 2) return null;
            foreach (string folder in new[] { path, Path.Combine(path, "Genshin Impact Game") })
                foreach (string name in ClientFiles)
                {
                    string candidate = Path.Combine(folder, name);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            return null;
        }

        private static string FromLauncher(string vendor)
        {
            try
            {
                using (var hyp = Registry.CurrentUser.OpenSubKey(@"Software\" + vendor + @"\HYP"))
                {
                    if (hyp == null) return null;
                    foreach (string version in hyp.GetSubKeyNames())
                    using (var channel = hyp.OpenSubKey(version))
                    {
                        if (channel == null) continue;
                        foreach (string id in channel.GetSubKeyNames())
                        {
                            if (!id.StartsWith("hk4e_", StringComparison.OrdinalIgnoreCase)) continue;
                            using (var game = channel.OpenSubKey(id))
                            {
                                if (game == null) continue;
                                string path = FromLocation(game.GetValue("GameInstallPath") as string);
                                if (path != null) return path;
                            }
                        }
                    }
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            return null;
        }

        public static string FindExecutable(string saved)
        {
            string path = FromLocation(saved) ?? FromLauncher("miHoYo") ?? FromLauncher("Cognosphere");
            if (path != null) return path;
            foreach (var location in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string root = Environment.GetFolderPath(location);
                if (string.IsNullOrEmpty(root)) continue;
                foreach (string relative in new[] { @"miHoYo Launcher\games\Genshin Impact Game",
                           @"HoYoPlay\games\Genshin Impact game", "Genshin Impact" })
                {
                    path = FromLocation(Path.Combine(root, relative));
                    if (path != null) return path;
                }
            }
            return null;
        }

        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

        public static string ActivateRunning()
        {
            int session;
            using (var current = Process.GetCurrentProcess()) session = current.SessionId;
            bool found = false;
            IntPtr window = IntPtr.Zero;
            foreach (string name in ClientFiles)
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
                using (process)
                {
                    try
                    {
                        if (process.SessionId != session) continue;
                        found = true;
                        if (window == IntPtr.Zero) window = process.MainWindowHandle;
                    }
                    catch (InvalidOperationException) { } // The client may exit during enumeration.
                    catch (Win32Exception) { } // A running elevated client can restrict access.
                }
            if (!found) return null;
            if (window == IntPtr.Zero) return "原神已在运行，正在等待游戏窗口。";
            if (IsIconic(window)) ShowWindowAsync(window, 9); // SW_RESTORE
            return SetForegroundWindow(window) ? "已切回原神。" : "原神已在运行，可从任务栏切回游戏。";
        }

        public static void Start(string path)
        {
            if (!IsGameExecutable(path)) throw new FileNotFoundException("游戏程序已移动，请重新选择游戏位置。", path);
            using (var process = Process.Start(new ProcessStartInfo {
                FileName = path, WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true
            }))
            {
                if (process == null) throw new InvalidOperationException("Windows 未能启动原神，请检查游戏位置。");
            }
        }
    }
}
