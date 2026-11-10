using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// ================================================================
//  Экраны подарка внутри окна лаунчера.
//
//  Экраны переключаются ТОЛЬКО порядком отрисовки (BringToFront).
//  Visible=false здесь не работает: в этом лаунчере после гашения
//  потомки панели навсегда перестают рисоваться (свойства верные,
//  HWND созданы, z-order верный - а экран пустой).
// ================================================================

partial class LauncherForm
{
    const int StepWelcome = 0;
    const int StepGreeting = 1;
    const int StepSorry = 2;
    const int StepQuest = 3;

    Panel artemRoot;
    ArtemBackdrop artemBg;
    RoundedButton artemBtnPlay;      // Послушать поздравления
    RoundedButton artemBtnSkip;      // Не послушать  -> самолёт
    RoundedButton artemBtnYes;       // Будешь пользоваться
    RoundedButton artemBtnNo;        // Нет -> скример
    RoundedButton artemBtnSorry;     // Понял
    RoundedButton artemBtnStart;     // Начать прохождение
    List<List<Control>> artemSteps = new List<List<Control>>();
    int artemStep = -1;
    ArtemScreamer artemScream;
    ArtemFlyer artemPlane;

    void BuildArtem()
    {
        artemRoot = new Panel();
        artemRoot.BackColor = Ui.Bg;
        Controls.Add(artemRoot);

        artemBg = new ArtemBackdrop();
        artemRoot.Controls.Add(artemBg);

        artemBtnPlay = MakeArtemBtn("Послушать поздравления", true);
        artemBtnPlay.Click += delegate { Artem.PlayGreeting(); ArtemStep(StepGreeting); };
        artemRoot.Controls.Add(artemBtnPlay);

        artemBtnSkip = MakeArtemBtn("Не послушать", false);
        artemBtnSkip.Click += delegate { EscapeAsPlane(); };
        artemRoot.Controls.Add(artemBtnSkip);

        artemBtnYes = MakeArtemBtn("Будешь пользоваться?", true);
        artemBtnYes.Click += delegate { Artem.PlaySound(2); ArtemStep(StepQuest); };
        artemRoot.Controls.Add(artemBtnYes);

        artemBtnNo = MakeArtemBtn("Нет", false);
        artemBtnNo.Click += delegate { FireScreamer(); };
        artemRoot.Controls.Add(artemBtnNo);

        artemBtnSorry = MakeArtemBtn("Понял, больше не шучу", true);
        artemBtnSorry.Click += delegate { ArtemStep(StepQuest); };
        artemRoot.Controls.Add(artemBtnSorry);

        artemBtnStart = MakeArtemBtn("Начать прохождение", true);
        artemBtnStart.Click += delegate { StartArtemQuest(); };
        artemRoot.Controls.Add(artemBtnStart);

        // группы экранов
        artemSteps.Clear();
        artemSteps.Add(new List<Control> { artemBg, artemBtnPlay, artemBtnSkip });
        artemSteps.Add(new List<Control> { artemBg, artemBtnYes, artemBtnNo });
        artemSteps.Add(new List<Control> { artemBg, artemBtnSorry });
        artemSteps.Add(new List<Control> { artemBg, artemBtnStart });

        ArtemLayout();
        ArtemStep(StepWelcome);
    }

    RoundedButton MakeArtemBtn(string text, bool primary)
    {
        RoundedButton b = RoundedButton.Dark(text, 12);
        b.Size = new Size(250, 46);
        b.Font = Ui.Font(10.5f, primary ? FontStyle.Bold : FontStyle.Regular);
        if (primary)
        {
            b.Top = Ui.Accent1;
            b.Bottom = Ui.Accent2;
            b.ForeColor = Color.White;
            b.BorderColor = Ui.AccentDark;
            b.BorderW = 1;
        }
        else
        {
            b.Top = Color.FromArgb(46, 51, 62);
            b.Bottom = Color.FromArgb(38, 42, 52);
            b.ForeColor = Ui.Gray;
            b.BorderColor = Ui.Border;
            b.BorderW = 1;
        }
        return b;
    }

