using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

namespace TeyvatGuide
{
    internal static partial class Checks
    {
        private static string Probe(string directory) { return Path.Combine(work, directory, "GenshinImpact.exe"); }

        private static void PrepareLaunchCheck(string profile)
        {
            foreach (var name in new[] { "YuanShen", "GenshinImpact" })
                if (Process.GetProcessesByName(name).Length > 0)
                    throw new InvalidOperationException("A game client is already open; the isolated launch check needs its own test window.");
            Console.WriteLine("Detected installed client: " + (GameLauncher.FindExecutable(null) ?? "(none)"));
            foreach (string path in new[] { Probe("初始位置"), Probe("Selected Game 客户端") }) {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(Path.Combine(Program.BaseDir, "GameProbe.exe"), path);
            }
            var settings = new Settings { GameExecutable = Probe("初始位置") };
            settings.Keys["play"] = "Ctrl+P"; settings.Keys["pause"] = "Ctrl+O";
            settings.Save(Path.Combine(profile, "settings.json"));
        }

        [DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        private static IntPtr OwnedPicker(IntPtr owner)
        {
            var popup = GetLastActivePopup(owner);
            return popup != owner && GetWindow(popup, 4) == owner ? popup : IntPtr.Zero;
        }

        private static void EnterFile(IntPtr popup, string path)
        {
            var dialog = AutomationElement.FromHandle(popup);
            var host = dialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty,"FileNameControlHost"));
            var edit = host == null ? null : host.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit == null) edit = dialog.FindFirst(TreeScope.Descendants,new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty,"1148"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit)));
            if (edit == null) throw new InvalidOperationException("File name editor was not found.");
            ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
            var accept = dialog.FindFirst(TreeScope.Descendants,new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty,"1"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)));
            ((InvokePattern)accept.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }

        private static async Task TestGameLaunch(MainWindow main)
        {
            var owner = new WindowInteropHelper(main).Handle;
            bool freeP = RegisterHotKey(owner,0x6451,2,0x50), freeO = RegisterHotKey(owner,0x6452,2,0x4F);
            if (freeP) UnregisterHotKey(owner,0x6451);
            if (freeO) UnregisterHotKey(owner,0x6452);
            Check(freeP && freeO,"Ctrl+P and Ctrl+O are available to other applications after upgrading");
            string selected = Probe("Selected Game 客户端");
            string folder = Path.GetDirectoryName(selected), marker = Path.Combine(folder,"started.txt");
            string config = Path.Combine(work,"profile","settings.json");
            var launch = Find<Button>(main,"LaunchGame"); var status = Find<TextBlock>(main,"Status");
            Check(launch.IsVisible && launch.IsEnabled,"cover launch action is usable before loading any video");
            double width = main.Width; main.Width = main.MinWidth; main.UpdateLayout();
            var cover = Find<Border>(main,"CoverArt"); var location = Find<Button>(main,"GameLocation");
            var position = launch.TranslatePoint(new System.Windows.Point(0,0),cover);
            Check(position.X >= 0 && position.X + launch.ActualWidth <= cover.ActualWidth &&
                position.Y + launch.ActualHeight <= cover.ActualHeight,"launch action fits the cover at the minimum window width");
            main.Width = width; main.UpdateLayout();
            try {
                Find<RadioButton>(main,"TabKeys").IsChecked=true;
                var settingsScroll = Find<ScrollViewer>(main,"Scroll");
                settingsScroll.ScrollToBottom(); main.UpdateLayout();
                Check(location.IsVisible,"game location is available in settings");
                Click(main,"GameLocation");
                await Until(()=>OwnedPicker(owner)!=IntPtr.Zero,7000,"game location opens an owned file picker");
                PostMessage(OwnedPicker(owner),0x0010,IntPtr.Zero,IntPtr.Zero);
                await Until(()=>location.IsEnabled && status.Text.Contains("取消"),5000,"cancelled picker returns to settings");
                Check(Settings.Load(config).GameExecutable==Probe("初始位置"),"cancelling preserves the saved game");
                Click(main,"GameLocation");
                await Until(()=>OwnedPicker(owner)!=IntPtr.Zero,7000,"game location can be changed after cancelling");
                var picker = OwnedPicker(owner);
                await Task.Run(()=>EnterFile(picker,selected));
                await Until(()=>launch.IsEnabled && Settings.Load(config).GameExecutable==selected,6000,"manually selected game path persists");
                Check(!File.Exists(marker) && GameLauncher.FindExecutable(Settings.Load(config).GameExecutable)==selected,
                    "choosing a game only saves its path");
                Find<RadioButton>(main,"TabPlay").IsChecked=true; main.UpdateLayout();
                Click(main,"LaunchGame"); Click(main,"LaunchGame");
                await Until(()=>launch.IsEnabled && File.Exists(marker) && File.ReadAllLines(marker).Length==1,6000,
                    "cover starts the selected executable exactly once on repeated clicks");
                Check(File.ReadAllLines(marker)[0]==folder,"client starts in its own installation directory");
                Click(main,"LaunchGame");
                await Until(()=>launch.IsEnabled && (status.Text.Contains("切回原神") || status.Text.Contains("已在运行")),4000,
                    "cover reuses the running client");
                await Task.Delay(250);
                Check(File.ReadAllLines(marker).Length==1,"running client is not started twice");
            } finally {
                var picker = OwnedPicker(owner);
                if (picker!=IntPtr.Zero) PostMessage(picker,0x0010,IntPtr.Zero,IntPtr.Zero);
                File.WriteAllText(Path.Combine(folder,"finish"),"");
            }
        }
    }
}
