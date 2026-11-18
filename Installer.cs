// Per-user install (no admin): copies the app to %LOCALAPPDATA%\Programs\DynamicIsland, adds a
// Start-menu (and optional desktop) shortcut, a start-up entry and an "Apps & features" entry whose
// Uninstall runs "DynamicIsland.exe --uninstall".
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace DynIsland
{
    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DynamicIsland"); } }
        public static string InstalledExe { get { return Path.Combine(Dir, "DynamicIsland.exe"); } }
        static string StartMenuLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Dynamic Island.lnk"); } }
        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Dynamic Island.lnk"); } }

        public static bool RunningInstalled
        {
            get { try { return string.Equals(Path.GetFullPath(Store.ExePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
        }
        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }
        public static bool HasDesktopShortcut { get { return File.Exists(DesktopLnk); } }

        // returns null on success, otherwise the error
        public static string Install(bool desktop)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!RunningInstalled)
                {
                    File.Copy(Store.ExePath, InstalledExe, true);
                    string src = Path.Combine(Path.GetDirectoryName(Store.ExePath), "fonts");
                    if (Directory.Exists(src))
                    {
                        string dst = Path.Combine(Dir, "fonts");
                        Directory.CreateDirectory(dst);
                        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                    }
                }
                Shortcut(StartMenuLnk, InstalledExe);
                if (desktop) Shortcut(DesktopLnk, InstalledExe);
                else if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk);
                long bytes = new DirectoryInfo(Dir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Dynamic Island");
                    k.SetValue("DisplayIcon", InstalledExe);
                    k.SetValue("DisplayVersion", AppInfo.Version);
                    k.SetValue("Publisher", "Dynamic Island");
                    k.SetValue("InstallLocation", Dir);
                    k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                return null;
            }
            catch (Exception ex) { Log.Write("install: " + ex.Message); return ex.Message; }
        }

        static void Shortcut(string lnk, string target)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            var st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Dynamic Island for Windows" });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        // start-up entry pointing at a given exe (the installed one after an install)
        public static void SetStartup(bool on, string exe)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("DynamicIsland", "\"" + exe + "\" --silent");
                    else k.DeleteValue("DynamicIsland", false);
                }
            }
            catch { }
        }

        // "Apps & features" → Uninstall
        public static void Uninstall()
        {
            if (MessageBox.Show("Uninstall Dynamic Island?\n\nThis removes the app, its shortcuts and the start-up entry.", "Dynamic Island", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            bool wipe = MessageBox.Show("Also delete your settings, to-dos, shelf and AI chats?\n\nNo keeps them in case you install it again.", "Dynamic Island", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("DynamicIsland"))
                if (p.Id != me) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            SetStartup(false, null);
            try { if (File.Exists(StartMenuLnk)) File.Delete(StartMenuLnk); } catch { }
            try { if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (wipe) { try { Directory.Delete(Store.Dir, true); } catch { } }
            // the running exe can't delete its own folder: a hidden cmd does it a moment after we exit
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + Dir + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
            MessageBox.Show("Dynamic Island was removed." + (wipe ? "" : "\nYour settings are still in " + Store.Dir + "."), "Dynamic Island", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
