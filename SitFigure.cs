// ================================================================
//  ФИГУРА ИГРОКА НА ВИТРИНЕ
//
//  Раньше на баннере стоял нарисованный «Стив» из прямоугольников.
//  Теперь здесь настоящая текстура скина игрока (PlayerSkin.Tex),
//  натянутая на объёмную модель по игровой UV-развёртке 64x64,
//  в позе «сидит на камне» с медленной анимацией дыхания.
//
//  Рендер свой, без библиотек: ортографическая проекция, отсечение
//  задних граней, сортировка по глубине, ламбертово освещение от
//  «солнца» за спиной. Модель строится один раз на скин, каждый
//  кадр пересчитываются только позы — это дёшево.
// ================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

static class SitFigure
{
    // ---------- математика ----------

    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    // Звено трансформации: поворот вокруг точки-сустава.
    class Xf
    {
        public float Px, Py, Pz;
        public float Ax, Ay, Az;   // радианы
        public int Tag;            // 1 голова, 2 рука-пр, 3 рука-лев, 4 бедро, 5 колено

        public V3 Apply(V3 p)
        {
            float x = p.X - Px, y = p.Y - Py, z = p.Z - Pz;
            if (Ay != 0f)
            {
                float c = (float)Math.Cos(Ay), s = (float)Math.Sin(Ay);
                float nx = x * c + z * s;
                z = -x * s + z * c;
                x = nx;
            }
            if (Ax != 0f)
            {
                float c = (float)Math.Cos(Ax), s = (float)Math.Sin(Ax);
                float ny = y * c - z * s;
                z = y * s + z * c;
                y = ny;
            }
            if (Az != 0f)
            {
                float c = (float)Math.Cos(Az), s = (float)Math.Sin(Az);
                float nx = x * c - y * s;
                y = x * s + y * c;
                x = nx;
            }
            return new V3(Px + x, Py + y, Pz + z);
        }
    }

    // Грань модели: 4 угла + вырезка в текстуре.
    class Face
    {
        public V3 A, B, C, D;          // tex(0,0), tex(1,0), tex(0,1), tex(1,1)
        public Rectangle Tex;
        public bool Overlay;
        public Xf[] Chain;             // от внешнего сустава к внутреннему
    }

    static List<Face> faces;
    static Bitmap builtFor;
    static bool builtSlim;

    // ---------- сборка модели ----------

    static void Build(Bitmap tex, bool slim)
    {
        faces = new List<Face>();
        float aw = slim ? 3f : 4f;

        Xf torso = new Xf();                                   // корень
        Xf head = new Xf();  head.Tag = 1;  head.Px = 0;  head.Py = 24; head.Pz = 0;
        Xf armR = new Xf();  armR.Tag = 2;  armR.Px = -4;  armR.Py = 22.5f;
        Xf armL = new Xf();  armL.Tag = 3;  armL.Px = 4;   armL.Py = 22.5f;
        Xf hipR = new Xf();  hipR.Tag = 4;  hipR.Px = -2;  hipR.Py = 12;
        Xf hipL = new Xf();  hipL.Tag = 4;  hipL.Px = 2;   hipL.Py = 12;
        Xf kneeR = new Xf(); kneeR.Tag = 5;
        Xf kneeL = new Xf(); kneeL.Tag = 5;

        // голова (база + шапка)
        AddBox(-4, 4, 24, 32, -4, 4, 0, 0, 8, 8, 8, false, new Xf[] { head, torso });
        AddBox(-4.5f, 4.5f, 23.5f, 32.5f, -4.5f, 4.5f, 32, 0, 8, 8, 8, true, new Xf[] { head, torso });

        // тело (база + куртка)
        AddBox(-4, 4, 12, 24, -2, 2, 16, 16, 8, 12, 4, false, new Xf[] { torso });
        AddBox(-4.25f, 4.25f, 11.75f, 24.25f, -2.25f, 2.25f, 16, 32, 8, 12, 4, true, new Xf[] { torso });

        // руки: классика — 4 юнита, slim — 3
        AddBox(-4f - aw, -4f, 12, 24, -2, 2, 40, 16, (int)aw, 12, 4, false, new Xf[] { armR, torso });
        AddBox(-4f - aw - 0.25f, -4f + 0.25f, 11.75f, 24.25f, -2.25f, 2.25f,
               40, 32, (int)aw, 12, 4, true, new Xf[] { armR, torso });

        AddBox(4f, 4f + aw, 12, 24, -2, 2, 32, 48, (int)aw, 12, 4, false, new Xf[] { armL, torso });
        AddBox(4f - 0.25f, 4f + aw + 0.25f, 11.75f, 24.25f, -2.25f, 2.25f,
               48, 48, (int)aw, 12, 4, true, new Xf[] { armL, torso });

        // ноги: бедро (y 6..12) + голень (y 0..6), чтобы был сгиб в колене
        // UV: база правой (0,16), база левой (16,48);
        //     слой правой (0,32), слой левой (0,48)
        Leg(-3.9f, 0.1f, 0, 16, false, hipR, kneeR, torso);
        Leg(-4.15f, 0.35f, 0, 32, true, hipR, kneeR, torso);
        Leg(-0.1f, 3.9f, 16, 48, false, hipL, kneeL, torso);
        Leg(-0.35f, 4.15f, 0, 48, true, hipL, kneeL, torso);

        builtFor = tex;
        builtSlim = slim;
        collected = false;
    }

