using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CDriveCleaner
{
    public class CleanerEngine
    {
        public bool IsAdmin;
        public AppSettings Settings;

        public CleanerEngine(bool isAdmin, AppSettings settings)
        {
            IsAdmin = isAdmin;
            Settings = settings;
        }

        public void Scan(CleanItem item)
        {
            item.Size = 0;
            item.FileCount = 0;
            item.PartialAccess = false;
            item.Scanned = true;
            item.Extra = "";
            item.State = ItemState.Unscanned;
            item.CanClean = false;

            bool tooNew = false;
            bool inUse = false;
            bool processRunning = item.ProcessNames != null && item.ProcessNames.Length > 0 &&
                                  ProcessUtil.IsAnyRunning(item.ProcessNames);

            foreach (CleanPath p in item.Paths)
            {
                int files;
                bool partial;
                item.Size += SafeFs.Measure(p, out files, out partial);
                item.FileCount += files;
                if (partial) item.PartialAccess = true;

                if (p.Mode == PathMode.WholeDirectory)
                {
                    try
                    {
                        string root = p.Path;
                        if (Directory.Exists(root))
                        {
                            if (p.MinAgeDays > 0 &&
                                Directory.GetLastWriteTime(root) > DateTime.Now.AddDays(-p.MinAgeDays))
                            {
                                tooNew = true;
                            }
                            if (ProcessUtil.IsPathInUse(root)) inUse = true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (item.Special == SpecialAction.Hibernation)
            {
                item.Size = HibernateHelper.GetHibernateFileSize();
                item.FileCount = item.Size > 0 ? 1 : 0;
            }
            else if (item.Special == SpecialAction.Dism)
            {
                item.Size = 0;
                item.FileCount = 0;
            }
            else if (item.Special == SpecialAction.DshUpdater)
            {
                DshUpdaterState s = DshHelper.GetUpdaterState();
                item.Extra = s.InstalledVersion ?? "";
                if (s.PendingExists && !s.PendingMatches)
                {
                    item.State = ItemState.Skipped;
                    item.StatusText = "保留待安装更新";
                    item.CanClean = false;
                    item.Size = s.Size;
                    item.FileCount = s.Files;
                    item.Selected = false;
                    return;
                }
            }

            if (item.Special == SpecialAction.Dism)
            {
                if (!IsAdmin)
                {
                    item.State = ItemState.NeedAdmin;
                    item.StatusText = "需要管理员";
                }
                else
                {
                    item.State = ItemState.Ready;
                    item.StatusText = "运行后释放";
                    item.CanClean = true;
                }
            }
            else if (item.RequiresAdmin && !IsAdmin)
            {
                item.State = ItemState.NeedAdmin;
                item.StatusText = "需要管理员";
            }
            else if (processRunning && !item.AllowWhileRunning &&
                     (Settings == null || !Settings.CleanRunning))
            {
                item.State = ItemState.Running;
                item.StatusText = "运行中";
            }
            else if (inUse)
            {
                item.State = ItemState.Running;
                item.StatusText = "目录被占用";
            }
            else if (tooNew)
            {
                item.State = ItemState.Skipped;
                item.StatusText = "目录较新，跳过";
            }
            else if (item.Size <= 0 && item.Special != SpecialAction.Dism)
            {
                item.State = ItemState.Empty;
                item.StatusText = "无缓存";
            }
            else
            {
                item.State = ItemState.Ready;
                if (processRunning && !item.AllowWhileRunning && Settings != null && Settings.CleanRunning)
                    item.StatusText = "运行中（仅清未占用）";
                else
                    item.StatusText = item.PartialAccess ? "部分不可访问" : "可清理";
                item.CanClean = true;
            }

            if (item.CanClean && item.Special == SpecialAction.Dism)
            {
                item.Selected = false;
            }
            else if (!item.UserSelectionSet)
            {
                item.Selected = item.DefaultChecked && item.CanClean;
            }
            else if (!item.CanClean)
            {
                item.Selected = false;
            }
        }

        public CleanOutcome Clean(CleanItem item)
        {
            CleanOutcome outcome = new CleanOutcome(item);
            List<string> errors = new List<string>();

            if (!item.CanClean)
            {
                outcome.Success = false;
                outcome.Message = "当前状态不可清理：" + item.StatusText;
                return outcome;
            }
            if (item.RequiresAdmin && !IsAdmin)
            {
                outcome.Success = false;
                outcome.Message = "需要管理员权限。";
                return outcome;
            }
            if (!item.AllowWhileRunning && item.ProcessNames != null && item.ProcessNames.Length > 0 &&
                (Settings == null || !Settings.CleanRunning) &&
                ProcessUtil.IsAnyRunning(item.ProcessNames))
            {
                outcome.Success = false;
                outcome.Message = "应用正在运行，已跳过。";
                return outcome;
            }

            try
            {
                switch (item.Special)
                {
                    case SpecialAction.RecycleBin:
                        RecycleBinHelper.Empty(outcome, errors);
                        break;

                    case SpecialAction.WindowsUpdate:
                        errors.AddRange(ServiceHelper.WithServices(
                            new string[] { "wuauserv", "bits" },
                            delegate { DeleteAllPaths(item, outcome, errors); }));
                        break;

                    case SpecialAction.DeliveryOptimization:
                        errors.AddRange(ServiceHelper.WithServices(
                            new string[] { "DoSvc" },
                            delegate { DeleteAllPaths(item, outcome, errors); }));
                        break;

                    case SpecialAction.FontCache:
                        errors.AddRange(ServiceHelper.WithServices(
                            new string[] { "FontCache" },
                            delegate { DeleteAllPaths(item, outcome, errors); }));
                        break;

                    case SpecialAction.DshUpdater:
                        DshHelper.CleanUpdater(errors, outcome);
                        break;

                    case SpecialAction.Dism:
                        DismCleanup(outcome, errors);
                        break;

                    case SpecialAction.Hibernation:
                        HibernateHelper.Disable(outcome, errors);
                        break;

                    default:
                        DeleteAllPaths(item, outcome, errors);
                        break;
                }
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                errors.Add(ex.Message);
            }

            outcome.Errors.AddRange(errors);
            if (errors.Count > 0 && outcome.Failed == 0)
            {
                outcome.Failed = errors.Count;
            }
            outcome.Success = outcome.Success && outcome.Failed == 0;
            if (string.IsNullOrEmpty(outcome.Message))
            {
                if (outcome.Failed == 0) outcome.Message = "已清理。";
                else outcome.Message = "部分文件未能删除。";
            }
            return outcome;
        }

        private void DeleteAllPaths(CleanItem item, CleanOutcome outcome, List<string> errors)
        {
            foreach (CleanPath p in item.Paths)
            {
                if (p.Mode == PathMode.WholeDirectory && ProcessUtil.IsPathInUse(p.Path))
                {
                    errors.Add(p.Path + "  ::  目录正被进程使用");
                    outcome.Failed++;
                    continue;
                }
                CleanOutcomeResult r = SafeFs.Delete(p, errors);
                outcome.Freed += r.Freed;
                outcome.Deleted += r.Deleted;
                outcome.Failed += r.Failed;
            }
        }

        private void DismCleanup(CleanOutcome outcome, List<string> errors)
        {
            string output;
            string err;
            int code = ProcessRunner.Run("dism.exe",
                "/Online /Cleanup-Image /StartComponentCleanup /NoRestart",
                1800000, out output, out err);
            outcome.Message = "DISM 组件清理完成，退出代码 " + code + "。具体释放空间请查看系统组件目录。";
            if (code != 0)
            {
                outcome.Success = false;
                if (!string.IsNullOrEmpty(err)) errors.Add(err.Trim());
                else errors.Add("DISM 退出代码 " + code);
            }
        }
    }

    public static class RecycleBinHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public static void Empty(CleanOutcome outcome, List<string> errors)
        {
            long before;
            int countBefore;
            bool partial;
            before = SafeFs.MeasureDirectory(@"C:\$Recycle.Bin", out countBefore, out partial);

            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            if (hr != 0)
            {
                errors.Add("清空回收站失败，HRESULT=0x" + hr.ToString("X8"));
                outcome.Success = false;
            }

            long after;
            int countAfter;
            after = SafeFs.MeasureDirectory(@"C:\$Recycle.Bin", out countAfter, out partial);
            outcome.Freed = Math.Max(0, before - after);
            outcome.Deleted = Math.Max(0, countBefore - countAfter);
            outcome.Message = "回收站已清空。";
        }
    }

    public static class HibernateHelper
    {
        private static string FilePath
        {
            get { return Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "hiberfil.sys"); }
        }

        public static long GetHibernateFileSize()
        {
            try
            {
                FileInfo fi = new FileInfo(FilePath);
                if (fi.Exists) return fi.Length;
            }
            catch
            {
            }
            return 0;
        }

        public static void Disable(CleanOutcome outcome, List<string> errors)
        {
            long before = GetHibernateFileSize();
            string output;
            string err;
            int code = ProcessRunner.Run("powercfg.exe", "/h off", 60000, out output, out err);
            long after = GetHibernateFileSize();
            outcome.Freed = Math.Max(0, before - after);
            outcome.Deleted = after == 0 && before > 0 ? 1 : 0;
            outcome.Message = after == 0 ? "休眠已关闭，休眠文件已删除。" : "休眠文件仍存在，可能被系统占用。";
            if (code != 0)
            {
                outcome.Success = false;
                if (!string.IsNullOrEmpty(err)) errors.Add(err.Trim());
            }
        }
    }

    public static class ProcessRunner
    {
        public static int Run(string fileName, string arguments, int timeoutMs, out string output, out string error)
        {
            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            object gate = new object();

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            int exitCode = -1;
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (gate) stdout.AppendLine(e.Data);
                    }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (gate) stderr.AppendLine(e.Data);
                    }
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    lock (gate) stderr.AppendLine("进程超时，已终止。");
                }
                else
                {
                    p.WaitForExit();
                }
                try { exitCode = p.ExitCode; } catch { }
            }

            lock (gate)
            {
                output = stdout.ToString();
                error = stderr.ToString();
            }
            return exitCode;
        }
    }

    public class RunLogger : IDisposable
    {
        private StreamWriter writer;
        public string Path;

        public RunLogger()
        {
            string dir = System.IO.Path.Combine(AppSettings.DataDir, "logs");
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, "clean-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            writer = new StreamWriter(Path, false, new UTF8Encoding(true));
            writer.AutoFlush = true;
        }

        public void Write(string message)
        {
            try
            {
                writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message);
            }
            catch
            {
            }
        }

        public void Write(string format, params object[] args)
        {
            Write(string.Format(format, args));
        }

        public void Dispose()
        {
            try { writer.Dispose(); } catch { }
        }
    }

    public static class ReportWriter
    {
        public static string WriteRun(List<CleanOutcome> outcomes, bool admin, long freeBefore, long freeAfter, string logPath)
        {
            string dir = System.IO.Path.Combine(AppSettings.DataDir, "reports");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "clean-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html");

            long totalFreed = 0;
            int totalDeleted = 0;
            int totalFailed = 0;
            foreach (CleanOutcome o in outcomes)
            {
                totalFreed += o.Freed;
                totalDeleted += o.Deleted;
                totalFailed += o.Failed;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine("<title>C盘清理报告</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:'Microsoft YaHei UI','Segoe UI',sans-serif;background:#f4f6fb;color:#0f172a;margin:0;padding:32px;}");
            sb.AppendLine(".wrap{max-width:1100px;margin:0 auto;}");
            sb.AppendLine("h1{font-size:26px;margin:0 0 6px 0;}h2{font-size:18px;margin:28px 0 10px 0;}");
            sb.AppendLine(".sub{color:#64748b;margin-bottom:20px;}");
            sb.AppendLine(".cards{display:flex;gap:14px;flex-wrap:wrap;margin:18px 0 8px 0;}");
            sb.AppendLine(".card{background:#fff;border:1px solid #e2e8f0;border-radius:14px;padding:16px 18px;min-width:150px;flex:1;}");
            sb.AppendLine(".card .k{color:#64748b;font-size:12px;margin-bottom:8px;}.card .v{font-size:22px;font-weight:700;}");
            sb.AppendLine(".green{color:#059669;}.red{color:#dc2626;}.blue{color:#2563eb;}");
            sb.AppendLine("table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;}");
            sb.AppendLine("th,td{padding:10px 12px;border-bottom:1px solid #eef2f7;text-align:left;font-size:13px;vertical-align:top;}");
            sb.AppendLine("th{background:#f8fafc;color:#475569;font-weight:600;}tr:last-child td{border-bottom:none;}");
            sb.AppendLine("td.num{text-align:right;white-space:nowrap;}.mono{font-family:Consolas,monospace;font-size:12px;color:#64748b;}");
            sb.AppendLine(".ok{color:#059669;}.warn{color:#d97706;}.err{color:#dc2626;}");
            sb.AppendLine(".note{background:#fff;border:1px solid #e2e8f0;border-radius:12px;padding:14px 16px;color:#475569;font-size:13px;line-height:1.8;}");
            sb.AppendLine("</style></head><body><div class=\"wrap\">");
            sb.AppendLine("<h1>C盘清理报告</h1>");
            sb.AppendLine("<div class=\"sub\">" + Fmt.Time(DateTime.Now) + " · " + (admin ? "管理员模式" : "普通模式") + "</div>");
            sb.AppendLine("<div class=\"cards\">");
            sb.AppendLine("<div class=\"card\"><div class=\"k\">本次释放</div><div class=\"v green\">" + Fmt.Size(totalFreed) + "</div></div>");
            sb.AppendLine("<div class=\"card\"><div class=\"k\">删除文件</div><div class=\"v\">" + totalDeleted.ToString("N0") + "</div></div>");
            sb.AppendLine("<div class=\"card\"><div class=\"k\">失败/占用</div><div class=\"v " + (totalFailed > 0 ? "red" : "green") + "\">" + totalFailed.ToString("N0") + "</div></div>");
            sb.AppendLine("<div class=\"card\"><div class=\"k\">C盘可用空间</div><div class=\"v blue\">" + Fmt.Size(freeAfter) + "</div><div class=\"k\">清理前 " + Fmt.Size(freeBefore) + "</div></div>");
            sb.AppendLine("</div>");

            sb.AppendLine("<h2>清理明细</h2>");
            sb.AppendLine("<table><thead><tr><th>项目</th><th>类别</th><th>释放</th><th>文件</th><th>失败</th><th>说明</th></tr></thead><tbody>");
            foreach (CleanOutcome o in outcomes)
            {
                string status = o.Success ? "ok" : "warn";
                string msg = Fmt.EscapeHtml(o.Message);
                if (o.Errors.Count > 0)
                {
                    msg += "<div class=\"mono\">" + Fmt.EscapeHtml(o.Errors[0]) + "</div>";
                    if (o.Errors.Count > 1)
                        msg += "<div class=\"mono\">... 另有 " + (o.Errors.Count - 1) + " 条错误</div>";
                }
                sb.AppendLine("<tr><td>" + Fmt.EscapeHtml(o.Item.Name) + "</td><td>" + Fmt.EscapeHtml(o.Item.Group) +
                    "</td><td class=\"num\">" + Fmt.Size(o.Freed) + "</td><td class=\"num\">" + o.Deleted.ToString("N0") +
                    "</td><td class=\"num " + (o.Failed > 0 ? "err" : "") + "\">" + o.Failed.ToString("N0") +
                    "</td><td class=\"" + status + "\">" + msg + "</td></tr>");
            }
            sb.AppendLine("</tbody></table>");

            sb.AppendLine("<h2>说明</h2><div class=\"note\">");
            sb.AppendLine("缓存删除后应用会按需重建；不会删除文档、下载、桌面、聊天记录或账号凭据。<br>");
            sb.AppendLine("被应用占用的文件会自动跳过；关闭相关应用后重新扫描即可清理。<br>");
            sb.AppendLine("系统级项目需要管理员权限；Windows 更新缓存清理不会卸载已安装更新。<br>");
            sb.AppendLine("日志：" + Fmt.EscapeHtml(logPath));
            sb.AppendLine("</div></div></body></html>");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

            HistoryEntry h = new HistoryEntry();
            h.Time = DateTime.Now;
            h.Freed = totalFreed;
            h.Deleted = totalDeleted;
            h.Failed = totalFailed;
            h.Admin = admin;
            h.ReportPath = path;
            h.LogPath = logPath;
            HistoryStore.Append(h);
            return path;
        }
    }

    public static class HistoryStore
    {
        public static string HistoryPath
        {
            get { return System.IO.Path.Combine(AppSettings.DataDir, "history.tsv"); }
        }

        public static void Append(HistoryEntry h)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(h.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(h.Freed.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(h.Deleted.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(h.Failed.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(h.Admin ? "1" : "0").Append('\t');
                sb.Append(h.ReportPath ?? "").Append('\t');
                sb.Append(h.LogPath ?? "");
                sb.AppendLine();
                File.AppendAllText(HistoryPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch
            {
            }
        }

        public static List<HistoryEntry> Load()
        {
            List<HistoryEntry> list = new List<HistoryEntry>();
            try
            {
                if (!File.Exists(HistoryPath)) return list;
                foreach (string line in File.ReadAllLines(HistoryPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 7) continue;
                    HistoryEntry h = new HistoryEntry();
                    DateTime t;
                    if (!DateTime.TryParse(parts[0], out t)) continue;
                    h.Time = t;
                    long freed;
                    long.TryParse(parts[1], out freed);
                    h.Freed = freed;
                    int deleted;
                    int.TryParse(parts[2], out deleted);
                    h.Deleted = deleted;
                    int failed;
                    int.TryParse(parts[3], out failed);
                    h.Failed = failed;
                    h.Admin = parts[4] == "1";
                    h.ReportPath = parts[5];
                    h.LogPath = parts[6];
                    list.Add(h);
                }
            }
            catch
            {
            }
            list.Sort(delegate(HistoryEntry a, HistoryEntry b) { return b.Time.CompareTo(a.Time); });
            return list;
        }
    }

    public class LargeFile
    {
        public string Path;
        public long Size;
        public DateTime Modified;

        public LargeFile(string path, long size, DateTime modified)
        {
            Path = path;
            Size = size;
            Modified = modified;
        }
    }

    public class FolderSize
    {
        public string Path;
        public long Size;
        public int Files;

        public FolderSize(string path, long size, int files)
        {
            Path = path;
            Size = size;
            Files = files;
        }
    }

    public class SpaceProgress
    {
        public long Files;
        public long Bytes;
        public int LargeFound;
        public string Current;
    }

    public class SpaceResult
    {
        public List<LargeFile> LargeFiles = new List<LargeFile>();
        public List<FolderSize> Folders = new List<FolderSize>();
        public long TotalBytes;
        public long TotalFiles;
        public long LargeBytes;
    }

    public static class SpaceAnalyzer
    {
        private static readonly string[] Excluded = new string[] {
            @"C:\Windows\WinSxS",
            @"C:\Windows\Installer",
            @"C:\Windows\System32\DriverStore",
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\$WinREAgent",
            @"C:\$Windows.~BT",
            @"C:\$Windows.~WS"
        };

        public static SpaceResult Scan(IProgress<SpaceProgress> progress, CancellationToken token,
                                        long threshold, int maxLargeFiles)
        {
            SpaceResult result = new SpaceResult();
            Dictionary<string, long> folderBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> folderFiles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            string root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(root)) root = "C:\\";

            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            long filesSeen = 0;
            long bytesSeen = 0;

            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string dir = stack.Pop();

                if (IsExcluded(dir)) continue;

                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(dir);
                }
                catch
                {
                    continue;
                }

                foreach (string entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    FileAttributes attr;
                    try
                    {
                        attr = File.GetAttributes(entry);
                    }
                    catch
                    {
                        continue;
                    }
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;

                    if ((attr & FileAttributes.Directory) != 0)
                    {
                        if (!IsExcluded(entry)) stack.Push(entry);
                        continue;
                    }

                    try
                    {
                        FileInfo fi = new FileInfo(entry);
                        if (!fi.Exists) continue;
                        filesSeen++;
                        bytesSeen += fi.Length;
                        result.TotalFiles++;
                        result.TotalBytes += fi.Length;

                        string key = TopFolder(root, fi.FullName);
                        long old;
                        if (folderBytes.TryGetValue(key, out old)) folderBytes[key] = old + fi.Length;
                        else folderBytes[key] = fi.Length;
                        int oldCount;
                        if (folderFiles.TryGetValue(key, out oldCount)) folderFiles[key] = oldCount + 1;
                        else folderFiles[key] = 1;

                        if (fi.Length >= threshold)
                        {
                            result.LargeFiles.Add(new LargeFile(fi.FullName, fi.Length, fi.LastWriteTime));
                            result.LargeBytes += fi.Length;
                        }

                        if (filesSeen % 1000 == 0)
                        {
                            if (result.LargeFiles.Count > maxLargeFiles * 2)
                            {
                                result.LargeFiles.Sort(delegate(LargeFile a, LargeFile b) { return b.Size.CompareTo(a.Size); });
                                if (result.LargeFiles.Count > maxLargeFiles)
                                    result.LargeFiles.RemoveRange(maxLargeFiles, result.LargeFiles.Count - maxLargeFiles);
                            }
                            if (progress != null)
                            {
                                SpaceProgress sp = new SpaceProgress();
                                sp.Files = filesSeen;
                                sp.Bytes = bytesSeen;
                                sp.LargeFound = result.LargeFiles.Count;
                                sp.Current = dir;
                                progress.Report(sp);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            result.LargeFiles.Sort(delegate(LargeFile a, LargeFile b) { return b.Size.CompareTo(a.Size); });
            if (result.LargeFiles.Count > maxLargeFiles)
                result.LargeFiles.RemoveRange(maxLargeFiles, result.LargeFiles.Count - maxLargeFiles);

            foreach (KeyValuePair<string, long> kv in folderBytes)
            {
                int count = 0;
                folderFiles.TryGetValue(kv.Key, out count);
                result.Folders.Add(new FolderSize(kv.Key, kv.Value, count));
            }
            result.Folders.Sort(delegate(FolderSize a, FolderSize b) { return b.Size.CompareTo(a.Size); });

            if (progress != null)
            {
                SpaceProgress sp = new SpaceProgress();
                sp.Files = filesSeen;
                sp.Bytes = bytesSeen;
                sp.LargeFound = result.LargeFiles.Count;
                sp.Current = "完成";
                progress.Report(sp);
            }
            return result;
        }

        private static bool IsExcluded(string path)
        {
            foreach (string ex in Excluded)
            {
                if (path.Equals(ex, StringComparison.OrdinalIgnoreCase)) return true;
                if (path.StartsWith(ex + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string TopFolder(string root, string path)
        {
            string rel = path.Substring(root.Length).TrimStart('\\');
            int idx = rel.IndexOf('\\');
            if (idx <= 0) return "(根目录)";
            return Path.Combine(root, rel.Substring(0, idx));
        }
    }

    public static class Cli
    {
        public static int RunScan(string outputPath)
        {
            AppSettings settings = AppSettings.Load();
            List<CleanItem> items = Catalog.Build(settings);
            CleanerEngine engine = new CleanerEngine(false, settings);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Id\tGroup\tName\tRisk\tAdmin\tSize\tFiles\tStatus\tState\tPath");
            foreach (CleanItem item in items)
            {
                try
                {
                    engine.Scan(item);
                }
                catch (Exception ex)
                {
                    item.State = ItemState.Error;
                    item.StatusText = "扫描错误: " + ex.Message;
                }
                string first = item.Paths.Count > 0 ? item.Paths[0].Path : "";
                sb.Append(item.Id).Append('\t');
                sb.Append(item.Group).Append('\t');
                sb.Append(item.Name).Append('\t');
                sb.Append(item.RiskText).Append('\t');
                sb.Append(item.RequiresAdmin ? "1" : "0").Append('\t');
                sb.Append(item.Size.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(item.FileCount.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(item.StatusText).Append('\t');
                sb.Append(item.State.ToString()).Append('\t');
                sb.Append(first.Replace("\t", " ")).AppendLine();
            }
            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(true));
            return 0;
        }
    }
}
