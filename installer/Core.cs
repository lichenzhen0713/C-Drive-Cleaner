// C盘清理大师 - 安装/卸载程序共用逻辑
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CDriveCleaner.Setup
{
    internal static class Product
    {
        public const string Name = "C盘清理大师";
        public const string Version = "3.0.0.0";
        public const string AppExe = "C盘清理大师.exe";
        public const string UninstallExe = "Uninstall.exe";
        public const string RegUninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CDriveCleaner";
        public const string RegApp = @"SOFTWARE\CDriveCleaner";
        public const string DataDirName = "CDriveCleaner";

        /// <summary>写入/读取的注册表根；正式安装为 HKLM，测试时可改为 HKCU。</summary>
        private static RegistryKey regRoot;

        public static RegistryKey RegRoot
        {
            get { return regRoot == null ? Registry.LocalMachine : regRoot; }
            set { regRoot = value; }
        }

        public static string DefaultDir
        {
            get
            {
                string pf = Environment.GetEnvironmentVariable("ProgramW6432");
                if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(pf, Name);
            }
        }

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataDirName); }
        }

        public static string InstalledDir
        {
            get
            {
                try
                {
                    using (RegistryKey k = RegRoot.OpenSubKey(RegApp))
                    {
                        if (k != null)
                        {
                            string s = k.GetValue("InstallPath") as string;
                            if (!string.IsNullOrEmpty(s) && Directory.Exists(s)) return s;
                        }
                    }
                }
                catch { }
                try
                {
                    using (RegistryKey k = RegRoot.OpenSubKey(RegUninstall))
                    {
                        if (k != null)
                        {
                            string s = k.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrEmpty(s) && Directory.Exists(s)) return s;
                        }
                    }
                }
                catch { }
                return null;
            }
        }

        public static string InstalledVersion
        {
            get
            {
                try
                {
                    using (RegistryKey k = RegRoot.OpenSubKey(RegUninstall))
                        if (k != null) return k.GetValue("DisplayVersion") as string;
                }
                catch { }
                return null;
            }
        }

        public static string StartMenuLink
        {
            get
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
                if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                return Path.Combine(Path.Combine(dir, "Programs"), Name + ".lnk");
            }
        }

        public static string DesktopLink
        {
            get
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return Path.Combine(dir, Name + ".lnk");
            }
        }

        public static void CreateShortcut(string lnkPath, string target, string workDir, string iconFile)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type t = sc.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { iconFile + ",0" });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { Name + " - 现代化 C 盘清理工具" });
                t.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, sc, new object[] { 1 });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            finally
            {
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }

        public static void RemoveShortcuts()
        {
            string[] paths = new string[]
            {
                StartMenuLink, DesktopLink,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"),
                Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ""), Name + ".lnk")
            };
            foreach (string p in paths)
            {
                try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) File.Delete(p); }
                catch { }
            }
        }

        public static void KillApp(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            string exe = Path.Combine(dir, AppExe);
            Process[] list;
            try { list = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExe)); }
            catch { return; }
            foreach (Process p in list)
            {
                try
                {
                    string path = null;
                    try { path = p.MainModule.FileName; }
                    catch { }
                    if (path != null && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                        continue;
                    p.Kill();
                    p.WaitForExit(4000);
                }
                catch { }
            }
        }

        public static long DirectorySize(string dir)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        /// <summary>延迟删除目录（用于删除正在运行的程序自身目录）。</summary>
        public static void ScheduleDelete(params string[] dirs)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("/c ping -n 3 127.0.0.1 >nul");
            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                sb.Append(" & rmdir /s /q ");
                sb.Append('"');
                sb.Append(d.TrimEnd('\\'));
                sb.Append('"');
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", sb.ToString());
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch { }
        }

        public static bool LooksLikeInstallDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                string full = Path.GetFullPath(dir).TrimEnd('\\');
                if (!Directory.Exists(full)) return false;
                if (string.Equals(full, Path.GetPathRoot(full).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
                return File.Exists(Path.Combine(full, UninstallExe)) || File.Exists(Path.Combine(full, AppExe));
            }
            catch { return false; }
        }

        public static void DeleteTree(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        public static Stream GetResource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        /// <summary>从内嵌 ICO 中取最接近目标尺寸的一帧并解码为位图（支持 PNG 帧）。</summary>
        public static Bitmap LoadIconBitmap(int preferred)
        {
            try
            {
                byte[] ico;
                using (Stream s = GetResource("CDriveCleaner.app.ico"))
                {
                    if (s == null) return null;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        ico = ms.ToArray();
                    }
                }
                return DecodeIco(ico, preferred);
            }
            catch { return null; }
        }

        private static Bitmap DecodeIco(byte[] ico, int preferred)
        {
            if (ico == null || ico.Length < 6) return null;
            int count = BitConverter.ToUInt16(ico, 4);
            int bestOff = -1, bestSize = 0, bestScore = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int o = 6 + i * 16;
                if (o + 16 > ico.Length) break;
                int w = ico[o] == 0 ? 256 : ico[o];
                int bytes = (int)BitConverter.ToUInt32(ico, o + 8);
                int off = (int)BitConverter.ToUInt32(ico, o + 12);
                if (off <= 0 || bytes <= 0 || off + bytes > ico.Length) continue;
                int score = Math.Abs(w - preferred) + (w < preferred ? 1000 : 0);
                if (score < bestScore) { bestScore = score; bestOff = off; bestSize = bytes; }
            }
            if (bestOff < 0) return null;

            bool isPng = bestSize > 8 && ico[bestOff] == 0x89 && ico[bestOff + 1] == 0x50
                && ico[bestOff + 2] == 0x4E && ico[bestOff + 3] == 0x47;
            if (isPng)
            {
                using (MemoryStream ms = new MemoryStream(ico, bestOff, bestSize))
                using (Image img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            try
            {
                using (MemoryStream ms = new MemoryStream(ico))
                using (Icon ic = new Icon(ms, preferred, preferred))
                    return ic.ToBitmap();
            }
            catch { return null; }
        }
    }
}
