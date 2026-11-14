using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// ================================================================
//  РАЗДЕЛ «СКИН» — рисуем скин прямо в лаунчере
//
//  Канва 64x64 — формат скина Minecraft. Инструменты: карандаш,
//  ластик, заливка, пипетка. Отмена/возврат, импорт и экспорт PNG,
//  модель classic/slim.
//
//  Кнопка «Отправить на аккаунт» грузит скин через Auth.cs прямо на
//  аккаунт Microsoft — такой скин видят ВСЕ игроки, моды не нужны.
//  Это единственный способ сделать скин видимым всем: свой скин с
//  клиента виден только тем, у кого стоит тот же мод.
//
//  Рисование живёт в SkinCanvas, раздел собирает панель вокруг него.
// ================================================================

partial class LauncherForm
{
    List<Control> skinUi = new List<Control>();
    SkinCanvas skinCanvas;
    // холст уже получил текущую текстуру игрока (не начинаем с пустого)
    bool skinCanvasSeeded;
    Panel skinTools;
    Panel skinToolsInner;
    ScrollPanel skinToolsScroll;
    Label skinStatusLbl;
    RoundedButton skinToolPen, skinToolErase, skinToolFill, skinToolPick;
    RoundedButton skinBtnClassic, skinBtnSlim;
    Panel skinSwatch;
    TextBox skinHex;
    string skinModel = "CLASSIC";
    SkinView3D skin3d;
    RoundedButton skinSpin;
    Label skin3dHint;
    RoundedButton skinUpBtn, skinFromBtn, skinResetBtn;
    // Полная высота содержимого панели инструментов: нужна раскладке,
    // чтобы понять, появится ли вертикальный скролл.
    int skinToolsContentH = 0;

    // Отладочные прогоны со снимками: скин из файла и рисование по модели
    public static string DbgSkinFile = null;
    public static bool DbgSkinDemo = false;
    public static bool DbgSkinNoSpin = false;
    public static bool DbgSkinDump = false;
    public static bool DbgSkinReadOnly = false;
    public static bool DbgSkinUpload = false;
    public static bool DbgSkinOffline = false;
    public static bool DbgSkinGet = false;
    public static bool DbgSkinClear = false;
    // --shot-run ... scrollto:N — прокрутить активный список в низ
    public static int DbgScrollTo = -1;
    // --shot-run ... settings:N - открыть настройки на вкладке N
    public static int DbgSettingsTab = -1;
    // --shot-run ... dd:N - открыть выпадающий список N (снимки)
    public static int DbgDropdown = -1;
    // --shot-run ... ai / ai:запрос - открыть помощника (и задать запрос)
    public static bool DbgAiOpen = false;
    public static string DbgAiQuery = null;

    // Палитра: 32 цвета, по 16 в ряд
    static readonly string[] SkinPalette = new string[]
    {
        "#FFFFFF", "#C6C6C6", "#8B8B8B", "#4A4A4A", "#1A1A1A", "#000000", "#5B3A29", "#8B5A2B",
        "#C19A6B", "#E8CDA3", "#FFD9B3", "#E0A070", "#B5651D", "#FF5555", "#C0392B", "#7B241C",
        "#FF9E4F", "#FFC93C", "#F7EC59", "#B8E986", "#5DD35B", "#2E9E4F", "#1ABC9C", "#3FD0D4",
        "#4FA3F7", "#2A5FD6", "#5B4FE0", "#A55EEA", "#E056FD", "#FF7BAC", "#8E6E53", "#3B2F2F"
    };

    // ============================================================
    //  СБОРКА РАЗДЕЛА
    // ============================================================

