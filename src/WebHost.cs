using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CDriveCleaner
{
    public class WebHost : Form
    {
        private WebView2 web;
        private Label loading;
        private bool isAdmin;
        private AppSettings settings;
        private CleanerEngine engine;
        private List<CleanItem> items;
        private JavaScriptSerializer json;
        private bool scanning;
        private bool cleaning;
        private bool spaceScanning;
        private CancellationTokenSource spaceCts;
        private string snapshotPath;
        private string snapshotPage;
        private bool snapshotStarted;
        private bool customMaximized;
        private Rectangle restoreBounds;
        private bool closingApps;

        private const int WM_NCHITTEST = 0x84;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public WebHost(string snapshotFilePath, string snapshotPageName)
        {
            snapshotPath = snapshotFilePath;
            snapshotPage = snapshotPageName;
            isAdmin = IsAdministrator();
#if DEV
            if (Environment.GetEnvironmentVariable("CDC_FORCE_ADMIN") == "1") isAdmin = true;
#endif
            settings = AppSettings.Load();
            engine = new CleanerEngine(isAdmin, settings);
            items = Catalog.Build(settings);
            json = new JavaScriptSerializer();
            json.MaxJsonLength = int.MaxValue;

            Text = "C盘清理大师";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            try
            {
                using (Graphics g = CreateGraphics())
                {
                    float scale = g.DpiX / 96f;
                    if (scale < 1f) scale = 1f;
                    Size = new Size((int)(1180 * scale), (int)(800 * scale));
                    MinimumSize = new Size((int)(760 * scale), (int)(480 * scale));
                }
            }
            catch
            {
                Size = new Size(1180, 800);
                MinimumSize = new Size(760, 480);
            }
            BackColor = Color.FromArgb(244, 246, 251);
            DoubleBuffered = true;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                if (Width <= wa.Width && Height <= wa.Height)
                {
                    Location = new Point(wa.Left + (wa.Width - Width) / 2,
                                         wa.Top + (wa.Height - Height) / 2);
                }
                else
                {
                    restoreBounds = new Rectangle(Location, Size);
                    Bounds = wa;
                    customMaximized = true;
                }
            }
            catch { }

            web = new WebView2();
            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(244, 246, 251);
            Controls.Add(web);

            loading = new Label();
            loading.Dock = DockStyle.Fill;
            loading.Text = "正在加载界面...";
            loading.TextAlign = ContentAlignment.MiddleCenter;
            loading.Font = new Font("Microsoft YaHei UI", 13F);
            loading.ForeColor = Color.FromArgb(100, 116, 139);
            loading.BackColor = Color.FromArgb(244, 246, 251);
            Controls.Add(loading);
            loading.BringToFront();

            Shown += async delegate { await InitializeWebAsync(); };
        }

        protected override void OnShown(EventArgs e)
        {
            try
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                MaximizedBounds = wa;
                if (Width > wa.Width || Height > wa.Height)
                {
                    restoreBounds = Bounds;
                    Bounds = wa;
                    customMaximized = true;
                }
                else
                {
                    Location = new Point(wa.Left + (wa.Width - Width) / 2,
                                         wa.Top + (wa.Height - Height) / 2);
                }
            }
            catch { }
            SetWindowCorner(!customMaximized);
            base.OnShown(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Maximized) SetWindowCorner(false);
            else if (!customMaximized) SetWindowCorner(true);
        }

        private void ToggleMaximize()
        {
            if (customMaximized)
            {
                Bounds = restoreBounds;
                customMaximized = false;
            }
            else
            {
                restoreBounds = Bounds;
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                Bounds = wa;
                customMaximized = true;
            }
            Dictionary<string, object> w = new Dictionary<string, object>();
            w["maximized"] = customMaximized;
            Post("windowState", w);
            SetWindowCorner(!customMaximized);
        }

        private void StartDrag()
        {
            if (customMaximized)
            {
                customMaximized = false;
                Bounds = restoreBounds;
                Dictionary<string, object> w = new Dictionary<string, object>();
                w["maximized"] = false;
                Post("windowState", w);
                SetWindowCorner(true);
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowCorner(!customMaximized);
        }

        private void SetWindowCorner(bool rounded)
        {
            try
            {
                int preference = rounded ? 2 : 1;
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
                int noBorder = unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(Handle, 34, ref noBorder, sizeof(int));
            }
            catch { }
        }

        private async Task InitializeWebAsync()
        {
            try
            {
                string userData = Path.Combine(AppSettings.DataDir, "WebView2");
                Directory.CreateDirectory(userData);
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userData, null);
                await web.EnsureCoreWebView2Async(env);

                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                web.CoreWebView2.Settings.IsZoomControlEnabled = false;
                web.CoreWebView2.WebMessageReceived += OnWebMessage;
                web.CoreWebView2.NavigationCompleted += delegate
                {
                    try { loading.Visible = false; } catch { }
                };
                web.NavigateToString(LoadHtml());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "WebView2 初始化失败。\r\n\r\n" + ex.Message +
                    "\r\n\r\n请确认已安装 Microsoft Edge WebView2 Runtime：\r\n" +
                    "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    "C盘清理大师", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private string LoadHtml()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("CDriveCleaner.ui.html"))
            {
                if (s == null) throw new InvalidOperationException("找不到嵌入的界面资源。");
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                Dictionary<string, object> msg = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                string type = msg.ContainsKey("type") ? Convert.ToString(msg["type"]) : "";
                Dictionary<string, object> p = null;
                if (msg.ContainsKey("payload") && msg["payload"] is Dictionary<string, object>)
                    p = (Dictionary<string, object>)msg["payload"];

                switch (type)
                {
                    case "ready":
                        SendState();
                        if (snapshotPath != null)
                        {
                            var scan = ScanAllAsync();
                            var snap = MaybeSnapshotAsync();
                        }
                        else if (settings.AutoScan)
                        {
                            var scan = ScanAllAsync();
                        }
                        break;

                    case "scan":
                        var scanTask = ScanAllAsync();
                        break;

                    case "clean":
                        List<string> ids = new List<string>();
                        if (p != null && p.ContainsKey("ids") && p["ids"] != null)
                        {
                            System.Collections.IEnumerable arr = p["ids"] as System.Collections.IEnumerable;
                            if (arr != null)
                            {
                                foreach (object o in arr)
                                {
                                    string id = Convert.ToString(o);
                                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                                }
                            }
                        }
                        var cleanTask = CleanAsync(ids);
                        break;

                    case "select":
                        if (p != null)
                        {
                            string id = Convert.ToString(p["id"]);
                            bool sel = Convert.ToBoolean(p["selected"]);
                            CleanItem it = items.FirstOrDefault(delegate(CleanItem x) { return x.Id == id; });
                            if (it != null && it.CanClean)
                            {
                                it.Selected = sel;
                                it.UserSelectionSet = true;
                            }
                            Post("summary", BuildSummary());
                        }
                        break;

                    case "selectAllSafe":
                        foreach (CleanItem it in items)
                        {
                            if (it.Risk == RiskLevel.Safe && it.CanClean)
                            {
                                it.Selected = true;
                                it.UserSelectionSet = true;
                            }
                        }
                        SendState();
                        break;

                    case "closeAndClean":
                        var closeTask = CloseRunningAndCleanAsync();
                        break;

                    case "elevate":
                        Elevate();
                        break;

                    case "window":
                        if (p != null && p.ContainsKey("action"))
                        {
                            string act = Convert.ToString(p["action"]);
                            if (act == "minimize") WindowState = FormWindowState.Minimized;
                            else if (act == "maximize") ToggleMaximize();
                            else if (act == "close") Close();
                        }
                        break;

                    case "drag":
                        StartDrag();
                        break;

                    case "spaceScan":
                        int threshold = 100;
                        if (p != null && p.ContainsKey("threshold")) threshold = Convert.ToInt32(p["threshold"]);
                        var spaceTask = SpaceScanAsync(threshold);
                        break;

                    case "spaceStop":
                        try { if (spaceCts != null) spaceCts.Cancel(); } catch { }
                        break;

                    case "openReport":
                        if (p != null && p.ContainsKey("path")) OpenFile(Convert.ToString(p["path"]));
                        break;

                    case "openFolder":
                        if (p != null && p.ContainsKey("path")) OpenFolder(Convert.ToString(p["path"]));
                        break;

                    case "openLogs":
                        OpenFolder(Path.Combine(AppSettings.DataDir, "logs"));
                        break;

                    case "openData":
                        OpenFolder(AppSettings.DataDir);
                        break;

                    case "openDsh":
                        OpenFolder(DshHelper.DshRoot);
                        break;

                    case "cleanmgr":
                        try { Process.Start(new ProcessStartInfo("cleanmgr.exe", "/d C:") { UseShellExecute = true }); } catch { }
                        break;

                    case "settings":
                        if (p != null) SaveSettings(p);
                        break;

                    case "refresh":
                        SendState();
                        break;
                }
            }
            catch (Exception ex)
            {
                PostToast("error", "操作失败：" + ex.Message);
            }
        }

        // ============================================================
        // 状态
        // ============================================================
        private void SendState()
        {
            Post("state", BuildState());
        }

        private Dictionary<string, object> BuildState()
        {
            Dictionary<string, object> s = new Dictionary<string, object>();
            s["admin"] = isAdmin;
            s["maximized"] = customMaximized || WindowState == FormWindowState.Maximized;
            s["scanning"] = scanning;
            s["cleaning"] = cleaning;
            s["spaceScanning"] = spaceScanning;

            long total = 0, free = 0;
            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                total = d.TotalSize;
                free = d.AvailableFreeSpace;
            }
            catch { }
            s["total"] = total;
            s["free"] = free;
            s["used"] = total - free;
            s["freeText"] = Fmt.Size(free);
            s["usedText"] = Fmt.Size(total - free);
            s["totalText"] = Fmt.Size(total);
            s["usedPercent"] = total > 0 ? (int)((total - free) * 100L / total) : 0;

            List<Dictionary<string, object>> its = new List<Dictionary<string, object>>();
            foreach (CleanItem it in items) its.Add(BuildItem(it));
            s["items"] = its;

            List<Dictionary<string, object>> running = new List<Dictionary<string, object>>();
            foreach (CleanItem it in items)
            {
                if (it.State != ItemState.Running) continue;
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["id"] = it.Id;
                r["name"] = it.Name;
                r["status"] = it.StatusText;
                r["processes"] = it.ProcessNames;
                running.Add(r);
            }
            s["runningItems"] = running;
            s["closing"] = closingApps;

            s["summary"] = BuildSummary();
            s["settings"] = BuildSettings();
            s["history"] = BuildHistory();
            s["version"] = "3.0";
            return s;
        }

        private Dictionary<string, object> BuildItem(CleanItem it)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["id"] = it.Id;
            d["group"] = it.Group;
            d["name"] = it.Name;
            d["description"] = it.Description;
            d["note"] = it.Note;
            d["risk"] = it.Risk == RiskLevel.Safe ? "safe" : (it.Risk == RiskLevel.Moderate ? "moderate" : "advanced");
            d["riskText"] = it.RiskText;
            d["requiresAdmin"] = it.RequiresAdmin;
            d["size"] = it.Size;
            d["sizeText"] = it.Size > 0 ? Fmt.Size(it.Size) : "—";
            d["files"] = it.FileCount;
            d["filesText"] = it.Scanned ? Fmt.Number(it.FileCount) : "—";
            d["status"] = it.StatusText;
            d["state"] = it.State.ToString().ToLowerInvariant();
            d["selected"] = it.Selected && it.CanClean;
            d["canClean"] = it.CanClean;
            d["special"] = it.Special.ToString();
            d["shortPath"] = it.ShortPath;
            return d;
        }

        private Dictionary<string, object> BuildSummary()
        {
            List<CleanItem> selected = items.Where(delegate(CleanItem i) { return i.Selected && i.CanClean; }).ToList();
            long total = 0;
            foreach (CleanItem i in selected) total += i.Size;
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["selected"] = selected.Count;
            d["selectedBytes"] = total;
            d["selectedText"] = Fmt.Size(total);
            d["cleanable"] = items.Count(delegate(CleanItem i) { return i.CanClean && i.Size > 0; });
            d["running"] = items.Count(delegate(CleanItem i) { return i.State == ItemState.Running; });
            d["admin"] = isAdmin;
            return d;
        }

        private Dictionary<string, object> BuildSettings()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["dshDays"] = settings.DshImageDays;
            d["showAdvanced"] = settings.ShowAdvanced;
            d["autoScan"] = settings.AutoScan;
            d["confirm"] = settings.ConfirmBeforeClean;
            d["cleanRunning"] = settings.CleanRunning;
            return d;
        }

        private List<Dictionary<string, object>> BuildHistory()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            foreach (HistoryEntry h in HistoryStore.Load())
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["time"] = h.Time.ToString("yyyy-MM-dd HH:mm");
                d["freed"] = h.Freed;
                d["freedText"] = Fmt.Size(h.Freed);
                d["deleted"] = h.Deleted;
                d["failed"] = h.Failed;
                d["admin"] = h.Admin;
                d["report"] = h.ReportPath;
                list.Add(d);
                if (list.Count >= 30) break;
            }
            return list;
        }

        private void Post(string type, object payload)
        {
            if (web == null || web.CoreWebView2 == null) return;
            try
            {
                Dictionary<string, object> msg = new Dictionary<string, object>();
                msg["type"] = type;
                if (payload != null) msg["payload"] = payload;
                web.CoreWebView2.PostWebMessageAsJson(json.Serialize(msg));
            }
            catch { }
        }

        private void PostSafe(string type, object payload)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(delegate { Post(type, payload); }));
                else Post(type, payload);
            }
            catch { }
        }

        private void PostToast(string kind, string message)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["kind"] = kind;
            d["message"] = message;
            PostSafe("toast", d);
        }

        // ============================================================
        // 扫描与清理
        // ============================================================
        private async Task ScanAllAsync()
        {
            if (scanning || cleaning) return;
            scanning = true;
            PostSafe("scanStart", null);
            int total = items.Count;
            for (int i = 0; i < total; i++)
            {
                int index = i;
                CleanItem it = items[i];
                await Task.Run(delegate
                {
                    try { engine.Scan(it); }
                    catch (Exception ex)
                    {
                        it.State = ItemState.Error;
                        it.StatusText = "扫描错误";
                        it.Note = ex.Message;
                    }
                });
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["index"] = index + 1;
                d["total"] = total;
                d["progress"] = total > 0 ? (int)((index + 1) * 100L / total) : 0;
                d["item"] = BuildItem(it);
                PostSafe("scanProgress", d);
            }
            scanning = false;
            PostSafe("state", BuildState());
            if (snapshotPath != null) await MaybeSnapshotAsync();
        }

        private async Task CleanAsync(List<string> ids)
        {
            if (scanning || cleaning) return;
            List<CleanItem> selected = new List<CleanItem>();
            foreach (CleanItem it in items)
            {
                if (ids.Contains(it.Id) && it.CanClean) selected.Add(it);
            }
            if (selected.Count == 0)
            {
                PostToast("warn", "没有可清理的项目。");
                return;
            }

            cleaning = true;
            Dictionary<string, object> start = new Dictionary<string, object>();
            start["total"] = selected.Count;
            PostSafe("cleanStart", start);

            RunLogger logger = new RunLogger();
            logger.Write("开始清理，管理员=" + isAdmin + "，项目数=" + selected.Count);
            List<CleanOutcome> outcomes = new List<CleanOutcome>();
            long freeBefore = GetFreeSpace();
            await Task.Run(delegate
            {
                int done = 0;
                foreach (CleanItem it in selected)
                {
                    CleanOutcome o = engine.Clean(it);
                    outcomes.Add(o);
                    done++;
                    logger.Write("完成: " + it.Name + "，释放 " + Fmt.Size(o.Freed) + "，删除 " + o.Deleted + "，失败 " + o.Failed);
                    Dictionary<string, object> p = new Dictionary<string, object>();
                    p["done"] = done;
                    p["total"] = selected.Count;
                    p["name"] = it.Name;
                    p["freedText"] = Fmt.Size(o.Freed);
                    PostSafe("cleanProgress", p);
                }
            });
            long freeAfter = GetFreeSpace();
            long freed = 0;
            int deleted = 0;
            int failed = 0;
            List<string> errors = new List<string>();
            foreach (CleanOutcome o in outcomes)
            {
                freed += o.Freed;
                deleted += o.Deleted;
                failed += o.Failed;
                foreach (string e in o.Errors)
                {
                    if (errors.Count < 30) errors.Add(e);
                }
                itSuccess(o);
            }
            string report = null;
            try
            {
                report = ReportWriter.WriteRun(outcomes, isAdmin, freeBefore, freeAfter, logger.Path);
                logger.Write("清理结束，报告=" + report);
            }
            catch (Exception ex)
            {
                logger.Write("写报告失败: " + ex.Message);
            }
            logger.Dispose();

            cleaning = false;
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["freed"] = freed;
            result["freedText"] = Fmt.Size(freed);
            result["deleted"] = deleted;
            result["failed"] = failed;
            result["report"] = report;
            result["errors"] = errors;
            PostSafe("cleanDone", result);
            PostSafe("state", BuildState());
        }

        private void itSuccess(CleanOutcome o)
        {
            o.Item.Selected = false;
            o.Item.UserSelectionSet = true;
            if (o.Success)
            {
                o.Item.CanClean = false;
                o.Item.State = ItemState.Cleaned;
                o.Item.StatusText = "已清理";
                o.Item.Size = 0;
                o.Item.FileCount = 0;
            }
            else
            {
                o.Item.CanClean = true;
                o.Item.State = ItemState.Ready;
                o.Item.StatusText = "部分失败，可重试";
            }
        }

        private async Task CloseRunningAndCleanAsync()
        {
            if (scanning || cleaning || closingApps) return;
            List<CleanItem> running = items.Where(delegate(CleanItem i) { return i.State == ItemState.Running; }).ToList();
            if (running.Count == 0)
            {
                PostToast("warn", "没有正在占用缓存的应用。");
                return;
            }

            closingApps = true;
            Dictionary<string, object> start = new Dictionary<string, object>();
            start["total"] = running.Count;
            PostSafe("closeStart", start);

            List<string> names = new List<string>();
            foreach (CleanItem it in running)
            {
                if (it.ProcessNames == null) continue;
                foreach (string n in it.ProcessNames)
                {
                    if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
                }
            }

            foreach (string n in names) CloseProcessGroup(n);
            await Task.Delay(900);
            await ScanAllAsync();
            closingApps = false;

            List<string> ids = new List<string>();
            foreach (CleanItem it in items)
            {
                if (it.CanClean && it.Risk == RiskLevel.Safe && it.Size > 0)
                    ids.Add(it.Id);
            }
            PostSafe("closeDone", null);
            if (ids.Count == 0)
            {
                PostToast("warn", "应用已关闭，但没有找到可安全清理的新缓存。");
                PostSafe("state", BuildState());
                return;
            }
            await CleanAsync(ids);
        }

        private static bool IsOfficeProcess(string name)
        {
            string[] protectedNames = new string[] {
                "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "ONENOTE",
                "MSACCESS", "MSPUB", "VISIO"
            };
            foreach (string p in protectedNames)
            {
                if (string.Equals(p, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void CloseProcessGroup(string name)
        {
            if (IsOfficeProcess(name)) return;
            Process[] processes = null;
            try { processes = Process.GetProcessesByName(name); }
            catch { return; }
            int current = Process.GetCurrentProcess().Id;
            foreach (Process p in processes)
            {
                try
                {
                    if (p.Id == current) continue;
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        p.CloseMainWindow();
                    }
                }
                catch { }
            }
            System.Threading.Thread.Sleep(1000);
            foreach (Process p in processes)
            {
                try
                {
                    if (p.Id != current && !p.HasExited)
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        private long GetFreeSpace()
        {
            try { return new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)).AvailableFreeSpace; }
            catch { return 0; }
        }

        // ============================================================
        // 空间分析
        // ============================================================
        private async Task SpaceScanAsync(int thresholdMb)
        {
            if (spaceScanning) return;
            spaceScanning = true;
            spaceCts = new CancellationTokenSource();
            PostSafe("spaceStart", null);
            try
            {
                Progress<SpaceProgress> progress = new Progress<SpaceProgress>(delegate(SpaceProgress p)
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["files"] = p.Files;
                    d["bytes"] = p.Bytes;
                    d["large"] = p.LargeFound;
                    PostSafe("spaceProgress", d);
                });
                long threshold = Math.Max(1, thresholdMb) * 1024L * 1024L;
                SpaceResult result = await Task.Run(delegate
                {
                    return SpaceAnalyzer.Scan(progress, spaceCts.Token, threshold, 200);
                });
                PostSafe("spaceDone", BuildSpace(result, false, ""));
            }
            catch (OperationCanceledException)
            {
                PostSafe("spaceDone", BuildSpace(null, true, ""));
            }
            catch (Exception ex)
            {
                PostSafe("spaceDone", BuildSpace(null, false, ex.Message));
            }
            finally
            {
                spaceScanning = false;
                if (spaceCts != null) { spaceCts.Dispose(); spaceCts = null; }
            }
        }

        private Dictionary<string, object> BuildSpace(SpaceResult r, bool cancelled, string error)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["cancelled"] = cancelled;
            d["error"] = error;
            if (r != null)
            {
                List<Dictionary<string, object>> files = new List<Dictionary<string, object>>();
                foreach (LargeFile f in r.LargeFiles)
                {
                    Dictionary<string, object> x = new Dictionary<string, object>();
                    x["path"] = f.Path;
                    x["size"] = f.Size;
                    x["sizeText"] = Fmt.Size(f.Size);
                    x["modified"] = f.Modified.ToString("yyyy-MM-dd HH:mm");
                    x["folder"] = Path.GetDirectoryName(f.Path);
                    files.Add(x);
                }
                List<Dictionary<string, object>> folders = new List<Dictionary<string, object>>();
                foreach (FolderSize f in r.Folders)
                {
                    if (folders.Count >= 10) break;
                    Dictionary<string, object> x = new Dictionary<string, object>();
                    x["path"] = f.Path;
                    x["size"] = f.Size;
                    x["sizeText"] = Fmt.Size(f.Size);
                    x["files"] = f.Files;
                    x["percent"] = r.TotalBytes > 0 ? (int)(f.Size * 100L / r.TotalBytes) : 0;
                    folders.Add(x);
                }
                d["files"] = files;
                d["folders"] = folders;
                d["totalBytes"] = r.TotalBytes;
                d["totalText"] = Fmt.Size(r.TotalBytes);
                d["totalFiles"] = r.TotalFiles;
                d["largeCount"] = r.LargeFiles.Count;
            }
            return d;
        }

        // ============================================================
        // 设置 / 工具
        // ============================================================
        private void SaveSettings(Dictionary<string, object> p)
        {
            try
            {
                if (p.ContainsKey("dshDays")) settings.DshImageDays = Math.Max(1, Math.Min(90, Convert.ToInt32(p["dshDays"])));
                if (p.ContainsKey("showAdvanced")) settings.ShowAdvanced = Convert.ToBoolean(p["showAdvanced"]);
                if (p.ContainsKey("autoScan")) settings.AutoScan = Convert.ToBoolean(p["autoScan"]);
                if (p.ContainsKey("confirm")) settings.ConfirmBeforeClean = Convert.ToBoolean(p["confirm"]);
                if (p.ContainsKey("cleanRunning")) settings.CleanRunning = Convert.ToBoolean(p["cleanRunning"]);
                settings.Save();
                engine = new CleanerEngine(isAdmin, settings);
                items = Catalog.Build(settings);
                PostToast("ok", "设置已保存，正在重新扫描。");
                SendState();
                var scan = ScanAllAsync();
            }
            catch (Exception ex)
            {
                PostToast("error", "保存设置失败：" + ex.Message);
            }
        }

        private void Elevate()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("未获得管理员权限：" + ex.Message, "C盘清理大师",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OpenFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        // ============================================================
        // 截图模式
        // ============================================================
        private async Task MaybeSnapshotAsync()
        {
            if (snapshotPath == null || snapshotStarted) return;
            snapshotStarted = true;
            try
            {
                for (int i = 0; i < 80 && scanning; i++) await Task.Delay(250);
                await Task.Delay(700);
                if (!string.IsNullOrEmpty(snapshotPage))
                {
                    if (snapshotPage == "max")
                    {
                        ToggleMaximize();
                        await Task.Delay(900);
                    }
                    else if (snapshotPage == "space-menu")
                    {
                        Dictionary<string, object> p = new Dictionary<string, object>();
                        p["page"] = "space";
                        Post("navigate", p);
                        await Task.Delay(700);
                        try { await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('thresholdTrigger').click();"); } catch { }
                        await Task.Delay(500);
                    }
                    else
                    {
                        string pageName = snapshotPage;
                        if (pageName == "0") pageName = "dashboard";
                        else if (pageName == "1") pageName = "space";
                        else if (pageName == "2") pageName = "history";
                        else if (pageName == "3") pageName = "settings";
                        Dictionary<string, object> p = new Dictionary<string, object>();
                        p["page"] = pageName;
                        Post("navigate", p);
                        await Task.Delay(700);
                    }
                }
                using (FileStream fs = new FileStream(snapshotPath, FileMode.Create, FileAccess.Write))
                {
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                }
            }
            catch { }
            try { Close(); } catch { }
        }

        // ============================================================
        // 窗口边缘拖拽
        // ============================================================
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && !customMaximized && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1)
                {
                    int lp = m.LParam.ToInt32();
                    Point screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                    Point p = PointToClient(screen);
                    int edge = 7;
                    bool left = p.X <= edge;
                    bool right = p.X >= ClientSize.Width - edge;
                    bool top = p.Y <= edge;
                    bool bottom = p.Y >= ClientSize.Height - edge;
                    if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                    else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }
    }
}
