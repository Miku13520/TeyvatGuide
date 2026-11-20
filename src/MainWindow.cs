using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TeyvatGuide
{
    internal sealed partial class MainWindow : Window
    {
        private readonly FrameworkElement root;
        private readonly string settingsFile;
        private readonly Settings settings;
        private readonly VideoOverlay video;
        private readonly GuidePlayer player;
        private readonly GameForeground gameForeground = new GameForeground();
        private readonly DispatcherTimer poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        private readonly DispatcherTimer keyPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        private readonly DispatcherTimer save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        private Shortcuts shortcuts;
        private CancellationTokenSource opening;
        private Task finalProgress;
        private bool polling, closing, positioning, editingTime, updatingTime;
        private int commandVersion;
        private BiliLink activeGuide;
        private MediaInfo media;
        private DateTime openedAt;
        private bool reportedWait;
        private bool? pendingPlayback;

        public MainWindow(string storage)
        {
            Title = "提瓦特随行 · 原神攻略跟跑助手";
            Program.UseDarkCaption(this);
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"); FontSize = 14;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Foreground = (Brush)FindResource("Ink");
            Background = (Brush)FindResource("Shell");
            Width = Math.Min(1000, SystemParameters.WorkArea.Width);
            Height = Math.Min(758, SystemParameters.WorkArea.Height);
            MinWidth = Math.Min(760, SystemParameters.WorkArea.Width); MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            settingsFile = Path.Combine(storage, "settings.json");
            settings = Settings.Load(settingsFile);
            root = (FrameworkElement)XamlReader.Parse(Program.Resource("Main.xaml"));
            Content = root;
            using (var cover = typeof(Program).Assembly.GetManifestResourceStream("travel-banner.jpg"))
            {
                var picture = BitmapFrame.Create(cover, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                picture.Freeze();
                var art = UI<Border>("CoverArt");
                RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
                art.Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top };
            }
            video = new VideoOverlay();
            player = new GuidePlayer(video.Browser, Path.Combine(storage, "WebView2"));
            WirePlayback(); WireSettings(); WireKeys(); WireNavigation(); WireCollection(); WireGameLaunch();
            video.DismissRequested += CloseVideo;
            Click("ExitApp", Close);
            save.Tick += (s, e) => { save.Stop(); SaveNow(); };
            poll.Tick += async (s, e) => await PollAsync();
            keyPoll.Tick += (s,e) => shortcuts.UpdateGameFocus();
            Loaded += (s, e) => {
                PositionWindows(false);
                shortcuts = new Shortcuts(this, DispatchAction, gameForeground.GameWindow);
                string conflict = shortcuts.Register(settings.Keys);
                if (conflict != null) Status(conflict + "可在设置中更换快捷键。");
                UpdateTypingKeys();
                poll.Start(); keyPoll.Start();
            };
            Closing += CloseApplication;
        }
        private async void CloseApplication(object sender,System.ComponentModel.CancelEventArgs e)
        {
            if (!closing) {
                closing = true; poll.Stop(); keyPoll.Stop(); save.Stop();
                if (opening!=null) opening.Cancel();
                if (shortcuts!=null) shortcuts.Dispose();
                finalProgress=CaptureLastPosition();
                if (!finalProgress.IsCompleted) {
                    e.Cancel=true;
                    await finalProgress;
                    await Dispatcher.InvokeAsync(new Action(()=>{ if (IsLoaded) Close(); }));
                    return;
                }
            } else if (finalProgress!=null && !finalProgress.IsCompleted) { e.Cancel=true;return; }
            SaveNow(); player.Dispose();
            video.AllowClose=true;
            video.Close();
        }
        private T UI<T>(string name) where T : FrameworkElement { return (T)root.FindName(name); }
        private void Click(string name, Action action) { UI<Button>(name).Click += (s, e) => action(); }
        private void Status(string value) { if (!closing) UI<TextBlock>("Status").Text = value; }
        private void QueueSave() { if (!closing) { save.Stop(); save.Start(); } }
        private void SaveNow()
        {
            try { settings.Save(settingsFile); }
            catch (IOException) { Status("设置暂时无法写入，请检查磁盘空间。"); }
            catch (UnauthorizedAccessException) { Status("当前账户无法写入设置目录。"); }
        }

        private void WireNavigation()
        {
            var pages = new[] { "Play", "List", "Keys" };
            foreach (string page in pages)
            {
                string selected = page;
                UI<RadioButton>("Tab" + selected).Checked += (s, e) => {
                    if (recording != null) CancelRecording();
                    foreach (string item in pages)
                        UI<FrameworkElement>(item + "Page").Visibility = item == selected ? Visibility.Visible : Visibility.Collapsed;
                    UI<ScrollViewer>("Scroll").ScrollToTop();
                };
            }
        }
        private void WirePlayback()
        {
            UI<TextBox>("LinkBox").Text = settings.LastLink ?? "";
            UI<TextBox>("LinkBox").KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; OpenLink(false); } };
            Click("OpenLink", () => OpenLink(false));
            Click("CloseVideo", CloseVideo);
            Click("OpenFile", () => {
                var dialog = new OpenFileDialog { Title = "选择攻略视频", Filter = "视频文件|*.mp4;*.webm;*.m4v;*.mov;*.ogv" };
                if (ShowInputDialog(() => dialog.ShowDialog(this)) == true) OpenLocal(dialog.FileName);
            });
            Click("TogglePlay", () => DispatchAction("toggle")); Click("Back", () => DispatchAction("back"));
            Click("Forward", () => DispatchAction("forward")); Click("Slower", () => DispatchAction("slower"));
            Click("Faster", () => DispatchAction("faster")); Click("LockButton", () => DispatchAction("lock"));
            var slider = UI<Slider>("Timeline");
            slider.PreviewMouseLeftButtonDown += (s, e) => { editingTime = true; };
            slider.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(async (s, e) => {
                if (!editingTime) return;
                editingTime = false; await CommandAsync("seek", slider.Value);
            }), true);
            slider.LostMouseCapture += (s, e) => {
                if (editingTime && Mouse.LeftButton == MouseButtonState.Released)
                { editingTime = false; DispatchSeek(slider.Value); }
            };
            slider.ValueChanged += (s, e) => {
                if (!updatingTime && !editingTime && slider.IsKeyboardFocusWithin && Mouse.LeftButton != MouseButtonState.Pressed)
                    DispatchSeek(slider.Value);
            };
        }
        private async void DispatchSeek(double value) { await CommandAsync("seek", value); }
        private CancellationToken NewRequest()
        {
            if (opening != null) { opening.Cancel(); opening.Dispose(); }
            opening = new CancellationTokenSource();
            return opening.Token;
        }
        private async void OpenLink(bool import)
        {
            string input = UI<TextBox>(UI<RadioButton>("TabList").IsChecked == true ? "ListLinkBox" : "LinkBox").Text.Trim();
            if (input.Length == 0)
            {
                if (import) UI<RadioButton>("TabList").IsChecked = true;
                UI<TextBox>(import ? "ListLinkBox" : "LinkBox").Focus();
                Status(import ? "粘贴任意一集或 UP 主的合集链接，然后点“导入合集”。" : "请先粘贴一个 B 站链接。");
                return;
            }
            var token = NewRequest();
            UI<Button>("CloseVideo").IsEnabled = true;
            Status(import ? "正在读取 B 站分集目录…" : "正在打开 B 站攻略…");
            try
            {
                var address = await BiliLink.ResolveAddressAsync(input, token);
                if (token.IsCancellationRequested) return;
                var link = BiliLink.ParseVideo(address);
                if (import || link == null) await ImportCollectionAsync(address, token);
                else await OpenAsync(link, null, token);
            }
            catch (Exception ex) { if (!token.IsCancellationRequested) OpenError(ex, import ? "导入失败。" : "打开失败。"); }
        }
        internal async void OpenLocal(string file)
        {
            var token = NewRequest();
            try { await OpenAsync(null, file, token); }
            catch (Exception ex) { if (!token.IsCancellationRequested) OpenError(ex); }
        }
        private void OpenError(Exception ex, string prefix = "打开失败。")
        {
            if (!player.HasSource && video.IsVisible) video.Hide();
            UI<Button>("CloseVideo").IsEnabled = player.HasSource;
            Status(ex is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException
                ? "未找到 Edge WebView2 运行时。请安装微软的 Evergreen x64 运行时，再重新打开攻略。"
                : prefix + (ex is TaskCanceledException ? "B 站响应超时，请重试。" : ex.Message));
        }
        private async Task OpenAsync(BiliLink link, string file, CancellationToken token, string episodeTitle = null)
        {
            if (token.IsCancellationRequested) return;
            UI<Button>("CloseVideo").IsEnabled = true;
            if (!video.IsVisible) video.Show();
            await player.EnsureReadyAsync();
            if (token.IsCancellationRequested || closing) return;
            await CaptureLastPosition();
            if (token.IsCancellationRequested || closing) return;
            string key=link!=null ? link.Key : "file/"+Path.GetFullPath(file).ToUpperInvariant()+"/"+File.GetLastWriteTimeUtc(file).Ticks;
            double saved;
            double? restore=key!=null && settings.PlaybackPositions.TryGetValue(key,out saved) &&
                saved>=0 && saved<TimeSpan.MaxValue.TotalSeconds ? (double?)saved : null;
            if (link != null) player.OpenBili(restore.HasValue ? BiliLink.Create(link.Id,link.Part,restore.Value) : link);
            else player.OpenFile(file);
            progressKey=key; resumeTime=restore; lastResumeAttempt=DateTime.MinValue;
            resumeAttempts=0; nextProgressSave=DateTime.UtcNow.AddSeconds(5);
            string caption = link != null ? episodeTitle ?? link.Caption : Path.GetFileName(file);
            activeGuide = link;
            if (link != null) settings.LastLink = link.Address;
            media = null; pendingPlayback = null;
            video.SetCaption(caption); UI<TextBlock>("NowPlaying").Text = caption;
            UI<TextBlock>("NowPlaying").ToolTip = caption;
            UI<TextBlock>("PlayerState").Text = link != null ? "哔哩哔哩   /   第 " + link.Part + " P" : "本地视频";
            UI<WrapPanel>("Transport").IsEnabled = true;
            UI<Slider>("Timeline").IsEnabled = false; UI<TextBlock>("Clock").Text = "00:00 / 00:00";
            UI<Button>("TogglePlay").Content = "播放"; UI<TextBlock>("Rate").Text = "1×";
            openedAt = DateTime.UtcNow; reportedWait = false; QueueSave();
            Status(link != null ? "正在加载 B 站播放器。首次播放也可在悬浮窗内点击。" :
                "已打开本地视频。");
        }
        private async void CloseVideo()
        {
            if (closing) return;
            var token=NewRequest();
            await CaptureLastPosition();
            if (closing || token.IsCancellationRequested) return;
            player.Stop(); if (video.IsVisible) video.Hide();
            if (progressKey!=null) SaveNow();
            progressKey=null; resumeTime=null;
            activeGuide = null; media = null; pendingPlayback = null; editingTime = false;
            updatingTime = true;
            var timeline = UI<Slider>("Timeline"); timeline.IsEnabled = false; timeline.Value = 0; timeline.Maximum = 1;
            updatingTime = false;
            UI<WrapPanel>("Transport").IsEnabled = false; UI<Button>("CloseVideo").IsEnabled = false;
            UI<TextBlock>("NowPlaying").Text = "攻略播放"; UI<TextBlock>("NowPlaying").ToolTip = null;
            UI<TextBlock>("PlayerState").Text = "等待打开视频";
            UI<TextBlock>("Clock").Text = "00:00 / 00:00"; UI<TextBlock>("Rate").Text = "1×";
            UI<Button>("TogglePlay").Content = "播放"; Status("视频已关闭。");
        }
        private void WireSettings()
        {
            WireSlider("VideoAlpha", settings.VideoOpacity, "%", v => { settings.VideoOpacity = v; video.Opacity = v / 100; });
            WireSlider("VideoScale", settings.VideoWidth, " 像素", v => { settings.VideoWidth = v; video.SetWidth(v); });
            video.LocationChanged += (s, e) => {
                if (positioning) { settings.VideoLeft = video.Left; settings.VideoTop = video.Top; QueueSave(); }
            };
            Click("ResetPositions", () => { PositionWindows(true); Status("视频窗已移回主屏幕。"); });
            SetLocked(settings.Locked);
            UI<TextBox>("SeekBox").Text = settings.SeekSeconds.ToString(); UpdateSeekLabels();
        }
        private void WireSlider(string name, double initial, string unit, Action<double> apply)
        {
            var slider = UI<Slider>(name);
            slider.Value = initial;
            Action sync = () => {
                apply(slider.Value); UI<TextBlock>(name + "Text").Text = Math.Round(slider.Value) + unit;
            };
            sync(); slider.ValueChanged += (s,e) => { sync(); QueueSave(); };
        }
        private void PositionWindows(bool reset)
        {
            positioning = false;
            video.Place(reset ? null : settings.VideoLeft, reset ? null : settings.VideoTop);
            settings.VideoLeft = video.Left; settings.VideoTop = video.Top;
            positioning = true;
            if (reset) QueueSave();
        }
        private void UpdateSeekLabels()
        {
            UI<Button>("Back").Content = "快退 " + settings.SeekSeconds + " 秒";
            UI<Button>("Forward").Content = "快进 " + settings.SeekSeconds + " 秒";
        }
        private void SetLocked(bool locked)
        {
            settings.Locked = locked; video.SetLocked(locked);
            player.CleanPicture=locked;
            string action = locked ? "解锁视频窗" : "锁定视频窗";
            UI<TextBlock>("LockText").Text = action;
            UI<TextBlock>("LockGlyph").Text = locked ? "\uE785" : "\uE72E";
            var button = UI<Button>("LockButton");
            button.ToolTip = locked ? "解锁后可拖动和操作视频窗" : "锁定后鼠标穿透到游戏";
            AutomationProperties.SetName(button, action);
            QueueSave();
        }
        private async void DispatchAction(string action)
        {
            if (closing || recording != null) return;
            if (action == "lock") { SetLocked(!settings.Locked); return; }
            if (action == "previous" || action == "next") { await StepPlayingEpisode(action == "next" ? 1 : -1); return; }
            await CommandAsync(action, settings.SeekSeconds);
        }
        private async Task CommandAsync(string action, double argument)
        {
            if (!player.HasSource) { Status("先打开一个攻略视频。"); return; }
            if (closing) return;
            if ((media == null || !media.ready) && (action == "play" || action == "toggle" || action == "pause"))
            {
                pendingPlayback = action == "toggle" ? !(pendingPlayback ?? (media != null && !media.paused)) : action == "play";
                Status(pendingPlayback.Value ? "视频加载后将开始播放。" : "视频加载后将保持暂停。");
                return;
            }
            int generation = player.Generation;
            bool seeking=action=="back" || action=="forward" || action=="seek";
            int version = ++commandVersion;
            try
            {
                var state = await player.ExecuteAsync(action, argument);
                if (closing || generation != player.Generation || version != commandVersion) return;
                if (state == null) Status("播放器仍在加载。可解锁悬浮窗查看站内提示。");
                else {
                    if (seeking && state.duration>0) { resumeTime=null; RememberPlayback(state,true); }
                    ShowMedia(state); if (!string.IsNullOrEmpty(state.issue)) Status(state.issue);
                }
            }
            catch (Exception ex) { if (!closing) Status("播放操作失败。" + ex.Message); }
        }
        private async Task PollAsync()
        {
            if (closing || polling || !player.HasSource) return;
            polling = true;
            int version = commandVersion, generation = player.Generation;
            try
            {
                var state = await player.ExecuteAsync("read", null);
                if (closing || version != commandVersion || generation != player.Generation) return;
                if (state == null)
                {
                    if (!string.IsNullOrEmpty(player.NavigationError)) Status(player.NavigationError);
                    else if (!reportedWait && DateTime.UtcNow - openedAt > TimeSpan.FromSeconds(12))
                    {
                        reportedWait = true;
                        Status("尚未检测到视频。请解锁悬浮窗查看 B 站播放提示。");
                    }
                    return;
                }
                if (await WaitForResume(state)) return;
                if (closing || version != commandVersion || generation != player.Generation) return;
                ShowMedia(state);
                if (!string.IsNullOrEmpty(state.issue)) Status(state.issue);
                else if (state.ready && !reportedWait)
                {
                    reportedWait = true;
                    Status("视频已就绪。");
                }
                else if (!reportedWait && DateTime.UtcNow - openedAt > TimeSpan.FromSeconds(12))
                {
                    reportedWait = true;
                    Status("尚未收到可播放画面。可解锁悬浮窗查看提示。");
                }
                if (state.ready && pendingPlayback.HasValue)
                {
                    bool requested = pendingPlayback.Value;
                    pendingPlayback = null;
                    await CommandAsync(requested ? "play" : "pause", 0);
                }
            }
            catch (Exception ex)
            {
                if (!closing && generation == player.Generation) Status("读取播放器失败。" + ex.Message);
            }
            finally { polling = false; }
        }
        private void ShowMedia(MediaInfo state)
        {
            media = state;
            RememberPlayback(state);
            UI<TextBlock>("Clock").Text = ClockText(media.time) + " / " + ClockText(media.duration);
            UI<TextBlock>("Rate").Text = media.rate.ToString("0.##") + "×";
            UI<Button>("TogglePlay").Content = media.paused ? "播放" : "暂停";
            var slider = UI<Slider>("Timeline");
            updatingTime = true;
            slider.IsEnabled = media.ready && media.duration > 0;
            slider.Maximum = Math.Max(1, media.duration);
            if (!editingTime) slider.Value = media.time;
            updatingTime = false;
        }
        private static string ClockText(double value)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, value));
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + t.ToString(@"\:mm\:ss") : t.ToString(@"mm\:ss");
        }
    }
}
