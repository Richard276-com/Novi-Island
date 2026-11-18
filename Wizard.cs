// First-run setup wizard: welcome, install on this PC, name, look, pages, notifications & AI,
// shortcuts, done. Every choice applies live to the island behind it. Shown automatically on the
// very first start; reopen it from the tray menu, Settings → About, or "--ipc setup".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DynIsland
{
    class SetupWizard : Window
    {
        readonly IslandWindow isl;
        AppSettings S { get { return Store.S; } }
        readonly List<Func<FrameworkElement>> steps = new List<Func<FrameworkElement>>();
        readonly List<string> stepNames = new List<string>();
        Grid host; StackPanel dots; TextBlock stepLabel, nextText; Border backBtn, nextBtn; TextBlock skip;
        int cur = -1;
        // install choices
        bool doInstall = true, desktopLnk = false, startWithWindows = true, installedNow;
        string installError;

        public SetupWizard(IslandWindow isl)
        {
            this.isl = isl;
            Title = "Dynamic Island Setup";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Background = Theme.Surface;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0, 0, 0, 1), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(760, wa.Width - 40); Height = Math.Min(560, wa.Height - 40);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + (wa.Height - Height) / 2;
            K.CrispText(this, true);
            FontFamily = K.Font;
            Topmost = true;
            doInstall = !Installer.RunningInstalled;
            desktopLnk = Installer.HasDesktopShortcut;
            startWithWindows = Store.IsStartup() || Store.FirstRun;

            steps.Add(Welcome); stepNames.Add("Welcome");
            steps.Add(Install); stepNames.Add("Install");
            steps.Add(You); stepNames.Add("You");
            steps.Add(Look); stepNames.Add("Look");
            steps.Add(Pages); stepNames.Add("Pages");
            steps.Add(Notify); stepNames.Add("Notifications & AI");
            steps.Add(Keys); stepNames.Add("Shortcuts");
            steps.Add(Done); stepNames.Add("Done");
            Build();
            SourceInitialized += delegate
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int round = 2, dark = 1;
                Native.DwmSetWindowAttribute(h, 33, ref round, 4);
                Native.DwmSetWindowAttribute(h, 20, ref dark, 4);
            };
            PreviewKeyDown += (s, e) =>
            {
                if (Keyboard.FocusedElement is TextBox) { if (e.Key == Key.Enter) { e.Handled = true; Next(); } return; }
                if (e.Key == Key.Enter || e.Key == Key.Right) { e.Handled = true; Next(); }
                else if (e.Key == Key.Left || e.Key == Key.Back) { e.Handled = true; Go(cur - 1); }
                else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); }
            };
            Loaded += delegate { Go(0); };
        }

        void Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            root.MouseLeftButtonDown += (s, e) => { if (e.GetPosition(root).Y < 60) { try { DragMove(); } catch { } } };

            // top: step dots + "Step 2 of 8 · Install"
            var top = new Grid { Margin = new Thickness(32, 22, 22, 0) };
            dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < steps.Count; i++) dots.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Text3, Margin = new Thickness(0, 0, 5, 0) });
            stepLabel = K.TB("", 11.5, Theme.Text3, FontWeights.SemiBold); stepLabel.Margin = new Thickness(10, 0, 0, 0);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(dots); left.Children.Add(stepLabel);
            top.Children.Add(left);
            var close = K.IconBtn("", 30, 11, Theme.Text2, () => Finish(false)); close.HorizontalAlignment = HorizontalAlignment.Right; close.ToolTip = "Skip setup (Esc)";
            top.Children.Add(close);
            root.Children.Add(top);

            host = new Grid { Margin = new Thickness(40, 10, 40, 0), ClipToBounds = false };
            Grid.SetRow(host, 1);
            root.Children.Add(host);

            // bottom bar
            var bar = new Grid { Margin = new Thickness(32, 0, 28, 24) };
            skip = K.TB("Skip setup", 12, Theme.Text3, FontWeights.SemiBold); skip.Cursor = Cursors.Hand; skip.VerticalAlignment = VerticalAlignment.Center;
            skip.MouseLeftButtonUp += (s, e) => Finish(false);
            skip.MouseEnter += delegate { skip.Foreground = Theme.Text; };
            skip.MouseLeave += delegate { skip.Foreground = Theme.Text3; };
            bar.Children.Add(skip);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            backBtn = K.PillBtn("Back", Theme.Raised, Theme.Text, 38, () => Go(cur - 1)); backBtn.Width = 96;
            nextText = K.TB("Next", 13, Theme.OnAccent, FontWeights.SemiBold); nextText.HorizontalAlignment = HorizontalAlignment.Center;
            nextBtn = new Border { Height = 38, Width = 140, CornerRadius = new CornerRadius(19), Background = Theme.AccentFill, Child = nextText, Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand };
            K.Pressable(nextBtn, Next, false);
            btns.Children.Add(backBtn); btns.Children.Add(nextBtn);
            bar.Children.Add(btns);
            Grid.SetRow(bar, 2);
            root.Children.Add(bar);
        }

        public void ShowStep(int i) { Go(i); }   // test hook: view only, never runs the install

        void Go(int i)
        {
            if (i < 0 || i >= steps.Count || i == cur) return;
            int dir = i > cur ? 1 : -1;
            cur = i;
            for (int d = 0; d < dots.Children.Count; d++)
            {
                var b = (Border)dots.Children[d];
                b.Background = d <= cur ? Theme.Accent : Theme.Text3;
                b.BeginAnimation(WidthProperty, K.AnimRaw(d == cur ? 18 : 6, 320, K.OutBack));
            }
            stepLabel.Text = cur == 0 || cur == steps.Count - 1 ? "" : "Step " + cur + " of " + (steps.Count - 2) + " · " + stepNames[cur];
            backBtn.Visibility = cur == 0 ? Visibility.Hidden : Visibility.Visible;
            skip.Visibility = cur == steps.Count - 1 ? Visibility.Hidden : Visibility.Visible;
            nextText.Text = cur == 0 ? "Get started" : cur == 1 && doInstall && !Installer.RunningInstalled ? "Install" : cur == steps.Count - 1 ? "Finish" : "Next";
            // long steps scroll instead of running under the buttons
            FrameworkElement page = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = steps[cur](), Padding = new Thickness(0, 0, 0, 12) };
            var old =host.Children.Count > 0 ? (FrameworkElement)host.Children[0] : null;
            if (old != null)
            {
                var ot = new TranslateTransform(); old.RenderTransform = ot;
                ot.BeginAnimation(TranslateTransform.XProperty, K.AnimRaw(-40 * dir, 220, K.OutCubic));
                var of = K.AnimRaw(0, 180, K.OutCubic); of.Completed += delegate { host.Children.Remove(old); };
                old.BeginAnimation(OpacityProperty, of);
                old.IsHitTestVisible = false;
            }
            host.Children.Add(page);
            var tt = new TranslateTransform(50 * dir, 0); page.RenderTransform = tt; page.Opacity = 0;
            var a = K.AnimRaw(0, 420, K.OutQuint); a.BeginTime = TimeSpan.FromMilliseconds(60);
            tt.BeginAnimation(TranslateTransform.XProperty, a);
            var o = K.AnimRaw(1, 320, K.OutCubic); o.BeginTime = TimeSpan.FromMilliseconds(60);
            page.BeginAnimation(OpacityProperty, o);
        }

        void Next()
        {
            if (cur == 1)
            {
                if (Installer.RunningInstalled) { Installer.Install(desktopLnk); Installer.SetStartup(startWithWindows, Installer.InstalledExe); }   // refresh shortcuts
                else if (doInstall && !installedNow)
                {
                    nextText.Text = "Installing…";
                    installError = Installer.Install(desktopLnk);
                    if (installError != null) { var keep = cur; cur = -1; Go(keep); return; }   // re-render with the error
                    installedNow = true;
                    Installer.SetStartup(startWithWindows, Installer.InstalledExe);
                }
                else if (installedNow) Installer.SetStartup(startWithWindows, Installer.InstalledExe);
                else Store.SetStartup(startWithWindows);
            }
            if (cur == steps.Count - 1) { Finish(true); return; }
            Go(cur + 1);
        }

        void Finish(bool completed)
        {
            S.SetupDone = true;
            Store.SaveNow();
            Close();
            // just installed from somewhere else (e.g. Downloads): continue from the installed copy
            if (installedNow && !Installer.RunningInstalled)
            {
                try { Process.Start(Installer.InstalledExe, "--silent --restart"); } catch { }
                isl.Close();
                return;
            }
            if (completed) isl.Dispatcher.BeginInvoke(new Action(() => isl.Expand("home", true)), DispatcherPriority.Background);
        }

        void Changed() { Store.Save(); isl.ApplySettings(); }

        // ------------------------------------------------------------ building blocks
        static StackPanel Head(string title, string sub)
        {
            var sp = new StackPanel();
            sp.Children.Add(K.TB(title, 26, Theme.Text, FontWeights.SemiBold));
            var d = K.TB(sub, 13.5, Theme.Text2, FontWeights.Normal); d.TextWrapping = TextWrapping.Wrap; d.TextTrimming = TextTrimming.None; d.Margin = new Thickness(0, 6, 0, 18);
            sp.Children.Add(d);
            return sp;
        }

        static Grid Row(string title, string desc, FrameworkElement ctl)
        {
            var g = new Grid { Margin = new Thickness(16, 11, 16, 11), MinHeight = 30 };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            l.Children.Add(K.TB(title, 13.5, Theme.Text, FontWeights.Medium));
            if (!string.IsNullOrEmpty(desc)) { var d = K.TB(desc, 11.5, Theme.Text2, FontWeights.Normal); d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 2, 0, 0); l.Children.Add(d); }
            g.Children.Add(l);
            if (ctl != null) { ctl.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(ctl, 1); g.Children.Add(ctl); }
            return g;
        }

        static Border Card(params FrameworkElement[] rows)
        {
            var sp = new StackPanel();
            bool first = true;
            foreach (var r in rows)
            {
                if (r == null) continue;
                if (!first) sp.Children.Add(new Border { Height = 1, Background = Theme.Hairline, Margin = new Thickness(16, 0, 0, 0) });
                sp.Children.Add(r); first = false;
            }
            return new Border { CornerRadius = new CornerRadius(14), Background = Theme.Raised, Child = sp, Margin = new Thickness(0, 0, 0, 10) };
        }

        Toggle Tog(bool v, Action<bool> set) { var t = new Toggle(v); t.Changed += x => set(x); return t; }

        // a tiny drawing of the island (pill or notch) for the welcome and look steps
        static FrameworkElement IslandArt(bool notch, double w, double h, Brush fill)
        {
            var g = new Grid { Width = w + (notch ? 16 : 0), Height = h, HorizontalAlignment = HorizontalAlignment.Center };
            if (notch)
            {
                g.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(0, 0, h / 2.2, h / 2.2), Background = fill, HorizontalAlignment = HorizontalAlignment.Center });
                g.Children.Add(new Rectangle { Width = w + 16, Height = 2, Fill = fill, VerticalAlignment = VerticalAlignment.Top });
            }
            else g.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h / 2), Background = fill, HorizontalAlignment = HorizontalAlignment.Center });
            return g;
        }

        // ------------------------------------------------------------ steps
        FrameworkElement Welcome()
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
            // the island, breathing a little
            var art = new Grid { Height = 120, Width = 320 };
            var glow = new Border { Width = 220, Height = 56, CornerRadius = new CornerRadius(28), Background = Theme.AccentFill, Opacity = 0.35, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 40 } };
            art.Children.Add(glow);
            var pill = new Border { Width = 200, Height = 46, CornerRadius = new CornerRadius(23), Background = Brushes.Black, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1) };
            var pg = new Grid { Margin = new Thickness(14, 0, 16, 0) };
            pg.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = Theme.AccentFill, HorizontalAlignment = HorizontalAlignment.Left, Child = K.Glyph("", 12, Brushes.White) });
            var eq = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 4; i++)
            {
                var bar = new Border { Width = 3.5, Height = 16, CornerRadius = new CornerRadius(2), Background = Theme.Accent, Margin = new Thickness(1.5, 0, 1.5, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
                var st = new ScaleTransform(1, 0.4); bar.RenderTransform = st;
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(380 + i * 70)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                eq.Children.Add(bar);
            }
            pg.Children.Add(eq);
            pg.Children.Add(new TextBlock { Text = "Now playing", Foreground = Theme.Text, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            pill.Child = pg;
            var ps = new ScaleTransform(1, 1); pill.RenderTransform = ps; pill.RenderTransformOrigin = new Point(0.5, 0.5);
            var breathe = new DoubleAnimation(1, 1.04, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            ps.BeginAnimation(ScaleTransform.ScaleXProperty, breathe); ps.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
            art.Children.Add(pill);
            sp.Children.Add(art);
            var t = K.TB("Welcome to Dynamic Island", 30, Theme.Text, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(t);
            var d = K.TB("A living notch at the top of your screen: music, timers, notifications, files, AI chat and more.\nLet's set it up — it takes about a minute.", 14, Theme.Text2, FontWeights.Normal);
            d.TextAlignment = TextAlignment.Center; d.TextWrapping = TextWrapping.Wrap; d.TextTrimming = TextTrimming.None; d.MaxWidth = 520; d.Margin = new Thickness(0, 10, 0, 0); d.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(d);
            return sp;
        }

        FrameworkElement Install()
        {
            var sp = new StackPanel();
            if (Installer.RunningInstalled)
            {
                sp.Children.Add(Head("Installed", "Dynamic Island already runs from its install folder. Pick the extras you want."));
                sp.Children.Add(Card(Row("Location", Installer.Dir, K.TB("✓", 16, Theme.Green, FontWeights.Bold))));
            }
            else
            {
                sp.Children.Add(Head("Install on this PC", "Puts the app in your user folder (no admin needed), adds it to the Start menu and to Apps & features so it can be uninstalled any time."));
                var dirRow = Row("Install", "To " + Installer.Dir, null);
                var inst = Tog(doInstall, v => { doInstall = v; nextText.Text = v ? "Install" : "Next"; });
                ((Grid)dirRow).Children.Add(inst); Grid.SetColumn(inst, 1); inst.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(Card(dirRow));
            }
            sp.Children.Add(Card(
                Row("Start with Windows", "The island appears right after you sign in.", Tog(startWithWindows, v => startWithWindows = v)),
                Row("Desktop shortcut", null, Tog(desktopLnk, v => desktopLnk = v))));
            if (installError != null)
            {
                var e = K.TB("Couldn't install: " + installError, 12, Theme.Red, FontWeights.SemiBold); e.TextWrapping = TextWrapping.Wrap; e.Margin = new Thickness(4, 4, 0, 0);
                sp.Children.Add(e);
            }
            if (installedNow)
            {
                var ok = K.TB("✓ Installed. The island will continue from there when you finish.", 12, Theme.Green, FontWeights.SemiBold); ok.Margin = new Thickness(4, 4, 0, 0);
                sp.Children.Add(ok);
            }
            return sp;
        }

        FrameworkElement You()
        {
            var sp = new StackPanel();
            sp.Children.Add(Head("What should we call you?", "Your name appears in the greeting, on Home and in the AI chat."));
            var tb = new TextBox { Text = S.UserName ?? "" };
            var f = K.Field(tb, "Your name", 0, Theme.Raised);
            f.Height = 44; tb.FontSize = 16;
            tb.TextChanged += delegate { S.UserName = tb.Text.Trim(); Changed(); };
            sp.Children.Add(f);
            Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); tb.CaretIndex = tb.Text.Length; }), DispatcherPriority.Input);
            sp.Children.Add(new Border { Height = 14 });
            sp.Children.Add(Card(
                Row("Say hello on start", "A short greeting when the island starts.", Tog(S.Welcome, v => { S.Welcome = v; Changed(); })),
                Row("Preview", null, K.PillBtn("Show greeting", Theme.Surface, Theme.Text, 30, () => isl.PreviewWelcome()))));
            return sp;
        }

        FrameworkElement Look()
        {
            var sp = new StackPanel();
            sp.Children.Add(Head("Make it yours", "Everything changes live on the island at the top of your screen."));
            // style cards
            var styles = new UniformGrid { Columns = 2, Margin = new Thickness(-5, 0, -5, 10) };
            for (int i = 0; i < 2; i++)
            {
                int idx = i; bool sel = S.Style == i;
                var c = new Border { Height = 96, CornerRadius = new CornerRadius(14), Background = Theme.Raised, BorderThickness = new Thickness(2), BorderBrush = sel ? (Brush)Theme.Accent : Brushes.Transparent, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand };
                var cs = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                cs.Children.Add(IslandArt(i == 1, 110, 26, Brushes.Black));
                var l = K.TB(i == 0 ? "Pill" : "Notch", 13, Theme.Text, FontWeights.SemiBold); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 10, 0, 0);
                cs.Children.Add(l);
                var sub = K.TB(i == 0 ? "Floats below the top edge" : "Hangs from the top edge", 11, Theme.Text3, FontWeights.Normal); sub.HorizontalAlignment = HorizontalAlignment.Center;
                cs.Children.Add(sub);
                c.Child = cs;
                K.Pressable(c, () => { S.Style = idx; Changed(); var k = cur; cur = -1; Go(k); }, false);
                styles.Children.Add(c);
            }
            sp.Children.Add(styles);
            // theme swatches
            var pal = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var p in Theme.Palettes.Take(8))
            {
                var pl = p; bool sel = S.Palette == pl.Key;
                var sw = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = K.Solid(pl.Surface), BorderThickness = new Thickness(2), BorderBrush = sel ? (Brush)K.Solid(pl.Accent) : K.Solid(Theme.A(pl.Text, 0.15)), Margin = new Thickness(0, 0, 10, 8), Cursor = Cursors.Hand, ToolTip = pl.Label };
                sw.Child = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = K.Solid(pl.Accent) };
                K.Pressable(sw, () => { S.Palette = pl.Key; Changed(); var k = cur; cur = -1; Go(k); }, false);
                pal.Children.Add(sw);
            }
            var themeRow = new StackPanel { Margin = new Thickness(16, 12, 16, 6) };
            themeRow.Children.Add(K.TB("Theme", 13.5, Theme.Text, FontWeights.Medium));
            pal.Margin = new Thickness(0, 8, 0, 0);
            themeRow.Children.Add(pal);
            var font = new Segmented(new[] { "Inter", "Segoe UI", "JetBrains" }, S.Font == 3 ? 2 : S.Font == 1 ? 1 : 0, 86);
            font.Changed += i => { S.Font = i == 2 ? 3 : i; Changed(); };
            sp.Children.Add(Card(themeRow, Row("Typeface", null, font)));
            return sp;
        }

        FrameworkElement Pages()
        {
            var sp = new StackPanel();
            sp.Children.Add(Head("Pick your pages", "When the island is open you swipe through these. Turn off what you won't use — you can reorder them later in Settings → Pages."));
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-5, 0, -5, 6) };
            string[,] info = { { "media", "", "Media", "Controls for what's playing" }, { "ai", "", "AI chat", "Claude, Gemini or GPT" }, { "timer", "", "Timer", "Countdowns with presets" }, { "todo", "", "To-do", "A quick list" }, { "shelf", "", "Shelf", "Park files to drag later" }, { "system", "", "System", "CPU, memory, disk" }, { "games", "", "Games", "Blocks, Sudoku, Mines, Merge" } };
            for (int i = 0; i < info.GetLength(0); i++)
            {
                string key = info[i, 0];
                bool on = !S.HiddenPages.Contains(key);
                var g = new Grid { Margin = new Thickness(14, 10, 12, 10) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Theme.Surface, Child = K.Glyph(info[i, 1], 13, Theme.Text), Margin = new Thickness(0, 0, 10, 0) });
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                tx.Children.Add(K.TB(info[i, 2], 13, Theme.Text, FontWeights.Medium));
                tx.Children.Add(K.TB(info[i, 3], 10.5, Theme.Text3, FontWeights.Normal));
                Grid.SetColumn(tx, 1); g.Children.Add(tx);
                var t = Tog(on, v => { if (v) S.HiddenPages.Remove(key); else if (!S.HiddenPages.Contains(key)) S.HiddenPages.Add(key); Changed(); });
                Grid.SetColumn(t, 2); t.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(t);
                grid.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Theme.Raised, Margin = new Thickness(5, 0, 5, 10), Child = g });
            }
            sp.Children.Add(grid);
            return sp;
        }

        FrameworkElement Notify()
        {
            var sp = new StackPanel();
            sp.Children.Add(Head("Notifications & AI", "The island can show what your apps send, and talk to an AI. Both are optional."));
            string st = isl.AppNotifyStatus;
            sp.Children.Add(Card(
                Row("Notifications from other apps", st == "active" ? "WhatsApp, Discord, browsers, mail… on the island." : "Windows notification access: " + st + ". You can allow it later in Settings → Notifications.", Tog(S.EvApps, v => { S.EvApps = v; Changed(); })),
                Row("System events", "Volume, battery, charging, Bluetooth, Wi-Fi… (fine-tune later).", Tog(S.EvVolume || S.EvCharging, v => { S.EvVolume = S.EvBrightness = S.EvCharging = S.EvLowBattery = S.EvNetwork = S.EvBluetooth = S.EvDisplay = S.EvMic = S.EvClipboard = v; Changed(); }))));
            int pi = Math.Max(0, Array.IndexOf(AiService.Providers, S.AiProvider));
            var prov = new Segmented(new[] { "Claude", "Gemini", "GPT" }, pi, 78);
            prov.Changed += i => { S.AiProvider = AiService.Providers[i]; Changed(); };
            var sign = K.GlyphPill("", "Sign in", Theme.Surface, Theme.Text, 30, () => Svc.Ai.SignIn(S.AiProvider));
            var ai = new StackPanel { Orientation = Orientation.Horizontal };
            ai.Children.Add(prov); sign.Margin = new Thickness(10, 0, 0, 0); ai.Children.Add(sign);
            sp.Children.Add(Card(Row("AI chat", "Uses your own account through the official app (browser sign-in). Skip it if you don't want AI.", ai)));
            return sp;
        }

        FrameworkElement Keys()
        {
            var sp = new StackPanel();
            sp.Children.Add(Head("Good to know", "A few moves that make the island quick. All shortcuts can be changed in Settings → Shortcuts."));
            Func<string, string, FrameworkElement> key = (combo, what) =>
            {
                var keys = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var part in combo.Split('+'))
                {
                    keys.Children.Add(new Border { MinWidth = 26, Height = 24, CornerRadius = new CornerRadius(6), Background = Theme.Surface, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1, 1, 1, 2), Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(4, 0, 0, 0), Child = K.TB(part, 11.5, Theme.Text, FontWeights.SemiBold) });
                }
                return Row(what, null, keys);
            };
            Func<string, string> hk = id => S.Hotkeys.ContainsKey(id) && S.Hotkeys[id].Length > 0 ? S.Hotkeys[id] : Store.DefaultHotkeys[id];
            sp.Children.Add(Card(
                key(hk("toggle"), "Open / close the island"),
                key(hk("launcher"), "Search apps, calculate, open sites"),
                key(hk("ai"), "AI chat"),
                key(hk("wallpaper"), "Wallpaper picker"),
                key(hk("settings"), "Settings")));
            sp.Children.Add(Card(
                Row("Hover or click the island", "Opens it. Swipe, scroll or use ← → to change pages.", K.Glyph("", 16, Theme.Text2)),
                Row("Right-click the island", "Menu with Settings, wallpapers and more.", K.Glyph("", 16, Theme.Text2)),
                Row("Drop files on it", "They wait on the Shelf until you drag them out.", K.Glyph("", 16, Theme.Text2))));
            return sp;
        }

        FrameworkElement Done()
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
            var circle = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(42), Background = Theme.GreenSoft, HorizontalAlignment = HorizontalAlignment.Center };
            var chk = K.Glyph("", 36, Theme.Green);
            circle.Child = chk;
            sp.Children.Add(circle);
            K.PopIn(circle, 0.3, 600);
            string n = (S.UserName ?? "").Trim();
            var t = K.TB("You're all set" + (n.Length > 0 ? ", " + n : "") + "!", 28, Theme.Text, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 20, 0, 0);
            sp.Children.Add(t);
            var d = K.TB(installedNow ? "Dynamic Island is installed. Press Finish and it restarts from its new home — find it in the Start menu any time."
                : "Press Finish and the island opens so you can look around. Everything you picked can be changed in Settings.", 14, Theme.Text2, FontWeights.Normal);
            d.TextAlignment = TextAlignment.Center; d.TextWrapping = TextWrapping.Wrap; d.TextTrimming = TextTrimming.None; d.MaxWidth = 500; d.Margin = new Thickness(0, 10, 0, 0); d.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(d);
            return sp;
        }
    }
}
