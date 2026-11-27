// ============================================================
//  МАСКОТ LUXE
//
//  Маленький космический исследователь Люкс. Он ходит, пока идёт
//  загрузка: качает версию, моды, Java, обновление, друзей, плащи.
//  Рисуется вектором, поэтому чёткий на любом DPI и не требует
//  внешних картинок.
//
//  MascotToast — стеклянная плашка снизу справа с надписью
//  «Загрузка», процентом и полосой прогресса. Все фоновые операции
//  зовут MascotLoading.Begin/Progress/End, поэтому маскот появляется
//  везде, а не только в одном разделе.
// ============================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// ---------- рисование ----------

static class MascotArt
{
    public static readonly Color Cyan = Color.FromArgb(34, 211, 238);
    public static readonly Color Violet = Color.FromArgb(139, 92, 246);
    public static readonly Color Mint = Color.FromArgb(94, 234, 212);

    static GraphicsPath RR(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        if (w <= 0f || h <= 0f) return p;
        float d = r * 2f;
        if (d > w) d = w;
        if (d > h) d = h;
        if (d < 0.8f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
        p.AddArc(x, y, d, d, 180f, 90f);
        p.AddArc(x + w - d, y, d, d, 270f, 90f);
        p.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
        p.AddArc(x, y + h - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    static void Limb(Graphics g, float x, float y, float w, float h,
                     float angle, Color top, Color bottom, Color tip, bool boot)
    {
        GraphicsState st = g.Save();
        g.TranslateTransform(x + w * 0.5f, y);
        g.RotateTransform(angle);
        using (GraphicsPath p = RR(-w * 0.5f, 0f, w, h, w * 0.46f))
        using (LinearGradientBrush b = new LinearGradientBrush(
            new RectangleF(-w * 0.5f, 0f, w, h), top, bottom, 90f))
            g.FillPath(b, p);
        if (boot)
        {
            using (GraphicsPath bp = RR(-w * 0.62f, h - 5f, w * 1.24f, 7f, 3f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(15, 23, 42)))
                g.FillPath(b, bp);
            using (Pen pen = new Pen(Color.FromArgb(120, tip), 1.1f))
            {
                pen.Alignment = PenAlignment.Inset;
                using (GraphicsPath bp = RR(-w * 0.62f, h - 5f, w * 1.24f, 7f, 3f))
                    g.DrawPath(pen, bp);
            }
        }
        g.Restore(st);
    }

    static void Spark(Graphics g, float cx, float cy, float r, int alpha)
    {
        if (alpha <= 0) return;
        using (Pen pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 1.1f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLine(pen, cx - r, cy, cx + r, cy);
            g.DrawLine(pen, cx, cy - r, cx, cy + r);
        }
        using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha / 2, Cyan)))
        {
            g.FillEllipse(b, cx - r * 0.28f, cy - r * 0.28f, r * 0.56f, r * 0.56f);
        }
    }

