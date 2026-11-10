// ============================================================
//  Своя оконная рамка вместо системной
//
//  WinForms рисует системный заголовок светлым и он выбивается из
//  тёмной темы. Здесь окно переводится в режим без рамки, а тянуть
//  и разворачивать его умеют обычные контролы: панель шапки и восемь
//  невидимых «полосок» по краям.
//
//  Стиль WS_THICKFRAME оставляем: без него система не даёт
//  тянуть за края и не ограничивает разворот рабочим столом.
//  WM_NCCALCSIZE возвращает 0, поэтому невидимая рамка не съедает
//  место и клиентская область совпадает с окном.
// ============================================================
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

partial class LauncherForm
{
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int sz);
    [DllImport("user32.dll")]
    static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    const int WM_NCCALCSIZE = 0x0083;
    const int WM_NCPAINT = 0x0085;
    const int WM_NCACTIVATE = 0x0086;

    // флаги RedrawWindow: перерисовать рамку, стереть фон, обновить сразу
    const uint RDW_INVALIDATE = 0x0001;
    const uint RDW_ERASE = 0x0004;
    const uint RDW_FRAME = 0x0400;
    const uint RDW_ALLCHILDREN = 0x0080;
    const uint RDW_UPDATENOW = 0x0100;
    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int WM_NCHITTEST = 0x0084;
    const int WM_SYSCOMMAND = 0x0112;
    const int SC_MAXIMIZE = 0xF030;
    const int HTCAPTION = 2;
    const int HTCLIENT = 1;
    const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
              HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    const int WS_THICKFRAME = 0x00040000;
    const int WS_MINIMIZEBOX = 0x00020000;
    const int WS_MAXIMIZEBOX = 0x00010000;

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_BORDER_COLOR = 34;

    Panel[] grips;
    bool chromeOn = false;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // на Linux этих стилей нет — окно оформляет оконный менеджер
            if (Os.IsWin)
                cp.Style |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // на Linux своя рамка не работает и не нужна: сообщения
        // WM_NC* / WM_NCHITTEST — это Win32, отдаём всё базовому классу
        if (!Os.IsWin)
        {
            base.WndProc(ref m);
            return;
        }
        // своя шапка: не даём системе рисовать невидимую рамку
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        // Своя шапка — значит свою же «рамку» и стираем. Без этого
        // система на каждую активацию и разворот перерисовывает
        // невидимый неклиентский слой WS_THICKFRAME: по краям окна
        // остаются светлые «белые рамки» и полосы от старых размеров.
        if (m.Msg == WM_NCPAINT)
        {
            m.Result = (IntPtr)1;
            return;
        }
        if (m.Msg == WM_NCACTIVATE)
        {
            m.Result = (IntPtr)1;
            return;
        }
        // запасной вариант растаскивания окна за края — на случай,
        // если полоски-«ручки» почему-то не получили мышь
        if (m.Msg == WM_NCHITTEST && chromeOn)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT && WindowState != FormWindowState.Maximized)
            {
                int lp = m.LParam.ToInt32();
                Point scr = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                Point p = PointToClient(scr);
                int g = 6;
                bool l = p.X <= g, r = p.X >= ClientSize.Width - g;
                bool t = p.Y <= g, b = p.Y >= ClientSize.Height - g;
                if (t && l) m.Result = (IntPtr)HTTOPLEFT;
                else if (t && r) m.Result = (IntPtr)HTTOPRIGHT;
                else if (b && l) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (b && r) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (l) m.Result = (IntPtr)HTLEFT;
                else if (r) m.Result = (IntPtr)HTRIGHT;
                else if (t) m.Result = (IntPtr)HTTOP;
                else if (b) m.Result = (IntPtr)HTBOTTOM;
            }
            return;
        }
        // Win+Up и прилипание к верхнему краю максимизируют окно сами:
        // без MaximizedBounds окно вылезает за рабочий стол, и DWM
        // показывает светлую системную шапку. Отправляем через свой
        // ToggleMax, где MaximizedBounds выставлен заранее.
        if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == SC_MAXIMIZE)
        {
            ToggleMax();
            return;
        }
        base.WndProc(ref m);
    }

    void ChromeInit()
    {
        try
        {
            // Linux/Mono: свою рамку не рисуем — окно тянет и разворачивает
            // оконный менеджер. Иначе безрамочное окно нельзя бы перетащить.
            if (!Os.IsWin)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                chromeOn = false;
                return;
            }
            FormBorderStyle = FormBorderStyle.None;
            chromeOn = true;
        }
        catch { }
    }

    // Уголки Win11 и тёмная рамка. На Win10 вызовы просто
    // возвращают ошибку — это нормально, вид не ломается.
    void ChromeDwm()
    {
        if (!Os.IsWin) return;
        try
        {
            if (!IsHandleCreated) return;
            IntPtr h = Handle;
            int on = 1;
            DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
            int round = 2;
            DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            int none = unchecked((int)0xFFFFFFFE);   // DWMWA_COLOR_NONE
            DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref none, 4);
            // цвет заголовка DWM: если рамка вдруг мелькнёт (сбой,
            // прилипание к краям), она будет тёмной, а не светло-голубой
            try
            {
                Color c = Ui.Bg;
                int caption = c.R | (c.G << 8) | (c.B << 16);   // COLORREF 0x00BBGGRR
                DwmSetWindowAttribute(h, 35 /* DWMWA_CAPTION_COLOR */, ref caption, 4);
            }
            catch { }
        }
        catch { }
    }

    // «Ручки» по краям окна: тонкие полоски, которые только ловят мышь
    void ChromeGrips()
    {
        // на Linux окно растягивается за системную рамку — «ручки» не нужны
        if (!Os.IsWin) return;
        try
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            int g = 6, c = 10;
            grips = new Panel[8];
            grips[0] = Grip(0, c, g, H - 2 * c, HTLEFT, Cursors.SizeWE,
                AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom);
            grips[1] = Grip(W - g, c, g, H - 2 * c, HTRIGHT, Cursors.SizeWE,
                AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom);
            grips[2] = Grip(c, 0, W - 2 * c, g, HTTOP, Cursors.SizeNS,
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
            grips[3] = Grip(c, H - g, W - 2 * c, g, HTBOTTOM, Cursors.SizeNS,
                AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            grips[4] = Grip(0, 0, c, c, HTTOPLEFT, Cursors.SizeNWSE,
                AnchorStyles.Top | AnchorStyles.Left);
            grips[5] = Grip(W - c, 0, c, c, HTTOPRIGHT, Cursors.SizeNESW,
                AnchorStyles.Top | AnchorStyles.Right);
            grips[6] = Grip(0, H - c, c, c, HTBOTTOMLEFT, Cursors.SizeNESW,
                AnchorStyles.Bottom | AnchorStyles.Left);
            grips[7] = Grip(W - c, H - c, c, c, HTBOTTOMRIGHT, Cursors.SizeNWSE,
                AnchorStyles.Bottom | AnchorStyles.Right);
            ChromeTop();
        }
        catch { }
    }

    Panel Grip(int x, int y, int w, int h, int hit, Cursor cur, AnchorStyles a)
    {
        Panel p = new Panel();
        p.BackColor = Ui.Bg;
        p.Location = new Point(x, y);
        p.Size = new Size(w, h);
        p.Anchor = a;
        p.Cursor = cur;
        p.TabStop = false;
        int code = hit;
        p.MouseDown += delegate
        {
            try
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)code, IntPtr.Zero);
            }
            catch { }
        };
        Controls.Add(p);
        return p;
    }

    // ручки всегда поверх содержимого
    void ChromeTop()
    {
        try
        {
            if (grips == null) return;
            foreach (Panel p in grips)
            {
                if (p == null || p.IsDisposed) continue;
                p.BringToFront();
            }
        }
        catch { }
    }

    // Потянуть окно за шапку средствами системы: так работают
    // прилипание к краям экрана и разворот двойным щелчком.
    void TitleDrag()
    {
        // Linux: тащить окно не надо — над шапкой системный заголовок
        if (!Os.IsWin) return;
        try
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
        catch { }
    }

    void ToggleMax()
    {
        try
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                MaximizedBounds = wa;
                WindowState = FormWindowState.Maximized;
            }
        }
        catch { }
        ChromeRedraw();
    }

    // Полная перерисовка окна вместе с рамкой и всеми потомками.
    // Оставляет после разворота/восстановления чистое окно: DWM не
    // всегда сам стирает невидимый неклиентский слой, который остался
    // от старого размера (это и есть «белые рамки» и полосы).
    void ChromeRedraw()
    {
        if (!Os.IsWin) return;
        try
        {
            if (!IsHandleCreated) return;
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
        }
        catch { }
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        // потянули за угол и отпустили — доснимаем рамку целиком
        ChromeRedraw();
    }

    // ---------- кнопки окна в своей шапке ----------

    RoundedButton WinBtn(Ico ic, bool close)
    {
        RoundedButton b = new RoundedButton();
        b.Flat = true;
        b.BorderW = 0;
        b.Icon = ic;
        b.IconSize = 14;
        b.Radius = 8;
        b.Top = Ui.Bg;
        b.Bottom = Ui.Bg;
        b.HoverTop = close ? Color.FromArgb(226, 74, 74) : Ui.SurfaceHi;
        b.HoverBottom = b.HoverTop;
        b.PressTop = close ? Color.FromArgb(190, 52, 52) : Ui.Lerp(Ui.SurfaceHi, Color.Black, 0.2f);
        b.PressBottom = b.PressTop;
        b.ForeColor = Ui.GrayDim;
        b.HoverFore = close ? Color.White : Ui.Txt;
        b.Size = new Size(38, 30);
        b.Font = Ui.Font(9);
        return b;
    }

    // ---------- мелочи разметки ----------

    // Заголовок карточки-оверлея + крестик закрытия
    void CardHead(RoundedPanel card, string text, int cardW, Action onClose)
    {
        PixelLabel t = new PixelLabel();
        t.Text = text;
        t.Font = Ui.Font(13, FontStyle.Bold);
        t.ForeColor = Ui.Txt;
        t.BackColor = Color.Transparent;
        t.AutoSize = false;
        t.Location = new Point(24, 22);
        t.Size = new Size(cardW - 80, 26);
        card.Controls.Add(t);

        Panel line = new Panel();
        line.BackColor = Ui.Line;
        line.Location = new Point(24, 58);
        line.Size = new Size(cardW - 48, 1);
        card.Controls.Add(line);

        RoundedButton x = RoundedButton.IcoBtn(Ico.Close, 30, 8);
        x.Location = new Point(cardW - 24 - 30, 20);
        x.Click += delegate { if (onClose != null) onClose(); };
        card.Controls.Add(x);
    }

    // Поле ввода в скруглённой рамке (стоковый TextBox слишком «системный»)
    RoundedPanel FieldBox(int w, int h, int y)
    {
        RoundedPanel p = new RoundedPanel();
        p.Radius = 10;
        p.BackColor = Ui.SurfaceHi;
        p.Border = Ui.Line;
        p.HoverBorder = Ui.Accent1;
        p.Animated = true;
        p.Glass = false;
        p.Location = new Point(24, y);
        p.Size = new Size(w, h);
        return p;
    }

    // Горячие клавиши: поиск, обновление, закрытие панелей
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        try
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                try { topSearchBox.Focus(); topSearchBox.SelectAll(); } catch { }
                return true;
            }
            if (keyData == Keys.F5)
            {
                try { RefreshVersions(); } catch { }
                return true;
            }
            if (keyData == Keys.Escape)
            {
                try
                {
                    if (setCard != null && setCard.Visible) { HideOverlays(); return true; }
                    if (aiCard != null && aiCard.Visible) { HideOverlays(); return true; }
                    if (accCard != null && accCard.Visible) { HideOverlays(); return true; }
                    if (updateCard != null && updateCard.Visible) { HideOverlays(); return true; }
                }
                catch { }
            }
            if (keyData == (Keys.Alt | Keys.Enter))
            {
                ToggleMax();
                return true;
            }
        }
        catch { }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