    static void Leg(float x0, float x1, int u, int v, bool over, Xf hip, Xf knee, Xf torso)
    {
        // бедро: геометрия y 6..12, текстура — вся нога (0..12)
        AddBoxSlice(x0, x1, 6, 12, -2, 2, 0, 12, u, v, 4, 12, 4, over, new Xf[] { hip, torso });
        // голень: геометрия y 0..6
        AddBoxSlice(x0, x1, 0, 6, -2, 2, 0, 12, u, v, 4, 12, 4, over, new Xf[] { knee, hip, torso });
    }

    static void AddBox(float x0, float x1, float y0, float y1, float z0, float z1,
                       int u, int v, int w, int h, int d, bool over, Xf[] chain)
    {
        AddBoxSlice(x0, x1, y0, y1, z0, z1, y0, y1, u, v, w, h, d, over, chain);
    }

    // yA..yB — полный диапазон части (для нарезки текстуры),
    // y0..y1 — геометрический кусок.
    static void AddBoxSlice(float x0, float x1, float y0, float y1, float z0, float z1,
                            float yA, float yB, int u, int v, int w, int h, int d,
                            bool over, Xf[] chain)
    {
        float span = yB - yA;
        if (span <= 0.001f) span = 1f;
        int vOff = (int)Math.Round((yB - y1) / span * h);
        int vLen = (int)Math.Round((y1 - y0) / span * h);
        if (vLen < 1) vLen = 1;
        if (vOff < 0) vOff = 0;
        if (vOff + vLen > h) vLen = h - vOff;
        if (vLen < 1) return;

        bool fullTop = Math.Abs(y1 - yB) < 0.01f;
        bool fullBot = Math.Abs(y0 - yA) < 0.01f;

        if (fullTop)
            Quad(new V3(x0, y1, z0), new V3(x1, y1, z0), new V3(x0, y1, z1), new V3(x1, y1, z1),
                 new Rectangle(u + d, v, w, d), over, chain);
        if (fullBot)
            Quad(new V3(x0, y0, z0), new V3(x1, y0, z0), new V3(x0, y0, z1), new V3(x1, y0, z1),
                 new Rectangle(u + d + w, v, w, d), over, chain);
        Quad(new V3(x0, y1, z0), new V3(x0, y1, z1), new V3(x0, y0, z0), new V3(x0, y0, z1),
             new Rectangle(u, v + d + vOff, d, vLen), over, chain);
        Quad(new V3(x0, y1, z1), new V3(x1, y1, z1), new V3(x0, y0, z1), new V3(x1, y0, z1),
             new Rectangle(u + d, v + d + vOff, w, vLen), over, chain);
        Quad(new V3(x1, y1, z1), new V3(x1, y1, z0), new V3(x1, y0, z1), new V3(x1, y0, z0),
             new Rectangle(u + d + w, v + d + vOff, d, vLen), over, chain);
        Quad(new V3(x1, y1, z0), new V3(x0, y1, z0), new V3(x1, y0, z0), new V3(x0, y0, z0),
             new Rectangle(u + 2 * d + w, v + d + vOff, w, vLen), over, chain);
    }

    static void Quad(V3 a, V3 b, V3 c, V3 d, Rectangle tex, bool over, Xf[] chain)
    {
        if (tex.Width <= 0 || tex.Height <= 0) return;
        Face f = new Face();
        f.A = a; f.B = b; f.C = c; f.D = d;
        f.Tex = tex;
        f.Overlay = over;
        f.Chain = chain;
        faces.Add(f);
    }

    // Таз модели (y=12) садится на верх камня. Камень рисуется
    // отдельно по этой же величине — поэтому смещение нулевое.
    const float RootY = 0f;