    void BuildSkinSection()
    {
        skinUi = new List<Control>();

        Panel bg = new Panel();
        bg.BackColor = Ui.Bg;
        bg.Location = new Point(SecX, SecY);
        bg.Size = new Size(888, 518);
        Controls.Add(bg); skinUi.Add(bg);

        PixelLabel head = new PixelLabel();
        head.Text = "Скин";
        head.Font = Ui.Font(17, FontStyle.Bold);
        head.ForeColor = Ui.Txt;
        head.BackColor = Ui.Bg;
        head.AutoSize = false;
        head.Location = new Point(SecX, SecY);
        head.Size = new Size(400, 32);
        Controls.Add(head); skinUi.Add(head);

        Label sub = new Label();
        sub.Text = "Нарисуй скин: с лицензией он уйдёт на аккаунт Microsoft, без лицензии — в Luxe";
        sub.Font = Ui.Font(9.5f);
        sub.ForeColor = Ui.Gray;
        sub.BackColor = Ui.Bg;
        sub.AutoSize = false;
        sub.Location = new Point(SecX, SecY + 34);
        sub.Size = new Size(700, 20);
        Controls.Add(sub); skinUi.Add(sub);

        // ---- канва ----
        skinCanvas = new SkinCanvas();
        skinCanvas.Location = new Point(SecX, SecY + 72);
        skinCanvas.Size = new Size(384, 384);
        skinCanvas.Picked = delegate(Color c) { SkinSetColor(c); SelectSkinTool(0); };
        skinCanvas.Changed = delegate
        {
            SkinToolHint();
            if (skin3d != null) skin3d.Invalidate();
            // Главный экран живёт от той же текстуры: мазок кисти сразу
            // меняет фигуру на витрине и аватар в профиле.
            try { PlayerSkin.SetLocal(skinCanvas.Bmp); } catch { }
        };
        Controls.Add(skinCanvas); skinUi.Add(skinCanvas);

        // Подпись под канвой: что сейчас делает мышь
        Label hint = new Label();
        hint.Name = "skinHint";
        hint.Font = Ui.Font(9);
        hint.ForeColor = Ui.GrayDim;
        hint.BackColor = Ui.Bg;
        hint.AutoSize = false;
        hint.Location = new Point(SecX, SecY + 462);
        hint.Size = new Size(384, 50);
        Controls.Add(hint); skinUi.Add(hint);

        // ---- 3D-вид: та же текстура, но на модели ----
        skin3d = new SkinView3D();
        skin3d.Canvas = skinCanvas;
        skin3d.Location = new Point(SecX + 398, SecY + 72);
        skin3d.Size = new Size(256, 540);
        skin3d.HoverTexel = delegate(Point p)
        {
            if (skinCanvas == null) return;
            skinCanvas.Highlight = p;
            skinCanvas.Invalidate();
        };
        Controls.Add(skin3d); skinUi.Add(skin3d);

        skinSpin = RoundedButton.Dark("Вращение: вкл", 6);
        skinSpin.Font = Ui.Font(9);
        skinSpin.ForeColor = Ui.Txt;
        skinSpin.Location = new Point(SecX + 398, SecY + 618);
        skinSpin.Size = new Size(256, 28);
        skinSpin.Click += delegate { SkinToggleSpin(); };
        Controls.Add(skinSpin); skinUi.Add(skinSpin);

        skin3dHint = new Label();
        skin3dHint.Font = Ui.Font(8.5f);
        skin3dHint.ForeColor = Ui.GrayDim;
        skin3dHint.BackColor = Ui.Bg;
        skin3dHint.AutoSize = false;
        skin3dHint.Location = new Point(SecX + 398, SecY + 650);
        skin3dHint.Size = new Size(256, 36);
        skin3dHint.Text = "ЛКМ — рисовать, ПКМ — крутить, колесо — зум";
        Controls.Add(skin3dHint); skinUi.Add(skin3dHint);
        SkinPaintSpin();

        // ---- панель инструментов ----
        // Содержимого ~600px, а на низком окне панель получает 514: нижние
        // кнопки и статус раньше просто уходили за край и были недоступны.
        // Теперь панель прокручивается (ScrollPanel), а дети лежат на
        // «листе» Inner — он и едет вверх.
        ScrollPanel toolsScroll = new ScrollPanel();
        toolsScroll.BackColor = Ui.Bg;
        toolsScroll.Location = new Point(SecX + 668, SecY + 72);
        toolsScroll.Size = new Size(236, 614);
        Controls.Add(toolsScroll); skinUi.Add(toolsScroll);
        skinTools = toolsScroll;

        skinToolsInner = new Panel();
        skinToolsInner.BackColor = Ui.Bg;
        skinToolsInner.Location = new Point(0, 0);
        skinToolsInner.Size = new Size(222, 600);
        toolsScroll.Controls.Add(skinToolsInner);
        toolsScroll.Inner = skinToolsInner;
        toolsScroll.BarGutter = 14;
        skinToolsScroll = toolsScroll;
        skinTools = skinToolsInner;

        // Ширина листа 222, а не 236: 14px справа отданы под тонкую
        // полосу прокрутки, иначе она ложилась бы поверх кнопок.
        const int colW = 106;
        const int col2 = 116;

        int y = 0;

        skinTools.Controls.Add(SkinLabel("Инструмент", y)); y += 17;

        skinToolPen = SkinToolBtn("Карандаш", 0, y, colW, col2);
        skinToolErase = SkinToolBtn("Ластик", 1, y, colW, col2);
        skinToolFill = SkinToolBtn("Заливка", 2, y, colW, col2);
        skinToolPick = SkinToolBtn("Пипетка", 3, y, colW, col2);
        y += 72;

        skinTools.Controls.Add(SkinLabel("Цвет", y)); y += 17;

        skinSwatch = new Panel();
        skinSwatch.BackColor = Color.White;
        skinSwatch.BorderStyle = BorderStyle.FixedSingle;
        skinSwatch.Location = new Point(0, y);
        skinSwatch.Size = new Size(36, 32);
        skinTools.Controls.Add(skinSwatch);

        RoundedPanel hexBox = new RoundedPanel();
        hexBox.Radius = 6;
        hexBox.BackColor = Ui.SurfaceHi;
        hexBox.Border = Ui.Border;
        hexBox.BorderW = 1;
        hexBox.Location = new Point(44, y);
        hexBox.Size = new Size(80, 32);
        skinTools.Controls.Add(hexBox);

        skinHex = new TextBox();
        skinHex.Font = Ui.Font(10.5f);
        skinHex.BackColor = Ui.SurfaceHi;
        skinHex.ForeColor = Ui.Txt;
        skinHex.BorderStyle = BorderStyle.None;
        skinHex.Location = new Point(8, 7);
        skinHex.Size = new Size(64, 20);
        skinHex.Text = "FFFFFF";
        skinHex.KeyDown += delegate(object s, KeyEventArgs k)
        {
            if (k.KeyCode == Keys.Enter) { k.SuppressKeyPress = true; SkinApplyHex(); }
        };
        hexBox.Controls.Add(skinHex);

        RoundedButton hexGo = new RoundedButton();
        hexGo.Text = "OK";
        hexGo.Radius = 6;
        hexGo.Font = Ui.Font(9, FontStyle.Bold);
        hexGo.Location = new Point(col2, y);
        hexGo.Size = new Size(colW, 32);
        hexGo.Click += delegate { SkinApplyHex(); };
        skinTools.Controls.Add(hexGo);
        y += 36;

        RoundedButton newBtn = RoundedButton.Dark("Очистить холст", 6);
        newBtn.Font = Ui.Font(9);
        newBtn.ForeColor = Ui.Txt;
        newBtn.Location = new Point(0, y);
        newBtn.Size = new Size(222, 30);
        newBtn.Click += delegate
        {
            skinCanvas.Clear();
            SkinStatus("Холст очищен", true);
        };
        skinTools.Controls.Add(newBtn);
        y += 34;

        // ---- палитра: 8 столбцов, 4 ряда ----
        int palY = y;
        for (int i = 0; i < SkinPalette.Length; i++)
        {
            int col = i;
            Panel sw = new Panel();
            sw.BackColor = SkinColor(SkinPalette[i]);
            sw.BorderStyle = BorderStyle.FixedSingle;
            sw.Location = new Point((i % 8) * 27, palY + (i / 8) * 26);
            sw.Size = new Size(22, 22);
            sw.Cursor = Cursors.Hand;
            sw.Click += delegate { SkinSetColor(SkinColor(SkinPalette[col])); };
            skinTools.Controls.Add(sw);
        }
        y += 4 * 26;

        // ---- отмена ----
        RoundedButton undo = RoundedButton.Dark("Отменить", 6);
        undo.Font = Ui.Font(9);
        undo.ForeColor = Ui.Txt;
        undo.Location = new Point(0, y);
        undo.Size = new Size(colW, 30);
        undo.Click += delegate { skinCanvas.Undo(); };
        skinTools.Controls.Add(undo);

        RoundedButton redo = RoundedButton.Dark("Вернуть", 6);
        redo.Font = Ui.Font(9);
        redo.ForeColor = Ui.Txt;
        redo.Location = new Point(col2, y);
        redo.Size = new Size(colW, 30);
        redo.Click += delegate { skinCanvas.Redo(); };
        skinTools.Controls.Add(redo);
        y += 34;

        skinTools.Controls.Add(SkinLabel("Модель", y)); y += 17;

        skinBtnClassic = new RoundedButton();
        skinBtnClassic.Text = "Классическая";
        skinBtnClassic.Radius = 6;
        skinBtnClassic.Font = Ui.Font(9);
        skinBtnClassic.Location = new Point(0, y);
        skinBtnClassic.Size = new Size(colW, 30);
        skinBtnClassic.Click += delegate { SkinSetModel("CLASSIC"); };
        skinTools.Controls.Add(skinBtnClassic);

        skinBtnSlim = new RoundedButton();
        skinBtnSlim.Text = "Тонкая";
        skinBtnSlim.Radius = 6;
        skinBtnSlim.Font = Ui.Font(9);
        skinBtnSlim.Location = new Point(col2, y);
        skinBtnSlim.Size = new Size(colW, 30);
        skinBtnSlim.Click += delegate { SkinSetModel("SLIM"); };
        skinTools.Controls.Add(skinBtnSlim);
        y += 34;

        // ---- файлы ----
        RoundedButton imp = RoundedButton.Dark("Импорт PNG", 6);
        imp.Font = Ui.Font(9);
        imp.ForeColor = Ui.Txt;
        imp.Location = new Point(0, y);
        imp.Size = new Size(colW, 30);
        imp.Click += delegate { SkinImport(); };
        skinTools.Controls.Add(imp);

        RoundedButton exp = RoundedButton.Dark("Экспорт PNG", 6);
        exp.Font = Ui.Font(9);
        exp.ForeColor = Ui.Txt;
        exp.Location = new Point(col2, y);
        exp.Size = new Size(colW, 30);
        exp.Click += delegate { SkinExport(); };
        skinTools.Controls.Add(exp);
        y += 34;

        skinFromBtn = RoundedButton.Dark("Скин с аккаунта", 6);
        skinFromBtn.Font = Ui.Font(9);
        skinFromBtn.ForeColor = Ui.Txt;
        skinFromBtn.Location = new Point(0, y);
        skinFromBtn.Size = new Size(222, 30);
        skinFromBtn.Click += delegate { SkinFromAccount(); };
        skinTools.Controls.Add(skinFromBtn);
        y += 34;

        // ---- на аккаунт (или в Luxe, если лицензии нет) ----
        skinUpBtn = new RoundedButton();
        skinUpBtn.Text = "Отправить на аккаунт";
        skinUpBtn.Radius = 6;
        skinUpBtn.Font = Ui.Font(10, FontStyle.Bold);
        skinUpBtn.Location = new Point(0, y);
        skinUpBtn.Size = new Size(222, 38);
        skinUpBtn.Click += delegate { SkinUpload(); };
        skinTools.Controls.Add(skinUpBtn);
        y += 42;

        skinResetBtn = RoundedButton.Dark("Вернуть стандартный скин", 6);
        skinResetBtn.Font = Ui.Font(9);
        skinResetBtn.ForeColor = Ui.Txt;
        skinResetBtn.Location = new Point(0, y);
        skinResetBtn.Size = new Size(222, 30);
        skinResetBtn.Click += delegate { SkinReset(); };
        skinTools.Controls.Add(skinResetBtn);
        y += 34;

        skinStatusLbl = new Label();
        skinStatusLbl.Font = Ui.Font(9);
        skinStatusLbl.ForeColor = Ui.GrayDim;
        skinStatusLbl.BackColor = Ui.Bg;
        skinStatusLbl.AutoSize = false;
        skinStatusLbl.Location = new Point(0, y);
        // 58 вместо 40: длинный статус («Офлайн-ник: … но эта версия не
        // Fabric…») занимает три строки, на двух он обрывался на полуслове
        skinStatusLbl.Size = new Size(222, 58);
        skinTools.Controls.Add(skinStatusLbl);

        // высота листа = фактический низ содержимого: от неё зависит,
        // появится ли прокрутка, поэтому «на глаз» её задавать нельзя
        skinToolsContentH = y + 58 + 8;
        skinToolsInner.Height = skinToolsContentH;

        SelectSkinTool(0);
        SkinSetColor(Color.White);
        SkinSetModel("CLASSIC");
        SkinStatus("");
        // Холст стартует с текущей текстуры: раньше он открывался пустым,
        // и первый же мазок заменял скин на витрине прозрачной картинкой.
        SkinSeedCanvas();

        // Отладочный прогон: сразу открыть скин из файла
        if (DbgSkinFile != null && File.Exists(DbgSkinFile))
        {
            string ferr;
            Bitmap b = SkinCanvas.FromFile(DbgSkinFile, out ferr);
            if (b != null)
            {
                skinCanvas.Load(b);
                b.Dispose();
                if (DbgSkinFile.ToLower().Contains("slim")) SkinSetModel("SLIM");
            }
            else SkinStatus(ferr, false);
        }
        if (DbgSkinNoSpin) { skin3d.AutoSpin = false; SkinPaintSpin(); }
        // отладка: мышь не должна мешать снимку (залипание кнопок и т.п.)
        if (DbgSkinReadOnly) { skinCanvas.Enabled = false; skin3d.Enabled = false; }
        // отладка: что лежит в холсте сразу после сборки раздела
        if (DbgSkinDump) { try { DebugDumpSkin(Path.Combine(Cfg.BaseDir, "skin_dump_early.png")); } catch { } }
    }

