using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace TeyvatGuide
{
    internal static partial class Checks
    {
        private static int count;
        private static readonly string work = Path.GetFullPath(Path.Combine(Program.BaseDir, "..", "runs", Guid.NewGuid().ToString("N")));
        private static void Check(bool ok, string label)
        {
            if (!ok) throw new Exception(label);
            count++; Console.WriteLine("PASS " + label);
        }
        private static BiliLink Link(string text)
        { return BiliLink.ResolveAsync(text, CancellationToken.None).GetAwaiter().GetResult(); }
        private static void CoreChecks()
        {
            var link = Link("【跟跑】 https://www.bilibili.com/video/BV18i4y1m7LY/?p=2&t=23.5&share_source=copy");
            Check(link.Query == "bvid=BV18i4y1m7LY&p=2&t=23.5", "Bilibili share text preserves episode and timestamp");
            Check(Link("BV18i4y1m7LY").Part == 1 && Link("av12345").Id == "av12345", "raw BV and av identifiers");
            Check(Link("https://player.bilibili.com/player.html?aid=23&p=3").Key == "av23/p3", "official player URL");
            Check(BiliLink.ParseVideo(new Uri("https://www.bilibili.com.example/video/BV18i4y1m7LY")) == null, "host boundary");
            Check(BiliLink.ParseVideo(new Uri("https://www.bilibili.com/video/BV1/")) == null, "malformed video ID");
            Check(Link("[https://www.bilibili.com/video/BV1P6SNYREo8/](https://www.bilibili.com/video/BV1P6SNYREo8/)").Id == "BV1P6SNYREo8","pasted Markdown share link");
            var address = CollectionAddress.Parse(new Uri("https://space.bilibili.com/431073645/lists/4455715?type=season"));
            Check(address.Mid==431073645 && address.ListId==4455715 && address.Kind=="season", "UP space collection URL");
            Check(CollectionAddress.Parse(new Uri("https://space.bilibili.com/1773346/channel/seriesdetail?sid=449853")).Kind=="series", "classic series URL");
            Check(CollectionAddress.Parse(new Uri("https://space.bilibili.com.example/431073645/lists/4?type=season"))==null, "collection host validation");
            const string multi = @"{""code"":0,""data"":{""title"":""我的攻略"",""bvid"":""BV18i4y1m7LY"",""pages"":[{""page"":2,""part"":""second"",""duration"":180},{""page"":1,""part"":""first"",""duration"":33}]}}";
            var collection = CollectionImporter.FromVideo(multi,link);
            Check(collection.Episodes.Count==2 && collection.Episodes[0].Title=="first" && collection.Selected==1 &&
                collection.Episodes[1].LengthText=="3:00","part order, titles, durations and original selected P");
            const string season = @"{""code"":0,""data"":{""title"":""one"",""bvid"":""BV18i4y1m7LY"",""ugc_season"":{""title"":""routes"",""sections"":[{""episodes"":[{""bvid"":""BV18i4y1m7LY"",""title"":""north"",""pages"":[{""page"":1,""part"":""A""},{""page"":2,""part"":""B""}]}]},{""episodes"":[{""bvid"":""BV1P6SNYREo8"",""title"":""south"",""page"":{""page"":1}}]}]}}}";
            var nested = CollectionImporter.FromVideo(season,link);
            Check(nested.Title=="routes" && nested.Episodes.Count==3 && nested.Episodes[1].Title=="north · B" &&
                nested.Episodes[2].VideoId=="BV1P6SNYREo8","UGC season preserves sections, BVs and nested parts");
            var keys = Shortcuts.Defaults();
            Check(Shortcuts.Validate(keys) == null, "default shortcuts parse");
            Check(Shortcuts.All.Select(a => keys[a.Id]).SequenceEqual(new[] {
                "Ctrl+Space","Ctrl+Left","Ctrl+Right","Ctrl+Down","Ctrl+Up","Ctrl+0","Ctrl+PageUp","Ctrl+PageDown"
            }), "eight default bindings including episode navigation");
            keys["back"] = "Ctrl+Alt+Left";
            keys["lock"] = "Alt+Ctrl+Left";
            Check(Shortcuts.Validate(keys) != null, "duplicate modifiers in different order collide");
            Gesture key;
            Check(Gesture.TryParse("Ctrl+Shift+8", out key) && key.VirtualKey == 0x38, "numeric hotkey");
            Check(!Gesture.TryParse("Ctrl+F12", out key) && !Gesture.TryParse("Alt+Alt+A", out key), "reserved and malformed shortcuts");
            Check(Gesture.FromKeys(Key.D7,ModifierKeys.Control|ModifierKeys.Shift)=="Ctrl+Shift+7" &&
                Gesture.FromKeys(Key.Left,ModifierKeys.Alt)=="Alt+Left" && Gesture.FromKeys(Key.A,ModifierKeys.None)=="A","captured keyboard gestures");
            Check(Gesture.FromKeys(Key.OemQuestion,ModifierKeys.None)=="/" && Gesture.TryParse("/",out key) && key.VirtualKey==0xbf &&
                key.Modifiers==0 && Gesture.TryParse("0",out key) && key.VirtualKey==0x30,"main keyboard slash and zero are bare native hotkeys");
            Check(Gesture.TryParse("Divide",out key) && key.VirtualKey==0x6f && Gesture.TryParse("NumPad0",out key) && key.VirtualKey==0x60,
                "numeric keypad can be bound independently");
            using (var gameKeys=new GameKeys(IntPtr.Zero,()=>IntPtr.Zero)) {
                gameKeys.StrokeAction(0x11,0x1d,0,0);
                for (int i=0;i<Shortcuts.All.Length;i++) {
                    Gesture.TryParse(Shortcuts.All[i].DefaultKey,out key); gameKeys.Bind(key,i);
                    int stamp=150*i;
                    Check(gameKeys.StrokeAction(key.VirtualKey,0,0,stamp)==i && gameKeys.StrokeAction(key.VirtualKey,0,0,stamp+1)==-1 &&
                        gameKeys.StrokeAction(key.VirtualKey,0,1,stamp+10)==-1 && !gameKeys.FirstSignal(i,false,stamp+1),"game press, hold and desktop duplicate "+Shortcuts.All[i].Id);
                }
                Check(gameKeys.FirstSignal(0,false,1500) && gameKeys.StrokeAction(0x20,0x39,0,1500)==-1,"desktop-first space is not toggled twice");
                gameKeys.StrokeAction(0x20,0x39,1,1510); gameKeys.StrokeAction(0x11,0x1d,1,1550);
                Check(gameKeys.StrokeAction(0x20,0x39,0,1551)==-1,"bare space does not trigger the Ctrl+space default");
                gameKeys.Clear(); Gesture.TryParse("/",out key); gameKeys.Bind(key,0);
                int sampledActions=0;
                Action<int> countAction=id=>sampledActions++;
                gameKeys.Sample(k=>k==0xbf,1600,countAction);
                gameKeys.Sample(k=>false,1610,countAction);
                gameKeys.Sample(k=>false,1640,countAction);
                Check(sampledActions==1,"known press is delivered even if released before the fallback delay");
                gameKeys.Sample(k=>k==0xbf,1780,countAction);
                Check(gameKeys.FirstSignal(0,true,1775) && !gameKeys.FirstSignal(0,false,1778),"raw and desktop identify the same physical press");
                gameKeys.Sample(k=>k==0xbf,1825,countAction);
                Check(sampledActions==1,"a press delivered by both native paths does not repeat through state polling");
            }
            var file = Path.Combine(work,"settings.json");
            var settings = new Settings { VideoWidth = 510 };
            settings.Collection=collection; settings.PlaybackPositions[link.Key] = 27.5; settings.Save(file); settings.VideoWidth = 530; settings.Save(file);
            var read = Settings.Load(file);
            Check(read.VideoWidth == 530 && read.PlaybackPositions[link.Key] == 27.5, "window settings and playback progress persist");
            Check(read.Collection.Selected==1 && read.Collection.Episodes[1].Ordinal=="02","imported list and selection survive restart");
            var legacy = Shortcuts.All.ToDictionary(a => a.Id, a => "Ctrl+Alt+"+(a.Id=="toggle" ? "Space" : a.Id=="lock" ? "L" : a.DefaultKey));
            legacy["faster"] = "alt+Control+up";
            legacy["play"] = "Shift+F4"; legacy["pause"] = "Ctrl+O";
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            File.WriteAllText(file,json.Serialize(new { Keys=legacy, VideoOpacity=63 }));
            var upgraded = Settings.Load(file);
            Check(upgraded.ShortcutVersion==4 && upgraded.VideoOpacity==63 &&
                Shortcuts.All.All(a=>upgraded.Keys[a.Id]==a.DefaultKey),"legacy settings upgrade to Ctrl chords");
            Check(upgraded.Keys.Count==8 && !upgraded.Keys.ContainsKey("play") && !upgraded.Keys.ContainsKey("pause"),
                "old custom and default dedicated playback bindings are discarded");
            legacy["toggle"] = "Ctrl+Up"; legacy["back"] = "Alt+F8";
            File.WriteAllText(file,json.Serialize(new { Keys=legacy, ShortcutVersion=2 }));
            upgraded=Settings.Load(file);
            Check(Shortcuts.All.All(a=>upgraded.Keys[a.Id]==a.DefaultKey) &&
                Shortcuts.Validate(upgraded.Keys)==null,"the one-time upgrade also replaces old custom bindings");
            upgraded.Keys["lock"]="Ctrl+Alt+L"; upgraded.Keys["play"]="Ctrl+P"; upgraded.Save(file);
            var modern=Settings.Load(file);
            Check(modern.Keys["lock"]=="Ctrl+Alt+L" && !modern.Keys.ContainsKey("play"),
                "current profiles drop retired keys while preserving an explicit old-style chord");
            modern.Save(file);
            Check(!File.ReadAllText(file).Contains("\"play\""),"retired bindings stay removed on disk");
            var sixKeys = Shortcuts.Defaults(); sixKeys.Remove("previous"); sixKeys.Remove("next"); sixKeys["back"]="Ctrl+Prior";
            File.WriteAllText(file,json.Serialize(new { ShortcutVersion=1, Keys=sixKeys, ArrowLeft=844, ArrowTop=645, VideoOpacity=74 }));
            var expanded=Settings.Load(file);
            Check(expanded.VideoOpacity==74 && expanded.Keys["back"]=="Ctrl+Left" && expanded.Keys["previous"]=="Ctrl+PageUp" &&
                expanded.Keys["next"]=="Ctrl+PageDown" && Shortcuts.Validate(expanded.Keys)==null,"old six-key profiles get all eight Ctrl defaults");
            var lastRelease=Shortcuts.Defaults(); lastRelease["toggle"]="Control+OemQuestion"; lastRelease["back"]="Ctrl+F8";
            const string savedEpisode = "BV1P6SNYREo8/p2";
            File.WriteAllText(file,json.Serialize(new { ShortcutVersion=3,Keys=lastRelease, ArrowOpacity=95, MapGuideVersion=1,
                FollowTargets=true, ShowMinimapMarks=true, RouteProgress=new Dictionary<string,int>{{savedEpisode,5}},
                Tracking=false, Maps=new Dictionary<string,object>{{savedEpisode,new { X=.23, Y=.28, Diameter=.2 }}},
                PlaybackPositions=new Dictionary<string,double>{{savedEpisode,42.5}}, VideoOpacity=74 }));
            var evolved=Settings.Load(file);
            Check(evolved.Keys["toggle"]=="Ctrl+Space" && evolved.Keys["back"]=="Ctrl+F8" &&
                evolved.PlaybackPositions[savedEpisode]==42.5 && evolved.VideoOpacity==74,
                "old map settings do not interfere with playback preferences and saved positions");
            evolved.Save(file);
            var rewritten=File.ReadAllText(file);
            Check(!rewritten.Contains("ShowMinimapMarks") && !rewritten.Contains("RouteProgress") && !rewritten.Contains("ArrowOpacity") &&
                !rewritten.Contains("\"Tracking\"") && !rewritten.Contains("\"Maps\""),
                "removed map and tracking settings are no longer written");
            lastRelease["toggle"]="Alt+Space";
            File.WriteAllText(file,json.Serialize(new { ShortcutVersion=3,Keys=lastRelease, ArrowOpacity=70, MapGuideVersion=1 }));
            evolved=Settings.Load(file);
            Check(evolved.Keys["toggle"]=="Alt+Space","custom playback shortcut is preserved");
            File.WriteAllText(file, "{invalid");
            Check(Settings.Load(file).VideoWidth == 480, "corrupt preferences recover");
            string gameRoot = Path.Combine(work,"原神测试目录");
            string gameDir = Path.Combine(gameRoot,"Genshin Impact Game");
            Directory.CreateDirectory(gameDir);
            string cn = Path.Combine(gameDir,"YuanShen.exe"), global = Path.Combine(gameDir,"GenshinImpact.exe");
            File.WriteAllText(cn,""); File.WriteAllText(global,"");
            Check(GameLauncher.FromLocation(gameRoot)==cn && GameLauncher.FromLocation(gameDir.Replace('\\','/'))==cn,
                "launcher directory and parent resolve the Chinese game client");
            Check(GameLauncher.FindExecutable(global)==global,"saved international client takes precedence over auto-detection");
            string launcher=Path.Combine(gameRoot,"launcher.exe"); File.WriteAllText(launcher,"");
            Check(!GameLauncher.IsGameExecutable(launcher) && GameLauncher.FromLocation(Path.Combine(gameDir,"missing"))==null,
                "missing clients and launcher executables are excluded");
        }

        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint p);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flag);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] state);
        [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        private static bool DesktopBindingRegistered(MainWindow window,string action)
        {
            var shortcuts=(Shortcuts)typeof(MainWindow).GetField("shortcuts",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            var registered=(ICollection<int>)typeof(Shortcuts).GetField("registered",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(shortcuts);
            return registered.Contains(Array.FindIndex(Shortcuts.All,a=>a.Id==action));
        }
        private static void Hotkey(MainWindow window, string action)
        {
            int index = Array.FindIndex(Shortcuts.All, a => a.Id == action);
            if (index < 0) throw new ArgumentException("Unknown shortcut: " + action);
            if (DesktopBindingRegistered(window,action))
                PostMessage(new WindowInteropHelper(window).Handle, 0x0312, new IntPtr(0x3120 + index), IntPtr.Zero);
            else
                typeof(MainWindow).GetMethod("DispatchAction",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{action});
        }
        private static void SetPlaying(MainWindow window, bool playing)
        {
            bool paused = Equals(Find<Button>(window,"TogglePlay").Content,"播放");
            if (playing == paused) Hotkey(window,"toggle");
        }
        private static T Find<T>(Window window, string name) where T : FrameworkElement
        { return (T)((FrameworkElement)window.Content).FindName(name); }
        private static void Click(MainWindow window, string name)
        { Find<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static async Task FocusTestWindow(Window window, Point screen)
        {
            var handle = new WindowInteropHelper(window).Handle;
            window.Activate(); await Task.Delay(150);
            await FocusOwnedSurface(handle,screen);
        }
        private static async Task FocusOwnedSurface(IntPtr handle, Point screen)
        {
            if (GetForegroundWindow() == handle) return;
            var point = new NativePoint { X=(int)screen.X, Y=(int)screen.Y };
            if (GetAncestor(WindowFromPoint(point),2) != handle) return;
            var destination = new System.Drawing.Point(point.X,point.Y);
            var previous = System.Windows.Forms.Cursor.Position;
            try {
                System.Windows.Forms.Cursor.Position = destination;
                if (System.Windows.Forms.Cursor.Position == destination && GetAncestor(WindowFromPoint(point),2) == handle)
                {
                    // Only click an inert area that belongs to this test window.
                    mouse_event(2,0,0,0,UIntPtr.Zero); mouse_event(4,0,0,0,UIntPtr.Zero);
                    await Task.Delay(120);
                }
            } finally { if (System.Windows.Forms.Cursor.Position == destination) System.Windows.Forms.Cursor.Position = previous; }
        }
        private static void RouteChord(Button field, Key key, ModifierKeys mods)
        {
            // Deliver WPF keyboard events on the test UI thread without typing into the user's foreground app.
            var original = new byte[256];
            if (!GetKeyboardState(original)) throw new Exception("Cannot inspect test-thread keyboard state.");
            var state = (byte[])original.Clone();
            state[0x11] = state[0xa2] = (byte)((mods & ModifierKeys.Control) != 0 ? 128 : 0);
            state[0x12] = state[0xa4] = (byte)((mods & ModifierKeys.Alt) != 0 ? 128 : 0);
            state[0x10] = state[0xa0] = (byte)((mods & ModifierKeys.Shift) != 0 ? 128 : 0);
            state[0xa1] = state[0xa3] = state[0xa5] = state[0x5b] = state[0x5c] = 0;
            try {
                if (!SetKeyboardState(state)) throw new Exception("Cannot set test-thread modifiers.");
                foreach (var routed in new[] {Keyboard.PreviewKeyDownEvent, Keyboard.PreviewKeyUpEvent})
                {
                    var press = new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(field),Environment.TickCount,key) { RoutedEvent = routed };
                    field.RaiseEvent(press);
                }
            } finally { SetKeyboardState(original); }
        }
        private static async Task Until(Func<bool> condition, int milliseconds, string label)
        {
            var timer = Stopwatch.StartNew();
            while (!condition() && timer.ElapsedMilliseconds < milliseconds) await Task.Delay(80);
            Check(condition(), label);
        }
        private static void Screenshot(FrameworkElement view, string name)
        {
            view.UpdateLayout();
            const double scale = 1.5;
            var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * scale), (int)Math.Ceiling(view.ActualHeight * scale),96 * scale,96 * scale,PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
                dc.DrawRectangle(new VisualBrush(view), null, new Rect(0,0,view.ActualWidth,view.ActualHeight));
            image.Render(drawing); var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(work,name))) encoder.Save(stream);
        }
        private static async Task Desktop(MainWindow main, string mode)
        {
            Console.WriteLine("QA directory " + work);
            if (mode=="--settings") {
                Check(new[]{"TabTrack","BearingText","Calibrate","OpenDemo","ToggleNav","ShowEpisodes","ApplySeek"}.All(
                    name=>Find<FrameworkElement>(main,name)==null),"retired controls are absent from the console");
                Check(Find<Button>(main,"LaunchGame").IsVisible && !Find<Button>(main,"GameLocation").IsVisible &&
                    !Find<Button>(main,"CloseVideo").IsVisible,"home shows launch and hides inactive or secondary actions");
                Click(main,"ResetPositions");
                Screenshot((FrameworkElement)main.Content,"console.png");
                Find<RadioButton>(main,"TabKeys").IsChecked=true; main.UpdateLayout();
                var first=Find<StackPanel>(main,"KeyRows").Children.OfType<Border>().First();
                var key=((DockPanel)first.Child).Children.OfType<Button>().Single();
                var binding=key.Content.ToString();
                Check(binding.Contains("Ctrl") && binding.Contains("空格"),"visible default is Ctrl+space");
                Screenshot((FrameworkElement)main.Content,"default-keys.png");
                Check(Find<Button>(main,"GameLocation").IsVisible,"settings exposes the game location");
                var seek=Find<TextBox>(main,"SeekBox");
                Action loseFocus=()=>seek.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,Environment.TickCount,seek,key) {
                    RoutedEvent=Keyboard.LostKeyboardFocusEvent
                });
                seek.Text="15"; loseFocus();
                Check(Equals(Find<Button>(main,"Forward").Content,"快进 15 秒"),"jump step saves on leaving the field");
                seek.Text="0"; loseFocus();
                Check(seek.Text=="15" && Equals(Find<Button>(main,"Back").Content,"快退 15 秒"),"invalid jump step restores the saved value");
                Find<RadioButton>(main,"TabPlay").IsChecked=true; main.Width=main.MinWidth; main.UpdateLayout();
                var link=Find<TextBox>(main,"LinkBox");
                var import=Find<Button>(main,"ImportLink");
                Check(link.ActualWidth>=180 && import.ActualWidth>=66 && Find<Button>(main,"LaunchGame").IsVisible,
                    "home actions remain usable at the minimum width");
                Screenshot((FrameworkElement)main.Content,"console-compact.png");
                return;
            }
            Screenshot((FrameworkElement)main.Content,"console.png");
            if (mode == "--game") { await TestGameLaunch(main); return; }
            if (mode == "--game-keys") { await CheckGameKeys(main); return; }
            var cover = Find<Border>(main,"CoverArt").Background as ImageBrush;
            Check(cover != null && cover.ImageSource != null,"illustrated cover loaded from the executable");
            Check(Find<RadioButton>(main,"TabPlay").IsChecked==true && Find<Button>(main,"LockButton").IsVisible,"playback is the default page and lock is accessible");
            var status = Find<TextBlock>(main,"Status");
            bool allDefaultsAvailable=Shortcuts.All.All(action=>DesktopBindingRegistered(main,action.Id));
            CoreWebView2Frame official = null;
            var video = Application.Current.Windows.OfType<VideoOverlay>().Single();
            if (mode == "--collections") { await TestCollections(main,video); return; }
            video.Browser.CoreWebView2InitializationCompleted += (s,e) => {
                if (!e.IsSuccess) return;
                video.Browser.CoreWebView2.FrameCreated += (sender,args) => { if (args.Frame.Name == "guide") official = args.Frame; };
            };
            if (mode == "--bili" || mode == "--player-chrome")
            {
                Find<TextBox>(main,"LinkBox").Text = "https://www.bilibili.com/video/BV18i4y1m7LY/?p=2&t=23";
                Click(main,"OpenLink");
                Exception error = null;
                try {
                    await Until(() => Find<Slider>(main,"Timeline").IsEnabled, 42000, "live Bilibili P2 player exposed playable media");
                    Check(official != null, "documented player iframe bound");
                    string state = await official.ExecuteScriptAsync(Program.Resource("media.js") + "('read',null)");
                    Console.WriteLine("Public player state " + state);
                    var parsed = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<MediaInfo>(state);
                    Check(parsed != null && parsed.duration > 200 && parsed.duration < 400, "P2 media duration consistent with public guide");
                    if (mode=="--player-chrome") {
                        if (Equals(Find<Button>(main,"TogglePlay").Content,"播放")) Click(main,"TogglePlay");
                    } else SetPlaying(main,true);
                    await Until(() => Find<Slider>(main,"Timeline").Value > 23.6 && Find<Button>(main,"TogglePlay").Content.ToString()=="暂停",
                        25000,"public guide advances from the shared timestamp");
                    if (mode=="--player-chrome") Click(main,"TogglePlay");
                    else SetPlaying(main,false);
                    await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"播放"),5000,"pause official guide");
                    if (mode=="--player-chrome") {
                        const string display=@"Array.from(document.querySelectorAll('.bpx-player-top-wrap,.bpx-player-control-wrap,.bpx-player-ending-wrap,.bpx-player-enter-button,.bpx-player-relation-wrap'))
                            .map(e=>getComputedStyle(e).display==='none')";
                        if (video.Locked) { Click(main,"LockButton"); await Task.Delay(400); }
                        Check((await official.ExecuteScriptAsync(display)).Contains("false"),"unlocked official player can display its own controls");
                        Click(main,"LockButton"); await Task.Delay(430);
                        Check(await official.ExecuteScriptAsync(display)=="[true,true,true,true,true]","lock removes the title, controls, backdrop and Bilibili entry prompts");
                        using (var screenshot=File.Create(Path.Combine(work,"locked-player.png")))
                            await video.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot);
                        var playing=PlayerIn(main);
                        Check((await playing.ExecuteAsync("read",null)).ready,"locked clean view keeps the video readable");
                        Click(main,"TogglePlay");
                        await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"暂停"),5000,"play command works with the chrome hidden");
                        Click(main,"TogglePlay");
                        await Until(()=>Equals(Find<Button>(main,"TogglePlay").Content,"播放"),5000,"pause command leaves the clean frame visible");
                        Check(await official.ExecuteScriptAsync(display)=="[true,true,true,true,true]","a second pause does not reintroduce chrome");
                        Click(main,"LockButton"); await Task.Delay(430);
                        Check((await official.ExecuteScriptAsync(display)).Contains("false"),"unlock restores the official controls");
                        return;
                    }
                    double initial = Find<Slider>(main,"Timeline").Value;
                    Hotkey(main,"forward"); await Task.Delay(700);
                    Check(Math.Abs(Find<Slider>(main,"Timeline").Value-initial-5) < .6,"seek public guide forward five seconds");
                    Hotkey(main,"faster"); await Task.Delay(400);
                    Check(Find<TextBlock>(main,"Rate").Text=="1.25×","set public guide playback rate");
                    SetPlaying(main,true); await Task.Delay(350);
                    string capture = await official.ExecuteScriptAsync(Program.Resource("media.js") + "('read',null)");
                    var sample = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<MediaInfo>(capture);
                    Check(sample != null && sample.ready,"read current rendered B station video");
                    Screenshot((FrameworkElement)video.Content, "public-player.png");
                    var shortLink = await BiliLink.ResolveAsync("【攻略分享】 https://b23.tv/C6ZZY6C", CancellationToken.None);
                    Check(shortLink.Id == "BV18PpTzVE9K","resolve a published mobile b23.tv video share");
                }
                catch (Exception ex) { error = ex; }
                Console.WriteLine("Application status " + status.Text);
                if (official != null) {
                    try { Console.WriteLine("Visible player message " + await official.ExecuteScriptAsync("document.body.innerText.slice(0,360)")); }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }
                    try { Console.WriteLine("Final media state " + await official.ExecuteScriptAsync(Program.Resource("media.js")+"('read',null)")); }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }
                }
                if (error != null) throw error;
                return;
            }
            main.OpenLocal(Path.Combine(Program.BaseDir,"example.webm"));
            try { await Until(() => Find<Slider>(main,"Timeline").IsEnabled, 22000, "normal launch, WebView2 and example video"); }
            finally { Console.WriteLine("Application status " + status.Text); }
            Check(Math.Abs(Find<Slider>(main,"Timeline").Maximum - 14) < .2, "fixture duration 14 seconds");
            SetPlaying(main,false); await Task.Delay(240);
            Hotkey(main,"back"); await Task.Delay(250);
            SetPlaying(main,true);
            await Until(()=>Find<Slider>(main,"Timeline").Value>5.4,8000,"local video plays past the first seek step");
            Screenshot((FrameworkElement)video.Content,"example-playing.png");
            SetPlaying(main,false);
            await Until(()=>Find<Button>(main,"TogglePlay").Content.ToString()=="播放",1500,"the local video pauses");
            double old = Find<Slider>(main,"Timeline").Value;
            Hotkey(main,"back"); await Task.Delay(500);
            Check(Math.Abs(Find<Slider>(main,"Timeline").Value - (old - 5)) < .4, "seek back using registered command");
            Hotkey(main,"forward"); await Task.Delay(420);
            Check(Math.Abs(Find<Slider>(main,"Timeline").Value - old) < .4, "seek forward using registered command");
            Hotkey(main,"faster"); await Task.Delay(320);
            Check(Find<TextBlock>(main,"Rate").Text == "1.25×", "speed increase reaches media element");
            Hotkey(main,"slower"); await Task.Delay(320);
            Check(Find<TextBlock>(main,"Rate").Text == "1×", "speed decrease reaches media element");
            Check(Application.Current.Windows.OfType<OverlayWindow>().Single()==video,"the video is the only overlay");
            Find<Slider>(main,"VideoScale").Value = 400; Find<Slider>(main,"VideoAlpha").Value = 65;
            Check(video.Width == 400 && Math.Abs(video.Opacity - .65) < .001, "floating size and opacity");
            var backdrop = new Window { Title = "Test surface", WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, WindowState = WindowState.Maximized, Background = Brushes.DarkSlateGray,
                ShowInTaskbar = false, Topmost = true };
            main.WindowState = WindowState.Minimized;
            backdrop.Show(); backdrop.Activate(); await Task.Delay(200);
            try
            {
                var backdropHandle = new WindowInteropHelper(backdrop).Handle;
                bool hadForeground = GetForegroundWindow() == backdropHandle;
                var handle = new WindowInteropHelper(video).Handle;
                video.SetLocked(false);
                // Put the test backdrop immediately behind the overlay, even if Windows kept another app active.
                Check(SetWindowPos(backdropHandle,handle,0,0,0,0,0x0013),"borderless test surface placed behind the player without activation");
                Point header = video.PointToScreen(new Point(40,15));
                var headerPoint = new NativePoint { X=(int)header.X, Y=(int)header.Y };
                await Until(() => GetAncestor(WindowFromPoint(headerPoint),2)==handle,2500,
                    "unlocked video receives input above borderless surface at " + headerPoint.X + "," + headerPoint.Y +
                    " (hit " + GetAncestor(WindowFromPoint(headerPoint),2) + ", overlay " + handle + ", backdrop " + backdropHandle + ")");
                Click(main,"LockButton"); await Task.Delay(160);
                var style = GetWindowLongPtrW(handle,-20).ToInt64();
                Check((style & 0x08080028) == 0x08080028 && video.IsVisible, "layered topmost nonactivating click-through window");
                Point screen = video.PointToScreen(new Point(40,40));
                var point = new NativePoint {X=(int)screen.X,Y=(int)screen.Y};
                await Until(() => GetAncestor(WindowFromPoint(point),2)==backdropHandle,2500,
                    "borderless fullscreen surface receives pointer through video");
                if (hadForeground) Check(GetForegroundWindow()==backdropHandle,"locking preserves foreground focus");
                await FocusTestWindow(backdrop,backdrop.PointToScreen(new Point(backdrop.ActualWidth/2,backdrop.ActualHeight/2)));
                if (GetForegroundWindow()==backdropHandle && DesktopBindingRegistered(main,"toggle") && DesktopBindingRegistered(main,"lock"))
                {
                    System.Windows.Forms.SendKeys.SendWait("^ ");
                    await Until(() => Find<Button>(main,"TogglePlay").Content.ToString()=="暂停",2500,
                        "Ctrl+space starts playback with the console minimized");
                    Check(GetForegroundWindow()==backdropHandle,"playback hotkey preserves foreground focus");
                    System.Windows.Forms.SendKeys.SendWait("^ ");
                    await Until(() => Find<Button>(main,"TogglePlay").Content.ToString()=="播放",2500,
                        "Ctrl+space also pauses playback");
                    System.Windows.Forms.SendKeys.SendWait("^0");
                    await Until(()=>!video.Locked,2200,"Ctrl+zero unlocks the overlay from the focused test surface");
                    System.Windows.Forms.SendKeys.SendWait("^0");
                    await Until(()=>video.Locked,2200,"Ctrl+zero locks the overlay again");
                }
                else
                {
                    Console.WriteLine("Physical desktop-key check needs foreground focus and unoccupied defaults. Fullscreen input is covered separately.");
                    SetPlaying(main,true);
                    await Until(() => Find<Button>(main,"TogglePlay").Content.ToString()=="暂停",2500,
                        "playback action starts with console minimized");
                }
                Hotkey(main,"lock"); await Task.Delay(180);
                Check(!video.Locked, "hotkey unlock with other window foreground");
            }
            finally { backdrop.Close(); main.WindowState = WindowState.Normal; }
            Find<RadioButton>(main,"TabKeys").IsChecked = true;
            main.UpdateLayout();
            await FocusTestWindow(main,Find<Border>(main,"NavPane").PointToScreen(new Point(30,300)));
            var rows = Find<StackPanel>(main,"KeyRows").Children.OfType<Border>();
            Check(rows.Count()==8,"shortcut editor shows eight actions");
            var field = ((DockPanel)rows.First().Child).Children.OfType<Button>().Single();
            string oldKey = field.Content.ToString();
            Check(oldKey.Contains("Ctrl") && oldKey.Contains("空格"),"shortcut page displays the Ctrl+space default");
            Screenshot((FrameworkElement)main.Content,"default-keys.png");
            RoutedEventArgs click = new RoutedEventArgs(Button.ClickEvent);
            field.RaiseEvent(click); RouteChord(field,Key.Escape,ModifierKeys.None);
            Check(field.Content.ToString()==oldKey && (status.Text.Contains("取消") || status.Text.Contains("占用")),
                "Escape cancels recording without changing the binding");
            field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (GetForegroundWindow()==new WindowInteropHelper(main).Handle && field.IsKeyboardFocused)
            {
                System.Windows.Forms.SendKeys.SendWait("{F9}");
                await Until(() => status.Text.Contains("已生效"),2000,"Windows chord is recorded through the focused control");
            }
            else { RouteChord(field,Key.F9,ModifierKeys.None); }
            Check(status.Text.Contains("已生效") && field.Content.ToString()=="F9","one-click single-key replacement commits on release");
            var hwnd = new WindowInteropHelper(main).Handle;
            if (allDefaultsAvailable) {
                Check(RegisterHotKey(hwnd,0x6402,2,0x20),"old Ctrl+space binding released after recording");
                UnregisterHotKey(hwnd,0x6402);
            }
            Check(RegisterHotKey(hwnd,0x6400,6,0x79), "test conflict owner registered");
            try
            {
                field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); RouteChord(field,Key.F10,ModifierKeys.Control|ModifierKeys.Shift);
                Check(status.Text.Contains("占用") && Equals(field.Content,"F9"),"OS binding conflict rolls back");
                Check(!RegisterHotKey(hwnd,0x6401,0,0x78),"previous custom binding still held");
            }
            finally { UnregisterHotKey(hwnd,0x6400); UnregisterHotKey(hwnd,0x6401); }
            field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); RouteChord(field,Key.Left,ModifierKeys.Control);
            Check(status.Text.Contains("相同") && field.Content.ToString().Contains("F9"),"duplicate chord keeps old binding");
            field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<RadioButton>(main,"TabList").IsChecked=true;
            Check(!RegisterHotKey(hwnd,0x6401,0,0x78),"changing page cancels recording and restores registration");
            Find<RadioButton>(main,"TabKeys").IsChecked=true; main.UpdateLayout();
            Screenshot((FrameworkElement)main.Content,"shortcuts.png");
            Click(main,"DefaultKeys");
            if (allDefaultsAvailable)
                Check(field.Content.ToString()==oldKey,"restore Ctrl default bindings");
            else
                Check(status.Text.Contains("占用") && Equals(field.Content,"F9"),"restore preserves the active preference when defaults are occupied");
            Find<RadioButton>(main,"TabPlay").IsChecked=true;
            var linkField=Find<TextBox>(main,"LinkBox"); main.UpdateLayout(); linkField.Focus(); await Task.Delay(100);
            if (allDefaultsAvailable && GetForegroundWindow()==hwnd && linkField.IsKeyboardFocused) {
                Check(RegisterHotKey(hwnd,0x6403,2,0x20),"Ctrl+space is released while the link field has input focus");
                UnregisterHotKey(hwnd,0x6403);
                linkField.Clear();
                PostMessage(hwnd,0x0102,new IntPtr('/'),IntPtr.Zero);
                PostMessage(hwnd,0x0102,new IntPtr('0'),IntPtr.Zero);
                await Until(()=>linkField.Text=="/0",1800,"slash and zero type normally inside the guide link field");
                Find<Button>(main,"OpenLink").Focus(); await Task.Delay(120);
                Check(!RegisterHotKey(hwnd,0x6403,2,0x20),"global playback key re-arms after leaving text input");
            }
            await PlayerIn(main).ExecuteAsync("pause",0);
            const double restartAt = 9.25;
            await PlayerIn(main).ExecuteAsync("seek",restartAt);
            var head = (DockPanel)((Border)((DockPanel)video.Content).Children[0]).Child;
            head.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !video.IsVisible && video.Browser.CoreWebView2.Source=="about:blank",3000,
                "overlay close button unloads media and removes the video overlay");
            main.OpenLocal(Path.Combine(Program.BaseDir,"example.webm"));
            await Until(() => Find<Slider>(main,"Timeline").IsEnabled && Math.Abs(Find<Slider>(main,"Timeline").Value-restartAt)<.5,6000,
                "local video reopens near its saved playback position");
            Click(main,"LockButton"); Click(main,"CloseVideo");
            await Until(() => !video.IsVisible && video.Browser.CoreWebView2.Source=="about:blank",3000,"console can stop a locked overlay from another page");
            main.OpenLocal(Path.Combine(Program.BaseDir,"example.webm")); await Until(() => Find<Slider>(main,"Timeline").IsEnabled,4000,"reopen locked overlay");
            video.Close(); await Until(() => !video.IsVisible && !Find<Button>(main,"CloseVideo").IsEnabled,2500,"native window close dismisses the player");
            Click(main,"ExitApp"); await Until(()=>!main.IsVisible && !video.IsVisible,2500,"explicit exit closes all windows");
        }
        private static async Task TestCollections(MainWindow main, VideoOverlay overlay)
        {
            var parts = Find<ListBox>(main,"Episodes");
            var timeline = Find<Slider>(main,"Timeline");
            string url = "https://www.bilibili.com/video/BV1P6SNYREo8/?spm_id_from=333.1387.list.card_archive.click";
            Find<TextBox>(main,"LinkBox").Text = url; Click(main,"ImportLink");
            try {
                await Until(() => parts.Items.Count==20 && Find<RadioButton>(main,"TabList").IsChecked==true,22000,
                    "the supplied Natlan guide imports all 20 parts from the home screen");
                Check(((GuideEpisode)parts.Items[0]).Title.Contains("同步开图") && ((GuideEpisode)parts.Items[1]).Title.Contains("浮羽之湾"),
                    "first two episode titles match the supplied guide");
                await Until(() => timeline.IsEnabled && Math.Abs(timeline.Maximum-33)<2,35000,"import loads the correct first part");
                Hotkey(main,"previous");
                await Until(()=>parts.SelectedIndex==0 && Find<TextBlock>(main,"Status").Text.Contains("第一集"),2400,"previous action stays at first episode");
                main.WindowState=WindowState.Minimized;
                var keyTarget=new Window { Title="切集快捷键测试",Width=240,Height=100,ShowInTaskbar=false };
                keyTarget.Show();
                try {
                    await FocusTestWindow(keyTarget,keyTarget.PointToScreen(new Point(30,40)));
                    if (GetForegroundWindow()==new WindowInteropHelper(keyTarget).Handle && DesktopBindingRegistered(main,"next")) {
                        System.Windows.Forms.SendKeys.SendWait("^{PGDN}");
                        Console.WriteLine("Sent physical PageDown to the independent test window.");
                    } else Hotkey(main,"next");
                } finally { keyTarget.Close(); }
                await Until(() => parts.SelectedIndex==1 && timeline.IsEnabled && Math.Abs(timeline.Maximum-585)<2 && timeline.Value>0.1,
                    35000,"next episode plays P2 of the supplied guide while minimized");
                main.WindowState=WindowState.Normal;
                main.UpdateLayout();
                var viewport = Find<ScrollViewer>(main,"Scroll");
                Check(viewport.ScrollableHeight < 2 && parts.ActualHeight >= 64,"episode list scrolls independently with playback controls in view");
                Screenshot((FrameworkElement)main.Content,"collection.png");
                double originalHeight = main.Height;
                main.Height = Math.Min(originalHeight, 590); main.UpdateLayout();
                Check(viewport.ScrollableHeight < 2 && parts.ActualHeight >= 64,"collection actions remain visible in a shorter console");
                main.Height = originalHeight; main.UpdateLayout();
                string stored = Path.Combine(work,"profile","settings.json");
                Check(Settings.Load(stored).Collection.Selected==1 && Settings.Load(stored).Collection.Episodes.Count==20,
                    "real collection and selected episode saved");
                await PlayerIn(main).ExecuteAsync("seek",45);
                await Until(()=>timeline.IsEnabled && timeline.Value>=45.4,10000,"seek the supplied guide into its route");
                Find<TextBox>(main,"ListLinkBox").Text = "not-a-link"; Click(main,"ImportList"); await Task.Delay(100);
                Check(parts.Items.Count==20 && parts.SelectedIndex==1,"failed import preserves current catalogue");
                parts.SelectedIndex=7; Hotkey(main,"previous");
                await Until(() => parts.SelectedIndex==0 && timeline.IsEnabled && Math.Abs(timeline.Maximum-33)<2,30000,"previous episode follows the playing P2 even if another row is highlighted");
                Click(main,"CloseVideo"); await Until(() => !overlay.IsVisible && !timeline.IsEnabled,2000,"close from collection page");
                Find<TextBox>(main,"ListLinkBox").Text = "https://space.bilibili.com/431073645/lists/4455715?type=season";
                Click(main,"ImportList");
                await Until(() => parts.Items.Count>1 && Find<TextBlock>(main,"ListTitle").Text.Contains("绘想游迹"),25000,"UP space collection imports through its own input");
                Check(((GuideEpisode)parts.Items[0]).VideoId!=((GuideEpisode)parts.Items[1]).VideoId,"multi-video collection targets separate BVs");
                Find<TextBox>(main,"ListLinkBox").Text = url; Click(main,"ImportList"); Click(main,"CloseVideo");
                await Task.Delay(900);
                Check(!overlay.IsVisible && !timeline.IsEnabled && Find<TextBlock>(main,"ListTitle").Text.Contains("绘想游迹"),
                    "close cancels a pending import without reopening video or replacing the list");
                var series=await CollectionImporter.ImportAsync(new Uri("https://space.bilibili.com/1773346/lists/449853?type=series"),CancellationToken.None);
                Check(series.Episodes.Count>30 && series.Episodes.Select(e=>e.Key).Distinct().Count()==series.Episodes.Count,"all pages of a public series import in order");
                Find<RadioButton>(main,"TabPlay").IsChecked=true;
                Find<TextBox>(main,"LinkBox").Text="https://www.bilibili.com/video/BV1P6SNYREo8/?p=2"; Click(main,"OpenLink");
                await Until(()=>timeline.IsEnabled && Math.Abs(timeline.Maximum-585)<2,30000,"direct link opens P2 while a different catalogue is saved");
                Hotkey(main,"previous");
                await Until(()=>parts.Items.Count==20 && parts.SelectedIndex==0 && timeline.IsEnabled && Math.Abs(timeline.Maximum-33)<2,30000,
                    "direct-link previous hotkey resolves this guide's own collection and plays P1");
                Find<TextBox>(main,"LinkBox").Text="https://www.bilibili.com/video/BV1P6SNYREo8/?p=2";
                Click(main,"OpenLink");
                await Until(()=>timeline.IsEnabled && Math.Abs(timeline.Maximum-585)<2,30000,"P2 is ready to verify its saved progress");
                await PlayerIn(main).ExecuteAsync("pause",0);
                await PlayerIn(main).ExecuteAsync("seek",47.25);
                Click(main,"CloseVideo");
                await Until(()=>!timeline.IsEnabled && !Find<Button>(main,"CloseVideo").IsEnabled,3000,"close after a seek");
                Find<TextBox>(main,"LinkBox").Text="https://www.bilibili.com/video/BV1P6SNYREo8/?p=2&t=67";
                Click(main,"OpenLink");
                await Until(()=>timeline.IsEnabled && Math.Abs(timeline.Value-47.25)<.6,30000,
                    "resume uses the final saved position ahead of the shared start time");
            } finally { Console.WriteLine("Collection status " + Find<TextBlock>(main,"Status").Text); }
        }
        [STAThread]
        public static int Main(string[] args)
        {
            Directory.CreateDirectory(work);
            try
            {
                CoreChecks();
                string mode = args.FirstOrDefault() ?? "--core";
                if (mode != "--core")
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    Program.ApplyTheme(app);
                    var profile = Path.Combine(work,"profile");
                    if (mode == "--game") PrepareLaunchCheck(profile);
                    var main = new MainWindow(profile);
                    Exception error = null;
                    main.Loaded += async (s,e) => {
                        try { await Task.Delay(200); await Desktop(main,mode); }
                        catch (Exception ex) { error = ex; }
                        finally { if (main.IsLoaded) main.Close(); }
                    };
                    app.Run(main);
                    if (error != null) throw error;
                }
                Console.WriteLine(count + " checks passed."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
        }
    }
}
