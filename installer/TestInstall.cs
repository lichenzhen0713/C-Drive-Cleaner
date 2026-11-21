// 安装流程自动化测试（仅开发使用，不随安装包发布）
// 用法: test-install.exe <dist 目录>
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CDriveCleaner.Setup
{
    internal static class TestMain
    {
        private static int failed;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) failed++;
        }

        private static string HashOf(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        [STAThread]
        internal static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            string dist = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "dist");
            Console.WriteLine("dist = " + dist);

            // 测试用 HKCU，避免污染真实卸载项
            Product.RegRoot = Registry.CurrentUser;
            string dir = Path.Combine(Path.GetTempPath(), "cdc-e2e");
            Console.WriteLine("target = " + dir);

            try
            {
                Console.WriteLine("[1] 清理旧环境");
                Product.KillApp(dir);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                try { Registry.CurrentUser.DeleteSubKeyTree(Product.RegUninstall, false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(Product.RegApp, false); } catch { }
                Check(!Directory.Exists(dir), "目标目录已清空");

                Console.WriteLine("[2] 首次安装");
                List<int> pcts = new List<int>();
                InstallEngine.Run(dir, false, false, delegate (int p, string s) { pcts.Add(p); });
                Check(pcts.Count > 3 && pcts[pcts.Count - 1] == 100, "进度回调到达 100（共 " + pcts.Count + " 次）");
                Check(Directory.Exists(dir), "安装目录已创建");
                Check(File.Exists(Path.Combine(dir, Product.AppExe)), "主程序已写入");
                Check(File.Exists(Path.Combine(dir, Product.UninstallExe)), "卸载程序已写入");
                Check(File.Exists(Path.Combine(dir, "Microsoft.Web.WebView2.Core.dll")), "WebView2 Core DLL 已写入");
                Check(File.Exists(Path.Combine(dir, "Microsoft.Web.WebView2.WinForms.dll")), "WebView2 WinForms DLL 已写入");
                Check(File.Exists(Path.Combine(dir, "WebView2Loader.dll")), "WebView2Loader DLL 已写入");
                Check(File.Exists(Path.Combine(dir, "README.md")), "README 已写入");

                Console.WriteLine("[3] 文件内容与 dist 一致");
                foreach (string name in Directory.GetFiles(dist))
                {
                    string dest = Path.Combine(dir, Path.GetFileName(name));
                    Check(File.Exists(dest) && HashOf(dest) == HashOf(name), "哈希一致: " + Path.GetFileName(name));
                }

                Console.WriteLine("[4] 卸载程序文件有效");
                string un = Path.Combine(dir, Product.UninstallExe);
                byte[] head = new byte[2];
                using (FileStream fs = File.OpenRead(un)) fs.Read(head, 0, 2);
                Check(head[0] == (byte)'M' && head[1] == (byte)'Z', "Uninstall.exe 是合法的 PE 文件");
                Check(new FileInfo(un).Length > 4096, "Uninstall.exe 大小合理（" + new FileInfo(un).Length + " 字节）");

                Console.WriteLine("[5] 注册表卸载信息");
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Product.RegUninstall))
                {
                    Check(k != null, "卸载项已创建");
                    if (k != null)
                    {
                        Check((string)k.GetValue("DisplayName") == Product.Name, "DisplayName = " + k.GetValue("DisplayName"));
                        Check((string)k.GetValue("DisplayVersion") == Product.Version, "DisplayVersion = " + k.GetValue("DisplayVersion"));
                        Check((string)k.GetValue("InstallLocation") == dir, "InstallLocation 正确");
                        string us = k.GetValue("UninstallString") as string;
                        Check(us != null && us.Trim('"') == un, "UninstallString 指向 Uninstall.exe");
                        object sz = k.GetValue("EstimatedSize");
                        Check(sz is int && (int)sz > 0, "EstimatedSize > 0（" + sz + " KB）");
                    }
                }
                Check(string.Equals(Product.InstalledDir, dir, StringComparison.OrdinalIgnoreCase), "Product.InstalledDir 能读回安装路径");

                Console.WriteLine("[6] 覆盖安装（幂等）");
                InstallEngine.Run(dir, false, false, null);
                Check(File.Exists(Path.Combine(dir, Product.AppExe)), "重复安装后主程序仍存在");

                Console.WriteLine("[7] 快捷方式创建");
                string lnk = Path.Combine(Path.GetTempPath(), "cdc-test-shortcut.lnk");
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
                Product.CreateShortcut(lnk, Path.Combine(dir, Product.AppExe), dir, Path.Combine(dir, Product.AppExe));
                Check(File.Exists(lnk) && new FileInfo(lnk).Length > 100, ".lnk 快捷方式已生成（" + (File.Exists(lnk) ? new FileInfo(lnk).Length : 0) + " 字节）");

                Console.WriteLine("[8] 卸载");
                bool valid = UninstallEngine.Run(dir, false, delegate (int p, string s) { });
                Check(valid, "识别为有效安装目录");
                Check(!Directory.Exists(dir), "安装目录已删除");
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Product.RegUninstall))
                    Check(k == null, "卸载项已移除");
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Product.RegApp))
                    Check(k == null, "应用信息项已移除");
                Check(Product.InstalledDir == null, "Product.InstalledDir 返回 null");

                Console.WriteLine("[9] 空目录卸载的健壮性");
                bool valid2 = UninstallEngine.Run(dir, false, null);
                Check(!valid2, "不存在的目录不会被误判为有效安装");
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION: " + ex);
                failed++;
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(Product.RegUninstall, false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(Product.RegApp, false); } catch { }
                try { File.Delete(Path.Combine(Path.GetTempPath(), "cdc-test-shortcut.lnk")); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ALL PASSED" : ("FAILED: " + failed));
            return failed == 0 ? 0 : 1;
        }
    }
}
