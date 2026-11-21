using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CDriveCleaner
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string snapshot = null;
            string page = null;
            if (args.Length > 1 && string.Equals(args[0], "--snapshot", StringComparison.OrdinalIgnoreCase))
            {
                snapshot = args[1];
                if (args.Length > 2) page = args[2];
            }

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                try
                {
                    string dir = Path.Combine(AppSettings.DataDir, "logs");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "fatal.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.Exception + Environment.NewLine);
                }
                catch { }
                MessageBox.Show("程序出现异常：" + e.Exception.Message, "C盘清理大师",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                try
                {
                    string dir = Path.Combine(AppSettings.DataDir, "logs");
                    Directory.CreateDirectory(dir);
                    Exception ex = e.ExceptionObject as Exception;
                    File.AppendAllText(Path.Combine(dir, "fatal.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                        (ex == null ? "unknown" : ex.ToString()) + Environment.NewLine);
                }
                catch { }
            };

            Application.Run(new WebHost(snapshot, page));
        }
    }
}
