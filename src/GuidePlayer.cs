using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TeyvatGuide
{
    internal sealed class MediaInfo
    {
        public double time { get; set; }
        public double duration { get; set; }
        public double rate { get; set; }
        public bool ready { get; set; }
        public bool paused { get; set; }
        public bool seeking { get; set; }
        public string issue { get; set; }
    }

    internal sealed class GuidePlayer : IDisposable
    {
        private readonly WebView2CompositionControl view;
        private readonly string profile;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly string script = Program.Resource("media.js");
        private CoreWebView2Frame frame;
        private string target;
        private bool online, disposed;
        private Task initialization;
        public int Generation { get; private set; }
        public bool CleanPicture { get; set; }
        public string NavigationError { get; private set; }
        public bool HasSource { get { return target != null; } }
        private CoreWebView2 Core { get { return view.CoreWebView2; } }
        public GuidePlayer(WebView2CompositionControl view, string profile) { this.view = view; this.profile = profile; }

        private async Task InitializeAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            if (disposed) throw new ObjectDisposedException("播放器");
            await view.EnsureCoreWebView2Async(env);
            Core.Settings.AreHostObjectsAllowed = false;
            Core.Settings.IsWebMessageEnabled = false;
            Core.Settings.IsZoomControlEnabled = false;
            Core.Settings.AreDefaultContextMenusEnabled = false;
            Core.SetVirtualHostNameToFolderMapping("guide-player.example", Path.Combine(Program.BaseDir, "web"),
                                                   CoreWebView2HostResourceAccessKind.Deny);
            // Desktop buttons and global hotkeys live outside the web document.
            foreach (string origin in new[] { "https://guide-player.example", "https://player.bilibili.com" })
                await Core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Autoplay, origin, CoreWebView2PermissionState.Allow);
            Core.NewWindowRequested += (s, e) => { e.Handled = true; };
            Core.PermissionRequested += (s, e) => {
                Uri origin;
                bool playback = e.PermissionKind == CoreWebView2PermissionKind.Autoplay && Uri.TryCreate(e.Uri, UriKind.Absolute, out origin)
                    && (origin.IsFile || (origin.Scheme == "https" && (origin.Host == "guide-player.example" || origin.Host == "player.bilibili.com")));
                e.State = playback ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            };
            Core.DownloadStarting += (s, e) => { e.Cancel = true; };
            Core.NavigationStarting += (s, e) => {
                if (e.Uri != target && e.Uri != "about:blank") e.Cancel = true;
            };
            Core.NavigationCompleted += (s, e) => {
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    NavigationError = "播放器加载失败（" + e.WebErrorStatus + "）。请检查网络后重新打开。";
            };
            Core.FrameCreated += (s, e) => {
                var created = e.Frame;
                if (!online || created.Name != "guide") return;
                frame = created;
                created.NavigationCompleted += (sender, nav) => {
                    if (frame == created && !nav.IsSuccess && nav.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        NavigationError = "B 站播放器加载失败（" + nav.WebErrorStatus + "）。请检查网络后重新打开。";
                };
                created.Destroyed += (sender, args) => { if (frame == created) frame = null; };
            };
        }
        public async Task EnsureReadyAsync()
        {
            if (initialization == null) initialization = InitializeAsync();
            try { await initialization; }
            catch { initialization = null; throw; }
        }
        public void OpenBili(BiliLink link)
        {
            Navigate("https://guide-player.example/embed.html?" + link.Query, true);
        }
        public void OpenFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到视频文件。", path);
            Navigate(new Uri(Path.GetFullPath(path)).AbsoluteUri, false);
        }
        public void Stop()
        {
            Generation++; online = false; frame = null; NavigationError = null; target = null;
            if (disposed || Core == null) return;
            Core.IsMuted = true;
            Core.Stop();
            Core.Navigate("about:blank");
        }
        private void Navigate(string uri, bool isOnline)
        {
            Generation++; online = isOnline; frame = null; NavigationError = null; target = uri;
            // A new document is also required when reopening the same video.
            Core.Navigate(uri);
            Core.IsMuted = false;
        }
        private async Task<string> EvaluateAsync(string action, object argument)
        {
            if (!HasSource || disposed || Core == null || Core.Source != target) return null;
            int generation = Generation;
            var current = frame;
            if (online && current == null) return null;
            string call = script + "(" + json.Serialize(action) + "," + json.Serialize(argument) + "," + json.Serialize(CleanPicture) + ")";
            string result;
            try { result = online ? await current.ExecuteScriptAsync(call) : await Core.ExecuteScriptAsync(call); }
            catch (InvalidOperationException) { if (disposed || generation != Generation || current != frame) return null; throw; }
            return disposed || generation != Generation || result == "null" || string.IsNullOrEmpty(result) ? null : result;
        }
        public async Task<MediaInfo> ExecuteAsync(string action, object argument)
        {
            string result = await EvaluateAsync(action, argument);
            return result == null ? null : json.Deserialize<MediaInfo>(result);
        }
        public void Dispose() { disposed = true; Generation++; view.Dispose(); }
    }
}
