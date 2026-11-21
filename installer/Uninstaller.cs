// C盘清理大师 - 卸载程序
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CDriveCleaner.Setup
{
    internal static class UninstallProgram
    {
        [STAThread]
        internal static void Main(string[] args)
        {
            string dir = null;
            if (args.Length > 0 && Directory.Exists(args[0])) dir = args[0];
            if (string.IsNullOrEmpty(dir)) dir = Product.InstalledDir;

            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show(Product.Name + " 未安装或已被卸载。", Product.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string self = Application.ExecutablePath;
            string tempRoot = Path.Combine(Path.GetTempPath(), "CDriveCleaner_uninstall");
            string selfDir = Path.GetDirectoryName(Path.GetFullPath(self)).TrimEnd('\\');
            if (!string.Equals(selfDir, tempRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                // 从安装目录复制到临时目录再运行，才能删除自身所在目录
                try
                {
                    Directory.CreateDirectory(tempRoot);
                    string tempExe = Path.Combine(tempRoot, Path.GetFileName(self));
                    File.Copy(self, tempExe, true);
                    ProcessStartInfo psi = new ProcessStartInfo(tempExe, "\"" + dir + "\"");
                    // 直接继承当前已提升的令牌，避免再次弹出 UAC
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = tempRoot;
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("无法启动卸载程序：" + ex.Message, Product.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new UninstallForm(dir, tempRoot));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal class UninstallForm : DpiForm
    {
        private const int HeaderTop = 46;
        private const int HeaderH = 134;

        private PictureBox picIcon;
        private Label lblTitle, lblSub, lblPathCap, lblNote, lblStatus, lblDoneTitle, lblDoneDesc;
        private FieldPanel fieldPath;
        private CheckBox chkData;
        private ThinProgress progress;
        private RoundedButton btnPrimary, btnCancel;
        private DoneBadge badge;

        private readonly string targetDir;
        private readonly string tempRoot;
        private bool busy, finished;
        private bool cleanupScheduled;

        internal bool PreviewMode;
        internal bool DeleteDataOnClose;

        internal UninstallForm(string dir, string tempRoot)
        {
            targetDir = dir;
            this.tempRoot = tempRoot;

            Text = "卸载 " + Product.Name;
            ClientSize = new Size(540, 456);
            TitleBarHeight = HeaderTop;

            BuildUi();
            AddCloseButton();
            fieldPath.Text = dir;
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

            lblTitle = Ui.Label("卸载 " + Product.Name, 15f, FontStyle.Bold, Theme.Ink, 108, 86);
            lblTitle.MouseDown += delegate { BeginDrag(); };
            Controls.Add(lblTitle);

            lblSub = Ui.Label("将删除程序文件、快捷方式和卸载信息。", 9f, FontStyle.Regular, Theme.Muted, 110, 120);
            Controls.Add(lblSub);

            lblPathCap = Ui.Label("安装位置", 9f, FontStyle.Bold, Theme.Ink, 36, 200);
            Controls.Add(lblPathCap);

            fieldPath = new FieldPanel();
            fieldPath.Location = new Point(36, 226);
            fieldPath.Size = new Size(468, 40);
            fieldPath.Box.ReadOnly = true;
            fieldPath.Box.TabStop = false;
            Controls.Add(fieldPath);

            chkData = Ui.Check("同时删除清理记录、报告和设置", 36, 284, false);
            Controls.Add(chkData);

            lblNote = Ui.Label("此操作不可撤销。", 8.5f, FontStyle.Regular, Theme.Muted, 36, 312);
            Controls.Add(lblNote);

            progress = new ThinProgress();
            progress.Location = new Point(36, 340);
            progress.Size = new Size(468, 6);
            progress.Visible = false;
            Controls.Add(progress);

            lblStatus = Ui.Label("", 8.5f, FontStyle.Regular, Theme.Muted, 36, 352);
            lblStatus.Visible = false;
            Controls.Add(lblStatus);

            Ui.Separator(this, 378, Theme.Line);

            btnCancel = new RoundedButton();
            btnCancel.Text = "取消";
            btnCancel.Font = Theme.Font(9.5f, FontStyle.Regular);
            btnCancel.BackColor = Color.White;
            btnCancel.ForeColor = Theme.Ink;
            btnCancel.BorderColor = Theme.Line;
            btnCancel.HoverFill = Color.FromArgb(0xF2, 0xF6, 0xFD);
            btnCancel.DownFill = Color.FromArgb(0xE6, 0xEE, 0xFA);
            btnCancel.Size = new Size(108, 44);
            btnCancel.Location = new Point(268, 394);
            btnCancel.Click += delegate { if (!busy) Close(); };
            Controls.Add(btnCancel);

            btnPrimary = new RoundedButton();
            btnPrimary.Text = "卸载";
            btnPrimary.BackColor = Theme.Red;
            btnPrimary.HoverFill = Theme.RedHover;
            btnPrimary.DownFill = Theme.RedDown;
            btnPrimary.Size = new Size(128, 44);
            btnPrimary.Location = new Point(376, 394);
            btnPrimary.Click += delegate { OnPrimary(); };
            Controls.Add(btnPrimary);

            badge = new DoneBadge();
            badge.Location = new Point(36, 214);
            badge.Size = new Size(56, 56);
            badge.Visible = false;
            Controls.Add(badge);

            lblDoneTitle = Ui.Label("卸载完成", 15f, FontStyle.Bold, Theme.Ink, 108, 216);
            lblDoneTitle.Visible = false;
            Controls.Add(lblDoneTitle);

            lblDoneDesc = Ui.Label("", 9f, FontStyle.Regular, Theme.Muted, 110, 250);
            lblDoneDesc.MaximumSize = new Size(392, 0);
            lblDoneDesc.Visible = false;
            Controls.Add(lblDoneDesc);

            MouseDown += delegate(object s, MouseEventArgs e)
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

        private void OnPrimary()
        {
            if (finished) { Close(); return; }
            if (busy || PreviewMode) return;
            DoUninstall(PreviewMode ? chkData.Checked : chkData.Checked);
        }

        private void DoUninstall(bool deleteData)
        {
            busy = true;
            btnPrimary.Enabled = false;
            btnPrimary.Text = "正在卸载…";
            btnCancel.Enabled = false;
            chkData.Enabled = false;
            progress.Visible = true;
            lblStatus.Visible = true;
            progress.Value = 0;
            lblStatus.Text = "正在关闭程序…";

            Thread t = new Thread(delegate () { Work(deleteData); });
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

        private void Work(bool deleteData)
        {
            try
            {
                bool valid = UninstallEngine.Run(targetDir, deleteData, Report);
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate { ShowDone(valid); });
            }
            catch (Exception ex)
            {
                try { BeginInvoke((MethodInvoker)delegate { ShowFailure(ex); }); }
                catch { }
            }
        }
        private void ShowDone(bool removed)
        {
            finished = true;
            busy = false;
            cleanupScheduled = true;
            DeleteDataOnClose = chkData.Checked;

            picIcon.Visible = false;
            lblSub.Visible = false;
            lblPathCap.Visible = false;
            fieldPath.Visible = false;
            chkData.Visible = false;
            lblNote.Visible = false;
            progress.Visible = false;
            lblStatus.Visible = false;
            btnCancel.Visible = false;

            badge.Visible = true;
            lblDoneTitle.Visible = true;
            lblDoneDesc.Visible = true;
            lblDoneDesc.Text = removed
                ? Product.Name + " 已从本机移除。"
                : "未找到有效的程序文件，仅移除了快捷方式和卸载信息。";

            btnPrimary.Enabled = true;
            btnPrimary.Text = "关闭";
            btnPrimary.Focus();
            Invalidate();
        }

        private void ShowFailure(Exception ex)
        {
            busy = false;
            progress.Visible = false;
            lblStatus.Visible = false;
            btnPrimary.Enabled = true;
            btnPrimary.Text = "卸载";
            btnCancel.Enabled = true;
            chkData.Enabled = true;
            MessageBox.Show(this, "卸载失败：\r\n" + ex.Message, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // 退出后清理临时目录；若安装目录仍有残留（被占用）也一并清理
            if (cleanupScheduled && !string.IsNullOrEmpty(tempRoot))
            {
                if (Directory.Exists(targetDir)) Product.ScheduleDelete(tempRoot, targetDir);
                else Product.ScheduleDelete(tempRoot);
            }
        }

        internal void PreviewDone(string dir)
        {
            ShowDone(true);
        }
    }
}
