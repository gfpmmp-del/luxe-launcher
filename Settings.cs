// ============================================================
//  Luxe Launcher - настройки в стиле TL Legacy / TLauncher
//
//  Слева-сверху полоса вкладок, под ней содержимое выбранной
//  вкладки. Все вкладки рабочие: значения применяются сразу
//  (как в TL Legacy), отдельной кнопки «Сохранить» не нужно -
//  снизу только «Готово».
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// ============================================================
//  Luxe Launcher - встроенный чекбокс
// ============================================================

// Системный CheckBox на тёмной теме выглядит белым квадратом из
// Windows 95: свой элемент — пилюля 36x20 с бегунком, включённое
// состояние окрашивается акцентом.
class ToggleBox : Control
{
    public bool Checked;
    public event EventHandler Changed;
    string label = "";
    bool hover;

    float pos;          // 0..1 — положение бегунка
    Action tick;
    const float TStep = 0.22f;

    public ToggleBox(string text, bool val)
    {
        label = text;
        Checked = val;
        pos = val ? 1f : 0f;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        ForeColor = Ui.Txt;
        Cursor = Cursors.Hand;
        Font = Ui.Font(9);
        tick = delegate
        {
            if (IsDisposed || !IsHandleCreated) { Anim.Unsub(tick); return; }
            float np = Anim.Step(pos, Checked ? 1f : 0f, TStep);
            bool chg = !Anim.Near(np, pos);
            pos = np;
            if (chg) Invalidate();
            if (Anim.Near(pos, Checked ? 1f : 0f)) Anim.Unsub(tick);
        };
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        Anim.Sub(tick);
        Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // переключатель 38x20 справа от подписи — как в TL Legacy
        int sw = 38, sh = 20, sy = (Height - sh) / 2;
        int sx = 0;

        RectangleF track = new RectangleF(sx, sy, sw, sh);
        Color on = Ui.Accent1;
        Color off = hover ? Ui.Lerp(Ui.SurfaceHi, Color.White, 0.06f) : Ui.SurfaceHi;
        Color c0 = Ui.Lerp(off, on, pos);
        using (GraphicsPath tp = Ui.RoundRect(track, sh / 2f))
        using (SolidBrush tb = new SolidBrush(c0))
            g.FillPath(tb, tp);

        // бегунок
        float kx = 3f + pos * (sw - sh);
        using (SolidBrush kb = new SolidBrush(Color.White))
            g.FillEllipse(kb, kx, sy + 3f, sh - 6f, sh - 6f);

        // подпись
        TextRenderer.DrawText(g, label, Font,
            new Rectangle(sx + sw + 12, 0, Math.Max(10, Width - sw - 12), Height),
            Checked ? Ui.Txt : Ui.Gray,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

partial class LauncherForm
{
    // Размер карточки подобран так, чтобы она целиком влезала в
    // минимальное окно 1024x680 с полями по 38px.
    const int SetW = 760;
    const int SetH = 604;

    Panel setBody;
    List<RoundedButton> setTabBtns = new List<RoundedButton>();
    int setTabIdx = 0;
    Label setJavaState;
    TextBox setNickBox;
    Label setAccState;

    static readonly string[] SetTabNames = new string[]
    {
        "Игра", "Java", "Лаунчер", "Аккаунт", "Обновления", "О программе"
    };

    // ---------- каркас ----------

    void BuildSettingsTabs()
    {
        setCard = new RoundedPanel();
        setCard.Radius = 16;
        setCard.BackColor = Ui.Surface;
        setCard.Border = Ui.Line;
        setCard.Size = new Size(SetW, SetH);
        setCard.Visible = false;
        Controls.Add(setCard);
        CardHead(setCard, "Настройки", SetW, delegate { HideOverlays(); });

        // полоса вкладок: 6 равных «пилюль» под заголовком
        int bw = (SetW - 48 - 5 * 6) / 6;
        for (int i = 0; i < SetTabNames.Length; i++)
        {
            int idx = i;
            RoundedButton b = RoundedButton.Dark(SetTabNames[i], 9);
            b.Font = Ui.Font(9, FontStyle.Bold);
            b.BorderColor = Ui.Line;
            b.Size = new Size(bw, 34);
            b.Location = new Point(24 + i * (bw + 6), 76);
            b.Click += delegate { SetTab(idx); };
            setCard.Controls.Add(b);
            setTabBtns.Add(b);
        }

        setBody = new Panel();
        setBody.BackColor = Ui.Surface;
        setBody.Location = new Point(1, 122);
        setBody.Size = new Size(SetW - 2, SetH - 122 - 62);
        setCard.Controls.Add(setBody);

        // нижняя полоса: подсказка слева, «Готово» справа
        Label foot = new Label();
        foot.Text = "Изменения применяются сразу";
        foot.Font = Ui.Font(8);
        foot.ForeColor = Ui.GrayDim;
        foot.BackColor = Color.Transparent;
        foot.AutoSize = false;
        foot.Location = new Point(24, SetH - 48);
        foot.Size = new Size(SetW - 200, 24);
        foot.TextAlign = ContentAlignment.MiddleLeft;
        setCard.Controls.Add(foot);

        RoundedButton done = new RoundedButton();
        done.Text = "Готово";
        done.Radius = 10;
        done.Font = Ui.Font(9.5f, FontStyle.Bold);
        done.Size = new Size(132, 36);
        done.Location = new Point(SetW - 24 - 132, SetH - 54);
        done.Click += delegate { HideOverlays(); };
        setCard.Controls.Add(done);
    }

    // Показать вкладку: пересобираем содержимое с актуальными значениями
    void SetTab(int idx)
    {
        if (setBody == null) return;
        if (idx < 0 || idx >= SetTabNames.Length) idx = 0;
        setTabIdx = idx;
        for (int i = 0; i < setTabBtns.Count; i++)
            CapesPaintTab(setTabBtns[i], i == idx);
        for (int i = setBody.Controls.Count - 1; i >= 0; i--)
        {
            Control c = setBody.Controls[i];
            setBody.Controls.RemoveAt(i);
            try { c.Dispose(); } catch { }
        }
        switch (idx)
        {
            case 0: SetTabGame(); break;
            case 1: SetTabJava(); break;
            case 2: SetTabLauncher(); break;
            case 3: SetTabAccount(); break;
            case 4: SetTabUpdates(); break;
            default: SetTabAbout(); break;
        }
        setBody.Invalidate();
    }

    void PlaceSetCard()
    {
        if (setCard == null) return;
        int x = (ClientSize.Width - SetW) / 2;
        int y = (ClientSize.Height - SetH) / 2;
        if (x < 8) x = 8;
        if (y < 8) y = 8;
        setCard.Location = new Point(x, y);
    }

    // ---------- элементы вкладок ----------

    int SetInner { get { return SetW - 2 - 48; } }

    Label SetHead(string text, int y)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(10.5f, FontStyle.Bold);
        l.ForeColor = Ui.Txt;
        l.BackColor = Color.Transparent;
        l.AutoSize = false;
        l.Location = new Point(24, y);
        l.Size = new Size(SetInner, 22);
        setBody.Controls.Add(l);
        return l;
    }

    Label SetNote(string text, int y, int h)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(8.5f);
        l.ForeColor = Ui.GrayDim;
        l.BackColor = Color.Transparent;
        l.AutoSize = false;
        l.Location = new Point(24, y);
        l.Size = new Size(SetInner, h);
        setBody.Controls.Add(l);
        return l;
    }

    RoundedButton SetBtn(string text, int x, int y, int w, int h, bool dark)
    {
        RoundedButton b = dark ? RoundedButton.Dark(text, 9) : new RoundedButton();
        b.Text = text;
        b.Radius = 9;
        b.Font = Ui.Font(9, FontStyle.Bold);
        b.Location = new Point(x, y);
        b.Size = new Size(w, h);
        if (dark) b.ForeColor = Ui.Txt;
        setBody.Controls.Add(b);
        return b;
    }

    // тонкая линия между блоками вкладки
    void SetLine(int y)
    {
        Panel l = new Panel();
        l.BackColor = Ui.BorderSoft;
        l.Location = new Point(24, y);
        l.Size = new Size(SetInner, 1);
        setBody.Controls.Add(l);
    }

    // ---------- вкладка «Игра» ----------

    void SetTabGame()
    {
        int y = 6;
        SetHead("Производительность", y);
        SetNote("Сколько оперативной памяти отдавать Minecraft. Для сборок с модами — 4 ГБ и выше.",
             y + 22, 18);

        setRam = new Dropdown();
        setRam.Items.AddRange(new string[] { "1 GB", "2 GB", "3 GB", "4 GB", "6 GB", "8 GB", "10 GB", "12 GB" });
        setRam.Location = new Point(24, y + 44);
        setRam.Size = new Size(220, 34);
        setRam.SelectedItem = ram + " GB";
        if (setRam.SelectedItem == null) setRam.SelectedItem = "2 GB";
        setRam.SelectedIndexChanged += delegate
        {
            if (setRam.SelectedItem == null) return;
            ram = setRam.SelectedItem.ToString().Split(' ')[0];
            Mc.SaveConfig(nick, curVer(), ram, closeOnLaunch);
            SetStatus("Память: " + ram + " GB", true);
        };
        setBody.Controls.Add(setRam);

        ToggleBox chkKeep = new ToggleBox("Не закрывать лаунчер при запуске игры", !closeOnLaunch);
        chkKeep.Location = new Point(24, y + 88);
        chkKeep.Size = new Size(SetInner, 24);
        chkKeep.Changed += delegate
        {
            closeOnLaunch = !chkKeep.Checked;
            Mc.SaveConfig(nick, curVer(), ram, closeOnLaunch);
        };
        setBody.Controls.Add(chkKeep);

        y += 124;
        SetLine(y);
        y += 14;
        SetHead("Папка игры", y);
        Label p = new Label();
        p.Text = Cfg.McDir;
        p.Font = Ui.Font(8.5f);
        p.ForeColor = Ui.Gray;
        p.BackColor = Color.Transparent;
        p.AutoSize = false;
        p.AutoEllipsis = true;
        p.Location = new Point(24, y + 24);
        p.Size = new Size(SetInner, 18);
        setBody.Controls.Add(p);

        RoundedButton open = SetBtn("Открыть папку игры", 24, y + 46, 200, 36, true);
        open.Click += delegate { OpenPath(Cfg.McDir); };

        y += 96;
        SetLine(y);
        y += 14;
        SetHead("Версия", y);
        Label v = new Label();
        v.Text = "Текущая версия: " + curVer() + "  •  сборки и загрузчики — в разделе «Моды»";
        v.Font = Ui.Font(9);
        v.ForeColor = Ui.Gray;
        v.BackColor = Color.Transparent;
        v.AutoSize = false;
        v.Location = new Point(24, y + 24);
        v.Size = new Size(SetInner, 18);
        setBody.Controls.Add(v);

        RoundedButton toVer = SetBtn("Сменить версию", 24, y + 46, 200, 36, true);
        toVer.Click += delegate { HideOverlays(); ShowView(ViewMods); };

        // совет внизу: заполняет пустоту и объясняет ориентиры по памяти
        y += 96;
        RoundedPanel tip = new RoundedPanel();
        tip.Radius = 12;
        tip.BackColor = Ui.Card;
        tip.Border = Ui.BorderSoft;
        tip.Glass = false;
        tip.Location = new Point(24, y);
        tip.Size = new Size(SetInner, 60);
        setBody.Controls.Add(tip);

        Label tipH = new Label();
        tipH.Text = "Ориентир по памяти";
        tipH.Font = Ui.Font(9, FontStyle.Bold);
        tipH.ForeColor = Ui.Txt;
        tipH.BackColor = Color.Transparent;
        tipH.AutoSize = false;
        tipH.Location = new Point(16, 8);
        tipH.Size = new Size(SetInner - 32, 18);
        tip.Controls.Add(tipH);

        Label tipB = new Label();
        tipB.Text = "1–2 ГБ — ванила   •   4 ГБ — сборки до 150 модов   •   " +
                    "6–8 ГБ — тяжёлые сборки   •   12 ГБ — лишнее";
        tipB.Font = Ui.Font(8.5f);
        tipB.ForeColor = Ui.Gray;
        tipB.BackColor = Color.Transparent;
        tipB.AutoSize = false;
        tipB.Location = new Point(16, 30);
        tipB.Size = new Size(SetInner - 32, 22);
        tip.Controls.Add(tipB);
    }

    // ---------- вкладка «Java» ----------

    void SetTabJava()
    {
        int y = 6;
        SetHead("Java для запуска игры", y);

        setJavaState = new Label();
        setJavaState.Font = Ui.Font(8.5f);
        setJavaState.ForeColor = Ui.Gray;
        setJavaState.BackColor = Color.Transparent;
        setJavaState.AutoSize = false;
        setJavaState.Location = new Point(24, y + 24);
        setJavaState.Size = new Size(SetInner, 40);
        setBody.Controls.Add(setJavaState);
        PaintJavaState();

        RoundedButton find = SetBtn("Найти автоматически", 24, y + 72, 210, 36, true);
        find.Click += delegate
        {
            javaExe = Mc.FindLocalJava();
            PaintJavaState();
            SetStatus(javaExe == null ? "Java не найдена — скачайте её" : "Java: " + javaExe,
                javaExe != null);
        };

        RoundedButton dl = SetBtn("Скачать Java 21", 244, y + 72, 190, 36, false);
        dl.Click += delegate
        {
            SetStatus("Скачиваю Java...", true);
            string mc = Mc.BaseMcOf(curVer());
            Thread t = new Thread((ThreadStart)delegate
            {
                try { javaExe = Mc.EnsureJavaFor(mc, SetStatus); }
                catch (Exception ex) { ShowError("Java: " + ex.Message); }
                SocialPost(delegate { if (setJavaState != null) PaintJavaState(); });
            });
            t.IsBackground = true;
            t.Start();
        };

        y += 130;
        SetHead("Какая Java нужна", y);
        SetNote("Лаунчер подбирает сам: 1.16 и ниже — Java 8, 1.17–1.21.3 — 17, " +
                "1.21.4 и новее — 21, версии 26.x — 25.\n" +
                "Своя Java кладётся в папку данных, системная не затрагивается.", y + 22, 56);

        y += 110;
        SetNote("Если игра не запускается с ошибкой Java — нажмите «Скачать Java 21»: " +
                "она скачается в папку лаунчера и версии не конфликтуют.", y, 40);
    }

    void PaintJavaState()
    {
        if (setJavaState == null) return;
        if (string.IsNullOrEmpty(javaExe))
        {
            setJavaState.Text = "Java не найдена. Нажмите «Скачать Java 21» или «Найти автоматически».";
            setJavaState.ForeColor = Ui.Orange;
            return;
        }
        int major = 0;
        try { major = Mc.GetJavaMajor(javaExe); } catch { }
        setJavaState.Text = "Найдена Java " + (major > 0 ? major.ToString() : "?") + "\n" + javaExe;
        setJavaState.ForeColor = Ui.Green;
    }

    // ---------- вкладка «Лаунчер» ----------

    void SetTabLauncher()
    {
        int y = 6;
        SetHead("Данные лаунчера", y);
        SetNote("Настройки, кэш скинов и плащей, .minecraft и Java хранятся здесь — " +
                "рядом с exe ничего не создаётся.", y + 22, 34);

        Label p = new Label();
        p.Text = Cfg.DataDir;
        p.Font = Ui.Font(8.5f);
        p.ForeColor = Ui.Gray;
        p.BackColor = Color.Transparent;
        p.AutoSize = false;
        p.AutoEllipsis = true;
        p.Location = new Point(24, y + 58);
        p.Size = new Size(SetInner, 18);
        setBody.Controls.Add(p);

        RoundedButton open = SetBtn("Открыть папку данных", 24, y + 80, 210, 36, true);
        open.Click += delegate { OpenPath(Cfg.DataDir); };

        y += 134;
        SetLine(y);
        y += 14;
        SetHead("Кэш", y);
        SetNote("Обложки версий и картинки плащей скачиваются один раз и лежат в кэше.",
            y + 22, 18);

        RoundedButton clr = SetBtn("Очистить кэш", 24, y + 44, 210, 36, true);
        clr.Click += delegate
        {
            int n = 0;
            n += ClearPngs(Path.Combine(Cfg.DataDir, "covers"));
            n += ClearPngs(Path.Combine(Cfg.DataDir, "capes"));
            try { Luxe.DropImageCache(); } catch { }
            try { Covers.DropCache(); } catch { }
            SetStatus("Кэш очищен: " + n + " файлов", true);
        };

        y += 98;
        SetLine(y);
        y += 14;
        SetHead("Сброс", y);
        RoundedButton res = SetBtn("Сбросить настройки", 24, y + 24, 210, 36, true);
        res.Click += delegate
        {
            ram = "2";
            closeOnLaunch = true;
            Mc.SaveConfig(nick, curVer(), ram, closeOnLaunch);
            SetTab(0);
            SetStatus("Настройки сброшены: 2 ГБ, лаунчер закрывается", true);
        };
        SetNote("Сброс не удаляет версии, моды и аккаунт.", y + 68, 18);
    }

    static int ClearPngs(string dir)
    {
        int n = 0;
        try
        {
            if (!Directory.Exists(dir)) return 0;
            foreach (string f in Directory.GetFiles(dir, "*.png"))
            {
                try { File.Delete(f); n++; } catch { }
            }
        }
        catch { }
        return n;
    }

    // ---------- вкладка «Аккаунт» ----------

    void SetTabAccount()
    {
        int y = 6;
        SetHead("Ник для офлайн-игры", y);
        SetNote("3–16 символов: латиница, цифры и подчёркивание.", y + 22, 18);

        setNickBox = new TextBox();
        setNickBox.Font = Ui.Font(10.5f);
        setNickBox.BackColor = Ui.SurfaceHi;
        setNickBox.ForeColor = Ui.Txt;
        setNickBox.BorderStyle = BorderStyle.None;
        setNickBox.Location = new Point(12, 9);
        setNickBox.Size = new Size(SetInner - 24, 22);
        setNickBox.Text = nick;
        RoundedPanel box = FieldBox(SetInner, 40, y + 46);
        box.Controls.Add(setNickBox);
        setBody.Controls.Add(box);

        RoundedButton save = SetBtn("Сохранить ник", 24, y + 100, 200, 36, false);
        save.Click += delegate
        {
            if (ApplyNickname(setNickBox.Text)) SetStatus("Ник сохранён: " + nick, true);
        };

        y += 156;
        SetHead("Microsoft", y);
        setAccState = new Label();
        setAccState.Font = Ui.Font(8.5f);
        setAccState.ForeColor = Auth.LoggedIn ? Ui.Green : Ui.GrayDim;
        setAccState.BackColor = Color.Transparent;
        setAccState.AutoSize = false;
        setAccState.Location = new Point(24, y + 24);
        setAccState.Size = new Size(SetInner, 18);
        setAccState.Text = Auth.LoggedIn
            ? "Вход выполнен: " + Auth.Name + " — доступны лицензия, скины и плащи"
            : "Вход не выполнен — игра идёт офлайн";
        setBody.Controls.Add(setAccState);

        RoundedButton lic = SetBtn(Auth.LoggedIn ? "Выйти из аккаунта" : "Войти через Microsoft",
            24, y + 48, 230, 36, true);
        lic.Click += delegate
        {
            if (Auth.LoggedIn)
            {
                try { Auth.Logout(); } catch { }
                try { UpdateLicUi(); } catch { }
                SetTab(3);
                SetStatus("Вы вышли из аккаунта", true);
            }
            else
            {
                ShowAuth();
            }
        };

        SetNote("Офлайн-ник работает всегда. Аккаунт Microsoft нужен для лицензии и общего " +
                "каталога скинов/плащей Luxe.", y + 96, 34);
    }

    // ---------- вкладка «Обновления» ----------

    void SetTabUpdates()
    {
        int y = 6;
        SetHead("Версия лаунчера", y);

        Label v = new Label();
        v.Text = "Luxe Launcher " + Cfg.VersionLabel;
        v.Font = Ui.Font(12.5f, FontStyle.Bold);
        v.ForeColor = Ui.Txt;
        v.BackColor = Color.Transparent;
        v.AutoSize = false;
        v.Location = new Point(24, y + 26);
        v.Size = new Size(SetInner, 26);
        setBody.Controls.Add(v);

        if (Cfg.GiftBuild)
        {
            SetNote("Это подарочная сборка: обновления для неё отключены.", y + 60, 20);
            return;
        }

        RoundedButton chk = SetBtn("Проверить обновления", 24, y + 60, 220, 38, false);
        updCheckBtn = chk;
        chk.Click += delegate
        {
            UpdState("Проверяю обновления...", Ui.GrayDim);
            UpdBusy(true);
            Thread t = new Thread((ThreadStart)delegate { CheckUpdates(true); });
            t.IsBackground = true;
            t.Start();
        };

        // Итог проверки показываем здесь же, на этой вкладке: старая
        // строка статуса внизу окна закрыта карточкой настроек, и её
        // никто не видел.
        updState = new Label();
        updState.Text = "Нажми «Проверить обновления» — скажу, есть ли новая версия.";
        updState.Font = Ui.Font(8.5f);
        updState.ForeColor = Ui.GrayDim;
        updState.BackColor = Color.Transparent;
        updState.AutoSize = false;
        updState.Location = new Point(24, y + 104);
        updState.Size = new Size(SetInner, 36);
        setBody.Controls.Add(updState);

        y += 152;
        SetLine(y);
        y += 14;
        SetHead("Что нового", y);
        SetNote("Список изменений всех версий — в разделе «Новости».", y + 22, 18);
        RoundedButton news = SetBtn("Открыть новости", 24, y + 44, 200, 36, true);
        news.Click += delegate { HideOverlays(); ShowView(ViewNews); };

        y += 96;
        SetLine(y);
        y += 14;
        SetHead("Как обновляется", y);
        SetNote("Лаунчер проверяет обновления сам при запуске. Новая версия скачивается " +
                "в один клик — переустановка не нужна, настройки и версии игры остаются.",
            y + 22, 34);
    }

    // Строка результата проверки обновлений живёт на самой вкладке.
    // CheckUpdates крутится в фоновом потоке, поэтому маршалим в UI.
    void UpdState(string text, Color color)
    {
        if (InvokeRequired) { Invoke(new Action<string, Color>(UpdState), text, color); return; }
        if (updState == null || updState.IsDisposed) return;
        updState.Text = text;
        updState.ForeColor = color;
    }

    // На время проверки кнопка гаснет и меняет подпись — иначе её
    // можно нажать пять раз подряд и получить пять потоков.
    void UpdBusy(bool busy)
    {
        if (InvokeRequired) { Invoke(new Action<bool>(UpdBusy), busy); return; }
        if (updCheckBtn == null || updCheckBtn.IsDisposed) return;
        updCheckBtn.Enabled = !busy;
        updCheckBtn.Text = busy ? "Проверяю..." : "Проверить обновления";
        updCheckBtn.Invalidate();
    }

    // ---------- вкладка «О программе» ----------

    void SetTabAbout()
    {
        int y = 6;
        LogoBadge badge = new LogoBadge();
        badge.Location = new Point(24, y);
        badge.Size = new Size(56, 56);
        setBody.Controls.Add(badge);

        Label n = new Label();
        n.Text = "Luxe Launcher";
        n.Font = Ui.Font(15, FontStyle.Bold);
        n.ForeColor = Ui.Txt;
        n.BackColor = Color.Transparent;
        n.AutoSize = false;
        n.Location = new Point(94, y + 2);
        n.Size = new Size(300, 26);
        setBody.Controls.Add(n);

        Label sub = new Label();
        sub.Text = Cfg.VersionLabel + "  •  Minecraft: Java Edition";
        sub.Font = Ui.Font(9);
        sub.ForeColor = Ui.GrayDim;
        sub.BackColor = Color.Transparent;
        sub.AutoSize = false;
        sub.Location = new Point(94, y + 30);
        sub.Size = new Size(360, 18);
        setBody.Controls.Add(sub);

        y += 76;
        SetHead("Файлы", y);
        Label p = new Label();
        p.Text = Application.ExecutablePath;
        p.Font = Ui.Font(8.5f);
        p.ForeColor = Ui.Gray;
        p.BackColor = Color.Transparent;
        p.AutoSize = false;
        p.AutoEllipsis = true;
        p.Location = new Point(24, y + 24);
        p.Size = new Size(SetInner, 18);
        setBody.Controls.Add(p);

        RoundedButton open = SetBtn("Открыть папку с лаунчером", 24, y + 48, 240, 36, true);
        open.Click += delegate { OpenPath(Cfg.BaseDir); };

        y += 100;
        SetHead("Помощник", y);
        SetNote("Кнопка с искрой в верхней панели открывает ИИ-помощника: он найдёт скин в интернете " +
                "по нику или популярный, подберёт плащ и поставит сборку с Modrinth — и сразу применит.", y + 22, 40);

        RoundedButton ai = SetBtn("Открыть помощника", 24, y + 70, 220, 36, false);
        ai.Click += delegate { HideOverlays(); ShowAi(); };
    }

    // ---------- общее ----------

    void OpenPath(string path)
    {
        try
        {
            Os.OpenFolder(path);
        }
        catch (Exception ex) { ShowError("Не удалось открыть папку: " + ex.Message); }
    }

    // --shot-run ... settings:N — открыть настройки на нужной вкладке
    public void DebugOpenSettings(int tab)
    {
        setTabIdx = tab;
        ShowSettings();
    }

    // --shot-run ... updcheck — нажать «Проверить обновления» на вкладке
    public void DebugCheckUpdates()
    {
        CheckUpdates(true);
    }
}