// Notifications from other apps (WhatsApp, Discord, browsers, Outlook...) shown on the island.
// Reads the Windows notification centre through UserNotificationListener (works for this unpackaged
// exe once Windows allows notification access) and polls it, since the change event needs package
// identity. Only notifications that arrive while the island runs are shown.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynIsland
{
    partial class IslandWindow
    {
        UserNotificationListener nl;
        readonly HashSet<uint> nSeen = new HashSet<uint>();
        readonly Dictionary<string, ImageSource> nIcons = new Dictionary<string, ImageSource>();
        bool nReady, nBusy; DispatcherTimer nTimer; DateTime nPollStart, nPollEnd; int nPolls;
        public string AppNotifyStatus = "starting";
        public string NotifyDebug() { return string.Format("ready={0} busy={1} polls={2} lastStart={3:HH:mm:ss.f} lastEnd={4:HH:mm:ss.f} seen={5} timer={6}", nReady, nBusy, nPolls, nPollStart, nPollEnd, nSeen.Count, nTimer != null && nTimer.IsEnabled); }

        async void InitAppNotifications()
        {
            try
            {
                nl = UserNotificationListener.Current;
                var st = nl.GetAccessStatus();
                if (st != UserNotificationListenerAccessStatus.Allowed) st = await RT.Run(nl.RequestAccessAsync());
                AppNotifyStatus = st == UserNotificationListenerAccessStatus.Allowed ? "active" : st == UserNotificationListenerAccessStatus.Denied ? "denied" : "not allowed yet";
                if (st != UserNotificationListenerAccessStatus.Allowed) { Log.Write("app notifications: access " + st); return; }
                // what is already in the notification centre is history: don't replay it
                foreach (var n in await RT.Run(nl.GetNotificationsAsync(NotificationKinds.Toast))) nSeen.Add(n.Id);
                nReady = true;
                nTimer = Timer(1500, delegate { PollAppNotifications(); });
                nTimer.Start();
            }
            catch (Exception ex) { AppNotifyStatus = "not available"; Log.Write("app notifications: " + ex.Message); }
        }

        async void PollAppNotifications()
        {
            if (!nReady || nBusy) return;
            nBusy = true; nPollStart = DateTime.Now; nPolls++;
            try
            {
                var list = await RT.Run(nl.GetNotificationsAsync(NotificationKinds.Toast));
                var now = new HashSet<uint>();
                foreach (var n in list)
                {
                    now.Add(n.Id);
                    if (!nSeen.Add(n.Id)) continue;
                    if (!S.EvApps) continue;                                       // off: just remember it
                    if ((DateTimeOffset.Now - n.CreationTime).TotalSeconds > 90) continue;   // stale (e.g. after sleep)
                    if (S.DoNotDisturb) { nMissed++; continue; }                   // quiet: count it for the summary
                    ShowAppNotification(n);
                }
                nSeen.IntersectWith(now);   // forget dismissed ones
            }
            catch (Exception ex) { Log.Write("app notifications poll: " + ex.Message); }
            finally { nBusy = false; nPollEnd = DateTime.Now; }
        }

        void ShowAppNotification(UserNotification n)
        {
            string app = "", aumid = "";
            try { app = n.AppInfo.DisplayInfo.DisplayName ?? ""; } catch { }
            try { aumid = n.AppInfo.AppUserModelId ?? ""; } catch { }
            if (S.NotifyMuted != null && S.NotifyMuted.Any(m => m.Trim().Length > 0 && app.IndexOf(m.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)) return;
            var texts = new List<string>();
            try
            {
                var b = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (b != null) texts = b.GetTextElements().Select(t => (t.Text ?? "").Trim()).Where(t => t.Length > 0).ToList();
            }
            catch { }
            string title = texts.Count > 0 ? texts[0] : (app.Length > 0 ? app : "Notification");
            string body = string.Join(" · ", texts.Skip(1)).Replace("\r", " ").Replace("\n", " ");
            if (!S.NotifyShowText) { title = app.Length > 0 ? app : "Notification"; body = "New notification"; }
            if (body.Length > 220) body = body.Substring(0, 220) + "…";
            // one card per app: a newer message from the same app updates it and bumps the count
            string key = "app:" + (aumid.Length > 0 ? aumid : app);
            int count = 1;
            if (cur != null && cur.Key == key) count = cur.Count + 1;
            else { var q = queue.FirstOrDefault(x => x.Key == key); if (q != null) count = q.Count + 1; }
            Post(new IslandEvent
            {
                Kind = "notify", Key = key, Priority = Pri.Notification, Duration = Math.Max(1500, S.AppNotifyMs),
                Title = title, Sub = body, App = app.Length > 0 ? app : "App", Count = count, Image = AppIcon(aumid), Queueable = true,
                Click = () => { Dismiss(); OpenApp(aumid, app); }
            });
        }

        // ---- Do not disturb
        int nMissed;
        public void SetDoNotDisturb(bool on)
        {
            if (S.DoNotDisturb == on) return;
            S.DoNotDisturb = on; Store.Save();
            if (on) { nMissed = 0; SystemEvent("", Theme.Accent, "Do not disturb", "App notifications are held until you turn it off", Pri.System, 2600, "dnd", null); }
            else
            {
                SystemEvent("", Theme.Accent, "Do not disturb off", nMissed == 0 ? "Nothing came in" : nMissed + (nMissed == 1 ? " notification" : " notifications") + " while you were away · they're in the Windows notification centre", Pri.System, 4000, "dnd", null);
                nMissed = 0;
            }
        }

        // ---- the notification card: app icon (+count), app · now, title, two lines of message,
        // a close button and a countdown line that stops while hovered
        View vNotify;
        Border nIconBox, nCountBadge, nBar; TextBlock nApp, nWhen, nTitle, nBody, nIconGlyph, nCountText;
        const double NotifyW = 420, NotifyBarInset = 40;

        void BuildNotifyView()
        {
            vNotify = NewView("notify", NotifyW, 84, false);
            var g = new Grid { Margin = new Thickness(12, 10, 8, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var iconHost = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            nIconBox = new Border { CornerRadius = new CornerRadius(12), Background = Theme.Raised };
            RenderOptions.SetBitmapScalingMode(nIconBox, BitmapScalingMode.HighQuality);
            nIconGlyph = K.Glyph("", 18, Theme.Text2); nIconBox.Child = nIconGlyph;
            iconHost.Children.Add(nIconBox);
            nCountText = K.TB("", 9.5, Brushes.White, FontWeights.Bold); nCountText.HorizontalAlignment = HorizontalAlignment.Center;
            nCountBadge = new Border { MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Theme.Red, Padding = new Thickness(4, 0, 4, 0), BorderBrush = Theme.Island, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -6, 0), Child = nCountText, Visibility = Visibility.Collapsed };
            iconHost.Children.Add(nCountBadge);
            g.Children.Add(iconHost);

            var txt = new StackPanel { Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            nApp = K.TB("", 10.5, Theme.Text2, FontWeights.SemiBold);
            nWhen = K.TB(" · now", 10.5, Theme.Text3, FontWeights.Normal);
            head.Children.Add(nApp); head.Children.Add(nWhen);
            nTitle = K.TB("", 13.5, Theme.Text, FontWeights.SemiBold); nTitle.Margin = new Thickness(0, 1, 0, 0);
            nBody = new TextBlock { FontSize = 12, Foreground = Theme.Text2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 16, MaxHeight = 32, Margin = new Thickness(0, 1, 0, 0) };
            txt.Children.Add(head); txt.Children.Add(nTitle); txt.Children.Add(nBody);
            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);

            var close = K.IconBtn("", 24, 9, Theme.Text3, () => Dismiss());
            close.VerticalAlignment = VerticalAlignment.Top; close.ToolTip = "Dismiss";
            Grid.SetColumn(close, 2);
            g.Children.Add(close);
            vNotify.Root.Children.Add(g);

            // kept clear of the rounded corners and the outline along the bottom edge
            nBar = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = K.Solid(Theme.A(Theme.Current.Text, 0.24)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(NotifyBarInset, 0, NotifyBarInset, 8), Width = 0, IsHitTestVisible = false };
            vNotify.Root.Children.Add(nBar);
        }

        View FillNotify(IslandEvent e)
        {
            nApp.Text = e.App ?? "";
            nTitle.Text = e.Title ?? "";
            nBody.Text = e.Sub ?? "";
            nBody.Visibility = string.IsNullOrEmpty(e.Sub) ? Visibility.Collapsed : Visibility.Visible;
            if (e.Image != null)
            {
                var ib = new ImageBrush(e.Image) { Stretch = Stretch.Uniform }; ib.Freeze();
                nIconBox.Background = ib; nIconGlyph.Visibility = Visibility.Collapsed;
            }
            else { nIconBox.Background = Theme.Raised; nIconGlyph.Visibility = Visibility.Visible; }
            bool wasCount = nCountBadge.Visibility == Visibility.Visible;
            nCountBadge.Visibility = e.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            nCountText.Text = e.Count > 9 ? "9+" : e.Count.ToString();
            if (e.Count > 1) K.PopIn(nCountBadge, wasCount ? 0.7 : 0.3, 380);
            if (!inPlaceFill) PopIcon(nIconBox);
            else K.FadeIn(nBody, 4, 260);   // a newer message from the same app slides in
            // two lines when the message needs them
            double textW = NotifyW - 12 - 44 - 12 - 6 - 24 - 8;
            var probe = new TextBlock { Text = nBody.Text, FontSize = 12, FontFamily = K.Font, TextWrapping = TextWrapping.Wrap };
            probe.Measure(new Size(textW, double.PositiveInfinity));
            bool twoLines = nBody.Visibility == Visibility.Visible && probe.DesiredSize.Height > 18;
            SetViewSize(vNotify, NotifyW, twoLines ? 92 : 78);
            NotifyBarRun(e.Duration);
            return vNotify;
        }

        // countdown line: shrinks over the time left, holds while hovered
        void NotifyBarRun(double ms)
        {
            if (nBar == null || cur == null || cur.Kind != "notify" || ms <= 0) return;
            double full = NotifyW - 2 * NotifyBarInset;
            if (nBar.ActualWidth <= 0 || !inPlaceFill) { nBar.BeginAnimation(WidthProperty, null); nBar.Width = full; }
            if (hover && S.PauseOnHover) return;
            nBar.BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(ms)));
        }
        void NotifyBarHold()
        {
            if (nBar == null) return;
            double w = nBar.ActualWidth;
            nBar.BeginAnimation(WidthProperty, null); nBar.Width = w;
        }

        // the app's own icon, from its Start-menu entry (cached per app)
        ImageSource AppIcon(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return null;
            ImageSource img;
            if (nIcons.TryGetValue(aumid, out img)) return img;
            string parsing = "shell:AppsFolder\\" + aumid;
            img = Native.ShellThumb(parsing, 64) ?? Native.ShellIcon(parsing);
            // some apps notify under a helper id (Firefox: "FirefoxToast-<id>"): try the part after the dash
            int dash = aumid.LastIndexOf('-');
            if (img == null && dash > 0 && dash < aumid.Length - 1)
            {
                string alt = "shell:AppsFolder\\" + aumid.Substring(dash + 1);
                img = Native.ShellThumb(alt, 64) ?? Native.ShellIcon(alt);
            }
            nIcons[aumid] = img;
            return img;
        }

        // Clicking a notification: bring the app forward. 1) its window if it's running,
        // 2) launch it through its Start-menu entry, 3) a path-style id ({known folder}\...\app.exe).
        // Never falls back to Explorer (an unknown shell:AppsFolder id just opens a folder window).
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        static readonly Dictionary<string, Environment.SpecialFolder> KnownFolders = new Dictionary<string, Environment.SpecialFolder>(StringComparer.OrdinalIgnoreCase)
        {
            { "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", Environment.SpecialFolder.System },
            { "{F38BF404-1D43-42F2-9305-67DE0B28FC23}", Environment.SpecialFolder.Windows },
            { "{6D809377-6AF0-444B-8957-A3773F02200E}", Environment.SpecialFolder.ProgramFiles },
            { "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", Environment.SpecialFolder.ProgramFilesX86 },
        };

        static void OpenApp(string aumid, string appName)
        {
            if (FocusRunning(aumid, appName)) return;
            if (string.IsNullOrEmpty(aumid)) return;
            var ids = new List<string> { aumid };
            int dash = aumid.LastIndexOf('-');
            if (dash > 0 && dash < aumid.Length - 1) ids.Add(aumid.Substring(dash + 1));   // FirefoxToast-<id> -> <id>
            foreach (var id in ids)
                if (AumidName(id) != null) { try { System.Diagnostics.Process.Start("explorer.exe", "shell:AppsFolder\\" + id); } catch { } return; }
            string exe = AumidPath(aumid);
            if (exe != null) { try { System.Diagnostics.Process.Start(exe); } catch { } }
        }

        static string AumidPath(string aumid)
        {
            foreach (var kv in KnownFolders)
                if (aumid.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    string p = System.IO.Path.Combine(Environment.GetFolderPath(kv.Value), aumid.Substring(kv.Key.Length).TrimStart('\\'));
                    return System.IO.File.Exists(p) ? p : null;
                }
            return System.IO.File.Exists(aumid) ? aumid : null;
        }

        // a visible top-level window of the app that sent it: matched by exe path, product or process name
        static bool FocusRunning(string aumid, string appName)
        {
            string exePath = AumidPath(aumid ?? "");
            string name = (appName ?? "").ToLowerInvariant();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || p.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                    bool match = false;
                    string pn = p.ProcessName.ToLowerInvariant();
                    if (exePath != null) { try { match = string.Equals(p.MainModule.FileName, exePath, StringComparison.OrdinalIgnoreCase); } catch { } }
                    if (!match && name.Length > 0)
                    {
                        if (pn.Length >= 3 && (name.Contains(pn) || pn.Contains(name.Replace(" ", "")))) match = true;
                        else
                        {
                            string prod = "";
                            try { prod = (p.MainModule.FileVersionInfo.ProductName ?? "").ToLowerInvariant(); } catch { }
                            if (prod.Length >= 3 && (name.Contains(prod) || prod.Contains(name))) match = true;
                        }
                    }
                    if (!match) continue;
                    var h = p.MainWindowHandle;
                    if (IsIconic(h)) ShowWindow(h, 9);   // SW_RESTORE
                    SetForegroundWindow(h);
                    return true;
                }
                catch { }
            }
            return false;
        }

        // apps seen in the notification centre, for the Settings mute list
        public async void RecentNotifyApps(Action<List<string>> done)
        {
            var names = new List<string>();
            try
            {
                if (nl != null)
                    foreach (var n in await RT.Run(nl.GetNotificationsAsync(NotificationKinds.Toast)))
                    {
                        string a = ""; try { a = n.AppInfo.DisplayInfo.DisplayName ?? ""; } catch { }
                        if (a.Length > 0 && !names.Contains(a)) names.Add(a);
                    }
            }
            catch { }
            done(names);
        }
    }
}
