// ================================================================
//  ТЕКСТУРА СКИНА ИГРОКА
//
//  Один источник правды для главного экрана и аватара в профиле.
//  Раньше на витрине стоял нарисованный «Стив» (плоские квадраты),
//  а рядом с ником красовались две буквы — обе заглушки.
//
//  Здесь настоящая текстура 64x64: сначала с диска (мгновенно, без
//  сети), потом тихое обновление из Luxe (офлайн-ник) или из профиля
//  Microsoft (если вход выполнен). Сеть никогда не блокирует UI.
// ================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

static class PlayerSkin
{
    static readonly object Sync = new object();
    static Bitmap tex;
    static bool slim;
    static bool real;
    static bool started;
    static string nick = "";

    // Отложенное сохранение правок из редактора: при каждом мазке кисти
    // писать PNG на диск нельзя — это убивает плавность. Держим последнюю
    // картинку и сохраняем один раз после паузы.
    static Bitmap pendingSave;
    static System.Windows.Forms.Timer saveTimer;

    // Текстура для рисования. Всегда не null после Init.
    public static Bitmap Tex
    {
        get { lock (Sync) { return tex; } }
    }

    public static bool Slim
    {
        get { lock (Sync) { return slim; } }
    }

    // true — это настоящий скин игрока, false — встроенная заглушка.
    public static bool IsReal
    {
        get { lock (Sync) { return real; } }
    }

    // Вызывается из фонового потока, когда текстура обновилась.
    public static event Action Changed;

    static string CachePng()
    {
        return Path.Combine(Cfg.DataDir, "skin_cache.png");
    }

    static string CacheMeta()
    {
        return Path.Combine(Cfg.DataDir, "skin_cache.txt");
    }

    public static void Init(string user)
    {
        nick = user == null ? "" : user;
        lock (Sync)
        {
            if (tex == null) tex = LoadCached();
            if (tex == null) tex = DefaultTex();
        }
        if (started) return;
        started = true;
        Thread t = new Thread((ThreadStart)delegate
        {
            try { Fetch(); }
            catch { }
        });
        t.IsBackground = true;
        t.Start();
    }

    // ---------------- правки из редактора ----------------

