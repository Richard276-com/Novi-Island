// Launcher data (apps, calculator, URLs, commands) and the wallpaper service.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynIsland
{
    class OnlineWall { public string Id, Provider, Thumb, Full, Page, Title, Res; public bool Nsfw; public System.Windows.Media.ImageSource ThumbImg; }

    // Online wallpaper sources. All calls are blocking: run them off the UI thread.
    static class OnlineWallpapers
    {
        public static readonly string[] Providers = { "wallhaven", "konachan", "picsum", "bing" };
        public static string Label(string p) { return p == "wallhaven" ? "Wallhaven" : p == "konachan" ? "Konachan" : p == "picsum" ? "Unsplash" : "Bing"; }
        public static string[] Sorts(string p)
        {
            if (p == "wallhaven") return new[] { "Top", "Hot", "New", "Random" };
            if (p == "konachan") return new[] { "Top", "New", "Random" };
            if (p == "picsum") return new[] { "Browse", "Random" };
            return new[] { "Daily" };
        }
        public static bool Searchable(string p) { return p == "wallhaven" || p == "konachan"; }
        static readonly int seed = new Random().Next(100000, 999999);

        static OnlineWallpapers()
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12 | (System.Net.SecurityProtocolType)12288;
            System.Net.ServicePointManager.DefaultConnectionLimit = 12;
        }

        public static byte[] Get(string url, int timeout)
        {
            var r = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            r.UserAgent = AppInfo.UserAgent; r.Timeout = timeout; r.ReadWriteTimeout = timeout;
            r.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            using (var resp = r.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }

        static object Json(string url) { return Store.Js().DeserializeObject(Encoding.UTF8.GetString(Get(url, 20000))); }
        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        static string Abs(string u) { return u.StartsWith("//") ? "https:" + u : u; }

        // Settings choice -> wallhaven purity bits (sfw, sketchy, nsfw)
        // the purity bits in use: the free mix from the picker chips, else the Settings choice
        public static string PurityBits()
        {
            string b = Store.S.WallhavenPurityBits ?? "";
            return b.Length == 3 && b.Contains('1') && b.All(c => c == '0' || c == '1') ? b : Purity(Store.S.WallhavenPurity);
        }

        public static string Purity(int choice)
        {
            switch (choice) { case 1: return "110"; case 2: return "111"; case 3: return "001"; }
            return "100";
        }

        public static List<OnlineWall> Fetch(string p, int sort, string query, int page, out bool more)
        {
            var res = new List<OnlineWall>(); more = false;
            query = (query ?? "").Trim();
            if (p == "wallhaven")
            {
                string[] sorts = { "toplist", "hot", "date_added", "random" };
                // a search is an overall search: best matches first, no time window (Top = last month only)
                string s = query.Length > 0 ? "relevance" : sorts[Math.Max(0, Math.Min(3, sort))];
                string key = (Store.Unprotect(Store.S.WallhavenKeyEnc) ?? "").Trim();
                // purity bits sfw/sketchy/nsfw; NSFW only comes back with the API key of an account that allows it
                string purity = PurityBits();
                if (purity[2] == '1' && key.Length == 0) throw new Exception("NSFW needs your Wallhaven API key — Settings → Launcher & Wallpapers");
                string cats = Store.S.WallhavenCategories;
                string url = "https://wallhaven.cc/api/v1/search?categories=" + cats + "&purity=" + purity +
                    "&sorting=" + s + (s == "toplist" ? "&topRange=1M" : "") + (s == "random" ? "&seed=" + seed : "") +
                    "&atleast=" + Uri.EscapeDataString(Store.S.WallhavenAtleast ?? "") + "&ratios=landscape&page=" + page +
                    (query.Length > 0 ? "&q=" + Uri.EscapeDataString(query) : "") + (key.Length > 0 ? "&apikey=" + Uri.EscapeDataString(key) : "");
                Dictionary<string, object> d;
                try { d = Json(url) as Dictionary<string, object>; }
                catch (System.Net.WebException wex)
                {
                    var hr = wex.Response as System.Net.HttpWebResponse;
                    if (hr != null && hr.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new Exception("Wallhaven rejected the API key. Check it, and turn on NSFW in your Wallhaven account settings.");
                    throw;
                }
                foreach (var w in ((object[])d["data"]).OfType<Dictionary<string, object>>())
                {
                    var th = w["thumbs"] as Dictionary<string, object>;
                    res.Add(new OnlineWall { Provider = p, Id = Str(w, "id"), Full = Str(w, "path"), Thumb = Str(th, "large"), Page = Str(w, "url"), Res = Str(w, "resolution"), Title = "wallhaven " + Str(w, "id"), Nsfw = Str(w, "purity") == "nsfw" });
                }
                var meta = d.ContainsKey("meta") ? d["meta"] as Dictionary<string, object> : null;
                int cur, last;
                more = meta != null && int.TryParse(Str(meta, "current_page"), out cur) && int.TryParse(Str(meta, "last_page"), out last) && cur < last;
            }
            else if (p == "konachan")
            {
                // searching ignores the sort tabs: every matching post, newest first
                string tags = ("rating:safe " + query + (query.Length > 0 ? "" : sort == 0 ? " order:score" : sort == 2 ? " order:random" : "")).Trim();
                var arr = Json("https://konachan.net/post.json?limit=24&page=" + page + "&tags=" + Uri.EscapeDataString(tags)) as object[];
                foreach (var w in (arr ?? new object[0]).OfType<Dictionary<string, object>>())
                {
                    if (Str(w, "rating") != "s") continue;
                    string full = Str(w, "jpeg_url").Length > 0 ? Str(w, "jpeg_url") : Str(w, "file_url");
                    res.Add(new OnlineWall { Provider = p, Id = Str(w, "id"), Full = Abs(full), Thumb = Abs(Str(w, "preview_url")), Page = "https://konachan.net/post/show/" + Str(w, "id"), Res = Str(w, "width") + "x" + Str(w, "height"), Title = "konachan " + Str(w, "id") });
                }
                more = arr != null && arr.Length >= 24;
            }
            else if (p == "picsum")
            {
                int pg = sort == 1 ? new Random().Next(1, 40) : page;
                var arr = Json("https://picsum.photos/v2/list?page=" + pg + "&limit=24") as object[];
                foreach (var w in (arr ?? new object[0]).OfType<Dictionary<string, object>>())
                {
                    string id = Str(w, "id");
                    res.Add(new OnlineWall { Provider = p, Id = id, Thumb = "https://picsum.photos/id/" + id + "/400/250", Full = "https://picsum.photos/id/" + id + "/3840/2160", Page = Str(w, "url"), Res = Str(w, "width") + "x" + Str(w, "height"), Title = Str(w, "author") });
                }
                more = arr != null && arr.Length > 0;
            }
            else
            {
                if (page > 2) return res;
                var d = Json("https://www.bing.com/HPImageArchive.aspx?format=js&idx=" + (page == 1 ? 0 : 8) + "&n=8&mkt=en-US") as Dictionary<string, object>;
                foreach (var w in ((object[])d["images"]).OfType<Dictionary<string, object>>())
                {
                    string b = Str(w, "urlbase");
                    string id = b.Contains("id=") ? b.Substring(b.IndexOf("id=") + 3) : b.GetHashCode().ToString();
                    res.Add(new OnlineWall { Provider = p, Id = id, Thumb = "https://www.bing.com" + b + "_400x240.jpg", Full = "https://www.bing.com" + b + "_UHD.jpg", Page = Str(w, "copyrightlink"), Res = "3840x2160", Title = Str(w, "title") });
                }
                more = page < 2;
            }
            return res;
        }

        public static BitmapImage Thumb(OnlineWall w)
        {
            try
            {
                var bytes = Get(w.Thumb, 15000);
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 260; bi.StreamSource = new MemoryStream(bytes); bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // Downloads the original into the wallpaper folder; returns the local path.
        public static string Download(OnlineWall w, string dir, Action<double> progress)
        {
            Directory.CreateDirectory(dir);
            string ext = Path.GetExtension(new Uri(w.Full).AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
            string safeId = Regex.Replace(w.Id, @"[^\w.-]", "_");
            if (safeId.Length > 60) safeId = safeId.Substring(0, 60);
            string path = Path.Combine(dir, w.Provider + "-" + safeId + ext);
            if (File.Exists(path)) return path;
            var r = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(w.Full);
            r.UserAgent = AppInfo.UserAgent; r.Timeout = 30000; r.ReadWriteTimeout = 60000;
            string tmp = path + ".part";
            using (var resp = r.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var f = File.Create(tmp))
            {
                long total = resp.ContentLength, got = 0;
                var buf = new byte[65536]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    f.Write(buf, 0, n); got += n;
                    if (total > 0 && progress != null) progress((double)got / total);
                }
            }
            File.Move(tmp, path);
            return path;
        }
    }

    class AppEntry
    {
        public string Name, Path, Lower, Initials;
        public ImageSource Icon;
    }

    class LaunchResult
    {
        public string Kind, Title, Sub, Glyph, Key;
        public ImageSource Icon;
        public Action Run;
        public string CopyText;
        public double Score;
    }

    class AppIndex
    {
        public List<AppEntry> Apps = new List<AppEntry>();
        public int Count { get { return Apps.Count; } }
        public event Action Updated;
        Dispatcher ui; bool indexing;

        public void Init(Dispatcher d) { ui = d; Reindex(null); }

        public void Reindex(Action done)
        {
            if (indexing) { if (done != null) done(); return; }
            indexing = true;
            var t = new Thread(() =>
            {
                List<AppEntry> list;
                try { list = Enumerate(); } catch (Exception ex) { Log.Write("apps: " + ex.Message); list = new List<AppEntry>(); }
                ui.BeginInvoke(new Action(() => { if (list.Count > 0) Apps = list; if (Updated != null) Updated(); if (done != null) done(); }));
                // Icons, progressively, on this STA thread.
                int n = 0;
                foreach (var a in list)
                {
                    try { a.Icon = Native.ShellIcon("shell:AppsFolder\\" + a.Path); } catch { }
                    if (++n % 40 == 0) ui.BeginInvoke(new Action(() => { if (Updated != null) Updated(); }));
                }
                ui.BeginInvoke(new Action(() => { indexing = false; if (Updated != null) Updated(); }));
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static object Call(object o, string m, params object[] a) { return o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, a); }
        static object Get(object o, string p) { return o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null); }

        static List<AppEntry> Enumerate()
        {
            var res = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            object folder = Call(shell, "NameSpace", "shell:AppsFolder");
            object items = Call(folder, "Items");
            int count = Convert.ToInt32(Get(items, "Count"));
            for (int i = 0; i < count; i++)
            {
                object it = Call(items, "Item", i);
                if (it == null) continue;
                string name = Convert.ToString(Get(it, "Name")), path = Convert.ToString(Get(it, "Path"));
                Marshal.ReleaseComObject(it);
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue;
                string low = name.ToLowerInvariant();
                if (low.StartsWith("uninstall") || low.Contains("uninstaller") || low.EndsWith(" readme") || !seen.Add(name + "|" + path)) continue;
                res.Add(new AppEntry { Name = name, Path = path, Lower = low, Initials = new string(Regex.Split(low, @"[\s\-_.]+").Where(w => w.Length > 0).Select(w => w[0]).ToArray()) });
            }
            Marshal.ReleaseComObject(items); Marshal.ReleaseComObject(folder); Marshal.ReleaseComObject(shell);
            return res.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public void Launch(AppEntry a)
        {
            try { Process.Start("explorer.exe", "shell:AppsFolder\\" + a.Path); } catch (Exception ex) { Log.Write("launch: " + ex.Message); }
            int c; Store.S.LaunchCounts.TryGetValue(a.Path, out c);
            Store.S.LaunchCounts[a.Path] = c + 1; Store.Save();
        }

        static double Match(AppEntry a, string q)
        {
            if (a.Lower.StartsWith(q)) return 100 - Math.Min(20, a.Lower.Length - q.Length) * 0.2;
            if (Regex.IsMatch(a.Lower, @"(^|[\s\-_.(])" + Regex.Escape(q))) return 80;
            if (a.Initials.StartsWith(q) && q.Length >= 2) return 70;
            if (a.Lower.Contains(q)) return 50;
            int j = 0;
            foreach (char c in a.Lower) if (j < q.Length && c == q[j]) j++;
            return j == q.Length && q.Length >= 3 ? 20 : 0;
        }

        public List<LaunchResult> Search(string query, IList<LaunchResult> actions)
        {
            var res = new List<LaunchResult>();
            string q = (query ?? "").Trim();
            if (q.Length == 0) return res;
            string ql = q.ToLowerInvariant();
            if (ql.StartsWith("?"))
            {
                string term = q.Substring(1).Trim();
                if (term.Length > 0) res.Add(new LaunchResult { Kind = "web", Title = "Search \"" + term + "\"", Sub = EngineName(), Glyph = "", Run = () => Open(SearchUrl(term)), Score = 1000 });
                return res;
            }
            if (ql.StartsWith(">") || ql.StartsWith(":"))
            {
                string cmd = q.Substring(1).Trim();
                if (cmd.Length > 0) res.Add(new LaunchResult { Kind = "cmd", Title = cmd, Sub = "Run in terminal", Glyph = "", Run = () => RunCommand(cmd), Score = 1000 });
                return res;
            }
            string calc = Calc.TryEval(q);
            if (calc != null) res.Add(new LaunchResult { Kind = "calc", Title = calc, Sub = q + "  ·  Enter to copy", Glyph = "", CopyText = calc, Score = 900 });
            if (IsUrl(q)) res.Add(new LaunchResult { Kind = "url", Title = q, Sub = "Open in browser", Glyph = "", Run = () => Open(q.Contains("://") ? q : "https://" + q), Score = 850 });
            foreach (var a in Apps)
            {
                double s = Match(a, ql);
                if (s <= 0) continue;
                int c; Store.S.LaunchCounts.TryGetValue(a.Path, out c);
                var app = a;
                res.Add(new LaunchResult { Kind = "app", Title = a.Name, Sub = "App", Icon = a.Icon, Glyph = "", Key = a.Path, Run = () => Launch(app), Score = s + Math.Log(c + 1) * 12 });
            }
            if (actions != null)
                foreach (var act in actions)
                    if (act.Title.ToLowerInvariant().Contains(ql) || (act.Key ?? "").Contains(ql)) { act.Score = 60; res.Add(act); }
            // Loose subsequence matches only fill in when there are few real matches.
            if (res.Count(r => r.Kind == "app" && r.Score >= 50) >= 3) res.RemoveAll(r => r.Kind == "app" && r.Score < 50);
            res = res.OrderByDescending(r => r.Score).Take(30).ToList();
            if (res.Count == 0 || res[0].Kind == "app" || res.All(r => r.Kind != "web"))
                res.Add(new LaunchResult { Kind = "web", Title = "Search the web for \"" + q + "\"", Sub = EngineName(), Glyph = "", Run = () => Open(SearchUrl(q)), Score = 1 });
            return res;
        }

        public List<LaunchResult> Frequent(int n)
        {
            return Apps.Select(a => { int c; Store.S.LaunchCounts.TryGetValue(a.Path, out c); return new { a, c }; })
                .OrderByDescending(x => x.c).ThenBy(x => x.a.Name).Take(n)
                .Select(x => { var app = x.a; return new LaunchResult { Kind = "app", Title = app.Name, Icon = app.Icon, Glyph = "", Key = app.Path, Run = () => Launch(app) }; }).ToList();
        }

        static string EngineName() { return Store.S.SearchEngine == "duckduckgo" ? "DuckDuckGo" : Store.S.SearchEngine == "bing" ? "Bing" : "Google"; }
        static string SearchUrl(string q)
        {
            string e = Uri.EscapeDataString(q);
            return Store.S.SearchEngine == "duckduckgo" ? "https://duckduckgo.com/?q=" + e : Store.S.SearchEngine == "bing" ? "https://www.bing.com/search?q=" + e : "https://www.google.com/search?q=" + e;
        }
        static bool IsUrl(string q)
        {
            if (q.Contains(" ")) return false;
            return Regex.IsMatch(q, @"^(https?://)?(localhost|(\d{1,3}\.){3}\d{1,3}|([a-z0-9-]+\.)+[a-z]{2,})(:\d+)?(/\S*)?$", RegexOptions.IgnoreCase) && !Regex.IsMatch(q, @"^\d+(\.\d+)*$");
        }
        public static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        static void RunCommand(string cmd) { try { Process.Start(new ProcessStartInfo("cmd.exe", "/k " + cmd) { UseShellExecute = true }); } catch { } }
    }

    // Small expression evaluator: + - * / ^ %, parentheses, functions, "15% of 80".
    static class Calc
    {
        public static string TryEval(string input)
        {
            string s = input.Trim().ToLowerInvariant();
            if (s.Length == 0) return null;
            var m = Regex.Match(s, @"^([\d.,]+)\s*%\s*(of|dari)\s*([\d.,]+)$");
            if (m.Success)
            {
                double a, b;
                if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                    double.TryParse(m.Groups[3].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                    return Fmt(a / 100 * b);
            }
            if (!Regex.IsMatch(s, @"[\d)]") || !Regex.IsMatch(s, @"[+\-*/^%()]|sqrt|sin|cos|tan|log|ln|abs|pi")) return null;
            if (Regex.IsMatch(s, @"^[\d.,]+$")) return null;
            try
            {
                var p = new P(s.Replace("×", "*").Replace("÷", "/").Replace(",", "."));
                double v = p.Expr();
                p.Ws();
                if (p.i != p.s.Length || double.IsNaN(v) || double.IsInfinity(v)) return null;
                return Fmt(v);
            }
            catch { return null; }
        }

        static string Fmt(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-10 && Math.Abs(v) < 1e15) return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
            return v.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        class P
        {
            public readonly string s; public int i;
            public P(string s) { this.s = s; }
            public void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            bool Eat(char c) { Ws(); if (i < s.Length && s[i] == c) { i++; return true; } return false; }
            public double Expr()
            {
                double v = Term();
                while (true) { if (Eat('+')) v += Term(); else if (Eat('-')) v -= Term(); else return v; }
            }
            double Term()
            {
                double v = Pow();
                while (true)
                {
                    if (Eat('*')) v *= Pow();
                    else if (Eat('/')) v /= Pow();
                    else if (Eat('%')) { Ws(); if (i < s.Length && (char.IsDigit(s[i]) || s[i] == '(' || s[i] == '.')) v %= Pow(); else v /= 100; }
                    else return v;
                }
            }
            double Pow() { double b = Unary(); return Eat('^') ? Math.Pow(b, Pow()) : b; }
            double Unary() { if (Eat('-')) return -Unary(); if (Eat('+')) return Unary(); return Atom(); }
            double Atom()
            {
                Ws();
                if (Eat('(')) { double v = Expr(); if (!Eat(')')) throw new Exception(); return v; }
                int st = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (i > st) return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
                while (i < s.Length && char.IsLetter(s[i])) i++;
                string f = s.Substring(st, i - st);
                if (f == "pi") return Math.PI;
                if (f == "e") return Math.E;
                if (f.Length == 0) throw new Exception();
                double a = Unary();
                switch (f)
                {
                    case "sqrt": return Math.Sqrt(a);
                    case "sin": return Math.Sin(a * Math.PI / 180);
                    case "cos": return Math.Cos(a * Math.PI / 180);
                    case "tan": return Math.Tan(a * Math.PI / 180);
                    case "log": return Math.Log10(a);
                    case "ln": return Math.Log(a);
                    case "abs": return Math.Abs(a);
                    case "round": return Math.Round(a);
                }
                throw new Exception();
            }
        }
    }

    static class WallpaperService
    {
        static readonly string[] Exts = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".jfif" };
        static string seedPath; static Color seed = Theme.Hex("#5b7cfa");

        public static string Current()
        {
            var sb = new StringBuilder(520);
            Native.SystemParametersInfo(0x73, 520, sb, 0);
            return sb.ToString();
        }

        public static List<string> List(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return new List<string>();
                return Directory.GetFiles(dir).Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }

        public static BitmapImage Thumb(string path, int width)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = width; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public static void Apply(string path, int fit, Action done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                    {
                        if (k != null)
                        {
                            k.SetValue("WallpaperStyle", fit == 1 ? "6" : fit == 2 ? "0" : fit == 3 ? "22" : "10");
                            k.SetValue("TileWallpaper", "0");
                        }
                    }
                    Native.SystemParametersInfo(0x14, 0, path, 3);
                }
                catch (Exception ex) { Log.Write("wallpaper: " + ex.Message); }
                if (done != null) done();
            });
        }

        // Most vivid colour of the current wallpaper (for the "Wallpaper" palette).
        public static Color CurrentSeed()
        {
            string p = Current();
            if (p == seedPath || string.IsNullOrEmpty(p)) return seed;
            seedPath = p;
            try
            {
                var bi = Thumb(p, 48);
                if (bi == null) return seed;
                var fc = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                int w = fc.PixelWidth, h = fc.PixelHeight; var px = new byte[w * h * 4];
                fc.CopyPixels(px, w * 4, 0);
                var buckets = new double[36]; var sums = new double[36, 3];
                for (int i = 0; i < px.Length; i += 4)
                {
                    var c = Color.FromRgb(px[i + 2], px[i + 1], px[i]);
                    double hh, s, l; Theme.ToHsl(c, out hh, out s, out l);
                    double wgt = s * (1 - Math.Abs(l - 0.5) * 2);
                    int bkt = (int)(hh * 36) % 36;
                    buckets[bkt] += wgt; sums[bkt, 0] += c.R * wgt; sums[bkt, 1] += c.G * wgt; sums[bkt, 2] += c.B * wgt;
                }
                int best = 0;
                for (int i = 1; i < 36; i++) if (buckets[i] > buckets[best]) best = i;
                if (buckets[best] > 0.01)
                    seed = Color.FromRgb((byte)(sums[best, 0] / buckets[best]), (byte)(sums[best, 1] / buckets[best]), (byte)(sums[best, 2] / buckets[best]));
            }
            catch { }
            return seed;
        }
    }
}