    // Отладочный прогон: сохранить холст скина отдельным файлом
    public void DebugDumpSkin(string path)
    {
        try { if (skinCanvas != null && skinCanvas.Bmp != null) skinCanvas.Bmp.Save(path, ImageFormat.Png); } catch { }
    }

    // Отладочный прогон: отправить скин так, как это сделал бы игрок
    public void DebugSkinUpload()
    {
        SkinUpload();
    }

    // Отладочный прогон: достать свой скин из Luxe, как это сделал бы игрок
    public void DebugSkinGet()
    {
        SkinFromAccount();
    }

    // Отладочный прогон: убрать скин из Luxe без вопроса
    public void DebugSkinClear()
    {
        DbgSkinClear = true;
        try { SkinReset(); }
        finally { DbgSkinClear = false; }
    }

    // Отладочный прогон: ткнуть по модели в несколько мест
    public void DebugSkin3dDemo()
    {
        if (skin3d == null) return;
        int[][] pts = new int[][]
        {
            new int[] { skin3d.Width / 2, skin3d.Height / 2 },              // тело
            new int[] { skin3d.Width / 2, (int)(skin3d.Height * 0.22f) },   // голова
            new int[] { skin3d.Width / 2, (int)(skin3d.Height * 0.82f) },   // нога
            new int[] { (int)(skin3d.Width * 0.42f), (int)(skin3d.Height * 0.45f) } // рука
        };
        foreach (int[] p in pts) skin3d.DebugPaintAt(new Point(p[0], p[1]));
    }

