// ================================================================
//  3D-ПРЕДПРОСМОТР СКИНА
//
//  Модель персонажа рисуется вручную, без DirectX и библиотек:
//  ортогональная проекция, аффинные грани, сортировка по глубине,
//  отсечение задних граней. Текстура — та же битмапа 64x64, что и
//  у 2D-канвы, поэтому рисовать можно прямо по модели: экранная
//  точка переводится обратно в пиксель текстуры.
//
//  ЛКМ — рисовать, ПКМ — крутить, колесо — зум.
//  Пока курсор над моделью, авто-вращение стоит на паузе.
//
//  UV-разметка и ориентация граней сверены с skinview3d/three.js:
//   • −x (правая сторона модели): левый столбец текстуры = зад (z0)
//   • +z (лицо): левый столбец = левый край персонажа (x0)
//   • +x (левая сторона): левый столбец = перед (z1)
//   • −z (спина): левый столбец = правый край (x1)
//   • верх/низ: левый столбец = x0, верхняя строка = зад (z0)
// ================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

class SkinView3D : Control
{
    // ---------------- публичное ----------------

    public SkinCanvas Canvas;
    public bool AutoSpin = true;
    public Action<Point> HoverTexel;          // пиксель текстуры под курсором, (-1,-1) — нет

    bool slim = false;
    public bool Slim
    {
        get { return slim; }
        set
        {
            if (slim == value) return;
            slim = value;
            BuildModel();
            Invalidate();
        }
    }

    // ---------------- вид ----------------

    float yaw = 22f, pitch = 5f;
    float zoom = 1f;
    bool hover = false;
    bool rotating = false;
    bool drawing = false;
    Point rotFrom;
    int lastFace = -1, lastX = -1, lastY = -1;

    // наведение: что подсветить (и на 2D-канве тоже)
    int hovFaceIdx = -1;
    int hovTexX = -1, hovTexY = -1;

    System.Windows.Forms.Timer ticker;

    // ---------------- геометрия ----------------

    struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    class Face
    {
        public V3 A, B, C, D;     // углы: tex(0,0), tex(1,0), tex(0,1), tex(1,1)
        public V3 N;              // нормаль наружу
        public Rectangle Tex;     // прямоугольник в текстуре
        public bool Overlay;
    }

    // грань, уже спроецированная на экран (на один кадр)
    class PFace
    {
        public PointF A, B, C, D;
        public float Depth;
        public int Index;
    }

    List<Face> faces = new List<Face>();

    public SkinView3D()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Bg;
        Cursor = Cursors.Cross;
        Size = new Size(250, 500);