    void ArtemLayout()
    {
        try
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            artemRoot.Location = new Point(0, 0);
            artemRoot.Size = new Size(w, h);
            // фон с фото обязан занимать ВСЕ окно, иначе картинка
            // рисуется маленьким уголком
            artemBg.Location = new Point(0, 0);
            artemBg.Size = new Size(w, h);

            int col = 660;                     // ширина колонки с кнопками
            int cx = (w - col) / 2;
            if (cx < 20) cx = 20;

            // заголовок и текст рисует сам artemBg по фото,
            // отдельных меток больше нет

            // кнопки
            int btnY = h - 118;
            int gap = 18;
            int bw = 250;
            artemBtnPlay.Location = new Point(cx + (col - bw * 2 - gap) / 2, btnY);
            artemBtnSkip.Location = new Point(cx + (col - bw * 2 - gap) / 2 + bw + gap, btnY);
            artemBtnYes.Location = new Point(cx + (col - bw * 2 - gap) / 2, btnY);
            artemBtnNo.Location = new Point(cx + (col - bw * 2 - gap) / 2 + bw + gap, btnY);
            artemBtnSorry.Location = new Point(cx + (col - bw) / 2, btnY);
            artemBtnStart.Location = new Point(cx + (col - bw) / 2, btnY);

            ApplyArtemVisibility();
        }
        catch { }
    }


    void ArtemStep(int n)
    {
        try
        {
            if (n < 0 || n >= artemSteps.Count) return;
            artemStep = n;

            // текст и заголовок рисует сам фон - задаём ему содержимое
            if (n == StepWelcome)
            {
                artemBg.Title = "Артём, с днём рождения!";
                artemBg.TitleColor = Ui.Gold;
                artemBg.Body = "Здесь тебя ждёт персональный лаунчер.\n"
                    + "Товарищ всё настроил лично для тебя.";
            }
            else if (n == StepGreeting)
            {
                artemBg.Title = "Поздравление";
                artemBg.TitleColor = Ui.Txt;
                artemBg.Body = Artem.Text;
            }
            else if (n == StepSorry)
            {
                artemBg.Title = "Всё серьёзно";
                artemBg.TitleColor = Ui.Gray;
                artemBg.Body = "Ладно, шучу. Просто не нажимай это.";
            }
            else
            {
                artemBg.Title = "Начать прохождение";
                artemBg.TitleColor = Ui.Accent1;
                artemBg.Body = Artem.Quest;
            }
            artemBg.Invalidate();

            ArtemLayout();          // вернуть всех на свои места
            ApplyArtemVisibility();  // и спрятать лишних за окно
        }
        catch { }
    }

    // Невидимые экраны УБИРАЕМ за пределы окна, а не гасим через Visible:
    // после Visible=false потомки в этом лаунчере перестают рисоваться.
    static readonly Point ArtemAway = new Point(-4000, -4000);

    void ApplyArtemVisibility()
    {
        try
        {
            if (artemStep < 0 || artemStep >= artemSteps.Count) return;
            List<Control> keep = artemSteps[artemStep];

            // СНАЧАЛА поднимаем активный экран: заголовок и текст числятся
            // сразу в нескольких экранах, иначе следующий цикл их спрячет.
            foreach (Control c in keep) c.BringToFront();

            // и только потом убираем за окно то, чего на экране нет
            foreach (List<Control> g in artemSteps)
            {
                foreach (Control c in g)
                {
                    if (keep.Contains(c)) continue;
                    if (c.Location != ArtemAway) c.Location = ArtemAway;
                }
            }
            artemRoot.BringToFront();
            try { if (artemScream != null && !artemScream.IsDisposed) artemScream.BringToFront(); } catch { }
            try { artemRoot.Refresh(); } catch { }
        }
        catch { }
    }

    // ---------- «Не послушать» -> бабочка с крыльями ----------

    void EscapeAsPlane()
    {
        try
        {
            // у RoundedButton поле Top - это ЦВЕТ градиента, а не координата,
            // поэтому позицию берём через Location
            Point at = artemBtnSkip.Location;
            // кнопка исчезает совсем - остаётся только бабочка
            artemBtnSkip.Location = ArtemAway;
            artemSteps[StepWelcome].Remove(artemBtnSkip);

            ArtemFlyer fly = new ArtemFlyer();
            fly.X = at.X + (artemBtnSkip.Width - 84) / 2;
            fly.Y = at.Y;
            artemBg.Fly = fly;
            artemPlane = fly;

            Action tick = null;
            tick = delegate
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) { Anim.Unsub(tick); return; }
                    fly.Update(ClientSize.Width, ClientSize.Height);
                    artemBg.Invalidate();
                    if (fly.GoneFar())
                    {
                        Anim.Unsub(tick);
                        artemBg.Fly = null;
                        artemPlane = null;
                    }
                }
                catch { Anim.Unsub(tick); }
            };
            Anim.Sub(tick);
        }
        catch { }
    }

    // ---------- скример ----------

    void FireScreamer()
    {
        try
        {
            if (artemScream != null) return;
            artemScream = new ArtemScreamer();
            artemScream.Location = new Point(0, 0);
            artemScream.Size = new Size(ClientSize.Width, ClientSize.Height);
            Controls.Add(artemScream);
            artemScream.BringToFront();
            artemScream.onDone = delegate
            {
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            if (artemScream != null) { artemScream.Dispose(); artemScream = null; }
                            // «Нет» исчезает, остаётся одна кнопка
                            artemSteps[StepGreeting].Remove(artemBtnNo);
                            ArtemStep(StepSorry);
                        }
                        catch { }
                    }));
                }
                catch { }
            };
            artemScream.Start();
        }
        catch { }
    }

    void CloseArtem()
    {
        try
        {
            // экран подарка нужен только на старте: убираем его с формы,
            // чтобы он больше не перекрывал лаунчер
            Controls.Remove(artemRoot);
            if (artemRoot != null) artemRoot.Dispose();
            artemRoot = null;
            ShowView(ViewHome);
        }
        catch { }
    }

    // Отладка: прогнать нужный шаг/событие экрана подарка
    public void DebugArtem(int what)
    {
        if (InvokeRequired) { Invoke(new Action<int>(DebugArtem), what); return; }
        try
        {
            if (what == 10) { EscapeAsPlane(); return; }
            if (what == 11) { FireScreamer(); return; }
            if (what == 12)
            {
                // скример, который не исчезает - чтобы успеть снять
                artemScream = new ArtemScreamer();
                artemScream.Location = new Point(0, 0);
                artemScream.Size = new Size(ClientSize.Width, ClientSize.Height);
                Controls.Add(artemScream);
                artemScream.BringToFront();
                artemScream.Start();
                return;
            }
            if (what == 13)
            {
                // бабочка на месте, без полёта - чтобы рассмотреть
                ArtemFlyer p = new ArtemFlyer();
                p.X = 420; p.Y = 300;
                p.Angle = -1.1; p.Speed = 11.5;
                artemBg.Fly = p;
                artemPlane = p;
                artemBg.Invalidate();
                return;
            }
            if (what == 14) { StartArtemQuest(); return; }
            ArtemStep(what);
        }
        catch { }
    }

    // Раскладку экранов подарка держит общий OnResize в Launcher.cs,
    // чтобы не плодить вторую переопределённую копию.