    // t — фаза шага в радианах, bob — вертикальное покачивание,
    // happy — довольная мордочка после успешной загрузки.
    public static void Draw(Graphics g, Rectangle box, float t, float bob, bool happy)
    {
        if (box.Width < 8 || box.Height < 8) return;
        GraphicsState state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float s = Math.Min(box.Width, box.Height) / 64f;
        float ox = box.X + (box.Width - 64f * s) * 0.5f;
        float oy = box.Y + (box.Height - 64f * s) * 0.5f;
        g.TranslateTransform(ox, oy);
        g.ScaleTransform(s, s);

        float swing = (float)Math.Sin(t);
        float lift = (float)Math.Abs(Math.Sin(t * 2f));
        float y0 = bob + lift * 1.2f;
        g.TranslateTransform(0f, y0);

        // тень на «полу»
        using (SolidBrush sh = new SolidBrush(Color.FromArgb(64, 0, 0, 0)))
            g.FillEllipse(sh, 17f, 55f - bob * 0.6f, 30f, 6f);
        using (SolidBrush sh = new SolidBrush(Color.FromArgb(26, Cyan)))
            g.FillEllipse(sh, 21f, 56f - bob * 0.6f, 22f, 4f);

        // рюкзак
        using (GraphicsPath bp = RR(7f, 26f, 15f, 24f, 6f))
        using (LinearGradientBrush bg = new LinearGradientBrush(
            new RectangleF(7f, 26f, 15f, 24f), Color.FromArgb(49, 46, 129), Color.FromArgb(30, 27, 75), 90f))
            g.FillPath(bg, bp);
        using (Pen pen = new Pen(Color.FromArgb(90, 167, 139, 250), 1f))
            g.DrawPath(pen, RR(7f, 26f, 15f, 24f, 6f));
        using (SolidBrush b = new SolidBrush(Color.FromArgb(190, Cyan)))
            g.FillRectangle(b, 9.5f, 30f, 3f, 10f);

        // ноги
        float l1 = 23f + swing * 2.4f;
        float l2 = 33f - swing * 2.4f;
        Limb(g, l1, 44f, 8.5f, 13f, -swing * 10f,
            Color.FromArgb(67, 56, 202), Color.FromArgb(49, 46, 129), Cyan, true);
        Limb(g, l2, 44f, 8.5f, 13f, swing * 10f,
            Color.FromArgb(67, 56, 202), Color.FromArgb(49, 46, 129), Cyan, true);

        // руки (качаются в противофазе ногам)
        Limb(g, 13f, 27f, 7f, 18f, -swing * 14f,
            Color.FromArgb(124, 58, 237), Color.FromArgb(67, 56, 202), Mint, false);
        Limb(g, 44f, 27f, 7f, 18f, swing * 14f,
            Color.FromArgb(124, 58, 237), Color.FromArgb(67, 56, 202), Mint, false);

        // корпус
        using (GraphicsPath body = RR(17.5f, 24f, 29f, 24f, 10f))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(17.5f, 24f, 29f, 24f),
                Color.FromArgb(167, 139, 250), Color.FromArgb(67, 56, 202), 90f))
                g.FillPath(b, body);
            using (Pen pen = new Pen(Color.FromArgb(130, 221, 214, 254), 1f))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, body);
            }
        }

        // нагрудная панель с огоньком
        using (GraphicsPath panel = RR(24f, 31f, 16f, 10f, 3.5f))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(24f, 31f, 16f, 10f), Color.FromArgb(8, 15, 35), Color.FromArgb(10, 74, 92), 90f))
                g.FillPath(b, panel);
            using (Pen pen = new Pen(Color.FromArgb(120, Cyan), 0.9f))
                g.DrawPath(pen, panel);
        }
        float pulse = 0.5f + 0.5f * (float)Math.Sin(t * 2.4f);
        using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(150 + 105 * pulse), Cyan)))
            g.FillEllipse(b, 29.2f, 34.2f, 3.4f, 3.4f);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(150, Mint)))
            g.FillEllipse(b, 35.4f, 35.2f, 2f, 2f);

        // пояс
        using (SolidBrush b = new SolidBrush(Color.FromArgb(150, Cyan)))
            g.FillRectangle(b, 18f, 44.4f, 28f, 2.2f);

        // шарф: два «хвоста», которые развеваются при ходьбе
        using (GraphicsPath sc = new GraphicsPath())
        {
            float w1 = (float)Math.Sin(t * 1.7f) * 2.2f;
            sc.AddPolygon(new PointF[] {
                new PointF(21f, 24f), new PointF(38f, 24f), new PointF(35f, 29f),
                new PointF(21f, 29f) });
            sc.AddPolygon(new PointF[] {
                new PointF(20f, 25f), new PointF(12f + w1, 29f), new PointF(9f + w1, 26.5f),
                new PointF(17f, 23f) });
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(9f, 22f, 30f, 8f), Color.FromArgb(103, 232, 249), Color.FromArgb(99, 102, 241), 0f))
                g.FillPath(b, sc);
        }

        // голова
        using (GraphicsPath head = RR(15.5f, 5.5f, 33f, 23f, 10f))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(15.5f, 5.5f, 33f, 23f),
                Color.FromArgb(237, 233, 254), Color.FromArgb(124, 58, 237), 90f))
                g.FillPath(b, head);
            using (Pen pen = new Pen(Color.FromArgb(160, 245, 243, 255), 1f))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, head);
            }
        }

        // боковые «наушники» шлема
        using (GraphicsPath hp = RR(12f, 12f, 5.5f, 10f, 2.5f))
        using (SolidBrush b = new SolidBrush(Color.FromArgb(76, 29, 149)))
            g.FillPath(b, hp);
        using (GraphicsPath hp = RR(46.5f, 12f, 5.5f, 10f, 2.5f))
        using (SolidBrush b = new SolidBrush(Color.FromArgb(76, 29, 149)))
            g.FillPath(b, hp);

        // визор
        using (GraphicsPath visor = RR(21f, 10f, 22f, 13.5f, 6f))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(21f, 10f, 22f, 13.5f),
                Color.FromArgb(2, 6, 23), Color.FromArgb(8, 74, 92), 90f))
                g.FillPath(b, visor);
            using (Pen pen = new Pen(Color.FromArgb(150, Cyan), 1f))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, visor);
            }
            // блик стекла
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(22f, 10.5f, 18f, 6f),
                Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 20f))
            using (GraphicsPath gp = RR(22.5f, 11f, 17f, 5f, 2.5f))
                g.FillPath(b, gp);
        }

        if (happy)
        {
            // довольные «галочки» вместо глаз
            using (Pen pen = new Pen(Color.FromArgb(235, 240, 253, 255), 1.5f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, 25.5f, 14.5f, 4.5f, 3.6f, 200f, 140f);
                g.DrawArc(pen, 33.8f, 14.5f, 4.5f, 3.6f, 200f, 140f);
            }
        }
        else
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 240, 253, 255)))
            {
                g.FillEllipse(b, 26.4f, 15.5f, 2.6f, 3.4f);
                g.FillEllipse(b, 35f, 15.5f, 2.6f, 3.4f);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, Cyan)))
            {
                g.FillEllipse(b, 27.2f, 16.2f, 1.1f, 1.6f);
                g.FillEllipse(b, 35.8f, 16.2f, 1.1f, 1.6f);
            }
        }

        // антенна с пульсирующей лампочкой
        using (Pen pen = new Pen(Color.FromArgb(200, 196, 181, 253), 1.2f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLine(pen, 32f, 5.5f, 33.5f, 1.6f);
        }
        int glowA = 70 + (int)(120 * pulse);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(glowA / 3, Cyan)))
            g.FillEllipse(b, 30.6f, -1.4f, 6.8f, 6.8f);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, Cyan)))
            g.FillEllipse(b, 32.2f, 0.2f, 3.6f, 3.6f);

        // искры вокруг
        Spark(g, 5.5f, 14f, 2.2f, 70 + (int)(90 * (0.5f + 0.5f * Math.Sin(t * 1.3f))));
        Spark(g, 58f, 23f, 2.6f, 60 + (int)(100 * (0.5f + 0.5f * Math.Sin(t * 1.1f + 2f))));
        Spark(g, 52f, 4f, 1.7f, 50 + (int)(80 * (0.5f + 0.5f * Math.Sin(t * 1.8f + 4f))));

        if (happy)
        {
            // маленькое сердечко рядом
            using (GraphicsPath h = new GraphicsPath())
            {
                h.AddBezier(53f, 40f, 53f, 37f, 58f, 37f, 58f, 41f);
                h.AddBezier(58f, 37f, 63f, 37f, 63f, 41f, 53f, 47f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 251, 113, 133)))
                    g.FillPath(b, h);
            }
        }

        g.Restore(state);
    }
}

