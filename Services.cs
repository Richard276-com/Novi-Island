// System services: audio, brightness, network, bluetooth, stats, hotkeys, mouse hook.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DynIsland
{
    static class Svc
    {
        public static readonly AudioService Audio = new AudioService();
        public static readonly BrightnessService Brightness = new BrightnessService();
        public static readonly NetworkService Net = new NetworkService();
        public static readonly BluetoothService Bt = new BluetoothService();
        public static readonly SystemStats Stats = new SystemStats();
        public static readonly HotkeyService Hotkeys = new HotkeyService();
        public static readonly AiService Ai = new AiService();
        public static readonly AppIndex Apps = new AppIndex();
    }

    // ===================================================================== audio
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback { [PreserveSig] int OnNotify(IntPtr data); }

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public int pid; }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    class AudioService
    {
        public bool Available;
        /// level 0..1, muted
        public event Action<float, bool> VolumeChanged;
        public event Action<bool> MicMuteChanged;
        IMMDeviceEnumerator en; IAudioEndpointVolume vol, mic; VolCb cbOut, cbMic; DevCb devCb;
        Dispatcher ui; bool lastMicMute;
        static Guid ctx = Guid.NewGuid();

        [ComVisible(true)]
        class VolCb : IAudioEndpointVolumeCallback
        {
            readonly AudioService o; readonly bool isMic;
            public VolCb(AudioService o, bool isMic) { this.o = o; this.isMic = isMic; }
            public int OnNotify(IntPtr data)
            {
                try
                {
                    bool muted = Marshal.ReadInt32(data, 16) != 0;
                    float level = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
                    o.ui.BeginInvoke(new Action(() =>
                    {
                        if (isMic)
                        {
                            if (muted != o.lastMicMute) { o.lastMicMute = muted; if (o.MicMuteChanged != null) o.MicMuteChanged(muted); }
                        }
                        else if (o.VolumeChanged != null) o.VolumeChanged(level, muted);
                    }));
                }
                catch { }
                return 0;
            }
        }

        [ComVisible(true)]
        class DevCb : IMMNotificationClient
        {
            readonly AudioService o;
            public DevCb(AudioService o) { this.o = o; }
            public int OnDeviceStateChanged(string id, int state) { return 0; }
            public int OnDeviceAdded(string id) { return 0; }
            public int OnDeviceRemoved(string id) { return 0; }
            public int OnDefaultDeviceChanged(int flow, int role, string id)
            {
                if (role == 1) o.ui.BeginInvoke(new Action(o.Hook));
                return 0;
            }
            public int OnPropertyValueChanged(string id, PropertyKey key) { return 0; }
        }

        public void Init(Dispatcher d)
        {
            ui = d;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                Hook();
                devCb = new DevCb(this);
                en.RegisterEndpointNotificationCallback(devCb);
            }
            catch (Exception ex) { Log.Write("audio init: " + ex.Message); }
        }

        static IAudioEndpointVolume Endpoint(IMMDeviceEnumerator en, int flow)
        {
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(flow, 1, out dev) != 0 || dev == null) return null;
            object o; Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }

        void Hook()
        {
            try
            {
                if (vol != null && cbOut != null) vol.UnregisterControlChangeNotify(cbOut);
                if (mic != null && cbMic != null) mic.UnregisterControlChangeNotify(cbMic);
            }
            catch { }
            vol = null; mic = null;
            try
            {
                vol = Endpoint(en, 0);
                if (vol != null) { cbOut = new VolCb(this, false); vol.RegisterControlChangeNotify(cbOut); }
                mic = Endpoint(en, 1);
                if (mic != null) { cbMic = new VolCb(this, true); mic.RegisterControlChangeNotify(cbMic); bool m; mic.GetMute(out m); lastMicMute = m; }
            }
            catch (Exception ex) { Log.Write("audio hook: " + ex.Message); }
            Available = vol != null;
        }

        public float Level { get { float f = 0; try { if (vol != null) vol.GetMasterVolumeLevelScalar(out f); } catch { } return f; } }
        public bool Muted { get { bool m = false; try { if (vol != null) vol.GetMute(out m); } catch { } return m; } }

        public void SetLevel(float f)
        {
            if (vol == null) return;
            f = Math.Max(0, Math.Min(1, f));
            try
            {
                vol.SetMasterVolumeLevelScalar(f, ref ctx);
                if (f > 0 && Muted) vol.SetMute(false, ref ctx);
            }
            catch { }
        }
        public void Step(int dir) { SetLevel((float)(Math.Round(Level * 50) / 50 + dir * 0.02)); }
        public void ToggleMute() { try { if (vol != null) vol.SetMute(!Muted, ref ctx); } catch { } }
    }

    // ===================================================================== brightness
    class BrightnessService
    {
        public bool Available; public int Level;
        public event Action<int> Changed;
        ManagementEventWatcher watcher; Dispatcher ui;

        public void Init(Dispatcher d)
        {
            ui = d;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                        foreach (ManagementObject mo in s.Get()) { Level = Convert.ToInt32(mo["CurrentBrightness"]); Available = true; break; }
                    if (!Available) return;
                    watcher = new ManagementEventWatcher(new ManagementScope("root\\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                    watcher.EventArrived += (s2, e) =>
                    {
                        try
                        {
                            int b = Convert.ToInt32(e.NewEvent["Brightness"]);
                            ui.BeginInvoke(new Action(() => { Level = b; if (Changed != null) Changed(b); }));
                        }
                        catch { }
                    };
                    watcher.Start();
                }
                catch { Available = false; }
            });
        }

        int pending = -1; bool busy;
        public void Set(int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            pending = v; Level = v;
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (true)
                {
                    int t = Interlocked.Exchange(ref pending, -1);
                    if (t < 0) break;
                    try
                    {
                        using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                            foreach (ManagementObject mo in s.Get()) mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)t });
                    }
                    catch { }
                }
                busy = false;
            });
        }

        public void Stop() { try { if (watcher != null) watcher.Stop(); } catch { } }
    }

    // ===================================================================== network
    class NetworkService
    {
        public event Action<string, string, string> Event; // glyph, title, subtitle
        string last = null; DispatcherTimer debounce; Dispatcher ui;

        public void Init(Dispatcher d)
        {
            ui = d;
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            debounce.Tick += delegate { debounce.Stop(); Check(); };
            NetworkChange.NetworkAddressChanged += delegate { ui.BeginInvoke(new Action(() => { debounce.Stop(); debounce.Start(); })); };
            NetworkChange.NetworkAvailabilityChanged += delegate { ui.BeginInvoke(new Action(() => { debounce.Stop(); debounce.Start(); })); };
            Check();
        }

        void Check()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string state = "off", ssid = "";
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;
                        string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                        if (desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vmware") || desc.Contains("loopback") || desc.Contains("bluetooth") || desc.Contains("tap-") || desc.Contains("wsl")) continue;
                        if (ni.GetIPProperties().GatewayAddresses.Count == 0) continue;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) { state = "wifi"; break; }
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet) state = "eth";
                    }
                    if (state == "wifi") ssid = Ssid();
                }
                catch { }
                string key = state + ":" + ssid;
                ui.BeginInvoke(new Action(() =>
                {
                    if (last == null) { last = key; return; }
                    if (key == last) return;
                    last = key;
                    if (Event == null) return;
                    if (state == "wifi") Event("", "Wi-Fi connected", ssid);
                    else if (state == "eth") Event("", "Ethernet connected", "");
                    else Event("", "Disconnected", "No internet connection");
                }));
            });
        }

        static string Ssid()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
                    var m = Regex.Match(o, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
                    return m.Success ? m.Groups[1].Value.Trim() : "";
                }
            }
            catch { return ""; }
        }
    }

    // ===================================================================== bluetooth
    class BluetoothService
    {
        public bool Available;
        public event Action<string, bool> Event; // name, connected
        readonly Dictionary<string, string> names = new Dictionary<string, string>();
        readonly List<global::Windows.Devices.Enumeration.DeviceWatcher> watchers = new List<global::Windows.Devices.Enumeration.DeviceWatcher>();
        Dispatcher ui;

        public void Init(Dispatcher d)
        {
            ui = d;
            try
            {
                Watch(global::Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromConnectionStatus(global::Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected));
                Watch(global::Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(global::Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected));
                Available = true;
            }
            catch (Exception ex) { Log.Write("bt: " + ex.Message); }
        }

        void Watch(string aqs)
        {
            var w = global::Windows.Devices.Enumeration.DeviceInformation.CreateWatcher(aqs);
            bool ready = false;
            w.Added += (s, info) =>
            {
                string name = info.Name;
                ui.BeginInvoke(new Action(() =>
                {
                    bool known = names.ContainsKey(info.Id);
                    names[info.Id] = name;
                    if (ready && !known && Event != null && !string.IsNullOrEmpty(name)) Event(name, true);
                }));
            };
            w.Updated += (s, upd) => { };
            w.Removed += (s, upd) =>
            {
                string id = upd.Id;
                ui.BeginInvoke(new Action(() =>
                {
                    string name;
                    if (names.TryGetValue(id, out name)) { names.Remove(id); if (Event != null && !string.IsNullOrEmpty(name)) Event(name, false); }
                }));
            };
            w.EnumerationCompleted += (s, o) => { ui.BeginInvoke(new Action(() => ready = true)); };
            w.Start();
            watchers.Add(w);
        }

        public void Stop() { foreach (var w in watchers) { try { w.Stop(); } catch { } } }
    }

    // ===================================================================== stats
    class SystemStats
    {
        public double Cpu; public readonly List<double> History = new List<double>();
        public ulong MemTotal, MemUsed, CommitTotal, CommitUsed; public long DiskTotal, DiskUsed;
        public TimeSpan Uptime; public double TempC = double.NaN; public int Procs;
        long lastIdle, lastTotal; bool tempTried; DateTime lastTemp;

        public void Sample()
        {
            long idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                long total = kernel + user;
                if (lastTotal > 0)
                {
                    long dt = total - lastTotal, di = idle - lastIdle;
                    Cpu = dt > 0 ? Math.Max(0, Math.Min(1, 1 - (double)di / dt)) : 0;
                    History.Add(Cpu);
                    while (History.Count > 40) History.RemoveAt(0);
                }
                lastIdle = idle; lastTotal = total;
            }
            var m = new Native.MEMSTATUS(); m.dwLength = (uint)Marshal.SizeOf(m);
            if (Native.GlobalMemoryStatusEx(ref m))
            {
                MemTotal = m.ullTotalPhys; MemUsed = m.ullTotalPhys - m.ullAvailPhys;
                CommitTotal = m.ullTotalPageFile; CommitUsed = m.ullTotalPageFile - m.ullAvailPageFile;
            }
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                DiskTotal = di.TotalSize; DiskUsed = di.TotalSize - di.TotalFreeSpace;
            }
            catch { }
            Uptime = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            try { Procs = Process.GetProcesses().Length; } catch { }
            if ((!tempTried || !double.IsNaN(TempC)) && (DateTime.Now - lastTemp).TotalSeconds > 5)
            {
                tempTried = true; lastTemp = DateTime.Now;
                ThreadPool.QueueUserWorkItem(_ => { TempC = ReadTemp(); });
            }
        }

        static double ReadTemp()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("root\\cimv2", "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        double k = Convert.ToDouble(mo["Temperature"]);
                        if (k > 200) return k - 273.15;
                    }
            }
            catch { }
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                    foreach (ManagementObject mo in s.Get()) return Convert.ToDouble(mo["CurrentTemperature"]) / 10.0 - 273.15;
            }
            catch { }
            return double.NaN;
        }
    }

    // ===================================================================== hotkeys
    class HotkeyService
    {
        IntPtr hwnd; readonly Dictionary<int, string> ids = new Dictionary<int, string>();
        public readonly HashSet<string> Failed = new HashSet<string>();
        public event Action<string> Pressed;

        public void Init(IntPtr h) { hwnd = h; Apply(); }

        public void Apply()
        {
            if (hwnd == IntPtr.Zero) return;
            foreach (var id in ids.Keys) Native.UnregisterHotKey(hwnd, id);
            ids.Clear(); Failed.Clear();
            int n = 0x4100;
            foreach (var kv in Store.S.Hotkeys)
            {
                uint mods, vk;
                if (string.IsNullOrEmpty(kv.Value) || !Parse(kv.Value, out mods, out vk)) continue;
                int id = n++;
                if (Native.RegisterHotKey(hwnd, id, mods | 0x4000, vk)) ids[id] = kv.Key;
                else Failed.Add(kv.Key);
            }
            if (Failed.Count > 0) Log.Write("hotkeys failed: " + string.Join(", ", Failed));
        }

        public void Handle(int id)
        {
            string name;
            if (ids.TryGetValue(id, out name) && Pressed != null) Pressed(name);
        }

        public static bool Parse(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            foreach (var raw in s.Split('+'))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string l = p.ToLowerInvariant();
                if (l == "ctrl" || l == "control") mods |= 2;
                else if (l == "alt") mods |= 1;
                else if (l == "shift") mods |= 4;
                else if (l == "win" || l == "super") mods |= 8;
                else
                {
                    if (p.Length == 1 && char.IsLetterOrDigit(p[0])) vk = (uint)char.ToUpperInvariant(p[0]);
                    else
                    {
                        Key k;
                        if (l == "space") k = Key.Space;
                        else if (!Enum.TryParse(p, true, out k)) return false;
                        vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                    }
                }
            }
            return vk != 0 && mods != 0;
        }

        public static string KeyName(Key k)
        {
            if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
            if (k >= Key.A && k <= Key.Z) return k.ToString();
            return k.ToString();
        }

        public void Stop() { foreach (var id in ids.Keys) Native.UnregisterHotKey(hwnd, id); ids.Clear(); }
    }

    // ===================================================================== mouse hook (click outside)
    class MouseHook
    {
        IntPtr hook; Native.LowLevelProc proc;
        public event Action<Point> Down;
        Dispatcher ui;
        public bool Active { get { return hook != IntPtr.Zero; } }

        public void Start(Dispatcher d)
        {
            if (hook != IntPtr.Zero) return;
            ui = d;
            proc = Callback;
            hook = Native.SetWindowsHookEx(14, proc, Native.GetModuleHandle(null), 0);
        }
        public void Stop()
        {
            if (hook == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero;
        }
        IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int msg = w.ToInt32();
                if (msg == 0x201 || msg == 0x204 || msg == 0x207)
                {
                    // The hook's own point is always in physical pixels; after the display scale changes
                    // while we run, Windows virtualises our coordinates and the two disagree (clicks inside
                    // the island counted as outside and closed it). GetCursorPos answers in our space.
                    var p = Native.CursorPx();
                    if (Down != null) ui.BeginInvoke(new Action(() => { if (Down != null) Down(p); }));
                }
            }
            return Native.CallNextHookEx(hook, code, w, l);
        }
    }
}