// ---- задание подарка: поставить сборку, потом запустить ----

    public const string QuestVersion = "1.21.8-fabric";
    public const string QuestMc = "1.21.8";
    bool artemBusy;

    // строка статуса прямо на экране подарка
    void ArtemStatus(string s)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            Invoke(new Action<string>(delegate(string t)
            {
                artemBg.Body = t;
                artemBg.Invalidate();
            }), s);
        }
        catch { }
    }

    // прогресс установки: (готово, всего) -> полоска на экране подарка
    void ArtemProg2(int done, int total)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            Invoke(new Action<int, int>(delegate(int d, int t2)
            {
                artemBg.Progress = t2 > 0 ? (int)(d * 100.0 / t2) : 0;
            }), done, total);
        }
        catch { }
    }

    void ArtemStep2(string s, int pct)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            Invoke(new Action<string, int>(delegate(string t, int p)
            {
                artemBg.Body = t;
                artemBg.Invalidate();
                artemBg.Progress = p;
            }), s, pct);
        }
        catch { }
    }

    void ArtemQuestFailed(string msg)
    {
        artemBusy = false;
        try { artemBg.Progress = -1; } catch { }
        if (!artemSteps[StepQuest].Contains(artemBtnStart))
            artemSteps[StepQuest].Add(artemBtnStart);
        ArtemLayout();
        artemBg.Title = "Не получилось";
        artemBg.TitleColor = Ui.Orange;
        ArtemStatus(msg + "\nПопробуй ещё раз.");
    }

    // Артём не качает ничего вручную: по кнопке лаунчер сам ставит
    // сборку Fabric 1.21.8 и запускает игру.
    void StartArtemQuest()
    {
        try
        {
            if (artemBusy) return;
            artemBusy = true;

            // прячем кнопку, показываем прогресс
            artemBtnStart.Location = ArtemAway;
            if (!artemSteps[StepQuest].Contains(artemBtnStart))
                artemSteps[StepQuest].Add(artemBtnStart);
            artemBg.Progress = 0;
            artemBg.Title = "Ставлю сборку";
            artemBg.TitleColor = Ui.Accent1;
            ArtemStatus("Готовлю загрузку...");
            artemBg.Invalidate();

            Thread t = new Thread((ThreadStart)delegate
            {
                try
                {
                    // 1. сборка Fabric 1.21.8 (скачается, если её нет)
                    ArtemStep2("Скачиваю Minecraft " + QuestMc, 5);
                    Mc.PrepareLaunch(QuestVersion, ArtemStatus, ArtemProg2);

                    ArtemStep2("Готово. Запускаю...", 100);

                    if (IsDisposed) return;
                    Invoke(new Action(delegate
                    {
                        try
                        {
                            artemBusy = false;
                            CloseArtem();
                            RefreshVersions();
                            StartGame(QuestVersion);
                        }
                        catch (Exception ex) { ArtemQuestFailed(ex.Message); }
                    }));
                }
                catch (Exception ex)
                {
                    try { if (!IsDisposed && IsHandleCreated) Invoke(new Action<string>(ArtemQuestFailed), ex.Message); }
                    catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }
        catch (Exception ex)
        {
            artemBusy = false;
            try { ShowError(ex.Message); } catch { }
        }
    }

    // Раскладку экранов подарка держит общий OnResize в Launcher.cs,
    // чтобы не плодить вторую переопределённую копию.
}
