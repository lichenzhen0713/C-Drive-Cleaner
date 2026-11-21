using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CDriveCleaner
{
    public class FileEntry
    {
        public string FullName;
        public string Name;
        public long Length;
        public DateTime LastWriteTime;

        public FileEntry(string fullName, string name, long length, DateTime lastWriteTime)
        {
            FullName = fullName;
            Name = name;
            Length = length;
            LastWriteTime = lastWriteTime;
        }
    }

    public static class Wildcard
    {
        public static bool IsMatch(string name, string[] patterns)
        {
            if (patterns == null || patterns.Length == 0) return true;
            foreach (string p in patterns)
            {
                if (string.IsNullOrEmpty(p)) continue;
                string regex = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (Regex.IsMatch(name, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }
            return false;
        }
    }

    public static class ProcessUtil
    {
        public static bool IsAnyRunning(string[] names)
        {
            if (names == null || names.Length == 0) return false;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    foreach (string n in names)
                    {
                        if (string.Equals(p.ProcessName, n, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
            return false;
        }

        public static bool IsPathInUse(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string prefix = path.TrimEnd('\\') + "\\";
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string exe = p.MainModule.FileName;
                    if (!string.IsNullOrEmpty(exe) &&
                        exe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
            return false;
        }
    }

    public static class SafeFs
    {
        public static long Measure(CleanPath p, out int fileCount, out bool partial)
        {
            fileCount = 0;
            partial = false;
            List<FileEntry> files = GetFiles(p, out partial);
            long total = 0;
            foreach (FileEntry f in files) total += f.Length;
            fileCount = files.Count;
            return total;
        }

        public static long MeasureDirectory(string path, out int fileCount, out bool partial)
        {
            CleanPath p = new CleanPath(path, PathMode.WholeDirectory, 0, 0, null);
            return Measure(p, out fileCount, out partial);
        }

        public static CleanOutcomeResult Delete(CleanPath p, List<string> errors)
        {
            CleanOutcomeResult result = new CleanOutcomeResult();
            bool partial;
            List<FileEntry> files = GetFiles(p, out partial);
            if (partial) result.Partial = true;

            foreach (FileEntry f in files)
            {
                try
                {
                    TryDeleteFile(f.FullName);
                    result.Freed += f.Length;
                    result.Deleted++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    AddError(errors, f.FullName, ex);
                }
            }

            if (p.Mode == PathMode.Contents)
            {
                DeleteEmptyDirectories(p.Path, true);
            }
            else if (p.Mode == PathMode.WholeDirectory)
            {
                DeleteEmptyDirectories(p.Path, false);
            }

            return result;
        }

        public static List<FileEntry> GetFiles(CleanPath p, out bool partial)
        {
            partial = false;
            List<FileEntry> result = new List<FileEntry>();
            if (string.IsNullOrEmpty(p.Path)) return result;

            if (p.Mode == PathMode.Files)
            {
                if (!Directory.Exists(p.Path)) return result;
                DateTime fileCutoff = DateTime.Now.AddDays(-p.MinAgeDays).AddHours(-p.MinAgeHours);
                try
                {
                    foreach (string file in Directory.EnumerateFiles(p.Path))
                    {
                        FileInfo fi = null;
                        try
                        {
                            fi = new FileInfo(file);
                            if (!fi.Exists) continue;
                            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (fi.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!Wildcard.IsMatch(fi.Name, p.Filters)) continue;
                            if (fi.LastWriteTime > fileCutoff) continue;
                            result.Add(new FileEntry(fi.FullName, fi.Name, fi.Length, fi.LastWriteTime));
                        }
                        catch
                        {
                            partial = true;
                        }
                    }
                }
                catch
                {
                    partial = true;
                }
                return result;
            }

            if (!Directory.Exists(p.Path)) return result;
            try
            {
                if ((File.GetAttributes(p.Path) & FileAttributes.ReparsePoint) != 0) return result;
            }
            catch
            {
                partial = true;
                return result;
            }

            DateTime cutoff = DateTime.Now.AddDays(-p.MinAgeDays).AddHours(-p.MinAgeHours);
            Stack<string> stack = new Stack<string>();
            stack.Push(p.Path);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                IEnumerable<string> entries = null;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(dir);
                }
                catch
                {
                    partial = true;
                    continue;
                }

                foreach (string entry in entries)
                {
                    FileAttributes attr;
                    try
                    {
                        attr = File.GetAttributes(entry);
                    }
                    catch
                    {
                        partial = true;
                        continue;
                    }

                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;

                    if ((attr & FileAttributes.Directory) != 0)
                    {
                        stack.Push(entry);
                        continue;
                    }

                    try
                    {
                        FileInfo fi = new FileInfo(entry);
                        if (fi.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!Wildcard.IsMatch(fi.Name, p.Filters)) continue;
                        if (p.Mode == PathMode.Contents && fi.LastWriteTime > cutoff) continue;
                        result.Add(new FileEntry(fi.FullName, fi.Name, fi.Length, fi.LastWriteTime));
                    }
                    catch
                    {
                        partial = true;
                    }
                }
            }
            return result;
        }

        public static void TryDeleteFile(string path)
        {
            FileAttributes attr = File.GetAttributes(path);
            if ((attr & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            }
            File.Delete(path);
        }

        public static bool TryDeleteTree(string root, List<string> errors, out long freed, out int deleted, out int failed)
        {
            freed = 0;
            deleted = 0;
            failed = 0;
            if (!Directory.Exists(root)) return true;

            CleanPath p = new CleanPath(root, PathMode.WholeDirectory, 0, 0, null);
            bool partial;
            List<FileEntry> files = GetFiles(p, out partial);

            foreach (FileEntry f in files)
            {
                try
                {
                    TryDeleteFile(f.FullName);
                    freed += f.Length;
                    deleted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    AddError(errors, f.FullName, ex);
                }
            }

            DeleteEmptyDirectories(root, false);
            return !Directory.Exists(root);
        }

        public static void DeleteEmptyDirectories(string root, bool keepRoot)
        {
            if (!Directory.Exists(root)) return;
            List<string> dirs = new List<string>();
            Stack<string> stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                try
                {
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        try
                        {
                            FileAttributes attr = File.GetAttributes(sub);
                            if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                            dirs.Add(sub);
                            stack.Push(sub);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            dirs.Sort(delegate(string a, string b)
            {
                return b.Length.CompareTo(a.Length);
            });

            foreach (string d in dirs)
            {
                try
                {
                    if (IsDirectoryEmpty(d)) Directory.Delete(d, false);
                }
                catch
                {
                }
            }

            if (!keepRoot)
            {
                try
                {
                    if (IsDirectoryEmpty(root)) Directory.Delete(root, false);
                }
                catch
                {
                }
            }
        }

        public static bool IsDirectoryEmpty(string path)
        {
            try
            {
                foreach (string unused in Directory.EnumerateFileSystemEntries(path))
                {
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void AddError(List<string> errors, string path, Exception ex)
        {
            if (errors == null) return;
            if (errors.Count >= 200) return;
            errors.Add(path + "  ::  " + ex.Message);
        }
    }

    public class CleanOutcomeResult
    {
        public long Freed;
        public int Deleted;
        public int Failed;
        public bool Partial;
    }

    public static class ServiceHelper
    {
        public static List<string> WithServices(string[] names, Action action)
        {
            List<string> errors = new List<string>();
            List<string> stopped = new List<string>();

            foreach (string name in names)
            {
                try
                {
                    using (ServiceController sc = new ServiceController(name))
                    {
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                            stopped.Add(name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("停止服务 " + name + " 失败：" + ex.Message);
                }
            }

            try
            {
                action();
            }
            finally
            {
                for (int i = stopped.Count - 1; i >= 0; i--)
                {
                    string name = stopped[i];
                    try
                    {
                        using (ServiceController sc = new ServiceController(name))
                        {
                            if (sc.Status != ServiceControllerStatus.Running)
                            {
                                sc.Start();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add("恢复服务 " + name + " 失败：" + ex.Message);
                    }
                }
            }

            return errors;
        }
    }

    public class DshUpdaterState
    {
        public string InstalledVersion;
        public string PendingVersion;
        public bool PendingExists;
        public bool PendingMatches;
        public string PendingPath;
        public long Size;
        public int Files;
        public bool Partial;
    }

    public static class DshHelper
    {
        public static string DshRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"); }
        }

        public static string DshAppData
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "@deepseek-ai", "dsh-desktop"); }
        }

        public static string UpdaterDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "@deepseek-aidsh-desktop-updater"); }
        }

        public static string ProgramsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"); }
        }

        public static string InstalledDir
        {
            get { return Path.Combine(ProgramsDir, "DeepSeek Harness"); }
        }

        public static string InstalledExe
        {
            get { return Path.Combine(InstalledDir, "DeepSeek Harness.exe"); }
        }

        public static string GetInstalledVersion()
        {
            try
            {
                string v = FindVersion(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (!string.IsNullOrEmpty(v)) return v;
                v = FindVersion(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (!string.IsNullOrEmpty(v)) return v;
                v = FindVersion(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch
            {
            }
            return null;
        }

        private static string FindVersion(RegistryKey root, string subKey)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey))
                {
                    if (key == null) return null;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        using (RegistryKey app = key.OpenSubKey(name))
                        {
                            if (app == null) continue;
                            object display = app.GetValue("DisplayName");
                            if (display != null && string.Equals(display.ToString(), "DeepSeek Harness", StringComparison.OrdinalIgnoreCase))
                            {
                                object ver = app.GetValue("DisplayVersion");
                                if (ver != null) return ver.ToString().Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public static string GetFileVersion(string path)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrEmpty(info.ProductVersion)) return info.ProductVersion.Trim();
                if (!string.IsNullOrEmpty(info.FileVersion)) return info.FileVersion.Trim();
            }
            catch
            {
            }
            return null;
        }

        public static DshUpdaterState GetUpdaterState()
        {
            DshUpdaterState s = new DshUpdaterState();
            s.InstalledVersion = GetInstalledVersion();
            string pendingDir = Path.Combine(UpdaterDir, "pending");
            if (Directory.Exists(pendingDir))
            {
                try
                {
                    string[] exes = Directory.GetFiles(pendingDir, "*.exe", SearchOption.TopDirectoryOnly);
                    if (exes.Length > 0)
                    {
                        s.PendingExists = true;
                        s.PendingPath = exes[0];
                        s.PendingVersion = GetFileVersion(exes[0]);
                    }
                }
                catch
                {
                }
            }
            if (Directory.Exists(UpdaterDir))
            {
                int count;
                bool partial;
                CleanPath p = new CleanPath(UpdaterDir, PathMode.WholeDirectory, 0, 0, null);
                s.Size = SafeFs.Measure(p, out count, out partial);
                s.Files = count;
                s.Partial = partial;
            }
            s.PendingMatches = s.PendingExists && !string.IsNullOrEmpty(s.PendingVersion) &&
                               !string.IsNullOrEmpty(s.InstalledVersion) &&
                               string.Equals(s.PendingVersion, s.InstalledVersion, StringComparison.OrdinalIgnoreCase);
            return s;
        }

        public static void CleanUpdater(List<string> errors, CleanOutcome outcome)
        {
            DshUpdaterState s = GetUpdaterState();
            if (string.IsNullOrEmpty(s.InstalledVersion))
            {
                outcome.Success = false;
                outcome.Message = "无法读取 DSH 已安装版本，为安全起见未删除更新包。";
                return;
            }

            if (s.PendingExists && !s.PendingMatches)
            {
                outcome.Success = false;
                outcome.Message = "发现待安装版本 " + (s.PendingVersion ?? "未知") +
                                  "（当前 " + s.InstalledVersion + "），已保留，避免取消更新。";
                return;
            }

            long freed;
            int deleted;
            int failed;
            string pendingDir = Path.Combine(UpdaterDir, "pending");
            if (Directory.Exists(pendingDir))
            {
                SafeFs.TryDeleteTree(pendingDir, errors, out freed, out deleted, out failed);
                outcome.Freed += freed;
                outcome.Deleted += deleted;
                outcome.Failed += failed;
            }

            string installer = Path.Combine(UpdaterDir, "installer.exe");
            if (File.Exists(installer))
            {
                string v = GetFileVersion(installer);
                if (string.Equals(v, s.InstalledVersion, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        long len = new FileInfo(installer).Length;
                        SafeFs.TryDeleteFile(installer);
                        outcome.Freed += len;
                        outcome.Deleted++;
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        SafeFs.AddError(errors, installer, ex);
                    }
                }
            }

            string blockmap = Path.Combine(UpdaterDir, "current.blockmap");
            if (File.Exists(blockmap))
            {
                try
                {
                    long len = new FileInfo(blockmap).Length;
                    SafeFs.TryDeleteFile(blockmap);
                    outcome.Freed += len;
                    outcome.Deleted++;
                }
                catch (Exception ex)
                {
                    outcome.Failed++;
                    SafeFs.AddError(errors, blockmap, ex);
                }
            }

            outcome.Message = "已清理与已安装版本一致的 DSH 更新包。";
        }

        public static List<string> FindOldVersionDirs()
        {
            List<string> dirs = new List<string>();
            if (!File.Exists(InstalledExe)) return dirs;
            try
            {
                foreach (string dir in Directory.GetDirectories(ProgramsDir, "DeepSeek Harness.new-*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(dir);
                    if (!Regex.IsMatch(name, @"^DeepSeek Harness\.new-\{[0-9A-Fa-f\-]+\}$")) continue;
                    try
                    {
                        DateTime last = Directory.GetLastWriteTime(dir);
                        if ((DateTime.Now - last).TotalDays < 14) continue;
                    }
                    catch
                    {
                        continue;
                    }
                    dirs.Add(dir);
                }
            }
            catch
            {
            }
            return dirs;
        }

        public static Dictionary<string, long> GetDataSizes()
        {
            Dictionary<string, long> result = new Dictionary<string, long>();
            string[] names = new string[] { "sessions", "attachments", "profiles", "storages", "cache" };
            foreach (string name in names)
            {
                string p = Path.Combine(DshRoot, name);
                if (Directory.Exists(p))
                {
                    int count;
                    bool partial;
                    result[name] = SafeFs.MeasureDirectory(p, out count, out partial);
                }
                else
                {
                    result[name] = 0;
                }
            }
            return result;
        }
    }
}