// ---------- живой маскот на панели ----------

class MascotView : Control
{
    public bool Walking = true;
    public bool Happy = false;
    public float Speed = 1f;
    float phase = 0f;
    Action tick;
    bool animating = false;

    public MascotView()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        tick = new Action(delegate
        {
            if (IsDisposed || !IsHandleCreated) { Stop(); return; }
            if (!Visible)
            {
                // не жжём кадры, когда маскота не видно
                return;
            }
            phase += (Walking ? 0.13f : 0.035f) * Speed;
            if (phase > 1000f) phase = 0f;
            Invalidate();
        });
    }

    public void Start()
    {
        if (animating) return;
        animating = true;
        Anim.Sub(tick);
    }

    public void Stop()
    {
        if (!animating) return;
        animating = false;
        Anim.Unsub(tick);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) Start(); else Stop();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Visible) Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Stop();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        float bob = Walking ? (float)Math.Sin(phase * 2f) * 1.4f : 0f;
        MascotArt.Draw(e.Graphics, ClientRectangle, phase, bob, Happy);
    }
}

// ---------- полоса прогресса для плашки и карточки обновления ----------

class LoadBar : Control
{
    public int Value = 0;
    public bool Indeterminate = false;
    public Color Fill1 = MascotArt.Cyan;
    public Color Fill2 = MascotArt.Violet;
    float phase = 0f;
    Action tick;
    bool animating = false;

