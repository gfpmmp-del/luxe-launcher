using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// ================================================================
//  ПОДАРОЧНЫЙ БИЛД ДЛЯ АРТЁМА
//
//  Собирается отдельно: ключом /define:ARTYOM (см. build.ps1 -Gift).
//  В обычном билде весь этот файл компилируется, но Cfg.GiftBuild = false
//  и ничего из него не показывается - так код не протухает и проверяется
//  обеими сборками.
//
//  Экраны: фото -> поздравление -> мини-опрос -> скример -> задание.
//  Важно: экраны переключаются ТОЛЬКО порядком отрисовки (BringToFront).
//  Visible=false в этом лаунчере навсегда ломает отрисовку потомков.
// ================================================================

static class Artem
{
    public const string Text = "Привет, Артём!  Рад, что ты открыл этот лаунчер.\n" +
        "Собрал его специально для тебя: тут всё работает как надо - сборки,\n" +
        "моды и серверы в одном месте. Пользуйся на здоровье,\n" +
        "и пусть в твоей сборке всегда всё запускается с первого раза.";

    public const string Quest =
        "Скачай сборку Fabric 1.21.8 и поставь в неё два мода:\n\n" +
        "1.  Fabric API  0.136.1+1.21.8\n" +
        "2.  WorldEdit  7.3.16\n\n" +
        "Оба мода берутся прямо из раздела «Моды» в этом лаунчере.\n" +
        "Проверь, что WorldEdit работает: //wand и выдели блок //set stone.";

    public const string FabricApiUrl =
        "https://cdn.modrinth.com/data/P7dR8mSH/versions/g58ofrov/fabric-api-0.136.1%2B1.21.8.jar";
    public const string WorldEditUrl =
        "https://cdn.modrinth.com/data/1u6JkXh5/versions/R846T4GX/worldedit-mod-7.3.16.jar";

    // ---------- три звука подарка ----------
    //
    //  1 - «Послушать поздравления»
    //  2 - после «Будешь пользоваться?»
    //  3 - когда запускается версия
    //
    // Файл можно просто положить рядом с exe в папку assets:
    //  assets\gift1.wav (или .mp3), gift2, gift3 — тогда пересобирать
    //  лаунчер не нужно. Если файла нет вовсе — просто тишина.
    // Свой звук скримера: assets\screamer.wav (или .mp3).

    static readonly string[][] SoundAliases = new string[][]
    {
        new string[] { "gift1", "greeting" },
        new string[] { "gift2", "thanks" },
        new string[] { "gift3", "launch" }
    };
    static readonly string[] SoundExt = new string[] { ".wav", ".mp3" };

