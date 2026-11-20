using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TeyvatGuide
{
    // Foreground games can request RIDEV_NOHOTKEYS. Read physical make/break events on our own window for that case.
    internal sealed class GameKeys : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page,Usage; public uint Flags; public IntPtr Window; }
        [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type,Size; public IntPtr Device,Param; }
        [StructLayout(LayoutKind.Sequential)] private struct Packet {
            public Header Header; public ushort Scan,Flags,Reserved,Key; public uint Message,Extra;
        }
        [DllImport("user32.dll")] private static extern bool RegisterRawInputDevices(Device[] devices,uint count,uint size);
        [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr data,uint command,out Packet packet,ref uint size,uint headerSize);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern int GetMessageTime();
        private readonly IntPtr window;
        private readonly Func<IntPtr> foregroundGame;
        private readonly Dictionary<uint,int> bindings = new Dictionary<uint,int>();
        private readonly HashSet<uint> actions = new HashSet<uint>(), held = new HashSet<uint>();
        private readonly HashSet<uint> sampledDown = new HashSet<uint>();
        private readonly List<Stroke> pending = new List<Stroke>();
        private readonly int[] modifierKeys = { 0xa0,0xa1,0xa2,0xa3,0xa4,0xa5,0x5b,0x5c };
        private readonly bool[] modifiers = new bool[8];
        private IntPtr game;
        private bool attached;
        private DateTime retryAfter;
        private readonly List<Stroke> recent = new List<Stroke>();
        private struct Stroke { public int Action,Time,Paths; }

        public GameKeys(IntPtr window, Func<IntPtr> foregroundGame) { this.window=window; this.foregroundGame=foregroundGame; }
        public void Bind(Gesture key,int id) { bindings[key.Code]=id; actions.Add(key.VirtualKey); }
        public void Clear()
        {
            bindings.Clear(); actions.Clear(); ResetStrokeState(); game=IntPtr.Zero;
            if (attached) { Listen(false); attached=false; }
        }
        private void ResetStrokeState() { held.Clear(); sampledDown.Clear(); pending.Clear(); recent.Clear(); }
        private bool Listen(bool enable)
        {
            var device=new Device { Page=1,Usage=6,Flags=enable ? 0x100u : 1u,Window=enable ? window : IntPtr.Zero };
            return RegisterRawInputDevices(new[] {device},1,(uint)Marshal.SizeOf(typeof(Device)));
        }
        public void Sync()
        {
            IntPtr active=bindings.Count==0 ? IntPtr.Zero : foregroundGame();
            if (active==game && (active==IntPtr.Zero || attached || DateTime.UtcNow<retryAfter)) return;
            bool changed=active!=game;
            game=active;
            if (changed) ResetStrokeState();
            if (active==IntPtr.Zero) { if (attached) Listen(false); attached=false; return; }
            if (!attached) { attached=Listen(true); retryAfter=DateTime.UtcNow.AddSeconds(3); }
            for (int i=0;i<modifierKeys.Length;i++) modifiers[i]=GetAsyncKeyState(modifierKeys[i])<0;
            if (changed) foreach (uint key in actions) if (GetAsyncKeyState((int)key)<0) { held.Add(key); sampledDown.Add(key); }
        }
        public bool FromHotkey(int id)
        {
            return game==IntPtr.Zero || game!=foregroundGame() || FirstSignal(id,false,GetMessageTime());
        }
        // One physical stroke may produce both WM_HOTKEY and WM_INPUT, in either order.
        internal bool FirstSignal(int id,bool raw,int time) { return FirstSignal(id,raw ? 2 : 1,time); }
        private bool FirstSignal(int id,int path,int time)
        {
            for (int i=recent.Count-1;i>=0;i--) {
                var prior=recent[i]; int age=unchecked(time-prior.Time);
                if (age>110 || age< -110) { recent.RemoveAt(i); continue; }
                if (prior.Action==id && (prior.Paths&path)==0 && Math.Abs(age)<=40) {
                    prior.Paths|=path; recent[i]=prior; return false;
                }
            }
            recent.Add(new Stroke { Action=id,Paths=path,Time=time }); return true;
        }
        public void Poll(Action<int> dispatch)
        {
            Sync();
            if (game!=IntPtr.Zero) Sample(k=>GetAsyncKeyState(k)<0,Environment.TickCount,dispatch);
        }
        internal void Sample(Func<int,bool> down,int time,Action<int> dispatch)
        {
            uint mods=(down(0x10)?4u:0)|(down(0x11)?2u:0)|(down(0x12)?1u:0);
            bool windows=down(0x5b)||down(0x5c);
            foreach (uint key in actions) {
                if (!down((int)key)) { sampledDown.Remove(key); held.Remove(key); continue; }
                int id;
                if (sampledDown.Add(key) && !windows && bindings.TryGetValue((mods<<8)|key,out id))
                    pending.Add(new Stroke {Action=id,Time=time});
            }
            // Give queued WM_INPUT and WM_HOTKEY first chance to deliver the same keystroke.
            for (int i=0;i<pending.Count;) {
                var stroke=pending[i];
                if (unchecked(time-stroke.Time)<34) { i++;continue; }
                pending.RemoveAt(i);
                if (FirstSignal(stroke.Action,4,stroke.Time)) dispatch(stroke.Action);
            }
        }
        public int Read(IntPtr data)
        {
            if (!attached || game!=foregroundGame() || bindings.Count==0) return -1;
            uint size=(uint)Marshal.SizeOf(typeof(Packet)); Packet p;
            if (GetRawInputData(data,0x10000003,out p,ref size,(uint)Marshal.SizeOf(typeof(Header)))!=size || p.Header.Type!=1) return -1;
            return StrokeAction(p.Key,p.Scan,p.Flags,GetMessageTime());
        }
        internal int StrokeAction(uint key,ushort scan,ushort flags,int time)
        {
            bool down=(flags&1)==0, extended=(flags&2)!=0;
            if (key==0x10) key=scan==0x36 ? 0xa1u : 0xa0u;
            if (key==0x11) key=extended ? 0xa3u : 0xa2u;
            if (key==0x12) key=extended ? 0xa5u : 0xa4u;
            int modifier=Array.IndexOf(modifierKeys,(int)key);
            if (modifier>=0) { modifiers[modifier]=down; return -1; }
            if (!actions.Contains(key)) return -1;
            if (!down) { held.Remove(key); return -1; }
            if (!held.Add(key) || modifiers[6] || modifiers[7]) return -1;
            uint mods=(modifiers[0]||modifiers[1]?4u:0u)|(modifiers[2]||modifiers[3]?2u:0u)|(modifiers[4]||modifiers[5]?1u:0u);
            int id; return bindings.TryGetValue((mods<<8)|key,out id) && FirstSignal(id,true,time) ? id : -1;
        }
        public void Dispose() { Clear(); }
    }
}
