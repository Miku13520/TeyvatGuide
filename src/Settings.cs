using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TeyvatGuide
{
    internal sealed class Settings
    {
        public string LastLink { get; set; }
        public string GameExecutable { get; set; }
        public double VideoWidth { get; set; }
        public double VideoOpacity { get; set; }
        public double? VideoLeft { get; set; }
        public double? VideoTop { get; set; }
        public Dictionary<string,double> PlaybackPositions { get; set; }
        public bool Locked { get; set; }
        public int SeekSeconds { get; set; }
        public Dictionary<string, string> Keys { get; set; }
        public int ShortcutVersion { get; set; }
        public GuideCollection Collection { get; set; }
        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }; }

        public Settings()
        {
            LastLink = ""; VideoWidth = 480; VideoOpacity = 90;
            SeekSeconds = 5;
            PlaybackPositions=new Dictionary<string,double>();
            Keys = Shortcuts.Defaults();
        }
        public static double Limit(double value, double min, double max, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        }
        public static Settings Load(string file)
        {
            Settings settings = null;
            if (File.Exists(file))
            {
                try { settings = Serializer().Deserialize<Settings>(File.ReadAllText(file)); }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException ||
                          ex is UnauthorizedAccessException || ex is FormatException)) throw;
                }
            }
            settings = settings ?? new Settings();
            settings.VideoWidth = Limit(settings.VideoWidth, 240, 1120, 480);
            settings.VideoOpacity = Limit(settings.VideoOpacity, 20, 100, 90);
            settings.SeekSeconds = (int)Limit(settings.SeekSeconds, 1, 120, 5);
            settings.Keys = settings.Keys ?? Shortcuts.Defaults();
            Shortcuts.NormalizeSettings(settings.Keys, settings.ShortcutVersion);
            settings.ShortcutVersion = Math.Max(Shortcuts.ProfileVersion, settings.ShortcutVersion);
            settings.PlaybackPositions=settings.PlaybackPositions ?? new Dictionary<string,double>();
            if (settings.Collection != null) settings.Collection.Prepare();
            return settings;
        }
        public void Save(string file)
        {
            ShortcutVersion = Math.Max(Shortcuts.ProfileVersion, ShortcutVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, Serializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
        }
    }
}