    // путь к звуку: сначала файл рядом с exe, потом вшитый ресурс
    static string SoundPath(int n)
    {
        string[] names = SoundAliases[n - 1];
        try
        {
            string[] dirs = new string[] { Path.Combine(Cfg.BaseDir, "assets"), Cfg.BaseDir };
            for (int d = 0; d < dirs.Length; d++)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    for (int e = 0; e < SoundExt.Length; e++)
                    {
                        string p = Path.Combine(dirs[d], names[i] + SoundExt[e]);
                        if (File.Exists(p)) return p;
                    }
                }
            }
        }
        catch { }

        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                for (int e = 0; e < SoundExt.Length; e++)
                {
                    string rn = names[i] + SoundExt[e];
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(rn))
                    {
                        if (s == null) continue;
                        try { Directory.CreateDirectory(Cfg.DataDir); } catch { }
                        string tmp = Path.Combine(Cfg.DataDir, rn);
                        using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write)) s.CopyTo(fs);
                        return tmp;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    // mp3 играем через MCI: SoundPlayer умеет только wav
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string cmd, StringBuilder ret, int retLen, IntPtr hwnd);

    static SoundPlayer wavNow;
    static string mciNow = "";

    // два звука разом не нужны: перед новым глушим предыдущий
    static void StopSounds()
    {
        try { if (wavNow != null) { wavNow.Stop(); wavNow = null; } } catch { }
        try
        {
            if (mciNow.Length > 0)
            {
                mciSendString("stop " + mciNow, null, 0, IntPtr.Zero);
                mciSendString("close " + mciNow, null, 0, IntPtr.Zero);
                mciNow = "";
            }
        }
        catch { }
    }

    // wav играем SoundPlayer-ом, всё остальное - через MCI
    static void PlayFile(string p, string alias)
    {
        // на Linux нет ни MCI, ни рабочего SoundPlayer — просто молчим
        if (!Os.IsWin) return;
        if (p == null || !File.Exists(p)) return;
        if (p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            SoundPlayer sp = new SoundPlayer(p);
            sp.Load();
            wavNow = sp;
            sp.Play();
            return;
        }

        mciSendString("close " + alias, null, 0, IntPtr.Zero);
        int rc = mciSendString("open \"" + p + "\" type mpegvideo alias " + alias,
            null, 0, IntPtr.Zero);
        if (rc != 0) rc = mciSendString("open \"" + p + "\" alias " + alias,
            null, 0, IntPtr.Zero);
        if (rc != 0) return;
        mciNow = alias;
        mciSendString("play " + alias, null, 0, IntPtr.Zero);
    }

    public static void PlaySound(int n)
    {
        try
        {
            if (n < 1 || n > SoundAliases.Length) return;
            StopSounds();
            PlayFile(SoundPath(n), "luxegift" + n);
        }
        catch { }
    }

    public static void PlayGreeting() { PlaySound(1); }

    // ---------- синтез звука скримера ----------
    // Своего файла не требует. Громкость ограничена ПИКОМ амплитуды,
    // чтобы не оглушить: это ~-7 дБ относительно полной громкости.

    public const double ScreamPeak = 0.42;
    const int SampleRate = 22050;

    static byte[] MakeWav(int ms, double peak)
    {
        int n = SampleRate * ms / 1000;
        short[] pcm = new short[n];
        Random rnd = new Random(20261017);
        double phase = 0, phase2 = 0;
        double dur = ms / 1000.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double k = t / dur;
            // громкая в первой трети, потом спад
            double env = k < 0.08 ? k / 0.08 : (1.0 - (k - 0.08) / 0.92) * 0.85 + 0.15;
            if (env > 1.0) env = 1.0;
            // нисходящий вой + гармоники + шум
            double f = 1250.0 - 780.0 * k;
            phase += 2.0 * Math.PI * f / SampleRate;
            phase2 += 2.0 * Math.PI * f * 2.51 / SampleRate;
            double saw = (phase / Math.PI) % 2.0 - 1.0;
            double s = Math.Sin(phase) * 0.42 + Math.Sin(phase2) * 0.26 + saw * 0.14;
            s += (rnd.NextDouble() * 2.0 - 1.0) * 0.30;
            double v = s * env * peak;
            if (v > 1.0) v = 1.0; if (v < -1.0) v = -1.0;
            pcm[i] = (short)(v * 32767.0);
        }
        using (MemoryStream ms2 = new MemoryStream())
        {
            using (BinaryWriter w = new BinaryWriter(ms2))
            {
                int dataLen = n * 2;
                w.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataLen);
                w.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(SampleRate);
                w.Write(SampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataLen);
                for (int i = 0; i < n; i++) w.Write(pcm[i]);
            }
            return ms2.ToArray();
        }
    }

    // свой звук скримера: assets\screamer.wav (или .mp3, или gift4.*)
    // рядом с exe - играется вместо синтезированного
    static readonly string[] ScreamNames = new string[] { "screamer", "gift4" };

    static string ScreamerPath()
    {
        try
        {
            string[] dirs = new string[] { Path.Combine(Cfg.BaseDir, "assets"), Cfg.BaseDir };
            for (int d = 0; d < dirs.Length; d++)
            {
                for (int i = 0; i < ScreamNames.Length; i++)
                {
                    for (int e = 0; e < SoundExt.Length; e++)
                    {
                        string p = Path.Combine(dirs[d], ScreamNames[i] + SoundExt[e]);
                        if (File.Exists(p)) return p;
                    }
                }
            }
        }
        catch { }

        try
        {
            for (int i = 0; i < ScreamNames.Length; i++)
            {
                for (int e = 0; e < SoundExt.Length; e++)
                {
                    string rn = ScreamNames[i] + SoundExt[e];
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(rn))
                    {
                        if (s == null) continue;
                        try { Directory.CreateDirectory(Cfg.DataDir); } catch { }
                        string tmp = Path.Combine(Cfg.DataDir, rn);
                        using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write)) s.CopyTo(fs);
                        return tmp;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static void PlayScreamer()
    {
        if (!Os.IsWin) return;
        try
        {
            StopSounds();                            // поздравление обрываем
            string own = ScreamerPath();
            if (own != null) { PlayFile(own, "luxescream"); return; }
            string p = Path.Combine(Path.GetTempPath(), "luxe_screamer.wav");
            File.WriteAllBytes(p, MakeWav(1100, ScreamPeak));
            SoundPlayer sp = new SoundPlayer(p);
            sp.Load();
            sp.Play();
        }
        catch { }
    }

    // ---------- фото ----------

    static Image photo;

    public static Image Photo()
    {
        if (photo != null) return photo;
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("artem.jpg"))
            {
                if (s == null) return null;
                photo = new Bitmap(s);
            }
        }
        catch { photo = null; }
        return photo;
    }
}