    Label SkinLabel(string text, int y)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(9);
        l.ForeColor = Ui.GrayDim;
        l.BackColor = Ui.Bg;
        l.AutoSize = false;
        l.Location = new Point(0, y);
        l.Size = new Size(222, 17);
        return l;
    }

    RoundedButton SkinToolBtn(string text, int tool, int y, int w, int x2)
    {
        RoundedButton b = new RoundedButton();
        b.Text = text;
        b.Radius = 6;
        b.Font = Ui.Font(9.5f);
        b.Location = new Point((tool % 2) * x2, y + (tool / 2) * 38);
        b.Size = new Size(w, 34);
        b.Click += delegate { SelectSkinTool(tool); };
        skinTools.Controls.Add(b);
        return b;
    }


    // Раскладка раздела под текущий размер окна: канва | 3D | инструменты
    void LayoutSkin(int w, int h)
    {
        if (skinUi == null || skinUi.Count < 4) return;
        skinUi[0].Size = new Size(w, h);

        int gap = 14;
        int toolsW = 236;
        int toolsX = w - toolsW;
        // Канва обязана быть кратной 64 (сетка скина), поэтому «на глаз»
        // её не сжать: подбираем масштаб 6x/5x/4x под доступную ширину.
        int canvasW = 384;
        if (toolsX - gap - canvasW - gap < 150) canvasW = 320;
        if (toolsX - gap - canvasW - gap < 130) canvasW = 256;
        int threeX = canvasW + gap;
        // 3D-вид не должен заезжать под панель инструментов: при узком окне
        // он ужимается, а не наезжает на колонку справа.
        int threeW = toolsX - gap - threeX;
        if (threeW < 120) threeW = 120;
        // 3D + галочка вращения + подпись должны влезть в высоту раздела
        int threeH = h - 72 - 80;
        if (threeH > 540) threeH = 540;
        if (threeH < 200) threeH = 200;

        Control hint = null;
        foreach (Control c in skinUi) if (c.Name == "skinHint") { hint = c; break; }

        // канва масштабируется целым числом: Zoom 6/5/4 — пиксели скина
        // остаются ровными, без размытия и «полупикселей»
        if (skinCanvas != null)
        {
            int zoom = canvasW / SkinCanvas.SkinSize;
            if (zoom < 3) zoom = 3;
            skinCanvas.Zoom = zoom;
            int side = SkinCanvas.SkinSize * zoom;
            skinCanvas.Location = new Point(SecX, SecY + 72);
            skinCanvas.Size = new Size(side, side);
            canvasW = side;
        }

        if (hint != null)
        {
            hint.Location = new Point(SecX, SecY + 72 + canvasW + 6);
            hint.Size = new Size(canvasW, Math.Max(20, h - 72 - canvasW - 6));
        }

        if (skin3d != null)
        {
            skin3d.Location = new Point(SecX + threeX, SecY + 72);
            skin3d.Size = new Size(threeW, threeH);
        }
        if (skinSpin != null)
        {
            skinSpin.Location = new Point(SecX + threeX, SecY + 72 + threeH + 6);
            skinSpin.Size = new Size(threeW, 28);
        }
        if (skin3dHint != null)
        {
            skin3dHint.Location = new Point(SecX + threeX, SecY + 72 + threeH + 38);
            skin3dHint.Size = new Size(threeW, 36);
        }
        if (skinToolsScroll != null)
        {
            skinToolsScroll.Location = new Point(SecX + toolsX, SecY + 72);
            skinToolsScroll.Size = new Size(toolsW, h - 72);
        }
        if (skinToolsInner != null)
        {
            // прокрутка появляется только когда контент реально не влезает:
            // иначе лист растягивается на всю высоту панели
            int viewH = skinToolsScroll != null ? skinToolsScroll.ClientSize.Height : 0;
            skinToolsInner.Height = Math.Max(skinToolsContentH, viewH);
            skinToolsInner.Width = skinToolsScroll != null
                ? Math.Max(160, skinToolsScroll.ClientSize.Width - skinToolsScroll.BarGutter)
                : skinToolsInner.Width;
            if (skinToolsScroll != null && skinToolsInner.Height <= viewH)
                skinToolsScroll.ResetScroll();
        }
    }

    // ============================================================
    //  СОСТОЯНИЕ
    // ============================================================

    void SkinStatus(string s)
    {
        SkinStatus(s, true);
    }

    void SkinStatus(string s, bool ok)
    {
        try
        {
            if (skinStatusLbl == null) return;
            skinStatusLbl.Text = s;
            skinStatusLbl.ForeColor = ok ? Ui.Gray : Ui.Red;
        }
        catch { }
    }

    void SkinToolHint()
    {
        try
        {
            Control h = null;
            foreach (Control c in skinUi) if (c.Name == "skinHint") { h = c; break; }
            if (h == null) return;
            h.Text = "Клик — поставить пиксель, с зажатой кнопкой — линия. " +
                     "Справа та же текстура на модели — по ней тоже можно рисовать.";
        }
        catch { }
    }

    void SelectSkinTool(int t)
    {
        try
        {
            if (skinCanvas != null) skinCanvas.Tool = t;
            SkinPaint(skinToolPen, t == 0);
            SkinPaint(skinToolErase, t == 1);
            SkinPaint(skinToolFill, t == 2);
            SkinPaint(skinToolPick, t == 3);
        }
        catch { }
    }

    // Выбранный инструмент подсвечен акцентом, остальные — тёмные
    void SkinPaint(RoundedButton b, bool on)
    {
        if (b == null) return;
        Color top = on ? Ui.Accent1 : Ui.SurfaceHi;
        Color bot = on ? Ui.Accent2 : Ui.SurfaceHi;
        b.Top = top;
        b.Bottom = bot;
        b.HoverTop = ControlPaint.Light(top, 0.12f);
        b.HoverBottom = ControlPaint.Light(bot, 0.08f);
        b.PressTop = ControlPaint.Dark(bot, 0.1f);
        b.PressBottom = ControlPaint.Dark(bot, 0.2f);
        b.ForeColor = Ui.Txt;
        b.Invalidate();
    }

    void SkinSetColor(Color c)
    {
        try
        {
            if (skinCanvas != null) skinCanvas.Current = c;
            if (skinSwatch != null) skinSwatch.BackColor = c;
            if (skinHex != null) skinHex.Text = (c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"));
        }
        catch { }
    }

    // Кнопка «Вращение»: включена — авто-поворот модели
    void SkinToggleSpin()
    {
        if (skin3d == null) return;
        skin3d.AutoSpin = !skin3d.AutoSpin;
        SkinPaintSpin();
    }

    void SkinPaintSpin()
    {
        if (skinSpin == null || skin3d == null) return;
        bool on = skin3d.AutoSpin;
        skinSpin.Text = on ? "Вращение: вкл" : "Вращение: выкл";
        Color top = on ? Ui.Accent1 : Ui.SurfaceHi;
        Color bot = on ? Ui.Accent2 : Ui.SurfaceHi;
        skinSpin.Top = top;
        skinSpin.Bottom = bot;
        skinSpin.HoverTop = ControlPaint.Light(top, 0.12f);
        skinSpin.HoverBottom = ControlPaint.Light(bot, 0.08f);
        skinSpin.PressTop = ControlPaint.Dark(bot, 0.1f);
        skinSpin.PressBottom = ControlPaint.Dark(bot, 0.2f);
        skinSpin.ForeColor = on ? Color.White : Ui.Txt;
        skinSpin.Invalidate();
    }

    void SkinApplyHex()
    {
        try
        {
            string s = skinHex.Text.Trim().TrimStart('#');
            if (s.Length != 6) { SkinStatus("Цвет — 6 знаков, например FFFFFF", false); return; }
            int v = Convert.ToInt32(s, 16);
            SkinSetColor(Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF));
            SkinStatus("");
        }
        catch { SkinStatus("Цвет — 6 знаков, например FFFFFF", false); }
    }

    void SkinSetModel(string m)
    {
        skinModel = m;
        try
        {
            if (skin3d != null) skin3d.Slim = (m == "SLIM");
            SkinPaint(skinBtnClassic, m == "CLASSIC");
            SkinPaint(skinBtnSlim, m == "SLIM");
            // модель влияет на толщину рук, поэтому витрину надо обновить
            PlayerSkin.SetSlim(m == "SLIM");
        }
        catch { }
    }

    // Быстрая перекраска части скина: кнопки помощника «тёмные волосы»,
    // «одежда в цвет» и т.п. До этого они лишь меняли цвет кисти —
    // на экране не происходило ровно ничего.
    // part: hair | shirt | pants
    void SkinQuickPaint(string part, Color c)
    {
        Bitmap tex = PlayerSkin.Tex;
        if (tex == null) return;
        Bitmap work = null;
        try
        {
            work = tex.Clone(new Rectangle(0, 0, tex.Width, tex.Height),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Rectangle[] rects;
            if (part == "hair")
            {
                // верх, бока и затылок головы; лицо не трогаем
                rects = new Rectangle[] {
                    new Rectangle(8, 0, 8, 8), new Rectangle(16, 0, 8, 8),
                    new Rectangle(0, 8, 8, 8), new Rectangle(16, 8, 8, 8),
                    new Rectangle(24, 8, 8, 8),
                    new Rectangle(40, 0, 8, 8), new Rectangle(48, 8, 8, 8)
                };
            }
            else if (part == "shirt")
            {
                rects = new Rectangle[] {
                    new Rectangle(20, 16, 8, 4), new Rectangle(28, 16, 8, 4),
                    new Rectangle(16, 20, 4, 12), new Rectangle(20, 20, 8, 12),
                    new Rectangle(28, 20, 4, 12), new Rectangle(32, 20, 8, 12),
                    new Rectangle(44, 16, 4, 4), new Rectangle(48, 16, 4, 4),
                    new Rectangle(40, 20, 4, 12), new Rectangle(44, 20, 4, 12),
                    new Rectangle(48, 20, 4, 12), new Rectangle(52, 20, 4, 12)
                };
            }
            else
            {
                rects = new Rectangle[] {
                    new Rectangle(4, 16, 4, 4), new Rectangle(8, 16, 4, 4),
                    new Rectangle(0, 20, 4, 12), new Rectangle(4, 20, 4, 12),
                    new Rectangle(8, 20, 4, 12), new Rectangle(12, 20, 4, 12),
                    new Rectangle(20, 48, 4, 4), new Rectangle(24, 48, 4, 4),
                    new Rectangle(16, 52, 4, 12), new Rectangle(20, 52, 4, 12),
                    new Rectangle(24, 52, 4, 12), new Rectangle(28, 52, 4, 12)
                };
            }
            using (Graphics g = Graphics.FromImage(work))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, c)))
            {
                foreach (Rectangle r in rects) g.FillRectangle(b, r);
            }
            // витрина главной и аватар
            PlayerSkin.SetLocal(work);
            // редактор, если он уже построен, держим в том же состоянии
            if (skinCanvas != null) skinCanvas.Load(work);
            if (skin3d != null) skin3d.Invalidate();
            SkinStatus("Перекрашено: " + (part == "hair" ? "волосы"
                       : part == "shirt" ? "одежда" : "штаны"), true);
        }
        catch { }
        finally
        {
            if (work != null) { try { work.Dispose(); } catch { } }
        }
    }

    static Color SkinColor(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return Color.White; }
    }

    // Вызывается при входе в раздел
    // Первый показ редактора: рисуем поверх настоящего скина игрока.
    // Если текстура приехала из сети позже — SkinRefresh догонит.
    void SkinSeedCanvas()
    {
        if (skinCanvasSeeded || skinCanvas == null) return;
        Bitmap tex = PlayerSkin.Tex;
        if (tex == null) return;
        try
        {
            skinCanvasSeeded = true;
            skinCanvas.Load(tex);
        }
        catch { skinCanvasSeeded = false; }
    }

    void SkinRefresh()
    {
        try
        {
            SkinSeedCanvas();
            if (skinStatusLbl == null) return;
            SkinPaintMode();
            if (Auth.LoggedIn)
                SkinStatus("Аккаунт: " + Auth.Name + " • лицензия. Скин увидят все.");
            else
            {
                string kind = "";
                try { kind = Mc.KindOf(curVer()); } catch { }
                bool hasMod = kind == "fabric" || kind == "quilt";
                if (hasMod)
                    SkinStatus("Офлайн-ник: " + nick + ". Скин сохраню в Luxe — его увидят в сборках лаунчера.");
                else
                    SkinStatus("Офлайн-ник: " + nick + ". Скин сохраню в Luxe, но эта версия не Fabric — в игре мода не будет.");
            }
        }
        catch { }
    }

    // Кнопки раздела зависят от лицензии: у неё скин живёт на серверах
    // Mojang, а офлайн-игроку место скина даёт сервис Luxe.
    void SkinPaintMode()
    {
        bool lic = Auth.LoggedIn;
        if (skinUpBtn != null) skinUpBtn.Text = lic ? "Отправить на аккаунт" : "Сохранить скин в Luxe";
        if (skinFromBtn != null) skinFromBtn.Text = lic ? "Скин с аккаунта" : "Мой скин из Luxe";
        if (skinResetBtn != null) skinResetBtn.Text = lic ? "Вернуть стандартный скин" : "Убрать скин из Luxe";
    }

    // ============================================================
    //  ФАЙЛЫ
    // ============================================================

    void SkinImport()
    {
        try
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Открыть скин";
                d.Filter = "Скин Minecraft (*.png)|*.png";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string err;
                Bitmap b = SkinCanvas.FromFile(d.FileName, out err);
                if (b == null) { SkinStatus(err, false); return; }
                skinCanvas.Load(b);
                b.Dispose();
                SkinStatus("Скин загружен из файла", true);
            }
        }
        catch (Exception ex) { SkinStatus("Не открылось: " + ex.Message, false); }
    }

    void SkinExport()
    {
        try
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Сохранить скин";
                d.Filter = "Скин Minecraft (*.png)|*.png";
                d.FileName = (Auth.LoggedIn && Auth.Name.Length > 0 ? Auth.Name : "skin") + ".png";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                using (Bitmap b = skinCanvas.Export()) b.Save(d.FileName, ImageFormat.Png);
                SkinStatus("Сохранено: " + Path.GetFileName(d.FileName), true);
            }
        }
        catch (Exception ex) { SkinStatus("Не сохранилось: " + ex.Message, false); }
    }

    // ============================================================
    //  ОТПРАВКА НА АККАУНТ
    // ============================================================

    void SkinUpload()
    {
        byte[] png;
        try
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Bitmap b = skinCanvas.Export()) b.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }
        }
        catch (Exception ex) { SkinStatus("Не собрался PNG: " + ex.Message, false); return; }

        // Без лицензии скину негде жить у Mojang — сохраняем его в Luxe.
        // В игре его покажет мод, поэтому он и виден только в сборках лаунчера.
        if (!Auth.LoggedIn || DbgSkinOffline) { SkinUploadOffline(png); return; }

        SkinStatus("Отправляю скин на аккаунт...");
        string model = skinModel;
        Thread t = new Thread((ThreadStart)delegate
        {
            string err;
            bool ok = Auth.UploadSkin(png, model, out err);
            Ui_(delegate
            {
                if (ok) SkinStatus("Скин отправлен на аккаунт " + Auth.Name + " — его видят все.", true);
                else SkinStatus("Не получилось: " + err, false);
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // Офлайн-скин: кладём PNG на сервис Luxe под ником игрока.
    void SkinUploadOffline(byte[] png)
    {
        string who = nick;
        string model = skinModel;
        SkinUploadLog("старт who=" + who + " model=" + model + " байт=" + png.Length + " сервис=" + Luxe.Host());
        SkinStatus("Сохраняю скин в Luxe...");
        Thread t = new Thread((ThreadStart)delegate
        {
            string err = "";
            bool ok = false;
            try
            {
                if (!Luxe.LoginFor(who)) err = Luxe.LastError;
                else if (!Luxe.SetSkin(png, model)) err = Luxe.LastError;
                else ok = true;
            }
            catch (Exception ex) { err = ex.Message; }
            SkinUploadLog("итог ok=" + ok + " err=" + err);
            Ui_(delegate
            {
                if (ok) SkinStatus("Скин сохранён для " + who + ". В игре его покажет мод Luxe.", true);
                else SkinStatus("Не получилось: " + (err == "" ? "сервис не отвечает" : err), false);
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // Отладка отладочного прогона: обычным игрокам этот файл не нужен
    void SkinUploadLog(string s)
    {
        if (!Cfg.Dbg) return;
        try
        {
            File.AppendAllText(Path.Combine(Cfg.BaseDir, "skin_upload.log"),
                DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n", System.Text.Encoding.UTF8);
        }
        catch { }
    }

    // Загрузить текущий скин аккаунта в редактор (посмотреть и доработать)
    void SkinFromAccount()
    {
        // У офлайн-игрока «аккаунт» — это сервис Luxe
        if (!Auth.LoggedIn) { SkinFromLuxe(); return; }

        SkinStatus("Запрашиваю скин с аккаунта...");
        Auth.RefreshProfileAsync(delegate(bool ok, string msg)
        {
            if (!ok)
            {
                Ui_(delegate { SkinStatus("Не получилось: " + msg, false); });
                return;
            }
            string url = Auth.CurrentSkinUrl();
            if (url.Length == 0)
            {
                Ui_(delegate { SkinStatus("На аккаунте нет скина.", false); });
                return;
            }
            Thread t = new Thread((ThreadStart)delegate
            {
                string derr;
                byte[] png = Auth.DownloadSkin(url, out derr);
                Ui_(delegate
                {
                    if (png == null) { SkinStatus(derr, false); return; }
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(png))
                        using (Bitmap src = new Bitmap(ms))
                        {
                            string ferr;
                            Bitmap b = SkinCanvas.FromImage(src, out ferr);
                            if (b == null) { SkinStatus(ferr, false); return; }
                            skinCanvas.Load(b);
                            b.Dispose();
                        }
                        SkinSetModel(Auth.LastSkinVariant == "SLIM" ? "SLIM" : "CLASSIC");
                        SkinStatus("Скин с аккаунта " + Auth.Name + " загружен в редактор.", true);
                    }
                    catch (Exception ex) { SkinStatus("Скин не открылся: " + ex.Message, false); }
                });
            });
            t.IsBackground = true;
            t.Start();
        });
    }

    // Достать свой офлайн-скин из Luxe и открыть его в редакторе
    void SkinFromLuxe()
    {
        string who = nick;
        SkinStatus("Запрашиваю скин из Luxe...");
        Thread t = new Thread((ThreadStart)delegate
        {
            string url = "", model = "classic", err = "";
            bool ok = false;
            try
            {
                if (!Luxe.LoginFor(who)) err = Luxe.LastError;
                else if (Luxe.MySkin(out url, out model)) ok = true;
            }
            catch (Exception ex) { err = ex.Message; }

            if (!ok)
            {
                Ui_(delegate
                {
                    SkinStatus(err == "" ? "В Luxe пока нет скина для " + who + "." : "Не получилось: " + err, false);
                });
                return;
            }

            string full = url.StartsWith("http") ? url : Luxe.Host() + url;
            string derr;
            byte[] png = Auth.DownloadSkin(full, out derr);
            Ui_(delegate
            {
                if (png == null) { SkinStatus(derr, false); return; }
                try
                {
                    using (MemoryStream ms = new MemoryStream(png))
                    using (Bitmap src = new Bitmap(ms))
                    {
                        string ferr;
                        Bitmap b = SkinCanvas.FromImage(src, out ferr);
                        if (b == null) { SkinStatus(ferr, false); return; }
                        skinCanvas.Load(b);
                        b.Dispose();
                    }
                    SkinSetModel(model == "slim" ? "SLIM" : "CLASSIC");
                    SkinStatus("Скин из Luxe для " + who + " загружен в редактор.", true);
                }
                catch (Exception ex) { SkinStatus("Скин не открылся: " + ex.Message, false); }
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    void SkinReset()
    {
        // Офлайн-игроку сбрасываем не аккаунт Mojang, а запись в Luxe
        if (!Auth.LoggedIn)
        {
            if (!DbgSkinClear &&
                MessageBox.Show(this, "Убрать твой скин из Luxe? В игре снова будет стандартный.",
                    "Luxe Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string who = nick;
            SkinStatus("Убираю скин из Luxe...");
            Thread t = new Thread((ThreadStart)delegate
            {
                string err = "";
                bool ok = false;
                try
                {
                    if (!Luxe.LoginFor(who)) err = Luxe.LastError;
                    else if (!Luxe.ClearSkin()) err = Luxe.LastError;
                    else ok = true;
                }
                catch (Exception ex) { err = ex.Message; }
                Ui_(delegate
                {
                    if (ok) SkinStatus("Скин убран — в игре снова стандартный.", true);
                    else SkinStatus("Не получилось: " + (err == "" ? "сервис не отвечает" : err), false);
                });
            });
            t.IsBackground = true;
            t.Start();
            return;
        }

        if (MessageBox.Show(this, "Вернуть стандартный скин (Стив/Алекс) на аккаунте?",
                "Luxe Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        SkinStatus("Возвращаю стандартный скин...");
        Thread t2 = new Thread((ThreadStart)delegate
        {
            string err;
            bool ok = Auth.ResetSkin(out err);
            Ui_(delegate
            {
                if (ok) SkinStatus("Аккаунт снова со стандартным скином.", true);
                else SkinStatus("Не получилось: " + err, false);
            });
        });
        t2.IsBackground = true;
        t2.Start();
    }
}

// ================================================================
//  КАНВА СКИНА
//
//  64x64 пикселей, увеличенных в Zoom раз. Мышь переводится в
//  пиксели канвы делением на Zoom — рисование всегда по одному
//  пикселю скина, как бы ни был увеличен холст.
// ================================================================

class SkinCanvas : Control
{
    public const int SkinSize = 64;

    public Bitmap Bmp;
    public int Zoom = 6;
    public Color Current = Color.White;
    public int Tool = 0;                 // 0 карандаш, 1 ластик, 2 заливка, 3 пипетка
    public Action<Color> Picked;
    public Action Changed;

    // Подсветка пикселя, который сейчас под курсором в 3D-виде
    public Point Highlight = new Point(-1, -1);
    // Счётчик изменений текстуры: 3D-вид по нему понимает, что пора перерисоваться
    public int TexVersion = 0;

    List<Bitmap> undo = new List<Bitmap>();
    List<Bitmap> redo = new List<Bitmap>();
    bool down = false;
    int lastX = -1, lastY = -1;

    public SkinCanvas()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Bg;
        Bmp = new Bitmap(SkinSize, SkinSize, PixelFormat.Format32bppArgb);
        Size = new Size(SkinSize * Zoom, SkinSize * Zoom);
    }

    public void Clear()
    {
        PushUndo();
        using (Graphics g = Graphics.FromImage(Bmp))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
        }
        TexVersion++;
        Invalidate();
        if (Changed != null) Changed();
    }

    public void Load(Bitmap src)
    {
        PushUndo();
        using (Graphics g = Graphics.FromImage(Bmp))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, SkinSize, SkinSize));
        }
        TexVersion++;
        Invalidate();
        if (Changed != null) Changed();
    }

    public Bitmap Export()
    {
        return Bmp.Clone(new Rectangle(0, 0, SkinSize, SkinSize), PixelFormat.Format32bppArgb);
    }

    // ---- отмена ----

    public void PushUndo()
    {
        undo.Add(Export());
        if (undo.Count > 40) undo.RemoveAt(0);
        redo.Clear();
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Add(Export());
        Bmp.Dispose();
        Bmp = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        TexVersion++;
        Invalidate();
        if (Changed != null) Changed();
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Add(Export());
        Bmp.Dispose();
        Bmp = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);
        TexVersion++;
        Invalidate();
        if (Changed != null) Changed();
    }

    // ---- мышь ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int x = e.X / Zoom, y = e.Y / Zoom;
        if (x < 0 || y < 0 || x >= SkinSize || y >= SkinSize) return;

        if (Tool == 3)
        {
            Color c = Bmp.GetPixel(x, y);
            if (c.A == 0) c = Color.FromArgb(255, c);
            if (Picked != null) Picked(c);
            return;
        }
        PushUndo();
        down = true;
        lastX = x; lastY = y;
        if (Tool == 2) Flood(x, y);
        else Dot(x, y);
        Invalidate();
        if (Changed != null) Changed();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!down) return;
        int x = e.X / Zoom, y = e.Y / Zoom;
        if (x < 0 || y < 0 || x >= SkinSize || y >= SkinSize) return;
        if (x == lastX && y == lastY) return;
        // между двумя кадрами мышь может проскочить пиксель — дорисовываем линию
        Line(lastX, lastY, x, y);
        lastX = x; lastY = y;
        Invalidate();
        if (Changed != null) Changed();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        down = false;
        lastX = lastY = -1;
    }

    // Рисование из 3D-вида: те же операции, что и мышью по канве.
    // Dot/Line/Flood публичные, чтобы 3D-вид писал в ту же текстуру.
    public void Dot(int x, int y)
    {
        Color c = (Tool == 1) ? Color.Transparent : Color.FromArgb(255, Current);
        Bmp.SetPixel(x, y, c);
        TexVersion++;
    }

    public void Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            Dot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    // Заливка: ищем связную область того же цвета. Через стек, а не
    // рекурсию — на 64x64 рекурсия может уронить поток переполнением.
    public void Flood(int x, int y)
    {
        Color from = Bmp.GetPixel(x, y);
        Color to = Color.FromArgb(255, Current);
        if (Same(from, to)) return;

        Stack<Point> st = new Stack<Point>();
        st.Push(new Point(x, y));
        while (st.Count > 0)
        {
            Point p = st.Pop();
            if (p.X < 0 || p.Y < 0 || p.X >= SkinSize || p.Y >= SkinSize) continue;
            if (!Same(Bmp.GetPixel(p.X, p.Y), from)) continue;
            Bmp.SetPixel(p.X, p.Y, to);
            st.Push(new Point(p.X + 1, p.Y));
            st.Push(new Point(p.X - 1, p.Y));
            st.Push(new Point(p.X, p.Y + 1));
            st.Push(new Point(p.X, p.Y - 1));
        }
    }

    static bool Same(Color a, Color b)
    {
        return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
    }

    // Завершить изменение, пришедшее из 3D-вида (канва + оба вида)
    public void Commit()
    {
        Invalidate();
        if (Changed != null) Changed();
    }

    // ---- отрисовка ----

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Ui.Bg);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;

        // шахматка под прозрачностью, иначе пустой скин сливается с фоном
        using (SolidBrush dark = new SolidBrush(Color.FromArgb(255, 26, 30, 38)))
        using (SolidBrush lite = new SolidBrush(Color.FromArgb(255, 34, 39, 49)))
        {
            int cell = Zoom * 4;
            for (int y = 0; y * cell < Height; y++)
                for (int x = 0; x * cell < Width; x++)
                    g.FillRectangle(((x + y) % 2 == 0) ? dark : lite,
                        new Rectangle(x * cell, y * cell, cell, cell));
        }

        g.DrawImage(Bmp, new Rectangle(0, 0, SkinSize * Zoom, SkinSize * Zoom),
            0, 0, SkinSize, SkinSize, GraphicsUnit.Pixel);

        // сетка по пикселям и границы частей скина (правая половина — слои одежды)
        if (Zoom >= 4)
        {
            using (Pen thin = new Pen(Color.FromArgb(38, 255, 255, 255)))
            {
                for (int i = 1; i < SkinSize; i++)
                {
                    if (i % 8 == 0) continue;
                    g.DrawLine(thin, i * Zoom, 0, i * Zoom, SkinSize * Zoom);
                    g.DrawLine(thin, 0, i * Zoom, SkinSize * Zoom, i * Zoom);
                }
            }
            using (Pen mid = new Pen(Color.FromArgb(70, 255, 255, 255)))
            {
                for (int i = 0; i <= SkinSize; i += 8)
                {
                    g.DrawLine(mid, i * Zoom, 0, i * Zoom, SkinSize * Zoom);
                    g.DrawLine(mid, 0, i * Zoom, SkinSize * Zoom, i * Zoom);
                }
            }
            using (Pen edge = new Pen(Color.FromArgb(120, 96, 140, 255)))
            {
                g.DrawLine(edge, 32 * Zoom, 0, 32 * Zoom, SkinSize * Zoom);
                g.DrawLine(edge, 0, 32 * Zoom, SkinSize * Zoom, 32 * Zoom);
            }
        }

        // пиксель, который сейчас под курсором в 3D-виде — чтобы было
        // понятно, куда именно попадёт клик на развёртке
        if (Highlight.X >= 0 && Highlight.Y >= 0 && Highlight.X < SkinSize && Highlight.Y < SkinSize)
        {
            int hx = Highlight.X * Zoom, hy = Highlight.Y * Zoom;
            using (Pen p1 = new Pen(Color.FromArgb(190, 0, 0, 0), 1f))
                g.DrawRectangle(p1, hx - 1, hy - 1, Zoom + 1, Zoom + 1);
            using (Pen p2 = new Pen(Color.FromArgb(240, 255, 255, 255), 2f))
                g.DrawRectangle(p2, hx + 1, hy + 1, Zoom - 3, Zoom - 3);
        }
    }

    // ============================================================
    //  ФАЙЛЫ
    // ============================================================

    public static Bitmap FromFile(string path, out string err)
    {
        err = "";
        try
        {
            using (Bitmap src = new Bitmap(path))
                return FromImage(src, out err);
        }
        catch (Exception ex) { err = "Файл не читается: " + ex.Message; }
        return null;
    }

    // То же, но из уже загруженной картинки (например, скин с аккаунта)
    public static Bitmap FromImage(Bitmap src, out string err)
    {
        err = "";
        try
        {
            if (src.Width == 64 && src.Height == 64) return Copy(src);
            if (src.Width == 64 && src.Height == 32) return Modernize(src);
            err = "Нужен PNG 64x64 или старый 64x32, а тут " + src.Width + "x" + src.Height;
        }
        catch (Exception ex) { err = "Картинка не читается: " + ex.Message; }
        return null;
    }

    static Bitmap Copy(Bitmap src)
    {
        Bitmap b = new Bitmap(SkinSize, SkinSize, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(src, new Rectangle(0, 0, SkinSize, SkinSize));
        }
        return b;
    }

    // Старый скин 64x32 -> современный 64x64.
    // Ноги и руки в старом формате лежат там, где сейчас пусто, поэтому
    // их надо перенести: блок конечности 16x16 состоит из площадки 4x4
    // сверху, площадки 4x4 снизу и четырёх боковых полос 4x12.
    static Bitmap Modernize(Bitmap src)
    {
        Bitmap b = new Bitmap(SkinSize, SkinSize, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            // всё, что выше пояса, в старом формате стоит на своих местах
            g.DrawImage(src, new Rectangle(0, 0, 64, 32), 0, 0, 64, 32, GraphicsUnit.Pixel);
            Limb(g, src, 0, 16, 16, 48);     // правая нога
            Limb(g, src, 40, 16, 40, 32);    // правая рука
        }
        Mirror(b, 16, 48, 0, 48, 16, 16);    // левая нога — зеркало правой
        Mirror(b, 40, 32, 32, 48, 16, 16);   // левая рука — зеркало правой
        return b;
    }

    static void Limb(Graphics g, Bitmap src, int ox, int oy, int nx, int ny)
    {
        g.DrawImage(src, new Rectangle(nx + 4, ny, 4, 4), ox + 4, oy, 4, 4, GraphicsUnit.Pixel);
        g.DrawImage(src, new Rectangle(nx + 8, ny, 4, 4), ox + 8, oy, 4, 4, GraphicsUnit.Pixel);
        for (int i = 0; i < 4; i++)
            g.DrawImage(src, new Rectangle(nx + i * 4, ny + 4, 4, 12), ox + i * 4, oy + 4, 4, 12, GraphicsUnit.Pixel);
    }

    // Читаем из источника и пишем в приёмник: области не пересекаются,
    // поэтому портить ещё не прочитанное не можем
    static void Mirror(Bitmap b, int sx, int sy, int dx, int dy, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                b.SetPixel(dx + (w - 1 - x), dy + y, b.GetPixel(sx + x, sy + y));
    }
}
