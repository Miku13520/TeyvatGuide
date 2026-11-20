using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace TeyvatGuide
{
    internal sealed class BiliLink
    {
        private const string IdPattern = @"(?:BV[0-9A-Za-z]{10}|av[0-9]{1,18})";
        private static readonly Regex VideoPath = new Regex("^/video/(" + IdPattern + ")/?$", RegexOptions.IgnoreCase);
        private static readonly Regex ShareUrl = new Regex(@"https?://[^\s<>""'【】。，（）()\[\]]+", RegexOptions.IgnoreCase);
        public string Id { get; private set; }
        public int Part { get; private set; }
        public double Start { get; private set; }
        public string Key { get { return Id + "/p" + Part; } }
        public string Caption { get { return Id + "  /  第 " + Part + " P"; } }
        public string Address { get { return "https://www.bilibili.com/video/" + Id + "/?p=" + Part + "&t=" + Start.ToString("0.###", CultureInfo.InvariantCulture); } }
        public static bool ValidId(string id) { return id != null && Regex.IsMatch(id, "^" + IdPattern + "$", RegexOptions.IgnoreCase); }
        public static BiliLink Create(string id, int part, double start)
        {
            if (!ValidId(id)) throw new FormatException("视频编号无效。");
            return new BiliLink {
                Id = (id.StartsWith("BV", StringComparison.OrdinalIgnoreCase) ? "BV" : "av") + id.Substring(2),
                Part = Math.Max(1, part), Start = double.IsInfinity(start) || double.IsNaN(start) ? 0 : Math.Max(0, start)
            };
        }
        public string Query
        {
            get
            {
                string id = Id.StartsWith("BV") ? "bvid=" + Id : "aid=" + Id.Substring(2);
                return id + "&p=" + Part + "&t=" + Start.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        private static bool IsShort(Uri uri)
        {
            return uri.Host.Equals("b23.tv", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoHost(Uri uri)
        {
            return uri.Host == "www.bilibili.com" || uri.Host == "m.bilibili.com" ||
                   uri.Host == "bilibili.com" || uri.Host == "player.bilibili.com";
        }

        public static BiliLink ParseVideo(Uri uri)
        {
            if (!IsVideoHost(uri) || (uri.Scheme != "https" && uri.Scheme != "http") ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0) return null;
            var query = HttpUtility.ParseQueryString(uri.Query);
            string id = VideoPath.Match(uri.AbsolutePath).Groups[1].Value;
            if (uri.Host == "player.bilibili.com" && uri.AbsolutePath == "/player.html")
                id = query["bvid"] ?? ("av" + query["aid"]);
            if (!ValidId(id)) return null;
            int part;
            double start;
            int.TryParse(query["p"] ?? query["page"], out part);
            double.TryParse(query["t"] ?? query["start"], NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out start);
            return Create(id, part, start);
        }

        public static async Task<BiliLink> ResolveAsync(string input, CancellationToken token)
        {
            var video = ParseVideo(await ResolveAddressAsync(input, token));
            if (video == null) throw new FormatException("请在控制台选择“导入合集”。");
            return video;
        }
        public static async Task<Uri> ResolveAddressAsync(string input, CancellationToken token)
        {
            string value = (input ?? "").Trim();
            if (Regex.IsMatch(value, "^" + IdPattern + "$", RegexOptions.IgnoreCase))
                value = "https://www.bilibili.com/video/" + value;
            if (Regex.IsMatch(value, @"^(b23\.tv|(?:www\.|m\.|space\.)?bilibili\.com)/", RegexOptions.IgnoreCase))
                value = "https://" + value;
            Uri current = null;
            foreach (Match url in ShareUrl.Matches(value))
            {
                Uri candidate;
                if (Uri.TryCreate(url.Value.TrimEnd('.', ',', ';', ']', '！', '】'), UriKind.Absolute, out candidate)
                    && (IsShort(candidate) || IsVideoHost(candidate) || candidate.Host == "space.bilibili.com"))
                { current = candidate; break; }
            }
            if (current == null) throw new FormatException("请粘贴 B 站视频、空间合集、分享短链或 BV 号。");
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                for (int hop = 0; hop < 6; hop++)
                {
                    if (ParseVideo(current) != null || CollectionAddress.Parse(current) != null) return current;
                    if (!IsShort(current) || !current.IsDefaultPort || current.UserInfo.Length != 0 ||
                        (current.Scheme != "http" && current.Scheme != "https")) break;
                    var safeUrl = new UriBuilder(current) { Scheme = "https", Port = -1 }.Uri;
                    using (var response = await client.GetAsync(safeUrl, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        Uri next = response.Headers.Location;
                        if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400 || next == null)
                            throw new FormatException("此短链没有返回有效的跳转地址。请在 B 站重新分享，或复制视频页地址与 BV 号。");
                        current = new Uri(safeUrl, next);
                    }
                }
            }
            throw new FormatException("请复制视频页或 UP 主空间中合集、系列的详情页链接。");
        }
    }
}