    public LoadBar()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        tick = new Action(delegate
        {
            if (IsDisposed || !IsHandleCreated) { Stop(); return; }
            phase += 0.045f;
            if (phase > 2f) phase -= 2f;
            Invalidate();
        });
    }

    void Start()
    {
        if (animating) return;
        animating = true;
        Anim.Sub(tick);
    }

    void Stop()
    {
        if (!animating) return;
        animating = false;
        Anim.Unsub(tick);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && Indeterminate) Start(); else Stop();
    }

    public void SetMode(bool ind)
    {
        Indeterminate = ind;
        if (ind && Visible) Start();
        if (!ind) Stop();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Stop();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width <= 2 || Height <= 2) return;
        int rad = Height / 2;
        RectangleF track = new RectangleF(0f, 0f, Width, Height);
        using (GraphicsPath p = Ui.RoundRect(track, rad))
        using (SolidBrush b = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
            g.FillPath(b, p);

        float pct = Math.Max(0f, Math.Min(100f, Value)) / 100f;
        if (Indeterminate)
        {
            float w = Math.Max(30f, Width * 0.32f);
            float x = (Width + w) * ((float)((Math.Sin(phase) + 1.0) * 0.5)) - w;
            RectangleF seg = new RectangleF(x, 0f, w, Height);
            using (GraphicsPath p = Ui.RoundRect(seg, rad))
            using (LinearGradientBrush b = new LinearGradientBrush(seg,
                Color.FromArgb(0, Fill1), Color.FromArgb(255, Fill1), 0f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] {
                    Color.FromArgb(0, Fill1), Color.FromArgb(235, Fill1), Color.FromArgb(0, Fill2) };
                cb.Positions = new float[] { 0f, 0.55f, 1f };
                b.InterpolationColors = cb;
                g.FillPath(b, p);
            }
        }
        else if (pct > 0.001f)
        {
            RectangleF fill = new RectangleF(0f, 0f, Math.Max(Height, Width * pct), Height);
            using (GraphicsPath p = Ui.RoundRect(fill, rad))
            using (LinearGradientBrush b = new LinearGradientBrush(fill, Fill1, Fill2, 0f))
                g.FillPath(b, p);
            // блик по верхней кромке полосы
            using (GraphicsPath p = Ui.RoundRect(
                new RectangleF(1f, 1f, Math.Max(1f, fill.Width - 2f), Math.Max(1f, Height * 0.42f)), rad))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(54, 255, 255, 255)))
                g.FillPath(b, p);
        }
    }
}

// ---------- стеклянная плашка загрузки ----------

class MascotToast : RoundedPanel
{
    public static MascotToast Current;

