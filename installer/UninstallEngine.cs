// C盘清理大师 - 卸载核心流程
using System;
using System.IO;

namespace CDriveCleaner.Setup
{
    internal static class UninstallEngine
    {
        /// <summary>执行卸载，返回是否找到了有效的安装目录。</summary>
        public static bool Run(string dir, bool deleteData, Action<int, string> report)
        {
            Report(report, 4, "正在关闭 " + Product.Name + "…");
            Product.KillApp(dir);

            Report(report, 12, "正在删除快捷方式…");
            Product.RemoveShortcuts();

            Report(report, 20, "正在移除卸载信息…");
            TryDeleteKey(Product.RegUninstall);
            TryDeleteKey(Product.RegApp);

            bool valid = Product.LooksLikeInstallDir(dir);
            if (valid)
            {
                Report(report, 30, "正在删除程序文件…");
                DeleteFiles(dir, report);
            }

            if (deleteData)
            {
                Report(report, 82, "正在删除清理记录与设置…");
                Product.DeleteTree(Product.DataDir);
            }

            Report(report, 100, "卸载完成");
            return valid;
        }

        private static void Report(Action<int, string> report, int pct, string text)
        {
            if (report != null) report(pct, text);
        }

        private static void TryDeleteKey(string path)
        {
            try { Product.RegRoot.DeleteSubKeyTree(path, false); }
            catch { }
        }

        private static void DeleteFiles(string dir, Action<int, string> report)
        {
            string[] files;
            try { files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
            catch { files = new string[0]; }

            int n = files.Length;
            for (int i = 0; i < n; i++)
            {
                try { File.Delete(files[i]); }
                catch { }
                if (n > 0)
                    Report(report, 30 + (int)(48.0 * (i + 1.0) / n), "正在删除 " + Path.GetFileName(files[i]) + "…");
            }

            try
            {
                string[] dirs = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories);
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    try { Directory.Delete(dirs[i], true); }
                    catch { }
                }
            }
            catch { }

            try { Directory.Delete(dir, true); }
            catch { }
        }
}
}