    static V3 Run(Face f, V3 p)
    {
        for (int i = f.Chain.Length - 1; i >= 0; i--)
            p = f.Chain[i].Apply(p);
        p.Y += RootY;
        return p;
    }

    // ---------- поза ----------

    static Xf sTorso, sHead, sArmR, sArmL, sHipR, sHipL, sKneeR, sKneeL;
    static bool collected;

    static void Collect()
    {
        sTorso = null; sHead = null; sArmR = null; sArmL = null;
        sHipR = null; sHipL = null; sKneeR = null; sKneeL = null;
        for (int i = 0; i < faces.Count; i++)
        {
            Xf[] ch = faces[i].Chain;
            for (int k = 0; k < ch.Length; k++)
            {
                Xf f = ch[k];
                switch (f.Tag)
                {
                    case 1: sHead = f; break;
                    case 2: sArmR = f; break;
                    case 3: sArmL = f; break;
                    case 4: if (f.Px < 0) sHipR = f; else sHipL = f; break;
                    case 5: if (ch.Length > 1 && ch[ch.Length - 2].Px < 0) sKneeR = f; else sKneeL = f; break;
                }
            }
        }
        collected = true;
    }

    // Поза «сидит на камне»: бёдра вперёд, голени свободно свисают,
    // правая рука отведена назад как опора, левая лежит на колене.
    // Голова медленно поворачивается — фигура «живая», но не дёргается.
    static void Pose(float t)
    {
        if (!collected) Collect();
        float br = (float)Math.Sin(t * 1.15f);

        // Колено вращается в СИСТЕМНОЙ оси бедра (цепочка knee→hip):
        // чтобы голень встала вертикально вниз, его угол компенсирует
        // бедро (−64° → +56°, стопы чуть свисают вперёд — естественная
        // посадка). Малый угол заваливал голени вперёд «клиньями».
        float thighA = -64f + br * 1.2f;
        float kneeA = 56f + (float)Math.Sin(t * 1.15f + 0.55f) * 1.6f;

        // Руки: правая назад (опора), левая вперёд (на колено)
        float armRA = 34f + (float)Math.Sin(t * 1.15f + 1.1f) * 2.6f;
        float armLA = -16f + (float)Math.Sin(t * 1.15f + 0.4f) * 2.0f;

        if (sHead != null)
        {
            sHead.Ay = (float)Math.Sin(t * 0.42f) * 9f * (float)Math.PI / 180f;
            sHead.Ax = (-5f + (float)Math.Sin(t * 0.71f) * 2.0f) * (float)Math.PI / 180f;
        }
        if (sArmR != null)
        {
            sArmR.Ax = armRA * (float)Math.PI / 180f;
            sArmR.Az = -6f * (float)Math.PI / 180f;
        }
        if (sArmL != null)
        {
            sArmL.Ax = armLA * (float)Math.PI / 180f;
            sArmL.Az = 7f * (float)Math.PI / 180f;
        }

        float th = thighA * (float)Math.PI / 180f;
        float kn = kneeA * (float)Math.PI / 180f;
        if (sHipR != null) sHipR.Ax = th;
        if (sHipL != null) sHipL.Ax = th;

        // колено вращается вокруг своей ТЕКУЩЕЙ точки: сначала бедро
        // повернуло ногу, поэтому сустав считаем уже в повёрнутой позе
        if (sHipR != null && sKneeR != null)
        {
            V3 k = sHipR.Apply(new V3(sHipR.Px, 6f, 0f));
            sKneeR.Px = k.X; sKneeR.Py = k.Y; sKneeR.Pz = k.Z;
            sKneeR.Ax = kn;
        }
        if (sHipL != null && sKneeL != null)
        {
            V3 k = sHipL.Apply(new V3(sHipL.Px, 6f, 0f));
            sKneeL.Px = k.X; sKneeL.Py = k.Y; sKneeL.Pz = k.Z;
            sKneeL.Ax = kn;
        }
    }

    // ---------- отрисовка ----------

    public static void Paint(Graphics g, Rectangle r, float t)
    {
        Bitmap tex = PlayerSkin.Tex;
        if (tex == null || r.Width <= 8 || r.Height <= 8) return;
        if (faces == null || builtFor != tex || builtSlim != PlayerSkin.Slim)
        {
            Build(tex, PlayerSkin.Slim);
            Collect();
        }
        if (faces.Count == 0) return;

        Pose(t);

        // камера: чуть справа и сверху — видно лицо и посадку.
        // yaw положительный, как в Skin3D (при 22° лицо повёрнуто к нам);
        // при отрицательном видно затылок.
        float yaw = 22f * (float)Math.PI / 180f;
        float pitch = 8f * (float)Math.PI / 180f;
        float cyaw = (float)Math.Cos(yaw), syaw = (float)Math.Sin(yaw);
        float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);

