using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TeyvatGuide
{
    internal static partial class Checks
    {
        private static GuidePlayer PlayerIn(MainWindow main)
        {
            return (GuidePlayer)typeof(MainWindow).GetField("player",BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelRect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out PixelRect rect);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd,int command);
        private static async Task FocusGameProbe(IntPtr hwnd)
        {
            ShowWindowAsync(hwnd,9);
            await Task.Delay(240);
            SetWindowPos(hwnd,new IntPtr(-1),0,0,0,0,0x43);
            try {
                for (int i=0;i<8 && GetForegroundWindow()!=hwnd;i++) {
                    await Task.Delay(130);SetForegroundWindow(hwnd);
                    PixelRect bounds;GetWindowRect(hwnd,out bounds);
                    await FocusOwnedSurface(hwnd,new Point(bounds.Left+60,bounds.Top+150));
                }
                Check(GetForegroundWindow()==hwnd,"fixture is the foreground game");
            } finally { SetWindowPos(hwnd,new IntPtr(-2),0,0,0,0,0x13); }
        }
        private static async Task CheckGameKeys(MainWindow main)
        {
            string exe=Probe("GameKeys"), folder=Path.GetDirectoryName(exe);
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(Program.BaseDir,"GameProbe.exe"),exe);
            main.OpenLocal(Path.Combine(Program.BaseDir,"example.webm"));
            var timeline=Find<Slider>(main,"Timeline");
            await Until(()=>timeline.IsEnabled,18000,"demo video is ready for the foreground keyboard test");
            var video=Application.Current.Windows.OfType<VideoOverlay>().Single();
            Find<Slider>(main,"VideoScale").Value=245;
            video.Left=SystemParameters.WorkArea.Right-video.Width; video.Top=SystemParameters.WorkArea.Bottom-video.Height;
            main.WindowState=WindowState.Minimized;
            using (var probe=Process.Start(new ProcessStartInfo(exe,"--game-keys") { WorkingDirectory=folder, UseShellExecute=false })) {
                try {
                    IntPtr hwnd=IntPtr.Zero;
                    await Until(()=>{ probe.Refresh(); hwnd=probe.MainWindowHandle; return hwnd!=IntPtr.Zero; },5000,"open isolated fullscreen test client");
                    await FocusGameProbe(hwnd);
                    await Until(()=>File.Exists(Path.Combine(folder,"game-input-ready")),2500,"game requests raw input without application hotkeys");
                    await Task.Delay(400);
                    await PlayerIn(main).ExecuteAsync("pause",0);
                    await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"播放"),2500,"guide is paused before the physical key");
                    if (GetForegroundWindow()!=hwnd) throw new InvalidOperationException("Test client lost focus");
                    System.Windows.Forms.SendKeys.SendWait("^ ");
                    await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"暂停"),3000,"Ctrl+space plays from the raw-input game");
                    await Task.Delay(350);
                    if (GetForegroundWindow()!=hwnd) throw new InvalidOperationException("Test client lost focus");
                    System.Windows.Forms.SendKeys.SendWait("^ ");
                    await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"播放"),2500,"second Ctrl+space pauses exactly once");
                    bool oldLock=video.Locked;
                    if (GetForegroundWindow()!=hwnd) throw new InvalidOperationException("Test client lost focus");
                    System.Windows.Forms.SendKeys.SendWait("^0");
                    await Until(()=>oldLock!=video.Locked,2500,"zero toggles the overlay from the raw-input game");
                    double oldTime=timeline.Value;
                    if (GetForegroundWindow()!=hwnd) throw new InvalidOperationException("Test client lost focus");
                    System.Windows.Forms.SendKeys.SendWait("^{RIGHT}");
                    await Until(()=>Math.Abs(timeline.Value-oldTime-5)<.6,2500,"right arrow seeks once from the raw-input game");
                    if (GetForegroundWindow()!=hwnd) throw new InvalidOperationException("Test client lost focus");
                    System.Windows.Forms.SendKeys.SendWait("^{LEFT}");
                    await Until(()=>Math.Abs(timeline.Value-oldTime)<.6,2500,"left arrow seeks back from the raw-input game");
                    if (GetForegroundWindow()!=hwnd) throw new InvalidOperationException("Test client lost focus");
                    System.Windows.Forms.SendKeys.SendWait("^{UP}");
                    await Until(()=>Find<TextBlock>(main,"Rate").Text=="1.25×",2500,"up arrow increases playback speed inside the game");
                    if (GetForegroundWindow()!=hwnd) throw new InvalidOperationException("Test client lost focus");
                    System.Windows.Forms.SendKeys.SendWait("^{DOWN}");
                    await Until(()=>Find<TextBlock>(main,"Rate").Text=="1×",2500,"down arrow restores playback speed inside the game");
                    await VerifyStateFallback(main,hwnd);
                } finally {
                    File.WriteAllText(Path.Combine(folder,"finish"),"");
                    probe.WaitForExit(2500);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct QaRawDevice { public ushort Page,Usage; public uint Flags; public IntPtr Target; }
        [DllImport("user32.dll")] private static extern bool RegisterRawInputDevices(QaRawDevice[] devices,uint count,uint size);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
        private static async Task HoldTestKey(IntPtr game,byte code)
        {
            if (GetForegroundWindow()!=game) throw new InvalidOperationException("Fullscreen fixture lost focus");
            keybd_event(0xa2,0,0,UIntPtr.Zero);
            keybd_event(code,0,0,UIntPtr.Zero);
            try { await Task.Delay(165); }
            finally { keybd_event(code,0,2,UIntPtr.Zero); keybd_event(0xa2,0,2,UIntPtr.Zero); }
            await Task.Delay(80);
        }
        private static async Task VerifyStateFallback(MainWindow main,IntPtr game)
        {
            PixelRect fullscreen;
            var screen=System.Windows.Forms.Screen.FromHandle(game).Bounds;
            Check(new GameForeground().GameWindow()==game && GetWindowRect(game,out fullscreen) &&
                fullscreen.Left==screen.Left && fullscreen.Right==screen.Right &&
                fullscreen.Top==screen.Top && fullscreen.Bottom==screen.Bottom,"fixture uses borderless fullscreen on its monitor");
            var remove=new QaRawDevice { Page=1,Usage=6,Flags=1 };
            Check(RegisterRawInputDevices(new[]{remove},1,(uint)Marshal.SizeOf(typeof(QaRawDevice))),"disable the test assistant raw-input delivery");
            await HoldTestKey(game,0x20);
            await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"暂停"),2500,"fullscreen key-state fallback catches Ctrl+space without raw input or hotkeys");
            await HoldTestKey(game,0x20);
            await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"播放"),2500,"a held Ctrl+space toggles once, then can be pressed again");
            var timeline=Find<Slider>(main,"Timeline"); double time=timeline.Value;
            await HoldTestKey(game,0x27);
            await Until(()=>Math.Abs(timeline.Value-time-5)<.6,2500,"fullscreen fallback also accepts the right arrow");
        }
    }
}