// Подпись с ПРОЗРАЧНЫМ фоном. Обычный Label без этого стиля рисует
// BackColor=Transparent как непрозрачный цвет родителя - на фото это
// даёт заметные тёмные прямоугольники.
class ArtemLabel : Label
{
    public ArtemLabel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }
}

// ================================================================
//  ФОН ПОДАРОЧНОГО ЭКРАНА
//  Фото на весь экран + затемнение, чтобы текст и кнопки читались.
// ================================================================

class ArtemBackdrop : Panel
{
    // Текст рисуем ПРЯМО здесь, а не отдельными Label: прозрачный фон
    // у метки просит рисовать родителя, а родитель - другая панель,
    // и метка выходит непрозрачным тёмным прямоугольником.
    public string Title = "";
    public string Body = "";
    public Color TitleColor = Color.White;
    // активная бабочка; рисуется тут же, поверх фото
    public ArtemFlyer Fly = null;
    // прогресс установки, -1 = не показывать
    public int Progress = -1;

    public ArtemBackdrop()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

// Рисуем бабочку прямо поверх фото - без отдельного окна,
    // иначе WinForms заливает прозрачный контрол серым фоном.
    public static void DrawFlyer(Graphics g, ArtemFlyer f)
    {
        const int W = 84, H = 72;
        float cx = (float)f.X + W / 2f, cy = (float)f.Y + H / 2f;
        float w = W, h = H;

        // взмах: крылья то расправлены, то сложены
        double open = 0.34 + 0.66 * Math.Abs(Math.Sin(f.Flap));

        GraphicsState stSave = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform((float)(f.Angle * 180.0 / Math.PI + 90.0));
        g.TranslateTransform(-cx, -cy);

        // мягкая тень под бабочкой, чтобы она не «плавала» в темноте
        using (GraphicsPath gp = new GraphicsPath())
        {
            gp.AddEllipse(cx - w * 0.46f, cy - h * 0.42f, w * 0.92f, h * 0.84f);
            using (PathGradientBrush sh = new PathGradientBrush(gp))
            {
                sh.CenterColor = Color.FromArgb(85, 0, 0, 0);
                sh.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                g.FillEllipse(sh, cx - w * 0.46f, cy - h * 0.42f, w * 0.92f, h * 0.84f);
            }
        }

        float bw = w * 0.10f, bh = h * 0.62f;
        float ww = (w * 0.42f) * (float)open + w * 0.05f;
        float lw = (w * 0.28f) * (float)open + w * 0.04f;

        // ---- верхние крылья ----
        GraphicsPath up = new GraphicsPath();
        up.AddBezier(cx, cy - bh * 0.35f,
                     cx - ww * 0.55f, cy - bh * 0.95f,
                     cx - ww, cy - bh * 0.35f,
                     cx - ww * 0.12f, cy + bh * 0.06f);
        up.CloseFigure();
        using (LinearGradientBrush b1 = new LinearGradientBrush(
            new RectangleF(cx - ww, cy - bh, ww * 1.3f, bh * 1.3f),
            Color.FromArgb(255, 158, 96, 238), Color.FromArgb(255, 70, 118, 202), 60f))
        using (Pen e1 = new Pen(Color.FromArgb(255, 38, 28, 66), 1.2f))
        {
            g.FillPath(b1, up);
            g.DrawPath(e1, up);
        }
        up.Dispose();

        // ---- нижние крылья ----
        GraphicsPath lo = new GraphicsPath();
        lo.AddBezier(cx, cy + bh * 0.02f,
                     cx - lw * 0.60f, cy + bh * 0.20f,
                     cx - lw * 0.78f, cy + bh * 0.62f,
                     cx - lw * 0.10f, cy + bh * 0.52f);
        lo.CloseFigure();
        using (LinearGradientBrush b2 = new LinearGradientBrush(
            new RectangleF(cx - lw, cy, lw * 1.3f, bh * 1.2f),
            Color.FromArgb(255, 100, 146, 238), Color.FromArgb(255, 44, 66, 148), 60f))
        using (Pen e2 = new Pen(Color.FromArgb(255, 28, 24, 58), 1.1f))
        {
            g.FillPath(b2, lo);
            g.DrawPath(e2, lo);
        }
        lo.Dispose();

        // ---- тело ----
        using (LinearGradientBrush bg = new LinearGradientBrush(
            new RectangleF(cx - bw, cy - bh * 0.4f, bw * 2f, bh),
            Color.FromArgb(74, 62, 80), Color.FromArgb(30, 26, 38), 90f))
            g.FillEllipse(bg, cx - bw / 2f, cy - bh * 0.40f, bw, bh);

        using (SolidBrush head = new SolidBrush(Color.FromArgb(54, 46, 64)))
            g.FillEllipse(head, cx - bw * 0.62f, cy - bh * 0.52f, bw * 1.24f, bw * 1.24f);

        using (Pen ant = new Pen(Color.FromArgb(54, 46, 64), 1.3f))
        {
            g.DrawLine(ant, cx - bw * 0.3f, cy - bh * 0.52f, cx - bw * 1.3f, cy - bh * 0.78f);
            g.DrawLine(ant, cx + bw * 0.3f, cy - bh * 0.52f, cx + bw * 1.3f, cy - bh * 0.78f);
        }

        using (SolidBrush spot = new SolidBrush(Color.FromArgb(205, 255, 236, 124)))
        {
            g.FillEllipse(spot, cx - ww * 0.55f, cy - bh * 0.52f, ww * 0.20f, bh * 0.14f);
            g.FillEllipse(spot, cx - lw * 0.42f, cy + bh * 0.26f, lw * 0.18f, bh * 0.11f);
        }

        g.Restore(stSave);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Rectangle r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 1. фото во весь экран (вписываем по принципу cover - обрезая лишнее)
        Image img = Artem.Photo();
        if (img != null)
        {
            try
            {
                float sc = Math.Max((float)r.Width / img.Width, (float)r.Height / img.Height);
                int dw = (int)(img.Width * sc) + 1;
                int dh = (int)(img.Height * sc) + 1;
                int dx = (r.Width - dw) / 2;
                int dy = (r.Height - dh) / 2;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new Rectangle(dx, dy, dw, dh));
            }
            catch { }
        }
        else
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(26, 29, 36)))
                g.FillRectangle(b, r);
        }

        // 2. затемнение: по краям сильнее, в центре слабее -
        //    текст в центре остаётся читаемым, но фото видно везде
        using (LinearGradientBrush scrim = new LinearGradientBrush(
            new Rectangle(0, 0, r.Width, r.Height),
            Color.FromArgb(120, 8, 9, 12), Color.FromArgb(90, 8, 9, 12), 90f))
            g.FillRectangle(scrim, r);
        using (GraphicsPath vp = Ui.RoundRect(r, 0))
        using (PathGradientBrush vig = new PathGradientBrush(vp))
        {
            vig.CenterColor = Color.FromArgb(0, 0, 0, 0);
            vig.SurroundColors = new Color[] { Color.FromArgb(175, 0, 0, 0) };
            g.FillRectangle(vig, r);
        }

        // 3. фирменный оттенок, чтобы фон был частью лаунчера
        using (LinearGradientBrush tint = new LinearGradientBrush(
            new Rectangle(0, r.Height - r.Height / 2, r.Width, r.Height / 2),
            Color.FromArgb(0, 26, 32, 48), Color.FromArgb(205, 20, 22, 27), 90f))
            g.FillRectangle(tint, 0, r.Height - r.Height / 2, r.Width, r.Height / 2);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        // ---- заголовок ----
        if (!string.IsNullOrEmpty(Title))
        {
            Font tf = Ui.Font(Math.Max(18f, Math.Min(30f, r.Height / 22f)), FontStyle.Bold);
            SizeF ts = g.MeasureString(Title, tf);
            float tx = r.Width / 2f - ts.Width / 2f;
            float ty = r.Height * 0.13f;
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.DrawString(Title, tf, sh, tx + 3, ty + 3);
            using (LinearGradientBrush tg = new LinearGradientBrush(
                new RectangleF(tx, ty, ts.Width + 4, ts.Height + 4),
                TitleColor, Color.FromArgb(255, Color.FromArgb(255, Math.Max(0, TitleColor.R - 60),
                    Math.Max(0, TitleColor.G - 60), Math.Max(0, TitleColor.B - 60))), 90f))
                g.DrawString(Title, tf, tg, tx, ty);
        }

        // ---- текст ----
        if (!string.IsNullOrEmpty(Body))
        {
            Font bf = Ui.Font(11f);
            float maxW = r.Width - 200;
            if (maxW < 240) maxW = r.Width - 40;
            RectangleF br = new RectangleF(20, r.Height * 0.28f, maxW, r.Height * 0.45f);
            using (StringFormat sf = new StringFormat(StringFormatFlags.LineLimit))
            {
                sf.Alignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(232, 236, 242)))
                    g.DrawString(Body, bf, bb, br, sf);
            }
        }

        // ---- бабочка поверх всего ----
        if (Fly != null && !Fly.GoneFar())
            DrawFlyer(g, Fly);

        // ---- полоса прогресса установки ----
        if (Progress >= 0)
        {
            int bw2 = Math.Min(520, r.Width - 120);
            int bx = (r.Width - bw2) / 2;
            int by = r.Height - (int)(r.Height * 0.28f);
            if (bx < 20) { bw2 = Math.Max(60, r.Width - 40); bx = 20; }
            Rectangle track = new Rectangle(bx, by, bw2, 10);
            using (GraphicsPath tp = Ui.RoundRect(track, 5))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(150, 10, 12, 16)))
                g.FillPath(tb, tp);
            int fill = (int)(bw2 * Math.Max(0, Math.Min(100, Progress)) / 100.0);
            if (fill > 4)
            {
                Rectangle fp = new Rectangle(bx, by, fill, 10);
                using (GraphicsPath fpp = Ui.RoundRect(fp, 5))
                using (LinearGradientBrush fg = new LinearGradientBrush(
                    fp, Color.FromArgb(255, 96, 150, 250), Color.FromArgb(255, 58, 104, 210), 90f))
                    g.FillPath(fg, fpp);
            }
        }
    }
}

