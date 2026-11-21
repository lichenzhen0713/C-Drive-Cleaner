using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CDriveCleaner
{
    public enum RiskLevel
    {
        Safe,
        Moderate,
        Advanced
    }

    public enum PathMode
    {
        Contents,
        Files,
        WholeDirectory
    }

    public enum SpecialAction
    {
        None,
        RecycleBin,
        WindowsUpdate,
        DeliveryOptimization,
        FontCache,
        DshUpdater,
        Dism,
        Hibernation
    }

    public enum ItemState
    {
        Unscanned,
        Ready,
        Running,
        NeedAdmin,
        Skipped,
        Cleaned,
        Empty,
        Error
    }

    public class CleanPath
    {
        public string Path;
        public PathMode Mode;
        public int MinAgeDays;
        public int MinAgeHours;
        public string[] Filters;

        public CleanPath(string path, PathMode mode, int minAgeDays, int minAgeHours, string[] filters)
        {
            Path = path;
            Mode = mode;
            MinAgeDays = minAgeDays;
            MinAgeHours = minAgeHours;
            Filters = filters == null ? new string[0] : filters;
        }
    }

    public class CleanItem
    {
        public string Id;
        public string Group;
        public string Name;
        public string Description;
        public string Note;
        public RiskLevel Risk;
        public bool RequiresAdmin;
        public bool DefaultChecked;
        public bool Advanced;
        public bool AllowWhileRunning;
        public string[] ProcessNames;
        public List<CleanPath> Paths;
        public SpecialAction Special;

        public long Size;
        public int FileCount;
        public bool PartialAccess;
        public bool Scanned;
        public bool CanClean;
        public bool Selected;
        public bool UserSelectionSet;
        public ItemState State;
        public string StatusText;
        public string Extra;

        public CleanItem(string id, string group, string name, string description,
                         RiskLevel risk, bool requiresAdmin, bool defaultChecked, bool advanced)
        {
            Id = id;
            Group = group;
            Name = name;
            Description = description;
            Risk = risk;
            RequiresAdmin = requiresAdmin;
            DefaultChecked = defaultChecked;
            Advanced = advanced;
            ProcessNames = new string[0];
            Paths = new List<CleanPath>();
            Special = SpecialAction.None;
            State = ItemState.Unscanned;
            StatusText = "未扫描";
            CanClean = true;
            Selected = defaultChecked;
            Note = "";
            Extra = "";
        }

        public void AddPath(CleanPath path)
        {
            Paths.Add(path);
        }

        public string ShortPath
        {
            get
            {
                if (Paths.Count == 0) return "";
                string p = Paths[0].Path;
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home) && p.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                {
                    return "~" + p.Substring(home.Length);
                }
                return p;
            }
        }

        public string RiskText
        {
            get
            {
                if (Risk == RiskLevel.Safe) return "安全";
                if (Risk == RiskLevel.Moderate) return "谨慎";
                return "高级";
            }
        }
    }

    public class CleanOutcome
    {
        public CleanItem Item;
        public long Freed;
        public int Deleted;
        public int Failed;
        public int Skipped;
        public bool Success;
        public string Message;
        public List<string> Errors;

        public CleanOutcome(CleanItem item)
        {
            Item = item;
            Errors = new List<string>();
            Success = true;
            Message = "";
        }
    }

    public class HistoryEntry
    {
        public DateTime Time;
        public long Freed;
        public int Deleted;
        public int Failed;
        public bool Admin;
        public string ReportPath;
        public string LogPath;

        public HistoryEntry()
        {
            Time = DateTime.Now;
        }
    }

    public class AppSettings
    {
        public int DshImageDays;
        public bool ShowAdvanced;
        public bool AutoScan;
        public bool ConfirmBeforeClean;
        public bool CleanRunning;

        public AppSettings()
        {
            DshImageDays = 7;
            ShowAdvanced = true;
            AutoScan = true;
            ConfirmBeforeClean = true;
            CleanRunning = false;
        }

        public static string DataDir
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CDriveCleaner");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsPath
        {
            get { return System.IO.Path.Combine(DataDir, "settings.ini"); }
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        int idx = t.IndexOf('=');
                        if (idx <= 0) continue;
                        string key = t.Substring(0, idx).Trim();
                        string val = t.Substring(idx + 1).Trim();
                        if (key.Equals("DshImageDays", StringComparison.OrdinalIgnoreCase))
                        {
                            int n;
                            if (int.TryParse(val, out n))
                            {
                                if (n < 1) n = 1;
                                if (n > 90) n = 90;
                                s.DshImageDays = n;
                            }
                        }
                        else if (key.Equals("ShowAdvanced", StringComparison.OrdinalIgnoreCase))
                        {
                            s.ShowAdvanced = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (key.Equals("AutoScan", StringComparison.OrdinalIgnoreCase))
                        {
                            s.AutoScan = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (key.Equals("ConfirmBeforeClean", StringComparison.OrdinalIgnoreCase))
                        {
                            s.ConfirmBeforeClean = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (key.Equals("CleanRunning", StringComparison.OrdinalIgnoreCase))
                        {
                            s.CleanRunning = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch
            {
            }
            return s;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# C盘清理大师设置");
                sb.AppendLine("DshImageDays=" + DshImageDays.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("ShowAdvanced=" + (ShowAdvanced ? "1" : "0"));
                sb.AppendLine("AutoScan=" + (AutoScan ? "1" : "0"));
                sb.AppendLine("ConfirmBeforeClean=" + (ConfirmBeforeClean ? "1" : "0"));
                sb.AppendLine("CleanRunning=" + (CleanRunning ? "1" : "0"));
                File.WriteAllText(SettingsPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch
            {
            }
        }
    }

    public static class Fmt
    {
        public static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
                return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("N2", CultureInfo.CurrentCulture) + " GB";
            if (bytes >= 1024L * 1024L)
                return (bytes / 1024.0 / 1024.0).ToString("N1", CultureInfo.CurrentCulture) + " MB";
            if (bytes >= 1024L)
                return (bytes / 1024.0).ToString("N1", CultureInfo.CurrentCulture) + " KB";
            return bytes.ToString("N0", CultureInfo.CurrentCulture) + " B";
        }

        public static string Number(int n)
        {
            return n.ToString("N0", CultureInfo.CurrentCulture);
        }

        public static string Time(DateTime t)
        {
            return t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        }

        public static string EscapeHtml(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