        // проход 1: габариты, чтобы вписать фигуру в r
        float minX = 9999, maxX = -9999, minY = 9999, maxY = -9999;
        for (int i = 0; i < faces.Count; i++)
        {
            Face f = faces[i];
            V3[] p = new V3[] { Run(f, f.A), Run(f, f.B), Run(f, f.C), Run(f, f.D) };
            for (int k = 0; k < 4; k++)
            {
                if (p[k].X < minX) minX = p[k].X;
                if (p[k].X > maxX) maxX = p[k].X;
                if (p[k].Y < minY) minY = p[k].Y;
                if (p[k].Y > maxY) maxY = p[k].Y;
            }
        }
        float mw = Math.Max(1f, maxX - minX);
        float mh = Math.Max(1f, maxY - minY);
        float scale = Math.Min(r.Width / mw, r.Height / mh) * 0.92f;
        if (scale < 0.5f) scale = 0.5f;

        float cx = r.X + r.Width * 0.5f - (minX + maxX) * 0.5f * scale;
        float cy = r.Y + r.Height * 0.5f + (minY + maxY) * 0.5f * scale;

        // Камень: верх на уровне таза (y=12 в модели), а не под ступнями.
        // Иначе фигура «стоит» на плите, а не сидит — ноги должны свисать
        // перед передней гранью блока.
        float pelvisY = 12f + RootY;
        float seatTop = cy - pelvisY * scale;
        DrawSeat(g, cx, seatTop, scale, mw);

        // тень на верхней грани камня — под тазом
        float shR = mw * scale * 0.46f;
        using (GraphicsPath shp = new GraphicsPath())
        {
            shp.AddEllipse(cx - shR * 0.55f, seatTop - shR * 0.14f, shR * 1.1f, shR * 0.28f);
            using (PathGradientBrush pb = new PathGradientBrush(shp))
            {
                pb.CenterColor = Color.FromArgb(160, 0, 0, 0);
                pb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                g.FillPath(pb, shp);
            }
        }

        // ---- проход 2: видимые грани с сортировкой ----
        List<PF> list = new List<PF>(faces.Count);
        for (int i = 0; i < faces.Count; i++)
        {
            Face f = faces[i];
            V3[] w3 = new V3[] { Run(f, f.A), Run(f, f.B), Run(f, f.C), Run(f, f.D) };

            float ux = w3[1].X - w3[0].X, uy = w3[1].Y - w3[0].Y, uz = w3[1].Z - w3[0].Z;
            float vx = w3[2].X - w3[0].X, vy = w3[2].Y - w3[0].Y, vz = w3[2].Z - w3[0].Z;
            // Нормаль = v × u (НЕ u × v): при нашем порядке вершин
            // (A=tex00, B=tex10, C=tex01) cross(u,v) смотрит ВНУТРЬ
            // модели — из-за этого отсекались все лицевые грани и
            // фигура выглядела вывернутой наизнанку.
            float nx = vy * uz - vz * uy;
            float ny = vz * ux - vx * uz;
            float nz = vx * uy - vy * ux;
            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 0.0001f) continue;
            nx /= len; ny /= len; nz /= len;

            float vnx = nx * cyaw + nz * syaw;
            float vnz = -nx * syaw + nz * cyaw;
            float vny = ny * cp - vnz * sp;
            float vnz2 = ny * sp + vnz * cp;
            if (vnz2 <= 0.02f) continue;          // задняя грань

            PF q = new PF();
            q.F = f;
            q.P = new PointF[4];
            float dsum = 0f;
            for (int k = 0; k < 4; k++)
            {
                float x = w3[k].X, y = w3[k].Y, z = w3[k].Z;
                float rx = x * cyaw + z * syaw;
                float rz = -x * syaw + z * cyaw;
                float ry = y * cp - rz * sp;
                float rz2 = y * sp + rz * cp;
                q.P[k] = new PointF(cx + rx * scale, cy - ry * scale);
                dsum += rz2;
            }
            q.Depth = dsum * 0.25f;