// ================================================================
//  КНОПКА, КОТОРАЯ ПРЕВРАЩАЕТСЯ В САМОЛЁТ
// ================================================================

// ================================================================
//  БАБОЧКА
//  Это НЕ отдельный контрол: если сделать окно поверх фото, WinForms
//  всё равно заливает его своим фоном - серый прямоугольник поверх
//  фотографии. Поэтому бабочка просто живёт координатами, а рисует
//  её панель с фото (ArtemBackdrop.DrawFlyer) в том же слое.
// ================================================================

class ArtemFlyer
{
    // Дробные координаты и курс углом, а не целые пиксели со сменой
    // знака скорости: раньше бабочка двигалась ступеньками и дёргалась
    // при каждой смене курса.
    public double X, Y;
    public double Angle = -1.1;   // курс, радианы
    public double Speed = 11.5;   // пикселей за кадр
    public double Flap = 0.7;     // фаза взмаха крыльев
    public double T;              // «время» для плавных синусоид
    public int Age;
    public bool Gone;
    public const int MaxFly = 175;   // ~3 секунды, потом улетает
    public const int W = 84, H = 72;

    public bool GoneFar() { return Age > MaxFly + 5000; }

    static readonly Random rnd = new Random(4242);

    public void Update(int bw, int bh)
    {
        Age++;
        T += 0.037;      // примерно 60 кадров в секунду
        Flap += 0.40;

        if (Age > MaxFly && !Gone)
        {
            Gone = true;
            Speed = 21.0;
            // направиться к ближайшему краю и улететь
            Angle = (X < bw / 2 ? Math.PI : 0) + (rnd.NextDouble() - 0.5) * 0.7;
        }

        if (Gone)
        {
            X += Math.Cos(Angle) * Speed;
            Y += Math.Sin(Angle) * Speed;
            Speed += 0.10;                 // плавный разгон
            if (X < -90 || X > bw + 90) Age = MaxFly + 10000;   // улетел совсем
            return;
        }

        // ПЛАВНОЕ блуждание: курс и скорость «дышат» синусоидами,
        // поэтому никаких рывков - траектория получается круговая.
        Angle += Math.Sin(T * 1.70) * 0.052 + Math.Sin(T * 0.61 + 1.1) * 0.034;
        double want = 11.5 + Math.Sin(T * 0.87) * 3.4;
        Speed += (want - Speed) * 0.035;   // инерция, не скачок

        X += Math.Cos(Angle) * Speed;
        Y += Math.Sin(Angle) * Speed;

        // отскок от краёв отражением курса - без смены знака скорости
        if (X < 4) { X = 4; Angle = Math.PI - Angle; }
        else if (X + W > bw - 4) { X = bw - 4 - W; Angle = Math.PI - Angle; }
        if (Y < 4) { Y = 4; Angle = -Angle; }
        else if (Y + H > bh - 4) { Y = bh - 4 - H; Angle = -Angle; }
    }
}
class ArtemScreamer : Panel
{
    Action tick = null;
    int frame = 0;
    Random rnd = new Random(13);
    public Action onDone;

