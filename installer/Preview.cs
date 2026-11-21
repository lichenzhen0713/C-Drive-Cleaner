// 安装程序界面预览工具（仅开发调试使用，不随安装包发布）
using System;
using System.IO;
using System.Windows.Forms;

namespace CDriveCleaner.Setup
{
    internal static class PreviewMain
    {
        [STAThread]
        internal static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string mode = args.Length > 0 ? args[0] : "";

                if (mode.StartsWith("--uninstall", StringComparison.Ordinal))
                {
                    string dir = args.Length > 1 ? args[1] : Product.DefaultDir;
                    UninstallForm u = new UninstallForm(dir, Path.Combine(Path.GetTempPath(), "CDriveCleaner_uninstall"));
                    u.PreviewMode = true;
                    if (mode == "--uninstall-done") u.PreviewDone(dir);
                    Application.Run(u);
                    return;
                }

                InstallerForm f = new InstallerForm();
                f.PreviewMode = true;
                if (mode == "--done") f.PreviewDone(@"C:\Program Files\C盘清理大师");
                f.Shown += delegate
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(Path.GetTempPath(), "cdc-preview-dpi.txt"),
                            "DeviceDpi=" + f.DeviceDpi + " ThemeDpi=" + Theme.Dpi + " Size=" + f.Size + " Client=" + f.ClientSize);
                    }
                    catch { }
                };
                Application.Run(f);
            }
            catch (Exception ex)
            {
                string log = Path.Combine(Path.GetTempPath(), "cdc-preview-error.txt");
                try { File.WriteAllText(log, ex.ToString()); } catch { }
            }
        }
    }
}