            // свет: тёплое солнце сзади-справа + холодная подсветка сверху
            float dot = vnx * 0.36f + vny * 0.78f + vnz2 * 0.51f;
            if (dot < 0f) dot = 0f;
            float br = 0.66f + 0.40f * dot;
            if (br > 1.08f) br = 1.08f;
            q.Bright = br;
            list.Add(q);
        }
        list.Sort(delegate(PF a, PF b) { return a.Depth.CompareTo(b.Depth); });

        InterpolationMode oldI = g.InterpolationMode;
        PixelOffsetMode oldP = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        for (int i = 0; i < list.Count; i++)
        {
            PF q = list[i];
            float br = q.Bright;
            ColorMatrix cm = new ColorMatrix(new float[][]
            {
                new float[] { br, 0, 0, 0, 0 },
                new float[] { 0, br, 0, 0, 0 },
                new float[] { 0, 0, br, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                ia.SetWrapMode(WrapMode.TileFlipXY);
                try
                {
                    g.DrawImage(tex, new PointF[] { q.P[0], q.P[1], q.P[2] },
                        q.F.Tex, GraphicsUnit.Pixel, ia);
                }
                catch { }
            }
        }
        g.InterpolationMode = oldI;
        g.PixelOffsetMode = oldP;
    }

    class PF
    {
        public Face F;
        public PointF[] P;
        public float Depth;
        public float Bright;
    }

    // Каменный блок, на котором сидит игрок. seatTop — ВЕРХ блока:
    // от него блок уходит вниз, поэтому таз оказывается ровно на камне.
    static void DrawSeat(Graphics g, float cx, float seatTop, float scale, float mw)
    {
        float w = mw * scale * 1.02f;
        float h = Math.Max(14f, mw * scale * 0.34f);
        RectangleF slab = new RectangleF(cx - w * 0.5f, seatTop, w, h);
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Ui.RoundRect(slab, h * 0.30f))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(slab.X, slab.Y, slab.Width, slab.Height + 2f),
                Color.FromArgb(176, 48, 52, 80), Color.FromArgb(212, 13, 15, 25), 90f))
                g.FillPath(b, p);
            // тёплая кромка сверху — от «солнца» в кадре
            using (Pen pen = new Pen(Color.FromArgb(104, 186, 176, 226), 1.2f))
                g.DrawPath(pen, p);
            // верхняя плоскость: светлая полоса, читается как грань блока
            using (LinearGradientBrush tb = new LinearGradientBrush(
                new RectangleF(slab.X, slab.Y, slab.Width, Math.Max(3f, h * 0.30f)),
                Color.FromArgb(120, 126, 132, 178), Color.FromArgb(0, 126, 132, 178), 90f))
                g.FillRectangle(tb, slab.X, slab.Y, slab.Width, Math.Max(3f, h * 0.30f));
        }
        g.Restore(st);
    }

    // Голова из настоящей текстуры скина: лицо + шапка-слой.
    // Заменяет две буквы ника в профиле.
    public static void PaintHead(Graphics g, Rectangle r)
    {
        Bitmap tex = PlayerSkin.Tex;
        if (tex == null || r.Width < 8 || r.Height < 8) return;

        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using (GraphicsPath clip = new GraphicsPath())
        {
            clip.AddEllipse(r);
            g.SetClip(clip, CombineMode.Replace);

            RectangleF dst = new RectangleF(r.X, r.Y, r.Width, r.Height);
            // лицо 8x8 из развёртки головы
            g.DrawImage(tex, dst, new Rectangle(8, 8, 8, 8), GraphicsUnit.Pixel);
            // шапка/волосы поверх (прозрачные пиксели не перекроют лицо)
            g.DrawImage(tex, dst, new Rectangle(40, 8, 8, 8), GraphicsUnit.Pixel);

            // объём: свет сверху-слева, тень снизу
            using (LinearGradientBrush lb = new LinearGradientBrush(
                new RectangleF(r.X, r.Y, r.Width, r.Height),
                Color.FromArgb(58, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 62f))
                g.FillEllipse(lb, r);
            using (LinearGradientBrush sb = new LinearGradientBrush(
                new RectangleF(r.X, r.Y + r.Height * 0.55f, r.Width, r.Height * 0.45f),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(90, 0, 0, 0), 90f))
                g.FillRectangle(sb, r.X, r.Y + r.Height * 0.55f, r.Width, r.Height * 0.45f);
        }
        g.Restore(st);

        // кольцо в брендовых цветах
        GraphicsState st2 = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
        using (Pen pen = new Pen(Ui.Lerp(Ui.Accent1, Ui.Accent2, 0.5f), Math.Max(1.6f, r.Width * 0.055f)))
            g.DrawEllipse(pen, rf);
        g.Restore(st2);
    }
}
