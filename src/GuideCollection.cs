using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace TeyvatGuide
{
    internal sealed class CollectionAddress
    {
        public long Mid, ListId;
        public string Kind;
        public string SelectedVideo;
        public static CollectionAddress Parse(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                (uri.Scheme != "https" && uri.Scheme != "http")) return null;
            string path = uri.AbsolutePath.TrimEnd('/');
            var q = HttpUtility.ParseQueryString(uri.Query);
            Match match;
            string kind = q["type"], mid, id;
            if (uri.Host == "space.bilibili.com")
            {
                match = Regex.Match(path, @"^/(\d+)/(?:lists/(\d+)|channel/(collectiondetail|seriesdetail))$");
                if (!match.Success) return null;
                mid = match.Groups[1].Value;
                id = match.Groups[2].Success ? match.Groups[2].Value : q["sid"];
                if (match.Groups[3].Success) kind = match.Groups[3].Value == "seriesdetail" ? "series" : "season";
            }
            else if (uri.Host == "www.bilibili.com" || uri.Host == "bilibili.com")
            {
                match = Regex.Match(path, @"^/(list|medialist/play)/(\d+)$");
                if (!match.Success) return null;
                mid = match.Groups[2].Value; id = q["sid"] ?? q["business_id"];
                if (q["business"] == "space_series") kind = "series";
                if (q["business"] == "space_collection") kind = "season";
            }
            else return null;
            long owner, list;
            if (!long.TryParse(mid, NumberStyles.None, CultureInfo.InvariantCulture, out owner) || owner <= 0 ||
                !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out list) || list <= 0) return null;
            if (kind != null && kind != "season" && kind != "series") return null;
            return new CollectionAddress { Mid = owner, ListId = list, Kind = kind, SelectedVideo = q["bvid"] };
        }
    }

    internal sealed class GuideEpisode
    {
        public string VideoId { get; set; }
        public int Part { get; set; }
        public string Title { get; set; }
        public int Seconds { get; set; }
        [ScriptIgnore] public int Number { get; set; }
        [ScriptIgnore] public string Ordinal { get { return Number.ToString("00"); } }
        [ScriptIgnore] public string Key { get { return VideoId + "/p" + Part; } }
        [ScriptIgnore] public string LengthText { get { return Seconds <= 0 ? "—" : TimeSpan.FromSeconds(Seconds).ToString(Seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss"); } }
    }

    internal sealed class GuideCollection
    {
        public string Title { get; set; }
        public List<GuideEpisode> Episodes { get; set; }
        public int Selected { get; set; }
        [ScriptIgnore] public double SharedStart { get; set; }
        public GuideCollection() { Episodes = new List<GuideEpisode>(); }
        public void Prepare()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Episodes = (Episodes ?? new List<GuideEpisode>()).Where(e => e != null && BiliLink.ValidId(e.VideoId) &&
                e.Part > 0 && seen.Add(e.Key)).ToList();
            for (int i = 0; i < Episodes.Count; i++) Episodes[i].Number = i + 1;
            Selected = Math.Max(0, Math.Min(Selected, Episodes.Count - 1));
        }
    }

    internal static class CollectionImporter
    {
        private sealed class Page { public int page { get; set; } public string part { get; set; } public int duration { get; set; } }
        private sealed class Archive { public long aid { get; set; } public string bvid { get; set; } public string title { get; set; } public int duration { get; set; } }
        private sealed class SeasonEpisode
        {
            public long aid { get; set; } public string bvid { get; set; } public string title { get; set; }
            public Page[] pages { get; set; } public Page page { get; set; } public Archive arc { get; set; }
        }
        private sealed class Section { public SeasonEpisode[] episodes { get; set; } }
        private sealed class Season { public string title { get; set; } public Section[] sections { get; set; } }
        private sealed class Video
        {
            public string title { get; set; } public string bvid { get; set; } public int duration { get; set; }
            public Page[] pages { get; set; } public Season ugc_season { get; set; }
        }
        private sealed class Meta { public long mid { get; set; } public string name { get; set; } }
        private sealed class Pagination { public int total { get; set; } }
        private sealed class ArchiveList
        {
            public Meta meta { get; set; } public Pagination page { get; set; } public Archive[] archives { get; set; }
        }
        private sealed class Reply<T> { public int code { get; set; } public string message { get; set; } public T data { get; set; } }
        private sealed class DirectoryError : Exception
        {
            public readonly int Code;
            public DirectoryError(int code, string message) : base("B 站目录返回 " + code + "（" + message + "）。") { Code = code; }
        }
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler {
            UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = TimeSpan.FromSeconds(15) };
        private static T Decode<T>(string body) where T : class
        {
            var reply = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Deserialize<Reply<T>>(body);
            if (reply == null) throw new FormatException("B 站没有返回目录。");
            if (reply.code != 0) throw new DirectoryError(reply.code, reply.message);
            if (reply.data == null) throw new FormatException("B 站未返回可用的分集目录。");
            return reply.data;
        }
        private static async Task<string> ReadAsync(string path, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bilibili.com/" + path))
            {
                request.Headers.Referrer = new Uri("https://www.bilibili.com/");
                using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested(); return json;
                }
            }
        }
        private static void AddPages(GuideCollection list, string id, string title, Page[] pages, int seconds, bool withinSeason)
        {
            if (!BiliLink.ValidId(id)) return;
            if (pages == null || pages.Length == 0)
            {
                list.Episodes.Add(new GuideEpisode { VideoId = id, Part = 1, Title = title, Seconds = seconds }); return;
            }
            foreach (var p in pages.Where(p => p != null && p.page > 0).OrderBy(p => p.page))
            {
                string part = string.IsNullOrWhiteSpace(p.part) ? "第 " + p.page + " P" : p.part;
                string caption = withinSeason && pages.Length > 1 ? title + " · " + part : pages.Length == 1 ? title : part;
                list.Episodes.Add(new GuideEpisode { VideoId = id, Part = p.page, Title = caption, Seconds = p.duration });
            }
        }
        internal static GuideCollection FromVideo(string json, BiliLink requested)
        {
            var video = Decode<Video>(json);
            var list = new GuideCollection { Title = video.title };
            var season = video.ugc_season;
            if (season != null && season.sections != null)
            {
                foreach (var section in season.sections.Where(s => s != null && s.episodes != null))
                    foreach (var e in section.episodes.Where(e => e != null))
                    {
                        var pages = e.pages != null && e.pages.Length > 0 ? e.pages : e.page != null ? new[] { e.page } : null;
                        AddPages(list, e.bvid ?? "av" + e.aid, e.title, pages, e.arc == null ? 0 : e.arc.duration, true);
                    }
                if (list.Episodes.Count > 0) list.Title = season.title ?? video.title;
            }
            if (list.Episodes.Count == 0)
                AddPages(list, video.bvid ?? requested.Id, video.title, video.pages, video.duration, false);
            list.Prepare();
            int selected = list.Episodes.FindIndex(e => e.VideoId == (video.bvid ?? requested.Id) && e.Part == requested.Part);
            list.Selected = Math.Max(0, selected);
            list.SharedStart = selected >= 0 ? requested.Start : 0;
            return list;
        }
        public static async Task<GuideCollection> ImportAsync(Uri address, CancellationToken token)
        {
            var video = BiliLink.ParseVideo(address);
            if (video != null)
            {
                string key = video.Id.StartsWith("BV") ? "bvid=" + video.Id : "aid=" + video.Id.Substring(2);
                return FromVideo(await ReadAsync("x/web-interface/view?" + key, token).ConfigureAwait(false), video);
            }
            var list = CollectionAddress.Parse(address);
            if (list == null) throw new FormatException("请粘贴视频页或 UP 主空间里的合集链接。");
            if (list.Kind != null) return await ReadSpaceAsync(list, list.Kind, token).ConfigureAwait(false);
            try {
                var season = await ReadSpaceAsync(list, "season", token).ConfigureAwait(false);
                if (season.Episodes.Count > 0) return season;
            }
            catch (DirectoryError e) { if (e.Code != -404 && e.Code != -400) throw; }
            return await ReadSpaceAsync(list, "series", token).ConfigureAwait(false);
        }
        private static async Task<GuideCollection> ReadSpaceAsync(CollectionAddress address, string kind, CancellationToken token)
        {
            var list = new GuideCollection();
            string ids = "mid=" + address.Mid + "&";
            bool series = kind == "series";
            if (series)
            {
                var meta = Decode<ArchiveList>(await ReadAsync("x/series/series?series_id=" + address.ListId, token).ConfigureAwait(false)).meta;
                if (meta == null || meta.mid != address.Mid) throw new FormatException("系列与链接中的 UP 主不一致。");
                list.Title = meta.name;
            }
            string path = series ? "x/series/archives?" + ids + "series_id=" + address.ListId + "&only_normal=true&ps=30&pn=" :
                "x/polymer/web-space/seasons_archives_list?" + ids + "season_id=" + address.ListId + "&page_size=30&page_num=";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int received = 0;
            for (int number = 1; ; number++)
            {
                token.ThrowIfCancellationRequested();
                var page = Decode<ArchiveList>(await ReadAsync(path + number, token).ConfigureAwait(false));
                if (page.meta != null && !string.IsNullOrWhiteSpace(page.meta.name)) list.Title = page.meta.name;
                var entries = page.archives ?? new Archive[0];
                int previous = seen.Count;
                foreach (var item in entries.Where(e => e != null && BiliLink.ValidId(e.bvid) && seen.Add(e.bvid)))
                    AddPages(list, item.bvid, item.title, null, item.duration, false);
                received += entries.Length;
                if (page.page != null ? received >= page.page.total : entries.Length < 30) break;
                if (previous == seen.Count) throw new FormatException("B 站返回的目录不完整，请稍后重新导入。");
            }
            list.Prepare(); list.Title = list.Title ?? "B 站合集";
            list.Selected = Math.Max(0, list.Episodes.FindIndex(e => e.VideoId == address.SelectedVideo));
            return list;
        }
    }
}
