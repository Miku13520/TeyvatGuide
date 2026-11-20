using System;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class GameProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page,Usage; public uint Flags; public IntPtr Window; }
    [DllImport("user32.dll")] private static extern bool RegisterRawInputDevices(Device[] items,uint count,uint size);
    [STAThread]
    private static void Main(string[] args)
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        File.AppendAllText(Path.Combine(folder, "started.txt"), Environment.CurrentDirectory + Environment.NewLine);
        using (var window = new Form { Text = "游戏启动测试窗口", Width = 240, Height = 80, ShowInTaskbar = false })
        using (var timer = new Timer { Interval = 100 })
        {
            bool gameKeys = args.Length == 1 && args[0] == "--game-keys";
            if (gameKeys) {
                window.ShowInTaskbar = true; window.AutoScaleMode = AutoScaleMode.None; window.FormBorderStyle = FormBorderStyle.None;
                window.StartPosition = FormStartPosition.Manual; window.Bounds=Screen.PrimaryScreen.Bounds;
                window.BackColor = Color.FromArgb(33,44,51);
                window.Shown += (s,e) => window.BeginInvoke(new Action(window.Activate));
                window.Shown += (s,e) => {
                    var device=new Device { Page=1,Usage=6,Flags=0x230,Window=window.Handle };
                    if (RegisterRawInputDevices(new[]{device},1,(uint)Marshal.SizeOf(typeof(Device)))) File.WriteAllText(Path.Combine(folder,"game-input-ready"),"");
                };
            }
            var limit = DateTime.UtcNow.AddSeconds(gameKeys ? 110 : 20);
            timer.Tick += (s,e) => {
                if (File.Exists(Path.Combine(folder, "finish")) || DateTime.UtcNow >= limit) window.Close();
            };
            timer.Start();
            Application.Run(window);
        }
    }
}
