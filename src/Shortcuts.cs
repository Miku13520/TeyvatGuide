using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TeyvatGuide
{
    internal sealed class Shortcut
    {
        public readonly string Id, Label, DefaultKey;
        public Shortcut(string id, string label, string defaultKey)
        {
            Id = id; Label = label; DefaultKey = defaultKey;
        }
    }

    internal sealed class Gesture
    {
        public uint Modifiers, VirtualKey;
        public uint Code { get { return (Modifiers << 8) | VirtualKey; } }
        public static bool IsModifier(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;
        }
        public static string FromKeys(Key key, ModifierKeys modifiers)
        {
            if (IsModifier(key) || (modifiers & ModifierKeys.Windows) != 0) return null;
            var names = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) names.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) names.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) names.Add("Shift");
            string name = key == Key.OemQuestion ? "/" :
                key >= Key.D0 && key <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString();
            names.Add(name);
            string candidate = string.Join("+", names);
            Gesture gesture;
            return TryParse(candidate, out gesture) ? candidate : null;
        }
        public static string Display(string value)
        {
            var parts = value.Split('+').Select(p => p.Trim()).ToArray();
            int last = parts.Length - 1;
            switch (parts[last].ToLowerInvariant())
            {
                case "space": parts[last] = "空格"; break;
                case "left": parts[last] = "←"; break;
                case "right": parts[last] = "→"; break;
                case "up": parts[last] = "↑"; break;
                case "down": parts[last] = "↓"; break;
                case "pageup": case "prior": parts[last] = "Page Up"; break;
                case "pagedown": case "next": parts[last] = "Page Down"; break;
                case "return": parts[last] = "Enter"; break;
                case "back": parts[last] = "Backspace"; break;
                case "oem2": case "oemquestion": parts[last] = "/"; break;
                case "divide": parts[last] = "小键盘 /"; break;
            }
            if (parts[last].StartsWith("NumPad",StringComparison.OrdinalIgnoreCase))
                parts[last] = "小键盘 " + parts[last].Substring(6);
            return string.Join("  +  ", parts);
        }
        public static bool TryParse(string text, out Gesture result)
        {
            result = null;
            var pieces = (text ?? "").Split('+').Select(s => s.Trim()).ToArray();
            if (pieces.Length > 4) return false;
            uint mods = 0;
            foreach (string part in pieces.Take(pieces.Length - 1))
            {
                uint bit;
                switch (part.ToLowerInvariant())
                {
                    case "alt": bit = 1; break;
                    case "ctrl": case "control": bit = 2; break;
                    case "shift": bit = 4; break;
                    default: return false;
                }
                if ((mods & bit) != 0) return false;
                mods |= bit;
            }
            string last = pieces.Last();
            if (last == "/") last = "OemQuestion";
            if (last.Length == 1 && last[0] >= '0' && last[0] <= '9') last = "D" + last;
            Key key;
            if (last.Length == 0 || !char.IsLetter(last[0]) || !Enum.TryParse(last, true, out key) || !Enum.IsDefined(typeof(Key), key))
                return false;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk <= 0 || vk >= 255 || vk == 0x7b || (vk >= 0xa0 && vk <= 0xa5) ||
                (vk >= 0x10 && vk <= 0x12) || vk == 0x5b || vk == 0x5c) return false;
            result = new Gesture { Modifiers = mods, VirtualKey = (uint)vk };
            return true;
        }
    }

    internal sealed class Shortcuts : IDisposable
    {
        public static readonly Shortcut[] All = {
            new Shortcut("toggle", "播放 / 暂停", "Ctrl+Space"),
            new Shortcut("back", "快退", "Ctrl+Left"),
            new Shortcut("forward", "快进", "Ctrl+Right"),
            new Shortcut("slower", "降低倍速", "Ctrl+Down"),
            new Shortcut("faster", "提高倍速", "Ctrl+Up"),
            new Shortcut("lock", "锁定 / 解锁视频窗", "Ctrl+0"),
            new Shortcut("previous", "上一集", "Ctrl+PageUp"),
            new Shortcut("next", "下一集", "Ctrl+PageDown")
        };
        private readonly IntPtr handle;
        private readonly HwndSource source;
        private readonly HashSet<int> registered = new HashSet<int>();
        private readonly Action<string> dispatch;
        private readonly GameKeys gameKeys;
        private const int FirstId = 0x3120;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
        public static Dictionary<string, string> Defaults() { return All.ToDictionary(s => s.Id, s => s.DefaultKey); }

        public const int ProfileVersion = 4;
        public static void NormalizeSettings(IDictionary<string,string> keys, int version)
        {
            keys.Remove("play");
            keys.Remove("pause");
            var occupied = new HashSet<uint>();
            var missing = new List<Shortcut>();
            if (version < 3) {
                foreach (var item in All) keys[item.Id]=item.DefaultKey;
                return;
            }
            if (version == 3) {
                string playback;
                Gesture previous,current,replacement;
                Gesture.TryParse("Ctrl+/",out previous);
                Gesture.TryParse(All[0].DefaultKey,out replacement);
                if (keys.TryGetValue("toggle",out playback) && Gesture.TryParse(playback,out current) &&
                    current.Code==previous.Code && !All.Skip(1).Any(item=>{
                        string value;
                        Gesture assigned;
                        return keys.TryGetValue(item.Id,out value) && Gesture.TryParse(value,out assigned) && assigned.Code==replacement.Code;
                    })) keys["toggle"]=All[0].DefaultKey;
            }
            foreach (var item in All)
            {
                string value;
                Gesture current;
                if (keys.TryGetValue(item.Id,out value) && Gesture.TryParse(value,out current)) {
                    if (occupied.Add(current.Code)) { keys[item.Id] = value; continue; }
                }
                missing.Add(item);
            }
            foreach (var item in missing) {
                var candidates = new[] { item.DefaultKey }.Concat(Enumerable.Range(1,11).Select(i => "Ctrl+F" + i));
                foreach (string option in candidates) {
                    Gesture gesture;
                    if (Gesture.TryParse(option,out gesture) && occupied.Add(gesture.Code)) { keys[item.Id] = option; break; }
                }
            }
        }

        public Shortcuts(Window window, Action<string> dispatch, Func<IntPtr> foregroundGame)
        {
            handle = new WindowInteropHelper(window).EnsureHandle();
            source = HwndSource.FromHwnd(handle);
            this.dispatch = dispatch;
            gameKeys=new GameKeys(handle,foregroundGame);
            source.AddHook(Message);
        }
        public void UpdateGameFocus() { gameKeys.Poll(id=>dispatch(All[id].Id)); }
        private IntPtr Message(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            int id = unchecked((int)wp.ToInt64()) - FirstId;
            if (msg == 0x0312 && registered.Contains(id))
            {
                handled = true;
                if (gameKeys.FromHotkey(id)) dispatch(All[id].Id);
            }
            if (msg==0x00ff) {
                int action=gameKeys.Read(lp);
                if (action>=0) dispatch(All[action].Id);
            }
            return IntPtr.Zero;
        }
        public static string Validate(IDictionary<string, string> values)
        {
            var seen = new HashSet<uint>();
            foreach (var action in All)
            {
                Gesture key; string value;
                if (!values.TryGetValue(action.Id, out value) || !Gesture.TryParse(value, out key))
                    return action.Label + "需要一个可用的按键。";
                if (!seen.Add(key.Code)) return action.Label + "与另一项使用了相同的键位。";
            }
            return null;
        }
        private bool Bind(int index, string value)
        {
            Gesture key;
            if (!Gesture.TryParse(value, out key) || !RegisterHotKey(handle, FirstId + index, key.Modifiers | 0x4000, key.VirtualKey))
                return false;
            registered.Add(index); gameKeys.Bind(key,index); return true;
        }
        public string Register(IDictionary<string, string> keys)
        {
            Clear();
            var conflicts = new List<string>();
            for (int index = 0; index < All.Length; index++)
            {
                var item = All[index];
                if (!Bind(index, keys[item.Id])) {
                    Gesture key;
                    if (Gesture.TryParse(keys[item.Id],out key)) gameKeys.Bind(key,index);
                    conflicts.Add(item.Label + "（" + keys[item.Id] + "）");
                }
            }
            return conflicts.Count == 0 ? null : string.Join("、", conflicts) + "的桌面快捷键已被占用，仍会在原神前台监听。";
        }
        public string Replace(Dictionary<string, string> next, Dictionary<string, string> previous)
        {
            string error = Validate(next);
            if (error != null) return error;
            error = Register(next);
            if (error == null) return null;
            string restore = Register(previous);
            return error + (restore == null ? "已恢复原设置。" : "原设置中仍有冲突。" + restore);
        }
        public bool ApplyRecorded(string id, Dictionary<string,string> next, Dictionary<string,string> previous, out string message)
        {
            string error = Validate(next);
            int index = Array.FindIndex(All, item => item.Id == id);
            if (error == null && !Bind(index, next[id])) error = "这个快捷键已被其他程序占用。";
            if (error != null)
            {
                string restore = Register(previous);
                message = error + (restore == null ? "已恢复原设置。" : restore);
                return false;
            }
            var unavailable = new List<string>();
            for (int i = 0; i < All.Length; i++)
                if (i != index && !Bind(i, next[All[i].Id])) {
                    Gesture key;
                    if (Gesture.TryParse(next[All[i].Id],out key)) gameKeys.Bind(key,i);
                    unavailable.Add(All[i].Label);
                }
            message = All[index].Label + "的新快捷键已生效。";
            if (unavailable.Count > 0) message += string.Join("、", unavailable) + "的桌面快捷键已被占用，仍会在原神前台监听。";
            return true;
        }
        public void Suspend()
        {
            Clear();
        }
        private void Clear()
        {
            foreach (int id in registered) UnregisterHotKey(handle, FirstId + id);
            registered.Clear(); gameKeys.Clear();
        }
        public void Dispose() { Clear(); gameKeys.Dispose(); source.RemoveHook(Message); }
    }
}
