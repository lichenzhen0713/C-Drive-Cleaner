// C盘清理大师 - 安装/卸载程序共用界面组件
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CDriveCleaner.Setup
{
    internal static class Theme
    {
        /// <summary>当前窗口 DPI（由窗口在加载时探测，供自绘控件使用）。</summary>
        public static int Dpi = 96;

        public static Color Bg = Color.FromArgb(0xF4, 0xF6, 0xFB);
        public static readonly Color Card = Color.White;
        public static readonly Color Ink = Color.FromArgb(0x0F, 0x17, 0x2A);
        public static readonly Color Muted = Color.FromArgb(0x64, 0x74, 0x8B);
        public static readonly Color Line = Color.FromArgb(0xE2, 0xE8, 0xF0);
        public static readonly Color Blue = Color.FromArgb(0x25, 0x63, 0xEB);
        public static readonly Color BlueHover = Color.FromArgb(0x1D, 0x4E, 0xD8);
        public static readonly Color BlueDown = Color.FromArgb(0x1E, 0x40, 0xAF);
        public static readonly Color Red = Color.FromArgb(0xE1, 0x1D, 0x48);
        public static readonly Color RedHover = Color.FromArgb(0xC8, 0x16, 0x3D);
        public static readonly Color RedDown = Color.FromArgb(0xA5, 0x11, 0x32);
        public static readonly Color Green = Color.FromArgb(0x10, 0xB9, 0x81);
        public static readonly Color HeaderTop = Color.FromArgb(0xF2, 0xF6, 0xFF);
        public static readonly Color HeaderBottom = Color.FromArgb(0xFF, 0xFF, 0xFF);

        public static Font Font(float pt, FontStyle style)
        {
            try { return new Font("Microsoft YaHei UI", pt, style); }
            catch { }
            try { return new Font("Segoe UI", pt, style); }
            catch { }
            return new Font(FontFamily.GenericSansSerif, pt, style);
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 1 || r.Width < 4 || r.Height < 4) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>取控件所处的父容器底色（用于圆角外侧的抗锯齿背景）。</summary>
        public static Color BackgroundOf(Control c)
        {
            Control p = c.Parent;
            while (p != null)
            {
                if (p.BackColor.A == 255) return p.BackColor;
                p = p.Parent;
            }
            return Card;
        }

        public static void FillBackground(Control c, PaintEventArgs e)
        {
            Color bg = BackgroundOf(c);
            using (SolidBrush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, c.ClientRectangle);
        }
    }

    /// <summary>无边框、圆角、可拖动的窗口基类。</summary>
    internal class DpiForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;
        private const int CS_DROPSHADOW = 0x20000;

        protected int TitleBarHeight = 46;

        public DpiForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Card;
            DoubleBuffered = true;
            KeyPreview = true;
            Font = Theme.Font(9.5f, FontStyle.Regular);
            MaximizeBox = false;
            MinimizeBox = false;
        }

        private bool dpiScaled;

        /// <summary>探测窗口 DPI；WinForms 的 DeviceDpi 在本程序中不可靠，直接问系统。</summary>
        protected int CurrentDpi
        {
            get
            {
                try
                {
                    if (IsHandleCreated)
                    {
                        uint v = GetDpiForWindow(Handle);
                        if (v >= 72 && v <= 480) return (int)v;
                    }
                }
                catch { }
                return 96;
            }
        }

        /// <summary>按当前显示 DPI 等比缩放整棵控件树（字体用磅值，无需缩放）。</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Theme.Dpi = CurrentDpi;
            if (dpiScaled) return;
            dpiScaled = true;
            float f = Theme.Dpi / 96f;
            if (Math.Abs(f - 1f) < 0.01f) return;

            // 先解除右侧/底部锚定，避免父窗口放大时锚点二次偏移
            List<Control> all = new List<Control>();
            CollectControls(this, all);
            Dictionary<Control, AnchorStyles> anchors = new Dictionary<Control, AnchorStyles>();
            foreach (Control c in all)
            {
                if ((c.Anchor & (AnchorStyles.Right | AnchorStyles.Bottom)) != 0)
                {
                    anchors[c] = c.Anchor;
                    c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                }
            }

            SuspendLayout();
            foreach (Control c in Controls) ScaleChild(c, f);
            Size = new Size(Px(Width * f), Px(Height * f));
            ResumeLayout(true);

            foreach (KeyValuePair<Control, AnchorStyles> kv in anchors)
                kv.Key.Anchor = kv.Value;
        }

        private static void CollectControls(Control parent, List<Control> list)
        {
            foreach (Control c in parent.Controls)
            {
                list.Add(c);
                CollectControls(c, list);
            }
        }

        internal static int Px(double v)
        {
            return (int)Math.Round(v);
        }

        private static void ScaleChild(Control c, float f)
        {
            foreach (Control ch in c.Controls) ScaleChild(ch, f);

            Label lbl = c as Label;
            if (lbl != null && lbl.AutoSize)
            {
                c.Location = new Point(Px(c.Left * f), Px(c.Top * f));
                return;
            }
            if (c.MaximumSize.Width > 0)
                c.MaximumSize = new Size(Px(c.MaximumSize.Width * f), c.MaximumSize.Height);
            c.Bounds = new Rectangle(Px(c.Left * f), Px(c.Top * f),
                Math.Max(1, Px(c.Width * f)), Math.Max(1, Px(c.Height * f)));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        private int CornerRadius
        {
            get { return (int)Math.Round(14.0 * Theme.Dpi / 96.0); }
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width, Height), CornerRadius))
                Region = new Region(p);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRegion();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
            using (Pen pen = new Pen(Theme.Line))
                g.DrawPath(pen, p);
        }

        protected void BeginDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) OnEscape();
        }

        protected virtual void OnEscape()
        {
            Close();
        }

        protected CloseButton AddCloseButton()
        {
            CloseButton b = new CloseButton();
            b.Size = new Size(34, 34);
            b.Location = new Point(Width - 46, 8);
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            b.Click += delegate { Close(); };
            Controls.Add(b);
            b.BringToFront();
            return b;
        }
    }

    internal class CloseButton : Control
    {
        private bool hover, down;

        public CloseButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillBackground(this, e);
            if (hover)
            {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath p = Theme.Round(r, Width / 3))
                using (SolidBrush b = new SolidBrush(down ? Theme.RedDown : Theme.Red))
                    g.FillPath(b, p);
            }
            Color c = hover ? Color.White : Theme.Muted;
            float penW = 1.7f * (Width / 34f);
            int pad = (int)Math.Round(Width * 0.31);
            using (Pen pen = new Pen(c, penW))
            {
                g.DrawLine(pen, pad, pad, Width - pad - 1, Height - pad - 1);
                g.DrawLine(pen, Width - pad - 1, pad, pad, Height - pad - 1);
            }
        }
    }

    internal class RoundedButton : Button
    {
        private bool hover, down;
        public int Radius = 10;
        public Color HoverFill = Color.Empty;
        public Color DownFill = Color.Empty;
        public Color BorderColor = Color.Empty;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = Theme.Font(10f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillBackground(this, e);
            Rectangle full = new Rectangle(0, 0, Width, Height);
            int radius = DpiForm.Px(Radius * Theme.Dpi / 96.0);

            Color fill;
            if (!Enabled) fill = Color.FromArgb(0xCB, 0xD5, 0xE1);
            else if (down) fill = DownFill == Color.Empty ? Theme.Mix(BackColor, Color.Black, 0.18) : DownFill;
            else if (hover) fill = HoverFill == Color.Empty ? Theme.Mix(BackColor, Color.Black, 0.08) : HoverFill;
            else fill = BackColor;

            using (GraphicsPath p = Theme.Round(full, radius))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);

            if (BorderColor != Color.Empty)
            {
                float penW = Math.Max(1f, (float)Theme.Dpi / 96f);
                int inset = (int)Math.Ceiling(penW / 2f);
                Rectangle br = new Rectangle(inset, inset,
                    Math.Max(1, Width - inset * 2 - 1), Math.Max(1, Height - inset * 2 - 1));
                using (GraphicsPath p = Theme.Round(br, Math.Max(2, radius - inset)))
                using (Pen pen = new Pen(BorderColor, penW))
                    g.DrawPath(pen, p);
            }

            Color text = Enabled ? ForeColor : Color.FromArgb(0xF4, 0xF7, 0xFB);
            TextRenderer.DrawText(g, Text, Font, full, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>带圆角边框的输入框容器。</summary>
    internal class FieldPanel : Panel
    {
        public readonly TextBox Box = new TextBox();
        private bool focused;

        public FieldPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.White;
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.Font(9.5f, FontStyle.Regular);
            Box.BackColor = Color.White;
            Box.ForeColor = Theme.Ink;
            Box.GotFocus += delegate { focused = true; Invalidate(); };
            Box.LostFocus += delegate { focused = false; Invalidate(); };
            Controls.Add(Box);
        }

        public override string Text
        {
            get { return Box.Text; }
            set { Box.Text = value; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = DpiForm.Px(12 * Theme.Dpi / 96.0);
            int h = Box.PreferredHeight;
            Box.SetBounds(pad, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - pad * 2), h);
            Invalidate();
        }

        private int R
        {
            get { return DpiForm.Px(9 * Theme.Dpi / 96.0); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillBackground(this, e);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath p = Theme.Round(r, R))
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillPath(b, p);
            float penW = focused ? 2f : Math.Max(1f, (float)Theme.Dpi / 96f);
            int inset = (int)Math.Ceiling(penW / 2f);
            Rectangle br = new Rectangle(inset, inset,
                Math.Max(1, Width - inset * 2 - 1), Math.Max(1, Height - inset * 2 - 1));
            using (GraphicsPath p = Theme.Round(br, Math.Max(2, R - inset)))
            using (Pen pen = new Pen(focused ? Theme.Blue : Theme.Line, penW))
                g.DrawPath(pen, p);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PerformLayout();
        }
    }

    internal class ThinProgress : Control
    {
        private int val;

        public ThinProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            Height = 6;
        }

        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(0, Math.Min(100, value));
                if (v != val) { val = v; Invalidate(); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillBackground(this, e);
            int r = Height / 2;
            Rectangle track = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath p = Theme.Round(track, r))
            using (SolidBrush b = new SolidBrush(Theme.Line))
                g.FillPath(b, p);
            if (val <= 0) return;
            int w = (int)Math.Round(track.Width * (val / 100.0));
            if (w < Height) w = Height;
            if (w > track.Width) w = track.Width;
            Rectangle fill = new Rectangle(0, 0, w, track.Height);
            using (GraphicsPath p = Theme.Round(fill, r))
            using (SolidBrush b = new SolidBrush(Theme.Blue))
                g.FillPath(b, p);
        }
    }

    internal class DoneBadge : Control
    {
        public DoneBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillBackground(this, e);
            Rectangle circle = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(0xE7, 0xFB, 0xF2)))
                g.FillEllipse(b, circle);
            using (Pen pen = new Pen(Theme.Green, Math.Max(2f, Width / 26f)))
            {
                int cx = Width / 2, cy = Height / 2, k = Width / 6;
                g.DrawLines(pen, new Point[]
                {
                    new Point(cx - k, cy),
                    new Point(cx - k / 3, cy + k / 2 + k / 8),
                    new Point(cx + k, cy - k / 2 - k / 6)
                });
            }
        }
    }

    internal static class Ui
    {
        public static Label Label(string text, float pt, FontStyle style, Color color, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Font(pt, style);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        public static CheckBox Check(string text, int x, int y, bool on)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Font = Theme.Font(9.5f, FontStyle.Regular);
            c.ForeColor = Theme.Ink;
            c.BackColor = Color.Transparent;
            c.AutoSize = true;
            c.Location = new Point(x, y);
            c.Checked = on;
            return c;
        }

        public static void Separator(Control parent, int y, Color c)
        {
            Panel p = new Panel();
            p.BackColor = c;
            p.Location = new Point(0, y);
            p.Size = new Size(parent.Width, 1);
            p.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(p);
        }
    }
}