    MascotView mascot;
    Label title;
    Label detail;
    Label pct;
    LoadBar bar;
    System.Windows.Forms.Timer showTimer;
    System.Windows.Forms.Timer hideTimer;
    bool loading = false;
    Control host;

    public MascotToast()
    {
        Radius = 16;
        Gradient = true;
        GradFrom = Color.FromArgb(34, 40, 78);
        GradTo = Color.FromArgb(12, 16, 36);
        Border = Color.FromArgb(90, MascotArt.Cyan);
        BorderW = 1;
        Glass = true;
        Size = new Size(338, 90);
        Visible = false;

        mascot = new MascotView();
        mascot.Location = new Point(10, 10);
        mascot.Size = new Size(70, 70);
        mascot.Walking = true;
        Controls.Add(mascot);

        title = new Label();
        title.Text = "Загрузка";
        title.Font = Ui.Font(10.5f, FontStyle.Bold);
        title.ForeColor = Ui.Txt;
        title.BackColor = Color.Transparent;
        title.AutoSize = false;
        title.Location = new Point(86, 11);
        title.Size = new Size(170, 20);
        Controls.Add(title);

        pct = new Label();
        pct.Text = "…";
        pct.Font = Ui.Font(9, FontStyle.Bold);
        pct.ForeColor = MascotArt.Cyan;
        pct.BackColor = Color.Transparent;
        pct.AutoSize = false;
        pct.TextAlign = ContentAlignment.TopRight;
        pct.Location = new Point(256, 11);
        pct.Size = new Size(66, 20);
        Controls.Add(pct);

        detail = new Label();
        detail.Text = "";
        detail.Font = Ui.Font(8.3f);
        detail.ForeColor = Ui.Gray;
        detail.BackColor = Color.Transparent;
        detail.AutoSize = false;
        detail.AutoEllipsis = true;
        detail.Location = new Point(86, 32);
        detail.Size = new Size(236, 17);
        Controls.Add(detail);

        bar = new LoadBar();
        bar.Location = new Point(86, 56);
        bar.Size = new Size(236, 7);
        bar.Indeterminate = true;
        Controls.Add(bar);

        showTimer = new System.Windows.Forms.Timer();
        showTimer.Interval = 260;
        showTimer.Tick += delegate
        {
            showTimer.Stop();
            if (!loading || IsDisposed) return;
            Visible = true;
            Reposition();
            BringToFront();
            bar.SetMode(true);
        };

        hideTimer = new System.Windows.Forms.Timer();
        hideTimer.Interval = 1400;
        hideTimer.Tick += delegate
        {
            hideTimer.Stop();
            Visible = false;
        };

        Current = this;
    }

    public void Attach(Control parent)
    {
        if (parent == null) return;
        host = parent;
        parent.Controls.Add(this);
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        Reposition();
        parent.Resize += delegate { Reposition(); };
    }

    void Reposition()
    {
        try
        {
            Control p = host ?? Parent;
            if (p == null) return;
            int x = p.ClientSize.Width - Width - 20;
            int y = p.ClientSize.Height - Height - 34;
            if (x < 12) x = 12;
            if (y < 12) y = 12;
            Location = new Point(x, y);
        }
        catch { }
    }

