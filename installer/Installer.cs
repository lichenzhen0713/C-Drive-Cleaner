// C盘清理大师 - 安装程序
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CDriveCleaner.Setup
{
    internal static class SetupProgram
    {
        [STAThread]
        internal static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new InstallerForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), Product.Name + " 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal class InstallerForm : DpiForm
    {
        private const int HeaderTop = 46;
        private const int HeaderH = 138;

        private PictureBox picIcon;
        private Label lblTitle, lblSub, lblSection, lblHint, lblStatus, lblFoot, lblIdleHint;
        private FieldPanel fieldPath;
        private RoundedButton btnBrowse, btnPrimary, btnCancel;
        private CheckBox chkDesktop, chkStart, chkRun;
        private ThinProgress progress;
        private DoneBadge badge;
        private Label lblDoneTitle, lblDoneDesc;

        private bool busy, finished;
        internal bool PreviewMode;
        internal string InstalledTo;

        public InstallerForm()
        {
            Text = Product.Name + " 安装程序";
            ClientSize = new Size(640, 512);
            TitleBarHeight = HeaderTop;

            BuildUi();
            AddCloseButton();

            string existing = Product.InstalledDir;
            fieldPath.Text = string.IsNullOrEmpty(existing) ? Product.DefaultDir : existing;
            if (!string.IsNullOrEmpty(existing))
                lblHint.Text = "检测到已安装版本 " + (Product.InstalledVersion ?? Product.Version) + "，将继续安装并覆盖程序文件。";
        }

        private int S(int v)
        {
            return (int)Math.Round(v * Theme.Dpi / 96.0);
        }

        private void BuildUi()
        {
            Bitmap appIcon = Product.LoadIconBitmap(64);

            picIcon = new PictureBox();
            picIcon.Location = new Point(36, 84);
            picIcon.Size = new Size(56, 56);
            picIcon.SizeMode = PictureBoxSizeMode.Zoom;
            picIcon.BackColor = Color.Transparent;
            if (appIcon != null) picIcon.Image = appIcon;
            Controls.Add(picIcon);

            lblTitle = Ui.Label(Product.Name, 17f, FontStyle.Bold, Theme.Ink, 110, 84);
            lblTitle.MouseDown += delegate { BeginDrag(); };
            lblTitle.Cursor = Cursors.SizeAll;
            Controls.Add(lblTitle);

            lblSub = Ui.Label("安装程序 · 版本 " + Product.Version, 9.5f, FontStyle.Regular, Theme.Muted, 112, 122);
            lblSub.MouseDown += delegate { BeginDrag(); };
            Controls.Add(lblSub);

            lblSection = Ui.Label("安装位置", 9.5f, FontStyle.Bold, Theme.Ink, 36, 202);
            Controls.Add(lblSection);

            fieldPath = new FieldPanel();
            fieldPath.Location = new Point(36, 228);
            fieldPath.Size = new Size(438, 40);
            Controls.Add(fieldPath);

            btnBrowse = new RoundedButton();
            btnBrowse.Text = "浏览…";
            btnBrowse.Font = Theme.Font(9.5f, FontStyle.Regular);
            btnBrowse.BackColor = Color.White;
            btnBrowse.ForeColor = Theme.Ink;
            btnBrowse.BorderColor = Theme.Line;
            btnBrowse.HoverFill = Color.FromArgb(0xF2, 0xF6, 0xFD);
            btnBrowse.DownFill = Color.FromArgb(0xE6, 0xEE, 0xFA);
            btnBrowse.Size = new Size(116, 40);
            btnBrowse.Location = new Point(488, 228);
            btnBrowse.Click += OnBrowse;
            Controls.Add(btnBrowse);

            lblHint = Ui.Label("将以管理员权限写入 Program Files，并注册卸载信息。", 8.5f, FontStyle.Regular, Theme.Muted, 36, 282);
            Controls.Add(lblHint);

            chkDesktop = Ui.Check("创建桌面快捷方式", 36, 318, true);
            Controls.Add(chkDesktop);

            chkStart = Ui.Check("创建开始菜单快捷方式", 36, 346, true);
            Controls.Add(chkStart);

            progress = new ThinProgress();
            progress.Location = new Point(36, 388);
            progress.Size = new Size(568, 6);
            progress.Visible = false;
            Controls.Add(progress);

            lblIdleHint = Ui.Label("点击「安装」开始，通常几秒即可完成；安装后可随时在「设置 → 应用」中卸载。",
                8.5f, FontStyle.Regular, Color.FromArgb(0x94, 0xA3, 0xB8), 36, 384);
            Controls.Add(lblIdleHint);

            lblStatus = Ui.Label("", 8.5f, FontStyle.Regular, Theme.Muted, 36, 400);
            lblStatus.Visible = false;
            Controls.Add(lblStatus);

            lblFoot = Ui.Label("未签名本地构建 · SmartScreen 可能提示“未知发布者”", 8f, FontStyle.Regular,
                Color.FromArgb(0x94, 0xA3, 0xB8), 36, 466);
            Controls.Add(lblFoot);

            Ui.Separator(this, 436, Theme.Line);

            btnCancel = new RoundedButton();
            btnCancel.Text = "取消";
            btnCancel.Font = Theme.Font(9.5f, FontStyle.Regular);
            btnCancel.BackColor = Color.White;
            btnCancel.ForeColor = Theme.Ink;
            btnCancel.BorderColor = Theme.Line;
            btnCancel.HoverFill = Color.FromArgb(0xF2, 0xF6, 0xFD);
            btnCancel.DownFill = Color.FromArgb(0xE6, 0xEE, 0xFA);
            btnCancel.Size = new Size(110, 44);
            btnCancel.Location = new Point(356, 452);
            btnCancel.Click += delegate { if (!busy) Close(); };
            Controls.Add(btnCancel);

            btnPrimary = new RoundedButton();
            btnPrimary.Text = "安装";
            btnPrimary.BackColor = Theme.Blue;
            btnPrimary.HoverFill = Theme.BlueHover;
            btnPrimary.DownFill = Theme.BlueDown;
            btnPrimary.Size = new Size(128, 44);
            btnPrimary.Location = new Point(476, 452);
            btnPrimary.Click += delegate { OnPrimary(); };
            Controls.Add(btnPrimary);

            badge = new DoneBadge();
            badge.Location = new Point(36, 214);
            badge.Size = new Size(56, 56);
            badge.Visible = false;
            Controls.Add(badge);

            lblDoneTitle = Ui.Label("安装完成", 16f, FontStyle.Bold, Theme.Ink, 110, 218);
            lblDoneTitle.Visible = false;
            Controls.Add(lblDoneTitle);

            lblDoneDesc = Ui.Label("", 9f, FontStyle.Regular, Theme.Muted, 112, 252);
            lblDoneDesc.MaximumSize = new Size(492, 0);
            lblDoneDesc.Visible = false;
            Controls.Add(lblDoneDesc);

            chkRun = Ui.Check("立即运行 " + Product.Name, 36, 330, true);
            chkRun.Visible = false;
            Controls.Add(chkRun);

            DragArea(this);
        }

        private void DragArea(Control c)
        {
            c.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < S(HeaderTop)) BeginDrag();
            };
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillRectangle(b, ClientRectangle);
            Rectangle hdr = new Rectangle(0, S(HeaderTop), Width, S(HeaderH));
            if (hdr.Height > 0)
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Point(0, hdr.Top), new Point(0, hdr.Bottom), Theme.HeaderTop, Theme.HeaderBottom))
                    g.FillRectangle(br, hdr);
            }
            using (Pen pen = new Pen(Theme.Line))
            {
                g.DrawLine(pen, 0, hdr.Top, Width, hdr.Top - 1);
                g.DrawLine(pen, 0, hdr.Bottom - 1, Width, hdr.Bottom - 1);
            }
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            if (busy) return;
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 " + Product.Name + " 的安装位置";
                dlg.ShowNewFolderButton = true;
                if (Directory.Exists(fieldPath.Text)) dlg.SelectedPath = fieldPath.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) fieldPath.Text = dlg.SelectedPath;
            }
        }

        private void OnPrimary()
        {
            if (finished)
            {
                if (chkRun.Checked) RunApp();
                Close();
                return;
            }
            if (busy || PreviewMode) return;

            string dir = fieldPath.Text.Trim();
            string err = ValidateDir(dir);
            if (err != null)
            {
                MessageBox.Show(this, err, "无法安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            StartInstall(dir);
        }

        private static string ValidateDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "请填写安装位置。";
            try
            {
                if (!Path.IsPathRooted(dir)) return "安装路径必须是完整路径，例如 D:\\Apps\\" + Product.Name + "。";
                string full = Path.GetFullPath(dir).TrimEnd('\\');
                if (string.Equals(full, Path.GetPathRoot(full).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return "不能直接安装到磁盘根目录，请选择子文件夹。";
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
                if (full.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase))
                    return "不能安装到 Windows 系统目录，请选择其他位置。";
                if (full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), StringComparison.OrdinalIgnoreCase))
                    return "不能安装到系统目录，请选择其他位置。";
            }
            catch (Exception ex)
            {
                return "路径无效：" + ex.Message;
            }
            return null;
        }

        private void StartInstall(string dir)
        {
            busy = true;
            bool desktop = chkDesktop.Checked;
            bool startMenu = chkStart.Checked;

            btnPrimary.Enabled = false;
            btnPrimary.Text = "安装中…";
            btnCancel.Enabled = false;
            btnBrowse.Enabled = false;
            fieldPath.Enabled = false;
            chkDesktop.Enabled = false;
            chkStart.Enabled = false;
            progress.Value = 0;
            progress.Visible = true;
            lblIdleHint.Visible = false;
            lblStatus.Visible = true;
            lblStatus.Text = "准备安装…";

            Thread t = new Thread(delegate () { DoInstall(dir, desktop, startMenu); });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void Report(int pct, string text)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    progress.Value = pct;
                    lblStatus.Text = text;
                });
            }
            catch { }
        }

        private void DoInstall(string dir, bool desktop, bool startMenu)
        {
            try
            {
                InstallEngine.Run(dir, desktop, startMenu, Report);
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate { ShowDone(dir); });
            }
            catch (Exception ex)
            {
                try { BeginInvoke((MethodInvoker)delegate { ShowFailure(ex); }); }
                catch { }
            }
        }
        private void ShowDone(string dir)
        {
            InstalledTo = dir;
            busy = false;
            finished = true;

            lblSection.Visible = false;
            fieldPath.Visible = false;
            btnBrowse.Visible = false;
            lblHint.Visible = false;
            chkDesktop.Visible = false;
            chkStart.Visible = false;
            progress.Visible = false;
            lblStatus.Visible = false;
            lblFoot.Visible = false;
            lblIdleHint.Visible = false;

            badge.Visible = true;
            lblDoneTitle.Visible = true;
            lblDoneDesc.Visible = true;
            chkRun.Visible = true;

            lblDoneDesc.Text = "已安装到 " + dir + "\r\n可在开始菜单" + (Directory.Exists(Path.GetDirectoryName(Product.DesktopLink)) ? "或桌面" : "") + "找到 " + Product.Name + "。";

            btnPrimary.Enabled = true;
            btnPrimary.Text = "完成";
            btnCancel.Visible = false;
            btnPrimary.Focus();
            Invalidate();
        }

        private void ShowFailure(Exception ex)
        {
            busy = false;
            progress.Visible = false;
            lblStatus.Visible = false;
            btnPrimary.Enabled = true;
            btnPrimary.Text = "安装";
            btnCancel.Enabled = true;
            btnBrowse.Enabled = true;
            fieldPath.Enabled = true;
            chkDesktop.Enabled = true;
            chkStart.Enabled = true;
            lblIdleHint.Visible = true;
            MessageBox.Show(this,
                "安装失败：\r\n" + ex.Message + "\r\n\r\n如提示权限不足，请确认已允许管理员权限。",
                Product.Name + " 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void RunApp()
        {
            try
            {
                string dir = InstalledTo;
                if (string.IsNullOrEmpty(dir)) dir = fieldPath.Text.Trim();
                Process.Start(new ProcessStartInfo(Path.Combine(dir, Product.AppExe)) { UseShellExecute = true, WorkingDirectory = dir });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法启动程序：" + ex.Message, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            btnPrimary.Focus();
        }

        protected override void OnEscape()
        {
            if (!busy) Close();
        }

        internal void PreviewDone(string dir)
        {
            ShowDone(dir);
        }
    }
}