    public ArtemScreamer()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
    }

    public void Start()
    {
        Artem.PlayScreamer();
        Action t = null;
        t = delegate
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) { Anim.Unsub(t); return; }
                frame++;
                // тряска окна
                int amp = frame < 18 ? 12 : 5;
                Location = new Point(rnd.Next(-amp, amp + 1), rnd.Next(-amp, amp + 1));
                Invalidate();
                if (frame > 78) { Anim.Unsub(t); Location = Point.Empty; if (onDone != null) onDone(); }
            }
            catch { }
        };
        tick = t;
        Anim.Sub(t);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Rectangle r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        // фон - красная вспышка
        int pulse = frame % 12 < 6 ? 58 : 16;
        using (SolidBrush bg = new SolidBrush(Color.FromArgb(pulse, 70, 0, 0)))
            g.FillRectangle(bg, r);

        int cx = r.Width / 2;
        int cy = (int)(r.Height * 0.42);
        int s = Math.Min(r.Width, r.Height);

        // ---- лицо: вытянутое, бледное, с тяжёлыми тенями ----
        int fw = (int)(s * 0.40), fh = (int)(s * 0.62);
        Rectangle faceR = new Rectangle(cx - fw / 2, cy - fh / 2, fw, fh);
        using (GraphicsPath fp = Ui.RoundRect(faceR, fw / 2))
        {
            using (LinearGradientBrush fb = new LinearGradientBrush(
                faceR, Color.FromArgb(214, 205, 188), Color.FromArgb(126, 116, 102), 90f))
                g.FillPath(fb, fp);
            using (Pen edge = new Pen(Color.FromArgb(110, 0, 0, 0), 3f))
                g.DrawPath(edge, fp);
        }
        // провалы под глазами и челюстью
        using (SolidBrush shade = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
        {
            g.FillEllipse(shade, cx - fw / 2 + fw / 9, cy - fh / 6, fw / 3, fh / 9);
            g.FillEllipse(shade, cx + fw / 2 - fw / 9 - fw / 3, cy - fh / 6, fw / 3, fh / 9);
        }

        // ---- брови злые ----
        using (SolidBrush brow = new SolidBrush(Color.FromArgb(40, 26, 20)))
        {
            g.FillPolygon(brow, new PointF[] {
                new PointF(cx - fw * 0.40f, cy - fh * 0.22f),
                new PointF(cx - fw * 0.08f, cy - fh * 0.32f),
                new PointF(cx - fw * 0.07f, cy - fh * 0.26f),
                new PointF(cx - fw * 0.39f, cy - fh * 0.16f) });
            g.FillPolygon(brow, new PointF[] {
                new PointF(cx + fw * 0.40f, cy - fh * 0.22f),
                new PointF(cx + fw * 0.08f, cy - fh * 0.32f),
                new PointF(cx + fw * 0.07f, cy - fh * 0.26f),
                new PointF(cx + fw * 0.39f, cy - fh * 0.16f) });
        }

        // ---- глаза: большие чёрные провалы с красным свечением ----
        int ew = (int)(fw * 0.30), eh = (int)(fh * 0.15);
        int ey = cy - (int)(fh * 0.06);
        int e1x = cx - fw / 2 + (int)(fw * 0.08);
        int e2x = cx + fw / 2 - (int)(fw * 0.08) - ew;
        using (SolidBrush hole = new SolidBrush(Color.FromArgb(8, 6, 6)))
        {
            g.FillEllipse(hole, e1x, ey, ew, eh);
            g.FillEllipse(hole, e2x, ey, ew, eh);
        }
        using (SolidBrush iris = new SolidBrush(Color.FromArgb(245, 28, 0, 0)))
        {
            int ir = Math.Max(3, eh / 4);
            g.FillEllipse(iris, e1x + ew / 2 - ir, ey + eh / 2 - ir, ir * 2, ir * 2);
            g.FillEllipse(iris, e2x + ew / 2 - ir, ey + eh / 2 - ir, ir * 2, ir * 2);
        }
        // кровь из глаз
        using (Pen tear = new Pen(Color.FromArgb(215, 105, 8, 12), Math.Max(3f, s / 110f)))
        {
            g.DrawLine(tear, e1x + ew / 2, ey + eh, e1x + ew / 2 + 4, ey + eh + fh / 9);
            g.DrawLine(tear, e2x + ew / 2, ey + eh, e2x + ew / 2 - 4, ey + eh + fh / 9);
        }

        // ---- нос: две щели ----
        using (SolidBrush nose = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            g.FillEllipse(nose, cx - fw * 0.10f, cy + fh * 0.04f, fw * 0.06f, fh * 0.04f);
            g.FillEllipse(nose, cx + fw * 0.04f, cy + fh * 0.04f, fw * 0.06f, fh * 0.04f);
        }

        // ---- рот: разинутая пасть с клыками ----
        int mw = (int)(fw * 0.60), mh = (int)(fh * 0.26);
        int mx = cx - mw / 2, my = cy + (int)(fh * 0.15);
        using (SolidBrush mouth = new SolidBrush(Color.FromArgb(16, 4, 4)))
            g.FillEllipse(mouth, mx, my, mw, mh);
        using (SolidBrush gum = new SolidBrush(Color.FromArgb(95, 40, 40)))
            g.FillEllipse(gum, mx + 2, my + 2, mw - 4, mh - 4);
        using (SolidBrush mouth2 = new SolidBrush(Color.FromArgb(10, 3, 3)))
            g.FillEllipse(mouth2, mx + 5, my + 5, mw - 10, mh - 10);
        // зубы сверху и снизу, неровные
        using (SolidBrush tooth = new SolidBrush(Color.FromArgb(232, 226, 210)))
        {
            int tn = 6;
            for (int i = 0; i < tn; i++)
            {
                int tw = (mw - 14) / tn;
                int tx = mx + 7 + i * tw;
                int th = mh / 4 + rnd.Next(-2, 3);
                g.FillPolygon(tooth, new PointF[] {
                    new PointF(tx, my + 4),
                    new PointF(tx + tw - 2, my + 4),
                    new PointF(tx + tw / 2, my + 4 + th) });
                g.FillPolygon(tooth, new PointF[] {
                    new PointF(tx, my + mh - 4),
                    new PointF(tx + tw - 2, my + mh - 4),
                    new PointF(tx + tw / 2, my + mh - 4 - th) });
            }
        }
        // потёки крови из уголков рта
        using (Pen blood = new Pen(Color.FromArgb(200, 12, 6, 8), Math.Max(4f, s / 80f)))
        {
            g.DrawLine(blood, mx + 3, my + mh / 2, mx - 10, my + mh + fh / 8);
            g.DrawLine(blood, mx + mw - 3, my + mh / 2, mx + mw + 12, my + mh + fh / 8);
        }

        // ---- крованый текст: подгоняем под ширину окна ----
        // ширину считаем ПОСИМВОЛЬНО: MeasureString всей строки и
        // поштучный DrawString дают разный результат, из-за чего
        // текст вылезал за правый край
        string msg = "БОЛЬШЕ ТАК НЕ ШУТИ";
        float fsz = Math.Max(16f, s / 13f);
        Font f = null;
        float total = 0;
        while (true)
        {
            f = Ui.Font(fsz, FontStyle.Bold);
            total = 0;
            foreach (char c in msg) total += g.MeasureString(c.ToString(), f).Width;
            if (total <= r.Width - 90 || fsz <= 12f) break;
            fsz -= 2f;
        }
        SizeF sz = g.MeasureString("А", f);
        float x = cx - total / 2f;
        float y = cy + fh / 2 + Math.Max(12f, s / 20f);
        for (int i = 0; i < msg.Length; i++)
        {
            string ch = msg[i].ToString();
            SizeF cs = g.MeasureString(ch, f);
            int jx = rnd.Next(-2, 3), jy = rnd.Next(-3, 4);
            using (LinearGradientBrush lb = new LinearGradientBrush(
                new RectangleF(x, y, Math.Max(cs.Width, 1f), sz.Height),
                Color.FromArgb(255, 70, 6, 6), Color.FromArgb(255, 196, 16, 16), 90f))
                g.DrawString(ch, f, lb, x + jx, y + jy);
            // подтёк из-под буквы
            if (i % 3 == 0)
                using (Pen drip = new Pen(Color.FromArgb(190, 150, 10, 12), Math.Max(2f, fsz / 10f)))
                    g.DrawLine(drip, x + cs.Width / 2, y + sz.Height - 2,
                        x + cs.Width / 2 + rnd.Next(-3, 4), y + sz.Height + sz.Height * 0.18f);
            x += cs.Width;
        }

        // ---- кровавые брызги ----
        using (SolidBrush spl = new SolidBrush(Color.FromArgb(165, 145, 10, 14)))
        {
            for (int i = 0; i < 30; i++)
            {
                int px = rnd.Next(0, r.Width), py = rnd.Next(0, r.Height);
                int pr = rnd.Next(6, 40);
                g.FillEllipse(spl, px, py, pr, pr / 2);
            }
        }

        // ---- виньетка ----
        using (GraphicsPath vp = Ui.RoundRect(r, 0))
        using (PathGradientBrush vg = new PathGradientBrush(vp))
        {
            vg.CenterColor = Color.FromArgb(0, 0, 0, 0);
            vg.SurroundColors = new Color[] { Color.FromArgb(205, 0, 0, 0) };
            g.FillRectangle(vg, r);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && tick != null) { try { Anim.Unsub(tick); } catch { } }
        base.Dispose(disposing);
    }
}