    void RunUi(Action a)
    {
        try
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }
        catch { }
    }

    public void ShowLoad(string label)
    {
        RunUi(delegate
        {
            loading = true;
            hideTimer.Stop();
            title.Text = "Загрузка";
            title.ForeColor = Ui.Txt;
            detail.Text = string.IsNullOrEmpty(label) ? "Готовлюсь..." : label;
            pct.Text = "…";
            pct.ForeColor = MascotArt.Cyan;
            bar.Value = 0;
            bar.SetMode(true);
            Border = Color.FromArgb(90, MascotArt.Cyan);
            mascot.Walking = true;
            mascot.Happy = false;
            if (Visible)
            {
                Reposition();
                BringToFront();
            }
            else
            {
                showTimer.Stop();
                showTimer.Start();
            }
        });
    }

    public void SetProgress(int value, string label)
    {
        RunUi(delegate
        {
            if (!loading)
            {
                loading = true;
                title.Text = "Загрузка";
                title.ForeColor = Ui.Txt;
                Border = Color.FromArgb(90, MascotArt.Cyan);
                mascot.Walking = true;
                mascot.Happy = false;
                if (!Visible) { Visible = true; Reposition(); BringToFront(); }
            }
            if (!string.IsNullOrEmpty(label)) detail.Text = label;
            if (value < 0)
            {
                bar.SetMode(true);
                pct.Text = "…";
            }
            else
            {
                if (value > 100) value = 100;
                bar.SetMode(false);
                bar.Value = value;
                pct.Text = value + "%";
            }
        });
    }

    public void Done(string message)
    {
        RunUi(delegate
        {
            loading = false;
            showTimer.Stop();
            hideTimer.Stop();
            title.Text = "Готово";
            title.ForeColor = Ui.Txt;
            detail.Text = string.IsNullOrEmpty(message) ? "Загрузка завершена" : message;
            pct.Text = "100%";
            pct.ForeColor = Ui.Green;
            bar.SetMode(false);
            bar.Value = 100;
            bar.Fill1 = Ui.Green;
            bar.Fill2 = MascotArt.Mint;
            Border = Color.FromArgb(110, Ui.Green);
            mascot.Walking = false;
            mascot.Happy = true;
            if (!Visible) { Visible = true; Reposition(); BringToFront(); }
            hideTimer.Interval = 1500;
            hideTimer.Start();
        });
    }

    public void Fail(string message)
    {
        RunUi(delegate
        {
            loading = false;
            showTimer.Stop();
            hideTimer.Stop();
            title.Text = "Не получилось";
            title.ForeColor = Ui.Txt;
            detail.Text = string.IsNullOrEmpty(message) ? "Попробуй ещё раз" : message;
            pct.Text = "";
            bar.SetMode(false);
            bar.Value = 0;
            bar.Fill1 = Ui.Red;
            bar.Fill2 = Ui.Orange;
            Border = Color.FromArgb(120, Ui.Red);
            mascot.Walking = false;
            mascot.Happy = false;
            if (!Visible) { Visible = true; Reposition(); BringToFront(); }
            hideTimer.Interval = 3200;
            hideTimer.Start();
        });
    }

    public void HideNow()
    {
        RunUi(delegate
        {
            loading = false;
            showTimer.Stop();
            hideTimer.Stop();
            Visible = false;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { showTimer.Stop(); hideTimer.Stop(); } catch { }
            if (Current == this) Current = null;
        }
        base.Dispose(disposing);
    }
}

// ---------- единая точка вызова из фоновых потоков ----------

static class MascotLoading
{
    static readonly object Sync = new object();
    static int depth = 0;

    static void RunUi(Action a)
    {
        MascotToast t = MascotToast.Current;
        if (t == null) return;
        try
        {
            if (t.IsDisposed) return;
            if (t.InvokeRequired) t.BeginInvoke(a);
            else a();
        }
        catch { }
    }

    public static void Begin(string label)
    {
        lock (Sync) depth++;
        RunUi(delegate { if (MascotToast.Current != null) MascotToast.Current.ShowLoad(label); });
    }

    public static void Progress(int pct, string detail)
    {
        RunUi(delegate { if (MascotToast.Current != null) MascotToast.Current.SetProgress(pct, detail); });
    }

    public static void End(string message)
    {
        lock (Sync)
        {
            if (depth > 0) depth--;
            if (depth > 0) return;
        }
        RunUi(delegate { if (MascotToast.Current != null) MascotToast.Current.Done(message); });
    }

    public static void Fail(string message)
    {
        lock (Sync) depth = 0;
        RunUi(delegate { if (MascotToast.Current != null) MascotToast.Current.Fail(message); });
    }

    public static void Hide()
    {
        lock (Sync) depth = 0;
        RunUi(delegate { if (MascotToast.Current != null) MascotToast.Current.HideNow(); });
    }
}