    // Скин, который нарисовали в редакторе, сразу становится витриной:
    // заменяем текстуру в памяти, будим подписчиков и мягко сохраняем
    // кэш. Раньше на главной до перезапуска оставался старый Стив.
    public static void SetLocal(Bitmap src)
    {
        if (src == null) return;
        Bitmap copy = null;
        try
        {
            copy = src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb);
        }
        catch { return; }
        lock (Sync)
        {
            if (tex != null && !ReferenceEquals(tex, copy)) tex.Dispose();
            tex = copy;
            real = true;
        }
        if (Changed != null)
        {
            try { Changed(); } catch { }
        }
        ScheduleSave(copy);
    }

    // Тонкая/классическая модель: фигура на витрине перестраивается.
    public static void SetSlim(bool isSlim)
    {
        bool same;
        lock (Sync) { same = slim == isSlim; slim = isSlim; }
        if (!same && Changed != null)
        {
            try { Changed(); } catch { }
        }
        Bitmap cur = Tex;
        if (cur != null) ScheduleSave(cur);
    }

    static void ScheduleSave(Bitmap b)
    {
        try
        {
            if (pendingSave != null) { pendingSave.Dispose(); pendingSave = null; }
            pendingSave = b.Clone(new Rectangle(0, 0, b.Width, b.Height), PixelFormat.Format32bppArgb);
            if (saveTimer == null)
            {
                saveTimer = new System.Windows.Forms.Timer();
                saveTimer.Interval = 1200;
                saveTimer.Tick += delegate
                {
                    saveTimer.Stop();
                    Bitmap toSave = pendingSave;
                    pendingSave = null;
                    if (toSave == null) return;
                    try { SaveCache(toSave, Slim); } catch { }
                    try { toSave.Dispose(); } catch { }
                };
            }
            saveTimer.Stop();
            saveTimer.Start();
        }
        catch { }
    }

    // ---------------- кэш на диске ----------------

    static Bitmap LoadCached()
    {
        try
        {
            string png = CachePng();
            if (!File.Exists(png)) return null;
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(png)))
            using (Bitmap src = new Bitmap(ms))
            {
                string err;
                Bitmap b = SkinCanvas.FromImage(src, out err);
                if (b == null) return null;
                // Пустой холст редактора когда-то мог попасть в кэш и
                // затереть скин: такую текстуру считаем битой и берём
                // встроенную — иначе игрок остаётся с «дырявой» фигурой.
                if (!HasPixels(b)) { b.Dispose(); return null; }
                real = true;
                slim = ReadSlim();
                return b;
            }
        }
        catch { return null; }
    }

    // Есть ли в текстуре хоть что-то, кроме прозрачности. У настоящего
    // скина заполнено больше половины пикселей, у пустого холста — ноль.
    static bool HasPixels(Bitmap b)
    {
        try
        {
            int opaque = 0, need = 0;
            for (int y = 0; y < b.Height; y += 2)
                for (int x = 0; x < b.Width; x += 2)
                {
                    need++;
                    if (b.GetPixel(x, y).A > 10) opaque++;
                }
            return opaque > need / 20;   // хотя бы 5% непрозрачных
        }
        catch { return true; }
    }

    static bool ReadSlim()
    {
        try
        {
            string meta = CacheMeta();
            if (!File.Exists(meta)) return false;
            string s = File.ReadAllText(meta).Trim();
            return s.StartsWith("slim", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Когда кэш обновлялся в последний раз (Unix-секунды), 0 — неизвестно.
    static double CacheStamp()
    {
        try
        {
            string meta = CacheMeta();
            if (!File.Exists(meta)) return 0;
            string[] parts = File.ReadAllText(meta).Trim().Split('|');
            if (parts.Length < 2) return 0;
            double v;
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return 0;
            return v;
        }
        catch { return 0; }
    }

    static void SaveCache(Bitmap b, bool isSlim)
    {
        try
        {
            Directory.CreateDirectory(Cfg.DataDir);
            b.Save(CachePng(), ImageFormat.Png);
            double now = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            File.WriteAllText(CacheMeta(),
                (isSlim ? "slim" : "classic") + "|" +
                now.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    // ---------------- сеть ----------------

    // Скин меняется не каждый час: если кэш свежий, сеть не трогаем.
    static void Fetch()
    {
        double age = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds - CacheStamp();
        if (real && age > 0 && age < 1800) return;

        if (Auth.LoggedIn)
        {
            Auth.RefreshProfileAsync(delegate(bool ok, string msg)
            {
                if (!ok) return;
                try
                {
                    string url = Auth.CurrentSkinUrl();
                    if (url.Length == 0) return;
                    string err;
                    byte[] png = Auth.DownloadSkin(url, out err);
                    Apply(png, Auth.LastSkinVariant == "SLIM");
                }
                catch { }
            });
            return;
        }

        // Офлайн-игрок: скин лежит в сервисе Luxe под тем же ником.
        if (nick.Length == 0) return;
        try
        {
            if (!Luxe.LoginFor(nick)) return;
            string url, model;
            if (!Luxe.MySkin(out url, out model)) return;
            string full = url.StartsWith("http") ? url : Luxe.Host() + url;
            string derr;
            byte[] png = Auth.DownloadSkin(full, out derr);
            Apply(png, model == "slim");
        }
        catch { }
    }

    static void Apply(byte[] png, bool isSlim)
    {
        if (png == null || png.Length < 64) return;
        try
        {
            Bitmap made = null;
            using (MemoryStream ms = new MemoryStream(png))
            using (Bitmap src = new Bitmap(ms))
            {
                string err;
                made = SkinCanvas.FromImage(src, out err);
            }
            if (made == null) return;

            bool changed;
            lock (Sync)
            {
                changed = !real || slim != isSlim || tex == null;
                if (tex != null) tex.Dispose();
                tex = made;
                slim = isSlim;
                real = true;
            }
            SaveCache(made, isSlim);
            if (changed && Changed != null)
            {
                try { Changed(); } catch { }
            }
        }
        catch { }
    }

    // ---------------- заглушка ----------------
    // Если скина ещё нет: аккуратный «Стив» в той же развёртке 64x64,
    // чтобы фигура на витрине никогда не оставалась пустой.
    static Bitmap DefaultTex()
    {
        Bitmap b = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.Transparent);

            Color skin = Color.FromArgb(214, 161, 116);
            Color skinD = Color.FromArgb(190, 138, 98);
            Color hair = Color.FromArgb(74, 48, 30);
            Color hairHi = Color.FromArgb(104, 70, 42);
            Color shirt = Color.FromArgb(0, 168, 168);
            Color shirtD = Color.FromArgb(0, 138, 140);
            Color pants = Color.FromArgb(58, 68, 170);
            Color pantsD = Color.FromArgb(44, 52, 138);
            Color shoe = Color.FromArgb(70, 68, 74);
            Color eye = Color.FromArgb(60, 52, 140);

            // голова: верх, низ, бока, лицо, затылок
            Fill(g, 8, 0, 8, 8, hair);
            Fill(g, 16, 0, 8, 8, skinD);
            Fill(g, 0, 8, 8, 8, hair);
            Fill(g, 8, 8, 8, 8, skin);
            Fill(g, 16, 8, 8, 8, hair);
            Fill(g, 24, 8, 8, 8, hair);
            Fill(g, 8, 8, 8, 2, hair);              // чёлка
            Fill(g, 8, 8, 1, 4, hair);              // висок
            Fill(g, 15, 8, 1, 4, hair);
            Fill(g, 9, 12, 2, 1, Color.White);
            Fill(g, 10, 12, 1, 1, eye);
            Fill(g, 13, 12, 2, 1, Color.White);
            Fill(g, 13, 12, 1, 1, eye);
            Fill(g, 11, 14, 2, 1, skinD);           // рот

            // тело
            Fill(g, 20, 16, 8, 4, shirt);
            Fill(g, 28, 16, 8, 4, shirtD);
            Fill(g, 16, 20, 4, 12, shirt);
            Fill(g, 20, 20, 8, 12, shirt);
            Fill(g, 28, 20, 4, 12, shirt);
            Fill(g, 32, 20, 8, 12, shirtD);
            Fill(g, 20, 20, 8, 1, Color.FromArgb(210, 235, 235)); // ворот

            // правая рука (u=40)
            Fill(g, 44, 16, 4, 4, shirt);
            Fill(g, 48, 16, 4, 4, shirtD);
            Fill(g, 40, 20, 4, 12, shirt);
            Fill(g, 44, 20, 4, 12, shirt);
            Fill(g, 48, 20, 4, 12, shirt);
            Fill(g, 52, 20, 4, 12, shirtD);
            Fill(g, 40, 28, 4, 4, skin);            // кисть
            Fill(g, 44, 28, 4, 4, skin);
            Fill(g, 48, 28, 4, 4, skin);
            Fill(g, 52, 28, 4, 4, skinD);

            // левая рука (u=32, v=48)
            Fill(g, 36, 48, 4, 4, shirt);
            Fill(g, 40, 48, 4, 4, shirtD);
            Fill(g, 32, 52, 4, 12, shirt);
            Fill(g, 36, 52, 4, 12, shirt);
            Fill(g, 40, 52, 4, 12, shirt);
            Fill(g, 44, 52, 4, 12, shirtD);
            Fill(g, 32, 60, 4, 4, skin);
            Fill(g, 36, 60, 4, 4, skin);
            Fill(g, 40, 60, 4, 4, skin);
            Fill(g, 44, 60, 4, 4, skinD);

            // правая нога (u=0, v=16)
            Fill(g, 4, 16, 4, 4, pants);
            Fill(g, 8, 16, 4, 4, pantsD);
            Fill(g, 0, 20, 4, 12, pants);
            Fill(g, 4, 20, 4, 12, pants);
            Fill(g, 8, 20, 4, 12, pants);
            Fill(g, 12, 20, 4, 12, pantsD);
            Fill(g, 0, 28, 4, 4, shoe);
            Fill(g, 4, 28, 4, 4, shoe);
            Fill(g, 8, 28, 4, 4, shoe);
            Fill(g, 12, 28, 4, 4, shoe);

            // левая нога (u=16, v=48)
            Fill(g, 20, 48, 4, 4, pants);
            Fill(g, 24, 48, 4, 4, pantsD);
            Fill(g, 16, 52, 4, 12, pants);
            Fill(g, 20, 52, 4, 12, pants);
            Fill(g, 24, 52, 4, 12, pants);
            Fill(g, 28, 52, 4, 12, pantsD);
            Fill(g, 16, 60, 4, 4, shoe);
            Fill(g, 20, 60, 4, 4, shoe);
            Fill(g, 24, 60, 4, 4, shoe);
            Fill(g, 28, 60, 4, 4, shoe);

            // шапка-слой: почти прозрачный, как у настоящих скинов.
            // Полностью залитый слой превращает голову в «бревно» —
            // раньше именно так и было.
            Fill(g, 40, 0, 8, 8, hair);              // верх (почти не виден)
            Fill(g, 48, 8, 8, 8, hair);              // затылок слоя
            Fill(g, 32, 8, 8, 2, hair);              // чёлка слоя
            Fill(g, 32, 10, 1, 4, hair);             // виски слоя
            Fill(g, 39, 10, 1, 4, hair);
        }
        real = false;
        slim = false;
        return b;
    }

    static void Fill(Graphics g, int x, int y, int w, int h, Color c)
    {
        using (SolidBrush b = new SolidBrush(c))
            g.FillRectangle(b, x, y, w, h);
    }
}