        ticker = new System.Windows.Forms.Timer();
        ticker.Interval = 33;
        ticker.Tick += delegate
        {
            if (IsDisposed || Canvas == null) return;
            if (AutoSpin && !hover && !rotating && !drawing)
            {
                yaw += 0.6f;
                if (yaw >= 360f) yaw -= 360f;
                Invalidate();
            }
        };
        ticker.Start();
    }

    protected override void Dispose(bool disposing)
    {
        try { if (ticker != null) ticker.Stop(); } catch { }
        if (disposing && ticker != null) ticker.Dispose();
        base.Dispose(disposing);
    }

    // ============================================================
    //  МОДЕЛЬ
    // ============================================================

    void BuildModel()
    {
        faces = new List<Face>();

        float aw = slim ? 3f : 4f;      // ширина руки
        float ax0 = 4f, ax1 = 4f + aw;  // левая рука (x > 0)
        float rx0 = -4f - aw, rx1 = -4f; // правая рука (x < 0)

        // ---- базовые части ----
        AddPart(-4, 4, 24, 32, -4, 4, 0, 0, 8, 8, 8, false);          // голова
        AddPart(-4, 4, 12, 24, -2, 2, 16, 16, 8, 12, 4, false);       // тело
        AddPart(rx0, rx1, 12, 24, -2, 2, 40, 16, (int)aw, 12, 4, false); // правая рука
        AddPart(ax0, ax1, 12, 24, -2, 2, 32, 48, (int)aw, 12, 4, false); // левая рука
        AddPart(-3.9f, 0.1f, 0, 12, -2, 2, 0, 16, 4, 12, 4, false);   // правая нога
        AddPart(-0.1f, 3.9f, 0, 12, -2, 2, 16, 48, 4, 12, 4, false);  // левая нога

        // ---- второй слой (куртка, шляпа, штаны) ----
        AddPart(-4.5f, 4.5f, 23.5f, 32.5f, -4.5f, 4.5f, 32, 0, 8, 8, 8, true);
        AddPart(-4.25f, 4.25f, 11.75f, 24.25f, -2.25f, 2.25f, 16, 32, 8, 12, 4, true);
        AddPart(rx0 - 0.25f, rx1 + 0.25f, 11.75f, 24.25f, -2.25f, 2.25f, 40, 32, (int)aw, 12, 4, true);
        AddPart(ax0 - 0.25f, ax1 + 0.25f, 11.75f, 24.25f, -2.25f, 2.25f, 48, 48, (int)aw, 12, 4, true);
        AddPart(-4.15f, 0.35f, -0.25f, 12.25f, -2.25f, 2.25f, 0, 32, 4, 12, 4, true);
        AddPart(-0.35f, 4.15f, -0.25f, 12.25f, -2.25f, 2.25f, 0, 48, 4, 12, 4, true);
    }

    // Коробка: (u,v) — левый верхний угол развёртки, w/h/d — размеры
    // сторон в пикселях текстуры (w — по x, h — по y, d — по z).
    void AddPart(float x0, float x1, float y0, float y1, float z0, float z1,
                 int u, int v, int w, int h, int d, bool over)
    {
        // верх: текстура (u+d, v), левый столбец — x0, верхняя строка — зад (z0)
        AddFace(new V3(x0, y1, z0), new V3(x1, y1, z0), new V3(x0, y1, z1), new V3(x1, y1, z1),
                new Rectangle(u + d, v, w, d), 0, 1, 0, over);
        // низ
        AddFace(new V3(x0, y0, z0), new V3(x1, y0, z0), new V3(x0, y0, z1), new V3(x1, y0, z1),
                new Rectangle(u + d + w, v, w, d), 0, -1, 0, over);
        // правая сторона (−x)
        AddFace(new V3(x0, y1, z0), new V3(x0, y1, z1), new V3(x0, y0, z0), new V3(x0, y0, z1),
                new Rectangle(u, v + d, d, h), -1, 0, 0, over);
        // лицо (+z)
        AddFace(new V3(x0, y1, z1), new V3(x1, y1, z1), new V3(x0, y0, z1), new V3(x1, y0, z1),
                new Rectangle(u + d, v + d, w, h), 0, 0, 1, over);
        // левая сторона (+x)
        AddFace(new V3(x1, y1, z1), new V3(x1, y1, z0), new V3(x1, y0, z1), new V3(x1, y0, z0),
                new Rectangle(u + d + w, v + d, d, h), 1, 0, 0, over);
        // спина (−z)
        AddFace(new V3(x1, y1, z0), new V3(x0, y1, z0), new V3(x1, y0, z0), new V3(x0, y0, z0),
                new Rectangle(u + 2 * d + w, v + d, w, h), 0, 0, -1, over);
    }

    void AddFace(V3 a, V3 b, V3 c, V3 d, Rectangle tex, int nx, int ny, int nz, bool over)
    {
        Face f = new Face();
        f.A = a; f.B = b; f.C = c; f.D = d;
        f.Tex = tex;
        f.N = new V3(nx, ny, nz);
        f.Overlay = over;
        faces.Add(f);
    }

    // ============================================================
    //  РИСОВАНИЕ
    // ============================================================

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (Width <= 4 || Height <= 4) return;

        // витрина
        g.Clear(Ui.Bg);
        using (GraphicsPath plate = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 12))
        {
            using (LinearGradientBrush br = new LinearGradientBrush(
                new RectangleF(0, 0, Width, Height),
                Color.FromArgb(30, 37, 50), Color.FromArgb(13, 16, 22), 90f))
                g.FillPath(br, plate);
            using (Pen pen = new Pen(Ui.BorderSoft, 1f))
                g.DrawPath(pen, plate);
        }

        if (faces.Count == 0) BuildModel();
        if (Canvas == null || Canvas.Bmp == null) return;

        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // масштаб: модель 32 юнита в высоту, ~19 в ширину
        float scale = Math.Min((Height - 70f) / 32.5f, (Width - 36f) / 19.5f) * zoom;
        if (scale < 0.5f) scale = 0.5f;
        float cx = Width / 2f;
        float cy = Height * 0.52f + 16f * scale;

        float ry = yaw * (float)Math.PI / 180f;
        float rx = pitch * (float)Math.PI / 180f;
        float cyaw = (float)Math.Cos(ry), syaw = (float)Math.Sin(ry);
        float cp = (float)Math.Cos(rx), sp = (float)Math.Sin(rx);

        // тень под ногами — до модели
        float gd;
        PointF feet = Proj(new V3(0, 0, 0), scale, cx, cy, cyaw, syaw, cp, sp, out gd);
        float rw = 11f * scale, rh = 3.4f * scale;
        using (GraphicsPath sh = new GraphicsPath())
        {
            sh.AddEllipse(feet.X - rw / 2f, feet.Y - rh / 2f + 2f, rw, rh);
            using (PathGradientBrush pb = new PathGradientBrush(sh))
            {
                pb.CenterColor = Color.FromArgb(105, 0, 0, 0);
                pb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                g.FillPath(pb, sh);
            }
        }

        // собрать видимые грани
        List<PFace> list = new List<PFace>();
        for (int i = 0; i < faces.Count; i++)
        {
            Face f = faces[i];
            float nz = NormZ(f.N, cyaw, syaw, cp, sp);
            if (nz <= 0.004f) continue;

            PFace p = new PFace();
            p.Index = i;
            float d1, d2, d3, d4;
            p.A = Proj(f.A, scale, cx, cy, cyaw, syaw, cp, sp, out d1);
            p.B = Proj(f.B, scale, cx, cy, cyaw, syaw, cp, sp, out d2);
            p.C = Proj(f.C, scale, cx, cy, cyaw, syaw, cp, sp, out d3);
            p.D = Proj(f.D, scale, cx, cy, cyaw, syaw, cp, sp, out d4);
            p.Depth = (d1 + d2 + d3 + d4) * 0.25f;
            list.Add(p);
        }
        list.Sort(delegate(PFace a, PFace b) { return a.Depth.CompareTo(b.Depth); });

        // свет фиксирован относительно камеры: верх-фронт-слева
        float lx = -0.42f, ly = 0.62f, lz = 0.66f;
        float ln = (float)Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ln; ly /= ln; lz /= ln;

        for (int i = 0; i < list.Count; i++)
        {
            PFace p = list[i];
            Face f = faces[p.Index];
            float nx = f.N.X, ny = f.N.Y, nz2 = f.N.Z;
            // поворот нормали (та же матрица, что и у точек)
            float vx = nx * cyaw + nz2 * syaw;
            float vz = -nx * syaw + nz2 * cyaw;
            float vy = ny * cp - vz * sp;
            float vz2 = ny * sp + vz * cp;
            float dot = vx * lx + vy * ly + vz2 * lz;
            if (dot < 0f) dot = 0f;
            float br = 0.68f + 0.34f * dot;
            if (br > 1f) br = 1f;

            if (!f.Overlay) DrawGhost(g, p, br);   // подложка: видна там, где текстура ещё пустая
            DrawFace(g, f, p, br);
        }

        // подсветка пикселя под курсором
        if (hovFaceIdx >= 0 && hovTexX >= 0 && hovFaceIdx < faces.Count)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Index != hovFaceIdx) continue;
                PFace p = list[i];
                Face f = faces[p.Index];
                if (f.Tex.Width <= 0 || f.Tex.Height <= 0) break;
                float u0 = (hovTexX - f.Tex.X) / (float)f.Tex.Width;
                float v0 = (hovTexY - f.Tex.Y) / (float)f.Tex.Height;
                float u1 = u0 + 1f / f.Tex.Width;
                float v1 = v0 + 1f / f.Tex.Height;
                PointF q0 = Lerp(p, u0, v0), q1 = Lerp(p, u1, v0),
                       q2 = Lerp(p, u1, v1), q3 = Lerp(p, u0, v1);
                using (Pen pen = new Pen(Color.FromArgb(200, 0, 0, 0), 2.6f))
                    g.DrawPolygon(pen, new PointF[] { q0, q1, q2, q3 });
                using (Pen pen = new Pen(Color.FromArgb(240, 255, 255, 255), 1.2f))
                    g.DrawPolygon(pen, new PointF[] { q0, q1, q2, q3 });
                break;
            }
        }
    }

    static PointF Lerp(PFace p, float u, float v)
    {
        float x = p.A.X + (p.B.X - p.A.X) * u + (p.C.X - p.A.X) * v;
        float y = p.A.Y + (p.B.Y - p.A.Y) * u + (p.C.Y - p.A.Y) * v;
        return new PointF(x, y);
    }

    // пустая текстура — больше не нужна: под каждой базовой гранью
    // всегда лежит «манекен», а текстура рисуется сверху

    // полупрозрачная фигура вместо пустого скина
    void DrawGhost(Graphics g, PFace p, float bright)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            path.AddPolygon(new PointF[] { p.A, p.B, p.D, p.C });
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70,
                (int)(198 * bright), (int)(208 * bright), (int)(224 * bright))))
                g.FillPath(b, path);
            using (Pen pen = new Pen(Color.FromArgb(80, 150, 162, 182), 1f))
                g.DrawPath(pen, path);
        }
    }

    void DrawFace(Graphics g, Face f, PFace p, float bright)
    {
        ColorMatrix cm = new ColorMatrix(new float[][]
        {
            new float[] { bright, 0, 0, 0, 0 },
            new float[] { 0, bright, 0, 0, 0 },
            new float[] { 0, 0, bright, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
        });
        using (ImageAttributes ia = new ImageAttributes())
        {
            ia.SetColorMatrix(cm);
            ia.SetWrapMode(WrapMode.TileFlipXY);
            try
            {
                g.DrawImage(Canvas.Bmp,
                    new PointF[] { p.A, p.B, p.C },
                    f.Tex, GraphicsUnit.Pixel, ia);
            }
            catch
            {
            }
        }
    }

    // ============================================================
    //  ПРЕОБРАЗОВАНИЯ
    // ============================================================

    static PointF Proj(V3 v, float scale, float cx, float cy,
                       float cyaw, float syaw, float cp, float sp, out float depth)
    {
        float x = v.X * cyaw + v.Z * syaw;
        float z = -v.X * syaw + v.Z * cyaw;
        float y = v.Y * cp - z * sp;
        float z2 = v.Y * sp + z * cp;
        depth = z2;
        return new PointF(cx + x * scale, cy - y * scale);
    }

    static float NormZ(V3 n, float cyaw, float syaw, float cp, float sp)
    {
        float z = -n.X * syaw + n.Z * cyaw;
        return n.Y * sp + z * cp;
    }

    // точка внутри параллелограмма? возвращает u,v (0..1)
    static bool Inside(PointF a, PointF b, PointF c, PointF p, out float u, out float v)
    {
        float det = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        u = v = 0f;
        if (Math.Abs(det) < 0.0001f) return false;
        u = ((p.X - a.X) * (c.Y - a.Y) - (p.Y - a.Y) * (c.X - a.X)) / det;
        v = ((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)) / det;
        return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
    }

    // ============================================================
    //  ПОПАДАНИЕ В ТЕКСТУРУ
    // ============================================================

    bool FrameData(out float scale, out float cx, out float cy,
                   out float cyaw, out float syaw, out float cp, out float sp)
    {
        scale = 1f; cx = cy = 0f; cyaw = 1f; syaw = 0f; cp = 1f; sp = 0f;
        if (Canvas == null || Canvas.Bmp == null) return false;
        if (faces.Count == 0) BuildModel();

        scale = Math.Min((Height - 70f) / 32.5f, (Width - 36f) / 19.5f) * zoom;
        if (scale < 0.5f) scale = 0.5f;
        cx = Width / 2f;
        cy = Height * 0.52f + 16f * scale;

        float ry = yaw * (float)Math.PI / 180f;
        float rx = pitch * (float)Math.PI / 180f;
        cyaw = (float)Math.Cos(ry); syaw = (float)Math.Sin(ry);
        cp = (float)Math.Cos(rx); sp = (float)Math.Sin(rx);
        return true;
    }

    bool Hit(Point pt, out int faceIdx, out int texX, out int texY)
    {
        faceIdx = -1; texX = texY = -1;
        float scale, cx, cy, cyaw, syaw, cp, sp;
        if (!FrameData(out scale, out cx, out cy, out cyaw, out syaw, out cp, out sp)) return false;

        List<PFace> list = new List<PFace>();
        for (int i = 0; i < faces.Count; i++)
        {
            Face f = faces[i];
            if (NormZ(f.N, cyaw, syaw, cp, sp) <= 0.004f) continue;
            PFace p = new PFace();
            p.Index = i;
            float d1, d2, d3;
            p.A = Proj(f.A, scale, cx, cy, cyaw, syaw, cp, sp, out d1);
            p.B = Proj(f.B, scale, cx, cy, cyaw, syaw, cp, sp, out d2);
            p.C = Proj(f.C, scale, cx, cy, cyaw, syaw, cp, sp, out d3);
            p.Depth = (d1 + d2 + d3) / 3f;
            list.Add(p);
        }
        // от ближних к дальним: первая попавшая грань и есть верхняя
        list.Sort(delegate(PFace a, PFace b) { return b.Depth.CompareTo(a.Depth); });

        for (int i = 0; i < list.Count; i++)
        {
            PFace p = list[i];
            float u, v;
            if (!Inside(p.A, p.B, p.C, pt, out u, out v)) continue;
            Face f = faces[p.Index];
            int tx = f.Tex.X + (int)(u * f.Tex.Width);
            int ty = f.Tex.Y + (int)(v * f.Tex.Height);
            if (tx >= f.Tex.Right) tx = f.Tex.Right - 1;
            if (ty >= f.Tex.Bottom) ty = f.Tex.Bottom - 1;
            if (tx < 0 || ty < 0 || tx >= 64 || ty >= 64) continue;
            faceIdx = p.Index; texX = tx; texY = ty;
            return true;
        }
        return false;
    }

    // ============================================================
    //  МЫШЬ
    // ============================================================

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Canvas == null) return;
        Focus();

        if (e.Button == MouseButtons.Right)
        {
            rotating = true;
            rotFrom = e.Location;
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        int fi, tx, ty;
        if (!Hit(e.Location, out fi, out tx, out ty)) return;

        if (Canvas.Tool == 3)
        {
            Color c = Canvas.Bmp.GetPixel(tx, ty);
            if (c.A == 0) c = Color.FromArgb(255, c);
            if (Canvas.Picked != null) Canvas.Picked(c);
            return;
        }

        Canvas.PushUndo();
        drawing = true;
        lastFace = fi;
        lastX = tx; lastY = ty;
        if (Canvas.Tool == 2) Canvas.Flood(tx, ty);
        else Canvas.Dot(tx, ty);
        Canvas.Commit();
        SetHover(fi, tx, ty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (rotating)
        {
            yaw += (e.X - rotFrom.X) * 0.55f;
            pitch += (e.Y - rotFrom.Y) * 0.32f;
            if (pitch > 38f) pitch = 38f;
            if (pitch < -38f) pitch = -38f;
            if (yaw >= 360f) yaw -= 360f;
            if (yaw < 0f) yaw += 360f;
            rotFrom = e.Location;
            hover = false;
            ClearHover();
            Invalidate();
            return;
        }

        int fi, tx, ty;
        bool hit = Hit(e.Location, out fi, out tx, out ty);

        if (drawing)
        {
            if (hit && Canvas != null)
            {
                if (fi != lastFace)
                {
                    Canvas.Dot(tx, ty);
                }
                else if (tx != lastX || ty != lastY)
                {
                    Canvas.Line(lastX, lastY, tx, ty);
                }
                lastFace = fi;
                lastX = tx; lastY = ty;
                Canvas.Commit();
            }
            SetHover(hit ? fi : -1, hit ? tx : -1, hit ? ty : -1);
            return;
        }

        SetHover(hit ? fi : -1, hit ? tx : -1, hit ? ty : -1);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        rotating = false;
        drawing = false;
        lastFace = -1;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        rotating = false;
        drawing = false;
        ClearHover();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta > 0) zoom *= 1.12f; else zoom /= 1.12f;
        if (zoom < 0.6f) zoom = 0.6f;
        if (zoom > 2.2f) zoom = 2.2f;
        ClearHover();
        Invalidate();
    }

    void SetHover(int fi, int tx, int ty)
    {
        if (hovFaceIdx == fi && hovTexX == tx && hovTexY == ty) return;
        hovFaceIdx = fi;
        hovTexX = tx;
        hovTexY = ty;
        if (HoverTexel != null)
            HoverTexel(fi >= 0 ? new Point(tx, ty) : new Point(-1, -1));
        Invalidate();
    }

    void ClearHover()
    {
        SetHover(-1, -1, -1);
    }

    // Отладка: имитация клика по модели (та же логика попадания,
    // что и у настоящей мыши) — для прогонов со снимками
    public bool DebugPaintAt(Point pt)
    {
        if (Canvas == null) return false;
        int fi, tx, ty;
        if (!Hit(pt, out fi, out tx, out ty)) return false;
        Canvas.PushUndo();
        Canvas.Dot(tx, ty);
        Canvas.Commit();
        SetHover(fi, tx, ty);
        return true;
    }
}
