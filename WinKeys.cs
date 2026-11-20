// Linux-style Win shortcuts: Win+1..9/0 workspaces (virtual desktops), Win+Enter terminal, Win+F files,
// Win+B browser, Win+Q close window, Win+Space / Win+/ launcher.
// Windows reserves these combos, so RegisterHotKey can't take them. A low-level keyboard hook swallows the
// key instead, then injects Win+<unused key>, which we *do* register: the WM_HOTKEY that follows gives this process
// foreground rights (so the launcher / new windows come to the front) and the extra key stops the Start
// menu from opening when Win is released.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace DynIsland
{
    class WinKeys
    {
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        [StructLayout(LayoutKind.Sequential)] struct KBDLL { public uint vk, scan, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

        const int HK_ID = 0x4F00;
        const uint VK_MASK = 0xE8;
        uint hkVk;
        static readonly IntPtr Marker = new IntPtr(0x4449574B);   // dwExtraInfo on our own injected keys

        IntPtr hwnd, hook; Native.LowLevelProc proc;
        Action<string> island;
        bool hkOk;
        string pending;
        readonly HashSet<uint> swallowed = new HashSet<uint>();
        readonly bool[] phys = new bool[256];      // physical (non-injected) key state from the hook
        int lastTarget = -1, lastCount; long lastSwitch;

        public bool Active { get { return hook != IntPtr.Zero; } }

        public void Init(IntPtr h, Action<string> islandAction) { hwnd = h; island = islandAction; Apply(); }

        public void Apply()
        {
            if (hwnd == IntPtr.Zero) return;
            bool want = Store.S.LinuxWinKeys;
            if (want && hook == IntPtr.Zero)
            {
                // a Win+<key nobody types> to bounce through WM_HOTKEY: unassigned VKs first, then F24…F13
                hkOk = false;
                var cand = new List<uint> { 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9D, 0x9E, 0x9F, 0x88, 0x89, 0x8A, 0x8B };
                for (uint f = 0x87; f >= 0x7C; f--) cand.Add(f);
                // registered for Win and Win+Shift: hotkeys match modifiers exactly, and Win+Shift+N bounces with Shift still held
                foreach (var vk in cand)
                {
                    if (!Native.RegisterHotKey(hwnd, HK_ID, 8 | 0x4000, vk)) continue;
                    if (!Native.RegisterHotKey(hwnd, HK_ID + 1, 8 | 4 | 0x4000, vk)) { Native.UnregisterHotKey(hwnd, HK_ID); continue; }
                    hkVk = vk; hkOk = true; break;
                }
                Log.Write(hkOk ? "winkeys: bounce key 0x" + hkVk.ToString("X2") : "winkeys: no bounce key free, direct dispatch");
                proc = Callback;
                hook = Native.SetWindowsHookEx(13, proc, Native.GetModuleHandle(null), 0);
                VDesk.Init();
                Log.Write("winkeys on hook=" + (hook != IntPtr.Zero) + " vdesk=" + VDesk.Ready);
            }
            else if (!want && hook != IntPtr.Zero) Stop();
        }

        public void Stop()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (hkOk) { Native.UnregisterHotKey(hwnd, HK_ID); Native.UnregisterHotKey(hwnd, HK_ID + 1); hkOk = false; }
            // Nothing else to undo: this feature never writes Windows settings or the registry (only reads the
            // virtual-desktop list and default browser), so with the hook gone Windows' own Win+key shortcuts
            // are back exactly as they were.
            swallowed.Clear(); pending = null; Array.Clear(phys, 0, phys.Length);
            Log.Write("winkeys off");
        }

        // WM_HOTKEY from the island's WndProc; true when it was ours
        public bool Handle(int id)
        {
            if (id != HK_ID && id != HK_ID + 1) return false;
            string a = pending; pending = null;
            if (a != null) Run(a);
            return true;
        }

        bool Down(int vk) { return phys[vk] && (GetAsyncKeyState(vk) & 0x8000) != 0; }

        IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            if (code < 0) return Native.CallNextHookEx(hook, code, w, l);
            var k = (KBDLL)Marshal.PtrToStructure(l, typeof(KBDLL));
            bool injected = (k.flags & 0x10) != 0;
            if (injected) return Native.CallNextHookEx(hook, code, w, l);
            int msg = w.ToInt32();
            bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
            if (k.vk < 256) { if (down) phys[k.vk] = true; else if (up) phys[k.vk] = false; }

            if (up && swallowed.Remove(k.vk)) return (IntPtr)1;
            if (!down) return Native.CallNextHookEx(hook, code, w, l);
            if (swallowed.Contains(k.vk)) return (IntPtr)1;   // auto-repeat of a combo we took

            bool win = Down(0x5B) || Down(0x5C), shift = Down(0xA0) || Down(0xA1);
            if (!win || Down(0xA2) || Down(0xA3) || Down(0xA4) || Down(0xA5))
                return Native.CallNextHookEx(hook, code, w, l);

            string act = Map(k.vk);
            if (act != null && shift) act = act.StartsWith("desk") ? "move" + act.Substring(4) : null;   // Win+Shift+N: send window to N
            if (act == null) return Native.CallNextHookEx(hook, code, w, l);
            swallowed.Add(k.vk);
            try
            {
                if (act.StartsWith("desk") && !(hkOk && VDesk.Ready)) SwitchDesktop(int.Parse(act.Substring(4)), false);
                else if (hkOk) { pending = act; Send(Key(hkVk, false), Key(hkVk, true)); }
                else { Send(Key(VK_MASK, false), Key(VK_MASK, true)); var a = act; System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => Run(a))); }
            }
            catch (Exception ex) { Log.Write("winkeys " + act + ": " + ex.Message); }
            return (IntPtr)1;
        }

        static string Map(uint vk)
        {
            if (vk >= 0x31 && vk <= 0x39) return "desk" + (vk - 0x30);
            if (vk == 0x30) return "desk10";
            switch (vk)
            {
                case 0x0D: return "terminal";
                case 0x46: return "files";     // F
                case 0x42: return "browser";   // B
                case 0x51: return "close";     // Q
                case 0x20: case 0xBF: return "launcher";   // Space, /
            }
            return null;
        }

        static INPUT Key(uint vk, bool up)
        {
            bool ext = vk == 0x25 || vk == 0x27;
            var i = new INPUT { type = 1 };
            i.ki.vk = (ushort)vk; i.ki.flags = (up ? 2u : 0u) | (ext ? 1u : 0u); i.ki.extra = Marker;
            return i;
        }
        static void Send(params INPUT[] keys) { SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(INPUT))); }

        // ------------------------------------------------------------ workspaces
        // Windows has no public "go to desktop N"; read the order + current one from the registry and walk
        // there with Ctrl+Win+←/→ (Win is still held by the user), creating missing desktops with Ctrl+Win+D.
        // addWin: called outside the hook, where the user may already have let go of Win
        void SwitchDesktop(int n, bool addWin)
        {
            int count, cur; ReadDesktops(out count, out cur);
            long now = Environment.TickCount;
            if (lastTarget >= 0 && now - lastSwitch < 900) { cur = lastTarget; count = Math.Max(count, lastCount); }   // registry lags behind fast presses
            int target = n - 1;
            var steps = new List<INPUT>();
            if (target >= count) { for (int i = count; i <= target; i++) { steps.Add(Key(0x44, false)); steps.Add(Key(0x44, true)); } count = target + 1; }
            else
            {
                int d = target - cur; uint vk = d > 0 ? 0x27u : 0x25u;
                for (int i = 0; i < Math.Abs(d); i++) { steps.Add(Key(vk, false)); steps.Add(Key(vk, true)); }
            }
            bool win = addWin && !Down(0x5B) && !Down(0x5C);
            var seq = new List<INPUT>();
            if (steps.Count > 0)
            {
                if (win) seq.Add(Key(0x5B, false));
                seq.Add(Key(0xA2, false)); seq.AddRange(steps); seq.Add(Key(0xA2, true));
            }
            if (seq.Count > 0 || !win) { seq.Add(Key(VK_MASK, false)); seq.Add(Key(VK_MASK, true)); }   // mask: no Start menu on Win release
            if (win && steps.Count > 0) seq.Add(Key(0x5B, true));
            if (seq.Count > 0) Send(seq.ToArray());
            lastTarget = target; lastCount = count; lastSwitch = now;
        }

        static void ReadDesktops(out int count, out int cur)
        {
            count = 1; cur = 0;
            const string root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\";
            byte[] ids = null, c = null;
            using (var k = Registry.CurrentUser.OpenSubKey(root + "VirtualDesktops"))
                if (k != null) { ids = k.GetValue("VirtualDesktopIDs") as byte[]; c = k.GetValue("CurrentVirtualDesktop") as byte[]; }
            if (c == null)
                using (var k = Registry.CurrentUser.OpenSubKey(root + @"SessionInfo\" + Process.GetCurrentProcess().SessionId + @"\VirtualDesktops"))
                    if (k != null) c = k.GetValue("CurrentVirtualDesktop") as byte[];
            if (ids == null || ids.Length < 16) return;
            count = ids.Length / 16;
            if (c == null || c.Length != 16) return;
            for (int i = 0; i < count; i++)
            {
                bool eq = true;
                for (int j = 0; j < 16 && eq; j++) eq = ids[i * 16 + j] == c[j];
                if (eq) { cur = i; return; }
            }
        }

        // ------------------------------------------------------------ shell virtual-desktop API (undocumented)
        // IVirtualDesktopManagerInternal, the Windows 11 24H2+ layout. The documented IVirtualDesktopManager can
        // only move this process's own windows. Interface IDs change between Windows builds; when they don't
        // match, QueryService fails, Ready stays false and Win+num falls back to Ctrl+Win+arrow keystrokes.
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider10 { [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr obj); }

        [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IObjectArray { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetAt(int i, ref Guid iid, out IntPtr obj); }

        [ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IVDManagerInternal
        {
            [PreserveSig] int GetCount(out int n);
            [PreserveSig] int MoveViewToDesktop(IntPtr view, IntPtr desktop);
            [PreserveSig] int CanViewMoveDesktops(IntPtr view, out int can);
            [PreserveSig] int GetCurrentDesktop(out IntPtr desktop);
            [PreserveSig] int GetDesktops(out IObjectArray desktops);
            [PreserveSig] int GetAdjacentDesktop(IntPtr from, int dir, out IntPtr desktop);
            [PreserveSig] int SwitchDesktop(IntPtr desktop);
            [PreserveSig] int SwitchDesktopAndMoveForegroundView(IntPtr desktop);
            [PreserveSig] int CreateDesktop(out IntPtr desktop);
        }

        [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAppViewCollection
        {
            [PreserveSig] int GetViews(out IntPtr a);
            [PreserveSig] int GetViewsByZOrder(out IntPtr a);
            [PreserveSig] int GetViewsByAppUserModelId(IntPtr id, out IntPtr a);
            [PreserveSig] int GetViewForHwnd(IntPtr hwnd, out IntPtr view);
        }

        static class VDesk
        {
            static readonly Guid CLSID_Shell = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
            static readonly Guid CLSID_VDMI = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
            static Guid IID_Desktop = new Guid("3F07F4BE-B107-441A-AF0F-39D82529072C");
            static IVDManagerInternal mgr; static IAppViewCollection views;
            public static bool Ready { get { return mgr != null; } }

            public static bool Init()
            {
                mgr = null; views = null;
                try
                {
                    var sp = (IServiceProvider10)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_Shell));
                    Guid s = CLSID_VDMI, i = typeof(IVDManagerInternal).GUID; IntPtr p;
                    if (sp.QueryService(ref s, ref i, out p) < 0 || p == IntPtr.Zero) { Log.Write("vdesk: manager interface not on this build"); return false; }
                    var m = (IVDManagerInternal)Marshal.GetObjectForIUnknown(p); Marshal.Release(p);
                    Guid v = typeof(IAppViewCollection).GUID; Guid v2 = v;
                    if (sp.QueryService(ref v, ref v2, out p) < 0 || p == IntPtr.Zero) { Log.Write("vdesk: view collection not on this build"); return false; }
                    views = (IAppViewCollection)Marshal.GetObjectForIUnknown(p); Marshal.Release(p);
                    // a desktop must come back with the expected IID, or the layout isn't the one declared above
                    IntPtr d = Desktop(m, 0);
                    if (d == IntPtr.Zero) { Log.Write("vdesk: desktop interface mismatch"); views = null; return false; }
                    Marshal.Release(d);
                    mgr = m;
                    return true;
                }
                catch (Exception ex) { Log.Write("vdesk init: " + ex.Message); mgr = null; views = null; return false; }
            }

            static IntPtr Desktop(IVDManagerInternal m, int index)
            {
                IObjectArray arr;
                if (m.GetDesktops(out arr) < 0 || arr == null) return IntPtr.Zero;
                try
                {
                    int n; arr.GetCount(out n);
                    IntPtr d;
                    if (index < 0 || index >= n || arr.GetAt(index, ref IID_Desktop, out d) < 0) return IntPtr.Zero;
                    return d;
                }
                finally { Marshal.ReleaseComObject(arr); }
            }

            // desktop #index, creating missing ones; caller releases. Retries once after Explorer restarts.
            static IntPtr Get(int index)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (mgr == null && !Init()) return IntPtr.Zero;
                    try
                    {
                        int n;
                        if (mgr.GetCount(out n) < 0) throw new COMException("GetCount");
                        while (n <= index) { IntPtr c; if (mgr.CreateDesktop(out c) < 0) break; Marshal.Release(c); n++; }
                        return Desktop(mgr, index);
                    }
                    catch (Exception ex) { Log.Write("vdesk: " + ex.Message + ", reconnecting"); mgr = null; }
                }
                return IntPtr.Zero;
            }

            public static bool Switch(int index)
            {
                IntPtr d = Get(index);
                if (d == IntPtr.Zero) return false;
                try { return mgr.SwitchDesktop(d) >= 0; }
                catch { mgr = null; return false; }
                finally { Marshal.Release(d); }
            }

            public static bool Move(IntPtr hwnd, int index)
            {
                IntPtr d = Get(index);
                if (d == IntPtr.Zero) return false;
                IntPtr view = IntPtr.Zero;
                try
                {
                    int hr = views.GetViewForHwnd(hwnd, out view);
                    if (hr < 0 || view == IntPtr.Zero) { Log.Write("vdesk: no view for window 0x" + hr.ToString("X8")); return false; }
                    hr = mgr.MoveViewToDesktop(view, d);
                    if (hr < 0) Log.Write("vdesk: move failed 0x" + hr.ToString("X8"));
                    return hr >= 0;
                }
                catch (Exception ex) { Log.Write("vdesk move: " + ex.Message); mgr = null; return false; }
                finally { Marshal.Release(d); if (view != IntPtr.Zero) Marshal.Release(view); }
            }

            public static string Info(IntPtr h)
            {
                if (mgr == null && !Init()) return "unavailable";
                int n; mgr.GetCount(out n);
                IntPtr v;
                int hr = views.GetViewForHwnd(h, out v);
                if (v != IntPtr.Zero) Marshal.Release(v);
                return "ready count=" + n + " view(0x" + h.ToString("X") + ")=0x" + hr.ToString("X8");
            }
        }

        // ------------------------------------------------------------ actions (UI thread, with foreground rights)
        public void Run(string act)
        {
            Log.Write("winkey " + act);
            switch (act)
            {
                case "launcher": if (island != null) island("launcher"); return;
                case "close": CloseActive(); return;
                case "terminal": Launch(Or(Store.S.WinTerminal, "wt.exe"), "powershell.exe"); return;
                case "files": Launch(Or(Store.S.WinFiles, "explorer.exe"), null); return;
                case "browser": Launch(Or(Store.S.WinBrowser, DefaultBrowser()), "https://www.google.com"); return;
                case "info": Log.Write("vdesk " + VDesk.Info(Native.GetForegroundWindow())); return;
            }
            if (act.StartsWith("info")) { long hv; if (long.TryParse(act.Substring(4), out hv)) Log.Write("vdesk " + VDesk.Info(new IntPtr(hv))); }
            else if (act.StartsWith("desk"))
            {
                int n = int.Parse(act.Substring(4));
                if (!VDesk.Switch(n - 1)) SwitchDesktop(n, true);
            }
            else if (act.StartsWith("move"))
            {
                var h = TargetWindow();
                if (h == IntPtr.Zero) return;
                int n = int.Parse(act.Substring(4));
                if (!VDesk.Move(h, n - 1)) { Log.Write("winkeys: moving windows needs the shell's virtual desktop API, unavailable on this build"); return; }
                if (Store.S.WinMoveFollow && VDesk.Switch(n - 1)) Native.SetForegroundWindow(h);
            }
        }

        // the foreground top-level window, unless it's the desktop / taskbar / the island itself
        IntPtr TargetWindow()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return IntPtr.Zero;
            var root = GetAncestor(fg, 2);
            if (root != IntPtr.Zero) fg = root;
            if (fg == hwnd) return IntPtr.Zero;
            var sb = new System.Text.StringBuilder(64); Native.GetClassName(fg, sb, 64);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return IntPtr.Zero;
            return fg;
        }

        static string Or(string a, string b) { return string.IsNullOrWhiteSpace(a) ? b : a.Trim(); }

        void CloseActive()
        {
            // TargetWindow skips the shell: closing the desktop window brings up the shutdown dialog
            var fg = TargetWindow();
            if (fg != IntPtr.Zero) PostMessage(fg, 0x0112, (IntPtr)0xF060, IntPtr.Zero);   // WM_SYSCOMMAND SC_CLOSE, same as Alt+F4
        }

        static void Launch(string cmd, string fallback)
        {
            if (string.IsNullOrEmpty(cmd)) { cmd = fallback; fallback = null; }
            if (cmd == null) return;
            AllowSetForegroundWindow(-1);   // let the new process take the front; we hold the rights right now
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string file, args; Split(cmd, out file, out args);
                try
                {
                    Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
                }
                catch (Exception ex)
                {
                    Log.Write("winkeys launch '" + cmd + "': " + ex.Message);
                    if (fallback != null) try { Process.Start(new ProcessStartInfo(fallback) { UseShellExecute = true }); } catch { }
                }
            });
        }

        static void Split(string cmd, out string file, out string args)
        {
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int e = cmd.IndexOf('"', 1);
                if (e > 0) { file = cmd.Substring(1, e - 1); args = cmd.Substring(e + 1).Trim(); return; }
            }
            if (File.Exists(cmd) || cmd.IndexOf(' ') < 0) { file = cmd; args = ""; return; }
            int s = cmd.IndexOf(' ');
            file = cmd.Substring(0, s); args = cmd.Substring(s + 1).Trim();
        }

        static string DefaultBrowser()
        {
            try
            {
                string prog = null;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                    if (k != null) prog = k.GetValue("ProgId") as string;
                if (prog == null) return null;
                using (var k = Registry.ClassesRoot.OpenSubKey(prog + @"\shell\open\command"))
                {
                    string c = k == null ? null : k.GetValue("") as string;
                    if (c == null) return null;
                    string file, args; Split(c, out file, out args);
                    return "\"" + file + "\"";
                }
            }
            catch { return null; }
        }
    }
}
