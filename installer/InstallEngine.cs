// C盘清理大师 - 安装核心流程
using System;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;

namespace CDriveCleaner.Setup
{
    internal static class InstallEngine
    {
        public static void Run(string dir, bool desktopShortcut, bool startMenuShortcut, Action<int, string> report)
        {
            Report(report, 2, "正在准备安装目录…");
            Product.KillApp(dir);
            Directory.CreateDirectory(dir);

            Report(report, 6, "正在解压程序文件…");
            ExtractPayload(dir, report);

            Report(report, 84, "正在写入卸载程序…");
            WriteUninstaller(dir);

            Report(report, 88, "正在创建快捷方式…");
            CreateShortcuts(dir, desktopShortcut, startMenuShortcut);

            Report(report, 94, "正在注册卸载信息…");
            WriteRegistry(dir);

            Report(report, 100, "安装完成");
        }

        private static void Report(Action<int, string> report, int pct, string text)
        {
            if (report != null) report(pct, text);
        }

        public static void ExtractPayload(string dir, Action<int, string> report)
        {
            using (Stream s = Product.GetResource("CDriveCleaner.payload.zip"))
            {
                if (s == null) throw new InvalidOperationException("安装包数据缺失，请重新下载安装程序。");
                using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    int total = zip.Entries.Count;
                    if (total == 0) throw new InvalidOperationException("安装包数据为空。");
                    string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                    int i = 0;
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        i++;
                        string name = entry.FullName.Replace('/', '\\');
                        if (string.IsNullOrEmpty(name)) continue;
                        string dest = Path.GetFullPath(Path.Combine(root, name));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.EndsWith("\\")) { Directory.CreateDirectory(dest); continue; }
                        string parent = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        entry.ExtractToFile(dest, true);
                        Report(report, 6 + (int)(76.0 * i / total), "正在解压 " + Path.GetFileName(dest) + "…");
                    }
                }
            }
        }

        public static void WriteUninstaller(string dir)
        {
            using (Stream s = Product.GetResource("CDriveCleaner.uninstaller.exe"))
            {
                if (s == null) throw new InvalidOperationException("卸载程序数据缺失。");
                string path = Path.Combine(dir, Product.UninstallExe);
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
        }

        public static void CreateShortcuts(string dir, bool desktopShortcut, bool startMenuShortcut)
        {
            string exe = Path.Combine(dir, Product.AppExe);
            if (startMenuShortcut)
            {
                try { Product.CreateShortcut(Product.StartMenuLink, exe, dir, exe); }
                catch { }
            }
            if (desktopShortcut)
            {
                try { Product.CreateShortcut(Product.DesktopLink, exe, dir, exe); }
                catch { }
            }
        }

        public static void WriteRegistry(string dir)
        {
            string exe = Path.Combine(dir, Product.AppExe);
            long size = Product.DirectorySize(dir);
            int kb = (int)Math.Max(1, size / 1024);

            using (RegistryKey k = Product.RegRoot.CreateSubKey(Product.RegApp))
            {
                k.SetValue("InstallPath", dir);
                k.SetValue("Version", Product.Version);
            }

            using (RegistryKey k = Product.RegRoot.CreateSubKey(Product.RegUninstall))
            {
                k.SetValue("DisplayName", Product.Name);
                k.SetValue("DisplayVersion", Product.Version);
                k.SetValue("Publisher", Product.Name);
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("UninstallString", "\"" + Path.Combine(dir, Product.UninstallExe) + "\"");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", kb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
    }
}
