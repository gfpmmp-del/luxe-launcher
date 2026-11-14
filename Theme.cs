// ============================================================
//  Luxe Launcher - тема, векторные иконки и рисованные «модельки»
//
//  Отдельный файл, чтобы Launcher.cs не распухал. Здесь только
//  внешний вид: ни логики, ни сети, ни файлов.
//
//  Зачем: раньше иконки были юникод-глифами (⌂ ⚙ ◈ ✉ ☰) - они
//  рисуются системным шрифтом, разного веса, с разной базовой
//  линией и выглядят как попало. Теперь всё рисуется вектором на
//  сетке 24x24: одинаковый вес линии, скруглённые концы, ровная
//  геометрия на любом DPI.
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

enum Ico
{
    None, Home, Box, Globe, News, List, Puzzle, User, Gear, Play, Pause,
    Search, Refresh, Download, Trash, Close, Min, Max, Restore, Check,
    ChevDown, ChevRight, ChevLeft, Camera, Folder, Sparkle, Star, Zap,
    Power, Info, Warning, Clock, Palette, Layers, Rocket, Grid, Eye, Plus, Stop,
    Bell, Sun, Moon, Users, Server, Cape, Crown, More, Shirt, Fire
}

static class Icons
{
    // ---------- сборка путей ----------

    class PathSet
    {
        public GraphicsPath P = new GraphicsPath();
        public void Add(GraphicsPath p)
        {
            if (p == null) return;
            P.AddPath(p, false);
            p.Dispose();
        }
    }

    // ломаная по точкам (x1,y1,x2,y2,...)
    static GraphicsPath Fig(params float[] xy)
    {
        GraphicsPath p = new GraphicsPath();
        if (xy.Length < 4) return p;
        p.StartFigure();
        for (int i = 0; i + 3 < xy.Length; i += 2)
            p.AddLine(xy[i], xy[i + 1], xy[i + 2], xy[i + 3]);
        return p;
    }

    static GraphicsPath RRect(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        if (w <= 0f || h <= 0f) return p;
        float d = r * 2f;
        if (d > w) d = w;
        if (d > h) d = h;
        if (d < 0.6f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
        p.AddArc(x, y, d, d, 180f, 90f);
        p.AddArc(x + w - d, y, d, d, 270f, 90f);
        p.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
        p.AddArc(x, y + h - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    static GraphicsPath Circ(float cx, float cy, float r)
    {
        GraphicsPath p = new GraphicsPath();
        if (r <= 0f) return p;
        p.AddEllipse(cx - r, cy - r, r * 2f, r * 2f);
        return p;
    }

    // ---------- рисование ----------

    public static void Draw(Graphics g, Ico ic, Rectangle box, Color col)
    {
        Draw(g, ic, box, col, 1.7f);
    }

    // Иконка рисуется на сетке 24x24 и масштабируется в box.
    // Толщина линии задаётся в экранных пикселях и не зависит от размера.
    public static void Draw(Graphics g, Ico ic, Rectangle box, Color col, float stroke)
    {
        if (ic == Ico.None || box.Width <= 0 || box.Height <= 0) return;
        float k = Math.Min(box.Width, box.Height) / 24f;
        if (k <= 0.01f) return;

        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // короткая палитра фигур: контуры и заливки
        PathSet ln = new PathSet();
        PathSet fl = new PathSet();

        float cx = 12f, cy = 12f;

        switch (ic)
        {
            case Ico.Home:
                ln.Add(Fig(3.1f, 11.4f, 12f, 3.5f, 20.9f, 11.4f));
                ln.Add(Fig(5.6f, 9.6f, 5.6f, 20.3f, 18.4f, 20.3f, 18.4f, 9.6f));
                ln.Add(Fig(9.9f, 20.3f, 9.9f, 14.6f, 14.1f, 14.6f, 14.1f, 20.3f));
                break;

            case Ico.Box:
                ln.Add(Fig(3.7f, 8.0f, 12f, 3.9f, 20.3f, 8.0f, 20.3f, 16.0f, 12f, 20.1f, 3.7f, 16.0f, 3.7f, 8.0f));
                ln.Add(Fig(3.7f, 8.0f, 12f, 12.1f, 20.3f, 8.0f));
                ln.Add(Fig(12f, 12.1f, 12f, 20.1f));
                break;

            case Ico.Globe:
                ln.Add(Circ(cx, cy, 8.6f));
                {
                    GraphicsPath mer = new GraphicsPath();
                    mer.AddEllipse(cx - 3.8f, cy - 8.6f, 7.6f, 17.2f);
                    ln.Add(mer);
                }
                ln.Add(Fig(3.4f, 12f, 20.6f, 12f));
                break;

            case Ico.News:
                ln.Add(RRect(3.4f, 4.8f, 17.2f, 14.6f, 3.2f));
                ln.Add(Fig(6.8f, 9.0f, 17.2f, 9.0f));
                ln.Add(Fig(6.8f, 12.6f, 17.2f, 12.6f));
                ln.Add(Fig(6.8f, 16.0f, 13.2f, 16.0f));
                break;

            case Ico.List:
                ln.Add(Fig(9.4f, 6.6f, 20f, 6.6f));
                ln.Add(Fig(9.4f, 12f, 20f, 12f));
                ln.Add(Fig(9.4f, 17.4f, 20f, 17.4f));
                fl.Add(Circ(5.2f, 6.6f, 1.35f));
                fl.Add(Circ(5.2f, 12f, 1.35f));
                fl.Add(Circ(5.2f, 17.4f, 1.35f));
                break;

            case Ico.Puzzle:
                {
                    GraphicsPath pp = new GraphicsPath();
                    pp.AddLine(3.9f, 7.4f, 8.7f, 7.4f);
                    pp.AddBezier(8.7f, 7.4f, 8.9f, 3.5f, 15.1f, 3.5f, 15.3f, 7.4f);
                    pp.AddLine(15.3f, 7.4f, 20.1f, 7.4f);
                    pp.AddLine(20.1f, 7.4f, 20.1f, 19.9f);
                    pp.AddLine(20.1f, 19.9f, 3.9f, 19.9f);
                    pp.CloseFigure();
                    ln.Add(pp);
                }
                break;

            case Ico.User:
                ln.Add(Circ(12f, 7.6f, 3.5f));
                {
                    GraphicsPath sh = new GraphicsPath();
                    sh.AddArc(4.4f, 13.4f, 15.2f, 15.2f, 180f, 180f);
                    ln.Add(sh);
                }
                break;

            case Ico.Gear:
                ln.Add(Circ(cx, cy, 7.2f));
                ln.Add(Circ(cx, cy, 3.1f));
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0;
                    float x1 = cx + (float)Math.Cos(a) * 7.2f;
                    float y1 = cy + (float)Math.Sin(a) * 7.2f;
                    float x2 = cx + (float)Math.Cos(a) * 10.4f;
                    float y2 = cy + (float)Math.Sin(a) * 10.4f;
                    ln.Add(Fig(x1, y1, x2, y2));
                }
                break;

            case Ico.Play:
                {
                    GraphicsPath tri = new GraphicsPath();
                    tri.AddPolygon(new PointF[] {
                        new PointF(8.4f, 5.2f), new PointF(18.8f, 12f), new PointF(8.4f, 18.8f) });
                    tri.CloseFigure();
                    fl.Add(tri);
                }
                break;

            case Ico.Pause:
                fl.Add(RRect(8.0f, 5.4f, 2.9f, 13.2f, 1.4f));
                fl.Add(RRect(13.1f, 5.4f, 2.9f, 13.2f, 1.4f));
                break;

            case Ico.Stop:
                fl.Add(RRect(6.4f, 6.4f, 11.2f, 11.2f, 2.6f));
                break;

            case Ico.Search:
                ln.Add(Circ(10.4f, 10.4f, 6.2f));
                ln.Add(Fig(15.1f, 15.1f, 20.3f, 20.3f));
                break;

            case Ico.Refresh:
                {
                    GraphicsPath a = new GraphicsPath();
                    a.AddArc(3.8f, 3.8f, 16.4f, 16.4f, -50f, 300f);
                    ln.Add(a);
                    GraphicsPath ar = new GraphicsPath();
                    ar.AddPolygon(new PointF[] {
                        new PointF(11.9f, 3.4f), new PointF(8.2f, 3.0f), new PointF(9.9f, 6.5f) });
                    ar.CloseFigure();
                    fl.Add(ar);
                }
                break;

            case Ico.Download:
                ln.Add(Fig(12f, 3.6f, 12f, 14.6f));
                ln.Add(Fig(7.5f, 10.2f, 12f, 14.8f, 16.5f, 10.2f));
                ln.Add(Fig(4.4f, 19.4f, 19.6f, 19.4f));
                break;

            case Ico.Trash:
                ln.Add(Fig(3.8f, 6.5f, 20.2f, 6.5f));
                ln.Add(Fig(9.3f, 6.5f, 9.3f, 3.9f, 14.7f, 3.9f, 14.7f, 6.5f));
                ln.Add(Fig(5.7f, 6.5f, 6.8f, 19.9f, 17.2f, 19.9f, 18.3f, 6.5f));
                ln.Add(Fig(10.2f, 10.2f, 10.6f, 16.6f));
                ln.Add(Fig(13.8f, 10.2f, 13.4f, 16.6f));
                break;

            case Ico.Close:
                ln.Add(Fig(6.5f, 6.5f, 17.5f, 17.5f));
                ln.Add(Fig(17.5f, 6.5f, 6.5f, 17.5f));
                break;

            case Ico.Min:
                ln.Add(Fig(6.2f, 12f, 17.8f, 12f));
                break;

            case Ico.Max:
                ln.Add(RRect(6.0f, 6.0f, 12.0f, 12.0f, 2.4f));
                break;

            case Ico.Restore:
                ln.Add(RRect(3.6f, 8.4f, 12.0f, 12.0f, 2.4f));
                ln.Add(Fig(8.0f, 5.2f, 20.4f, 5.2f, 20.4f, 17.4f));
                break;

            case Ico.Check:
                ln.Add(Fig(5.6f, 12.5f, 10.0f, 16.9f, 18.4f, 7.4f));
                break;

            case Ico.ChevDown:
                ln.Add(Fig(6.4f, 9.6f, 12f, 15.2f, 17.6f, 9.6f));
                break;

            case Ico.ChevRight:
                ln.Add(Fig(9.6f, 6.4f, 15.2f, 12f, 9.6f, 17.6f));
                break;

            case Ico.ChevLeft:
                ln.Add(Fig(14.4f, 6.4f, 8.8f, 12f, 14.4f, 17.6f));
                break;

            case Ico.Camera:
                ln.Add(RRect(3.3f, 7.4f, 17.4f, 12.4f, 3.2f));
                ln.Add(Fig(8.6f, 7.4f, 9.9f, 4.6f, 14.1f, 4.6f, 15.4f, 7.4f));
                ln.Add(Circ(12f, 13.6f, 3.1f));
                break;

            case Ico.Folder:
                ln.Add(Fig(3.6f, 8.2f, 3.6f, 6.0f, 9.4f, 6.0f, 11.7f, 8.2f));
                ln.Add(RRect(3.6f, 8.2f, 16.8f, 11.4f, 2.8f));
                break;

            case Ico.Sparkle:
                {
                    GraphicsPath s = new GraphicsPath();
                    s.AddPolygon(new PointF[] {
                        new PointF(12f, 2.6f), new PointF(13.9f, 10.1f), new PointF(21.4f, 12f),
                        new PointF(13.9f, 13.9f), new PointF(12f, 21.4f), new PointF(10.1f, 13.9f),
                        new PointF(2.6f, 12f), new PointF(10.1f, 10.1f) });
                    s.CloseFigure();
                    fl.Add(s);
                }
                break;

            case Ico.Star:
                {
                    GraphicsPath s = new GraphicsPath();
                    s.AddPolygon(StarPts(12f, 12.6f, 9.2f, 4.2f, 5));
                    s.CloseFigure();
                    ln.Add(s);
                }
                break;

            case Ico.Fire:
                {
                    // язык пламени: заливка с вырезом-«каплей» внутри.
                    // FillPath идёт в режиме Alternate, поэтому вторая
                    // замкнутая фигура внутри первой становится дыркой.
                    GraphicsPath s = new GraphicsPath();
                    // внешний силуэт
                    s.AddBezier(12f, 2.2f, 13.4f, 6.2f, 17.9f, 8.8f, 17.9f, 13.7f);
                    s.AddBezier(17.9f, 13.7f, 17.9f, 18.3f, 15.3f, 21.1f, 12f, 21.1f);
                    s.AddBezier(12f, 21.1f, 8.7f, 21.1f, 6.1f, 18.3f, 6.1f, 13.7f);
                    s.AddBezier(6.1f, 13.7f, 6.1f, 9.4f, 9.8f, 6.6f, 12f, 2.2f);
                    s.CloseFigure();
                    // внутренний вырез — перевёрнутая капля
                    s.AddBezier(12f, 10.6f, 13.5f, 12.9f, 15.1f, 13.8f, 15.1f, 16.0f);
                    s.AddBezier(15.1f, 16.0f, 15.1f, 18.2f, 13.7f, 19.4f, 12f, 19.4f);
                    s.AddBezier(12f, 19.4f, 10.3f, 19.4f, 8.9f, 18.2f, 8.9f, 16.0f);
                    s.AddBezier(8.9f, 16.0f, 8.9f, 13.6f, 10.7f, 12.6f, 12f, 10.6f);
                    s.CloseFigure();
                    fl.Add(s);
                }
                break;

            case Ico.Zap:
                {
                    GraphicsPath s = new GraphicsPath();
                    s.AddPolygon(new PointF[] {
                        new PointF(13.4f, 2.8f), new PointF(6.4f, 13.4f), new PointF(11.3f, 13.4f),
                        new PointF(10.6f, 21.2f), new PointF(17.6f, 10.6f), new PointF(12.7f, 10.6f) });
                    s.CloseFigure();
                    fl.Add(s);
                }
                break;

            case Ico.Power:
                {
                    GraphicsPath a = new GraphicsPath();
                    a.AddArc(4.6f, 4.8f, 14.8f, 14.8f, -55f, 290f);
                    ln.Add(a);
                    ln.Add(Fig(12f, 3.0f, 12f, 11.4f));
                }
                break;

            case Ico.Info:
                ln.Add(Circ(cx, cy, 8.6f));
                ln.Add(Fig(12f, 10.8f, 12f, 16.6f));
                fl.Add(Circ(12f, 7.6f, 1.15f));
                break;

            case Ico.Warning:
                ln.Add(Fig(12f, 3.6f, 21.2f, 19.8f, 2.8f, 19.8f, 12f, 3.6f));
                ln.Add(Fig(12f, 9.6f, 12f, 14.4f));
                fl.Add(Circ(12f, 17.0f, 1.1f));
                break;

            case Ico.Clock:
                ln.Add(Circ(cx, cy, 8.6f));
                ln.Add(Fig(12f, 6.8f, 12f, 12.4f, 16.2f, 14.4f));
                break;

            case Ico.Palette:
                ln.Add(Circ(cx, cy, 8.6f));
                fl.Add(Circ(8.6f, 9.2f, 1.3f));
                fl.Add(Circ(15.4f, 9.2f, 1.3f));
                fl.Add(Circ(9.6f, 15.6f, 1.3f));
                break;

            case Ico.Layers:
                ln.Add(Fig(3.8f, 12f, 12f, 7.6f, 20.2f, 12f));
                ln.Add(Fig(3.8f, 16.4f, 12f, 20.8f, 20.2f, 16.4f));
                break;

            case Ico.Rocket:
                ln.Add(Fig(12f, 3.4f, 17.4f, 9.4f, 15.4f, 17.6f, 8.6f, 17.6f, 6.6f, 9.4f, 12f, 3.4f));
                ln.Add(Circ(12f, 9.6f, 2.1f));
                ln.Add(Fig(9.4f, 17.6f, 7.2f, 20.8f));
                ln.Add(Fig(14.6f, 17.6f, 16.8f, 20.8f));
                break;

            case Ico.Grid:
                ln.Add(RRect(3.8f, 3.8f, 7.0f, 7.0f, 2.2f));
                ln.Add(RRect(13.2f, 3.8f, 7.0f, 7.0f, 2.2f));
                ln.Add(RRect(3.8f, 13.2f, 7.0f, 7.0f, 2.2f));
                ln.Add(RRect(13.2f, 13.2f, 7.0f, 7.0f, 2.2f));
                break;

            case Ico.Eye:
                ln.Add(Circ(cx, cy, 8.6f));
                ln.Add(Circ(cx, cy, 3.4f));
                break;

            case Ico.Plus:
                ln.Add(Fig(12f, 5.4f, 12f, 18.6f));
                ln.Add(Fig(5.4f, 12f, 18.6f, 12f));
                break;

            case Ico.Bell:
                ln.Add(Fig(6.0f, 17.0f, 18.0f, 17.0f));
                {
                    GraphicsPath b = new GraphicsPath();
                    b.AddBezier(6.4f, 16.8f, 7.2f, 13.6f, 7.2f, 11.0f, 7.2f, 8.4f);
                    b.AddBezier(7.2f, 8.4f, 7.2f, 5.0f, 16.8f, 5.0f, 16.8f, 8.4f);
                    b.AddBezier(16.8f, 8.4f, 16.8f, 11.0f, 16.8f, 13.6f, 17.6f, 16.8f);
                    ln.Add(b);
                }
                {
                    GraphicsPath cl = new GraphicsPath();
                    cl.AddArc(10.0f, 16.4f, 4.0f, 4.0f, 0f, 180f);
                    ln.Add(cl);
                }
                ln.Add(Fig(12f, 3.6f, 12f, 5.2f));
                break;

            case Ico.Sun:
                ln.Add(Circ(cx, cy, 4.6f));
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0;
                    ln.Add(Fig(cx + (float)Math.Cos(a) * 7.2f, cy + (float)Math.Sin(a) * 7.2f,
                               cx + (float)Math.Cos(a) * 9.8f, cy + (float)Math.Sin(a) * 9.8f));
                }
                break;

            case Ico.Moon:
                {
                    GraphicsPath m = new GraphicsPath();
                    m.AddBezier(15.8f, 3.6f, 8.2f, 4.8f, 6.0f, 12.4f, 9.6f, 17.2f);
                    m.AddBezier(9.6f, 17.2f, 4.6f, 16.6f, 3.4f, 9.0f, 8.6f, 5.0f);
                    m.AddBezier(8.6f, 5.0f, 10.8f, 3.4f, 13.4f, 3.2f, 15.8f, 3.6f);
                    m.CloseFigure();
                    ln.Add(m);
                }
                break;

            case Ico.Users:
                ln.Add(Circ(9.2f, 8.4f, 3.2f));
                {
                    GraphicsPath sh = new GraphicsPath();
                    sh.AddArc(2.6f, 13.6f, 13.2f, 13.2f, 180f, 180f);
                    ln.Add(sh);
                }
                ln.Add(Circ(17.0f, 9.4f, 2.5f));
                {
                    GraphicsPath sh2 = new GraphicsPath();
                    sh2.AddArc(12.8f, 14.2f, 9.4f, 9.4f, 200f, 140f);
                    ln.Add(sh2);
                }
                break;

            case Ico.Server:
                ln.Add(RRect(3.4f, 4.2f, 17.2f, 6.4f, 2.2f));
                ln.Add(RRect(3.4f, 13.4f, 17.2f, 6.4f, 2.2f));
                fl.Add(Circ(6.6f, 7.4f, 1.0f));
                fl.Add(Circ(6.6f, 16.6f, 1.0f));
                ln.Add(Fig(11.0f, 7.4f, 17.6f, 7.4f));
                ln.Add(Fig(11.0f, 16.6f, 17.6f, 16.6f));
                break;

            case Ico.Cape:
                {
                    GraphicsPath c = new GraphicsPath();
                    c.AddLine(8.4f, 3.8f, 15.6f, 3.8f);
                    c.AddLine(15.6f, 3.8f, 18.6f, 8.2f);
                    c.AddLine(18.6f, 8.2f, 17.4f, 20.2f);
                    c.AddLine(17.4f, 20.2f, 12f, 18.2f);
                    c.AddLine(12f, 18.2f, 6.6f, 20.2f);
                    c.AddLine(6.6f, 20.2f, 5.4f, 8.2f);
                    c.AddLine(5.4f, 8.2f, 8.4f, 3.8f);
                    c.CloseFigure();
                    ln.Add(c);
                }
                ln.Add(Fig(12f, 9.4f, 12f, 14.4f));
                break;

            case Ico.Crown:
                {
                    GraphicsPath cr = new GraphicsPath();
                    cr.AddLine(12f, 4f, 14.5f, 11f);
                    cr.AddLine(14.5f, 11f, 18f, 9f);
                    cr.AddLine(18f, 9f, 15.5f, 15f);
                    cr.AddLine(15.5f, 15f, 17f, 20f);
                    cr.AddLine(17f, 20f, 12f, 16f);
                    cr.AddLine(12f, 16f, 7f, 20f);
                    cr.AddLine(7f, 20f, 8.5f, 15f);
                    cr.AddLine(8.5f, 15f, 6f, 9f);
                    cr.AddLine(6f, 9f, 9.5f, 11f);
                    cr.AddLine(9.5f, 11f, 12f, 4f);
                    cr.CloseFigure();
                    fl.Add(cr);
                    // жемчужина в центре
                    fl.Add(Circ(12f, 12f, 1.6f));
                }
                break;

            case Ico.More:
                fl.Add(Circ(6.0f, 12f, 1.5f));
                fl.Add(Circ(12f, 12f, 1.5f));
                fl.Add(Circ(18f, 12f, 1.5f));
                break;

            case Ico.Shirt:
                {
                    GraphicsPath s = new GraphicsPath();
                    s.AddLine(8.6f, 3.8f, 15.4f, 3.8f);
                    s.AddLine(15.4f, 3.8f, 20.6f, 7.0f);
                    s.AddLine(20.6f, 7.0f, 18.4f, 11.4f);
                    s.AddLine(18.4f, 11.4f, 16.4f, 10.4f);
                    s.AddLine(16.4f, 10.4f, 16.4f, 20.2f);
                    s.AddLine(16.4f, 20.2f, 7.6f, 20.2f);
                    s.AddLine(7.6f, 20.2f, 7.6f, 10.4f);
                    s.AddLine(7.6f, 10.4f, 5.6f, 11.4f);
                    s.AddLine(5.6f, 11.4f, 3.4f, 7.0f);
                    s.AddLine(3.4f, 7.0f, 8.6f, 3.8f);
                    s.CloseFigure();
                    ln.Add(s);
                }
                break;
        }

        // масштаб и отрисовка
        g.TranslateTransform(box.X + (box.Width - 24f * k) / 2f, box.Y + (box.Height - 24f * k) / 2f);
        g.ScaleTransform(k, k);

        using (Pen p = new Pen(col, stroke / k))
        {
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            if (ln.P.PointCount > 0) g.DrawPath(p, ln.P);
        }
        if (fl.P.PointCount > 0)
        {
            using (SolidBrush b = new SolidBrush(col))
                g.FillPath(b, fl.P);
        }

        g.Restore(st);
        ln.P.Dispose();
        fl.P.Dispose();
    }

    static PointF[] StarPts(float cx, float cy, float ro, float ri, int n)
    {
        List<PointF> l = new List<PointF>();
        for (int i = 0; i < n * 2; i++)
        {
            double a = -Math.PI / 2.0 + i * Math.PI / n;
            float r = (i % 2 == 0) ? ro : ri;
            l.Add(new PointF(cx + (float)Math.Cos(a) * r, cy + (float)Math.Sin(a) * r));
        }
        return l.ToArray();
    }
}

// ============================================================
//  Дополнения к Ui: скругления, тени, свечения
// ============================================================
static partial class Ui
{
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        if (r.Width <= 0f || r.Height <= 0f) return p;
        float d = radius * 2f;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        if (d < 1f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180f, 90f);
        p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    // Мягкая тень: несколько контуров с затуханием, а не один жирный.
    public static void SoftShadow2(Graphics g, Rectangle r, int radius, int alpha, int spread, int dy)
    {
        for (int i = spread; i >= 1; i--)
        {
            int a = alpha * (spread - i + 1) / (spread * spread + spread);
            if (a < 1) continue;
            using (GraphicsPath p = RoundRect(new RectangleF(r.X - i, r.Y - i + dy,
                       r.Width + i * 2, r.Height + i * 2), radius + i))
            {
                using (Pen pen = new Pen(Color.FromArgb(a, 0, 0, 0), 2f))
                    g.DrawPath(pen, p);
            }
        }
    }

    // Радиальное свечение цветом (мягкий blob без видимых полос)
    public static void Glow2(Graphics g, RectangleF r, Color c, int alphaCenter)
    {
        if (r.Width <= 1f || r.Height <= 1f) return;
        using (GraphicsPath gp = new GraphicsPath())
        {
            gp.AddEllipse(r);
            using (PathGradientBrush pg = new PathGradientBrush(gp))
            {
                pg.CenterPoint = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                pg.CenterColor = Color.FromArgb(alphaCenter, c);
                pg.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                g.FillEllipse(pg, r);
            }
        }
    }

    // Точечная сетка-фактура
    public static void Dots(Graphics g, Rectangle r, int step, Color c)
    {
        using (SolidBrush b = new SolidBrush(c))
        {
            for (int y = r.Y + step / 2; y < r.Bottom; y += step)
                for (int x = r.X + step / 2; x < r.Right; x += step)
                    g.FillRectangle(b, x, y, 1.6f, 1.6f);
        }
    }

    // Тонкая рамка-градиент (сверху светлее) — «стекло»
    public static void GlassBorder(Graphics g, RectangleF r, float radius, int alphaTop, int alphaBottom)
    {
        using (GraphicsPath p = RoundRect(r, (int)radius))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                Color.FromArgb(alphaTop, 255, 255, 255),
                Color.FromArgb(alphaBottom, 255, 255, 255), 90f))
            using (Pen pen = new Pen(b, 1f))
                g.DrawPath(pen, p);
        }
    }

    // Горизонтальный «разделитель» с затуханием к краям
    public static void FadeLine(Graphics g, int x, int y, int w)
    {
        if (w <= 2) return;
        using (LinearGradientBrush b = new LinearGradientBrush(
            new Rectangle(x, y, w, 1), Color.FromArgb(0, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255), 0f))
        {
            ColorBlend cb = new ColorBlend(3);
            cb.Colors = new Color[] {
                Color.FromArgb(0, 255, 255, 255),
                Color.FromArgb(26, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255) };
            cb.Positions = new float[] { 0f, 0.5f, 1f };
            b.InterpolationColors = cb;
            g.FillRectangle(b, x, y, w, 1);
        }
    }
}

// ============================================================
//  «Модельки»: логотип, аватар, арт карточек, герой-панель
// ============================================================
static class Art
{
    // ---------- кэш тяжёлой графики ----------
    // Пейзаж — это десятки градиентов, эллипсов и фигур на кадр.
    // HeroPanel перерисовывается каждые 50 мс ради парящих блоков,
    // а сайдбар и миниатюры новостей — при каждом ресайзе. Рисуем
    // пейзаж один раз в Bitmap и дальше только блитим.
    // Ключ — размер + hue + seed: у каждой карточки свой пейзаж.
    static readonly Dictionary<string, Bitmap> landCache = new Dictionary<string, Bitmap>();

    public static Bitmap LandscapeCached(int w, int h, float hue, int seed)
    {
        if (w <= 4 || h <= 4) return null;
        string key = w + "x" + h + "|" + (int)hue + "|" + seed;
        Bitmap bmp;
        if (landCache.TryGetValue(key, out bmp) && bmp != null && !bmp.Size.IsEmpty) return bmp;
        try
        {
            bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Landscape(g, new Rectangle(0, 0, w, h), hue, seed);
            }
            // кэш ограничен: при долгой работе с разными размерами
            // окна иначе накапливались бы мегабайты битмапов
            if (landCache.Count > 60) ClearLandscapeCache();
            landCache[key] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    public static void ClearLandscapeCache()
    {
        foreach (KeyValuePair<string, Bitmap> kv in landCache)
        {
            try { if (kv.Value != null) kv.Value.Dispose(); } catch { }
        }
        landCache.Clear();
    }

    // ---------- логотип ----------
    // Фотографический фон лаунчера. Кадры лежат в exe ресурсами
    // (p_banner/p_cover/p_news/p_news2/p_news3/p_side). Раньше здесь был
    // процедурный пейзаж: он давал «мультяшный» вид и одинаковые
    // карточки. Фото уже подготовлены — с брендовым затемнением снизу
    // под текст, поэтому в рантайме только масштабирование и кроп.
    static readonly Dictionary<string, Bitmap> photoCache = new Dictionary<string, Bitmap>();

    // Загрузить фото-ассет по имени (без расширения) из ресурсов exe.
    // Возвращает null, если ресурса нет — тогда вызывающий рисует вектор.
    public static Bitmap Photo(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Bitmap cached;
        if (photoCache.TryGetValue(name, out cached) && cached != null) return cached;
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream s = asm.GetManifestResourceStream(name + ".jpg");
            if (s == null) s = asm.GetManifestResourceStream(name);
            if (s == null) { photoCache[name] = null; return null; }
            using (s)
            {
                Bitmap src = new Bitmap(s);
                // Копия в памяти: Bitmap поверх потока живёт, пока жив
                // поток, а поток мы закрываем.
                Bitmap copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(copy)) g.DrawImageUnscaled(src, 0, 0);
                src.Dispose();
                photoCache[name] = copy;
                return copy;
            }
        }
        catch { photoCache[name] = null; return null; }
    }

    // Нарисовать фото с заполнением прямоугольника (кроп по центру,
    // без искажений) и опциональным затемнением.
    public static bool PhotoFill(Graphics g, Rectangle r, string name, float focusY, int shadeBottom, int shadeTop)
    {
        Bitmap b = Photo(name);
        if (b == null || r.Width <= 2 || r.Height <= 2) return false;
        GraphicsState st = g.Save();
        g.SetClip(r);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float sc = Math.Max((float)r.Width / b.Width, (float)r.Height / b.Height);
        int dw = (int)Math.Ceiling(b.Width * sc), dh = (int)Math.Ceiling(b.Height * sc);
        int dx = r.X + (r.Width - dw) / 2;
        int dy = r.Y + (int)((r.Height - dh) * focusY);
        g.DrawImage(b, new Rectangle(dx, dy, dw, dh));
        if (shadeTop > 0)
        {
            int sh = Math.Max(2, (int)(r.Height * 0.5f));
            using (LinearGradientBrush gb = new LinearGradientBrush(
                new Rectangle(r.X, r.Y, Math.Max(2, r.Width), sh),
                Color.FromArgb(shadeTop, 6, 3, 20), Color.FromArgb(0, 6, 3, 20), 90f))
                g.FillRectangle(gb, r.X, r.Y, r.Width, sh);
        }
        if (shadeBottom > 0)
        {
            int sh = Math.Max(2, (int)(r.Height * 0.72f));
            using (LinearGradientBrush gb = new LinearGradientBrush(
                new Rectangle(r.X, r.Bottom - sh, Math.Max(2, r.Width), sh),
                Color.FromArgb(0, 6, 3, 20), Color.FromArgb(shadeBottom, 6, 3, 20), 90f))
                g.FillRectangle(gb, r.X, r.Bottom - sh, r.Width, sh);
        }
        g.Restore(st);
        return true;
    }

    // ---------- логотип ----------
    // Знак — объёмная изометрическая «L»: не плоский ромб, а настоящая
    // призма с гранями, собственным светом и бликом. Рисуется вектором
    // (резко на любом размере), результат кэшируется по размеру:
    // значок в сайдбаре перерисовывается при каждом наведении на меню.
    static readonly Dictionary<string, Bitmap> logoCache = new Dictionary<string, Bitmap>();

    public static void Logomark(Graphics g, Rectangle r)
    {
        if (r.Width <= 2 || r.Height <= 2) return;
        string key = r.Width + "x" + r.Height;
        Bitmap cached;
        if (logoCache.TryGetValue(key, out cached) && cached != null)
        {
            g.DrawImageUnscaled(cached, r.X, r.Y);
            return;
        }
        try
        {
            Bitmap bmp = new Bitmap(r.Width, r.Height);
            using (Graphics sg = Graphics.FromImage(bmp))
            {
                sg.SmoothingMode = SmoothingMode.AntiAlias;
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawLogo(sg, r.Width, r.Height);
            }
            logoCache[key] = bmp;
            g.DrawImageUnscaled(bmp, r.X, r.Y);
        }
        catch { }
    }

    // Маленький контейнер грани логотипа
    class LogoFace
    {
        public PointF[] P;
        public Color C;
        public float Depth;
    }

    struct V3L
    {
        public float X, Y, Z;
        public V3L(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    // Направление света: сверху-спереди-слева — тот же свет, что и в
    // остальном интерфейсе, поэтому знак не «выпадает» из темы.
    const float LLX = -0.38f, LLY = 0.74f, LLZ = 0.56f;

    static void DrawLogo(Graphics g, float w, float h)
    {
        float S = Math.Min(w, h);
        float ox = (w - S) * 0.5f;
        float oy = (h - S) * 0.5f;
        float rad = S * 0.30f;
        RectangleF rf = new RectangleF(ox, oy, S, S);

        // ---- плитка ----
        using (GraphicsPath tile = Ui.RoundRect(rf, rad))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(ox, oy, S, S),
                Color.FromArgb(44, 30, 104), Color.FromArgb(18, 13, 46), 108f))
                g.FillPath(b, tile);

            // радиальная подсветка внутри плитки: PathGradientBrush по
            // самой плитке — ореол не может вытечь за скругление
            using (PathGradientBrush pg = new PathGradientBrush(tile))
            {
                pg.CenterPoint = new PointF(ox + S * 0.5f, oy + S * 0.34f);
                pg.CenterColor = Color.FromArgb(190, 126, 104, 255);
                pg.SurroundColors = new Color[] { Color.FromArgb(0, 126, 104, 255) };
                g.FillPath(pg, tile);
            }

            // низ плитки темнее — знак «стоит» на подставке
            using (LinearGradientBrush vb = new LinearGradientBrush(
                new RectangleF(ox, oy + S * 0.55f, S, S * 0.45f),
                Color.FromArgb(0, 6, 3, 20), Color.FromArgb(130, 6, 3, 20), 90f))
                g.FillPath(vb, tile);
        }

        // ---- призма «L» ----
        float cyaw = (float)Math.Cos(-30.0 * Math.PI / 180.0);
        float syaw = (float)Math.Sin(-30.0 * Math.PI / 180.0);
        float cp = (float)Math.Cos(16.0 * Math.PI / 180.0);
        float sp = (float)Math.Sin(16.0 * Math.PI / 180.0);

        // вписываем модель (2 x 3 x 1) в плитку
        float scale = S * 0.155f;
        float cx = ox + S * 0.5f;
        float cy = oy + S * 0.53f;

        List<LogoFace> side = new List<LogoFace>();
        V3L center = new V3L(0, 0, 0);

        // контур буквы в плоскости XY (ножка 2x1 + стойка 1x2)
        PointF[] outline = new PointF[] {
            new PointF(0, 0), new PointF(2, 0), new PointF(2, 1),
            new PointF(1, 1), new PointF(1, 3), new PointF(0, 3) };

        Color sideCol = Color.FromArgb(126, 96, 255);

        // боковые грани: по одному кваду на ребро контура
        for (int i = 0; i < outline.Length; i++)
        {
            PointF a = outline[i];
            PointF b = outline[(i + 1) % outline.Length];
            LogoQuad(side, P(a.X, a.Y, 0f), P(b.X, b.Y, 0f), P(b.X, b.Y, 1f), P(a.X, a.Y, 1f),
                sideCol, cx, cy, scale, cyaw, syaw, cp, sp, center);
        }
        // задняя крышка (обычно отсекается, но пусть будет для полноты)
        LogoQuad(side, P(0, 0, 0), P(2, 0, 0), P(2, 1, 0), P(0, 1, 0),
            sideCol, cx, cy, scale, cyaw, syaw, cp, sp, center);
        LogoQuad(side, P(0, 1, 0), P(1, 1, 0), P(1, 3, 0), P(0, 3, 0),
            sideCol, cx, cy, scale, cyaw, syaw, cp, sp, center);

        side.Sort(delegate(LogoFace a, LogoFace b) { return a.Depth.CompareTo(b.Depth); });
        for (int i = 0; i < side.Count; i++)
        {
            using (SolidBrush b = new SolidBrush(side[i].C))
                g.FillPolygon(b, side[i].P);
        }

        // ---- передняя грань: одна деталь с градиентом, без шва ----
        PointF[] front = new PointF[outline.Length];
        for (int i = 0; i < outline.Length; i++)
            front[i] = Project(P(outline[i].X, outline[i].Y, 1f), cx, cy, scale, cyaw, syaw, cp, sp);

        RectangleF fb = Bounds(front);
        using (GraphicsPath fp = new GraphicsPath())
        {
            fp.AddPolygon(front);
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(fb.X, fb.Y, Math.Max(2, fb.Width), Math.Max(2, fb.Height)),
                Color.FromArgb(198, 176, 255), Color.FromArgb(108, 82, 250), 96f))
                g.FillPath(b, fp);

            // внутренняя фаска: тонкий светлый контур по передней грани
            using (Pen pen = new Pen(Color.FromArgb(120, 236, 230, 255),
                Math.Max(0.7f, S * 0.011f)))
                g.DrawPath(pen, fp);
        }

        // ---- блик по верхней кромке: узкая светлая полоса вдоль
        // верхнего ребра стойки, читается как отражение света ----
        using (Pen pen = new Pen(Color.FromArgb(190, 255, 255, 255),
            Math.Max(0.8f, S * 0.016f)))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLine(pen, front[4], front[5]);
            g.DrawLine(pen, front[5], front[0]);
        }

        // ---- искра в верхнем правом углу плитки ----
        float sx = ox + S * 0.775f, sy = oy + S * 0.235f;
        float sr = S * 0.105f;
        using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
        using (GraphicsPath sp2 = new GraphicsPath())
        {
            sp2.AddPolygon(new PointF[] {
                new PointF(sx, sy - sr),
                new PointF(sx + sr * 0.26f, sy - sr * 0.26f),
                new PointF(sx + sr, sy),
                new PointF(sx + sr * 0.26f, sy + sr * 0.26f),
                new PointF(sx, sy + sr),
                new PointF(sx - sr * 0.26f, sy + sr * 0.26f),
                new PointF(sx - sr, sy),
                new PointF(sx - sr * 0.26f, sy - sr * 0.26f) });
            sp2.CloseFigure();
            g.FillPath(b, sp2);
        }

        // ---- рамка плитки ----
        Ui.GlassBorder(g, rf, rad, 96, 14);
    }

    static V3L P(float x, float y, float z)
    {
        // центрируем модель: x 0..2, y 0..3, z 0..1
        return new V3L(x - 1f, y - 1.5f, z - 0.5f);
    }

    // Затемнение/высветление цвета с сохранением альфы (Ui.Sh приватный).
    static Color Shade(Color c, float f)
    {
        int r = (int)(c.R * f); if (r > 255) r = 255; if (r < 0) r = 0;
        int gg = (int)(c.G * f); if (gg > 255) gg = 255; if (gg < 0) gg = 0;
        int b = (int)(c.B * f); if (b > 255) b = 255; if (b < 0) b = 0;
        return Color.FromArgb(c.A, r, gg, b);
    }

    static PointF Project(V3L v, float cx, float cy, float scale,
        float cyaw, float syaw, float cp, float sp)
    {
        float x = v.X * cyaw + v.Z * syaw;
        float z = -v.X * syaw + v.Z * cyaw;
        float y = v.Y * cp - z * sp;
        return new PointF(cx + x * scale, cy - y * scale);
    }

    static RectangleF Bounds(PointF[] pts)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < pts.Length; i++)
        {
            if (pts[i].X < x0) x0 = pts[i].X;
            if (pts[i].Y < y0) y0 = pts[i].Y;
            if (pts[i].X > x1) x1 = pts[i].X;
            if (pts[i].Y > y1) y1 = pts[i].Y;
        }
        return new RectangleF(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    // Квад с честным освещением. Нормаль разворачиваем «наружу» от
    // центра модели — так порядок вершин в контуре может быть любым.
    static void LogoQuad(List<LogoFace> list, V3L a, V3L b, V3L c, V3L d, Color col,
        float cx, float cy, float scale, float cyaw, float syaw, float cp, float sp, V3L center)
    {
        float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        float nx = uy * vz - uz * vy;
        float ny = uz * vx - ux * vz;
        float nz = ux * vy - uy * vx;
        float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len < 0.0001f) return;
        nx /= len; ny /= len; nz /= len;

        float fx = (a.X + b.X + c.X + d.X) * 0.25f - center.X;
        float fy = (a.Y + b.Y + c.Y + d.Y) * 0.25f - center.Y;
        float fz = (a.Z + b.Z + c.Z + d.Z) * 0.25f - center.Z;
        if (nx * fx + ny * fy + nz * fz < 0f) { nx = -nx; ny = -ny; nz = -nz; }

        float vnx = nx * cyaw + nz * syaw;
        float vnz = -nx * syaw + nz * cyaw;
        float vny = ny * cp - vnz * sp;
        float vnz2 = ny * sp + vnz * cp;
        if (vnz2 <= 0.01f) return;               // задняя грань

        float dot = vnx * LLX + vny * LLY + vnz2 * LLZ;
        if (dot < 0f) dot = 0f;
        float br = 0.52f + 0.58f * dot;
        if (br > 1.15f) br = 1.15f;

        LogoFace f = new LogoFace();
        f.C = Shade(col, br);
        f.P = new PointF[4];
        V3L[] pts = new V3L[] { a, b, c, d };
        float dsum = 0f;
        for (int i = 0; i < 4; i++)
        {
            float x = pts[i].X, y = pts[i].Y, z = pts[i].Z;
            float rx = x * cyaw + z * syaw;
            float rz = -x * syaw + z * cyaw;
            float ry = y * cp - rz * sp;
            float rz2 = y * sp + rz * cp;
            f.P[i] = new PointF(cx + rx * scale, cy - ry * scale);
            dsum += rz2;
        }
        f.Depth = dsum * 0.25f;
        list.Add(f);
    }

    // ---------- аватар ----------
    // Круг: градиентное кольцо + тёмный диск + инициалы. Цвет кольца
    // выводится из ника, поэтому у каждого игрока свой.
    public static void Avatar(Graphics g, Rectangle r, string seed, string text)
    {
        if (r.Width <= 4 || r.Height <= 4) return;
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int h = 0;
        if (!string.IsNullOrEmpty(seed)) foreach (char c in seed) h = (h * 31 + c) % 360;
        Color c1 = Ui.Hsb(h, 0.62f, 0.66f);
        Color c2 = Ui.Hsb((h + 46) % 360, 0.72f, 0.46f);

        RectangleF rf = new RectangleF(r.X, r.Y, r.Width, r.Height);
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddEllipse(rf);

            // кольцо
            using (LinearGradientBrush b = new LinearGradientBrush(rf, c1, c2, 120f))
                g.FillPath(b, p);

            // внутренний тёмный диск
            RectangleF inner = new RectangleF(r.X + 2f, r.Y + 2f, r.Width - 4f, r.Height - 4f);
            using (GraphicsPath ip = new GraphicsPath())
            {
                ip.AddEllipse(inner);
                using (LinearGradientBrush b = new LinearGradientBrush(inner,
                    Ui.Lerp(Ui.Card, c2, 0.35f), Ui.Bg, 100f))
                    g.FillPath(b, ip);

                // блик сверху
                using (GraphicsPath hp = new GraphicsPath())
                {
                    hp.AddEllipse(new RectangleF(inner.X, inner.Y, inner.Width, inner.Height * 0.62f));
                    using (LinearGradientBrush hb = new LinearGradientBrush(
                        new RectangleF(inner.X, inner.Y, inner.Width, inner.Height * 0.62f),
                        Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillPath(hb, hp);
                }
            }
        }

        Font f = Ui.Font(Math.Max(7f, r.Height * 0.36f), FontStyle.Bold);
        Color tc = Ui.Lerp(c1, Color.White, 0.55f);
        TextRenderer.DrawText(g, text, f, r, tc,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        g.Restore(st);
    }

    // ---------- арт карточки версии ----------
    // Диагональный градиент + мягкий блоб + сетка + крупная версия.
    public static void VersionArt(Graphics g, Rectangle r, string ver, float hue, string tag)
    {
        if (r.Width <= 4 || r.Height <= 4) return;
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF rf = new RectangleF(r.X, r.Y, r.Width, r.Height);

        Color a = Ui.Hsb(hue, 0.52f, 0.50f);
        Color b = Ui.Hsb((hue + 42f) % 360f, 0.62f, 0.22f);

        using (LinearGradientBrush br = new LinearGradientBrush(rf, a, b, 128f))
            g.FillRectangle(br, r);

        // блоб в правом верхнем углу
        Ui.Glow2(g, new RectangleF(r.Right - r.Height * 1.15f, r.Y - r.Height * 0.45f,
                 r.Height * 1.6f, r.Height * 1.6f), Color.FromArgb(255, 255, 255), 46);

        Ui.Dots(g, r, 22, Color.FromArgb(15, 255, 255, 255));

        // подложка под крупную версию — чтобы текст не терялся
        using (LinearGradientBrush sh = new LinearGradientBrush(
            new RectangleF(r.X, r.Bottom - r.Height * 0.55f, r.Width, r.Height * 0.55f),
            Color.FromArgb(0, 0, 0, 0), Color.FromArgb(120, 0, 0, 0), 90f))
            g.FillRectangle(sh, r.X, r.Bottom - r.Height * 0.55f, r.Width, r.Height * 0.55f);

        if (!string.IsNullOrEmpty(ver))
        {
            float baseSize = Math.Max(13f, r.Height * 0.34f);
            Font f = Ui.Font(baseSize, FontStyle.Bold);
            // длинный номер (1.21.11, 1.20.5) вписываем по ширине карточки,
            // иначе он обрезается краем и версии выглядят одинаково
            float avail = r.Width - 24f;
            Size m0 = TextRenderer.MeasureText(ver, f);
            if (m0.Width > avail && m0.Width > 0)
                f = Ui.Font(Math.Max(9f, baseSize * avail / m0.Width), FontStyle.Bold);
            string s = ver;
            Size sz = TextRenderer.MeasureText(s, f);
            float tx = r.X + 12f;
            float ty = r.Bottom - sz.Height - 8f;
            TextRenderer.DrawText(g, s, f, new Point((int)tx + 1, (int)ty + 1),
                Color.FromArgb(110, 0, 0, 0));
            TextRenderer.DrawText(g, s, f, new Point((int)tx, (int)ty),
                Color.FromArgb(244, 255, 255, 255));
        }

        if (!string.IsNullOrEmpty(tag))
        {
            Font f = Ui.Font(Math.Max(6.5f, r.Height * 0.115f), FontStyle.Bold);
            Size sz = TextRenderer.MeasureText(tag, f);
            Rectangle pill = new Rectangle(r.X + 10, r.Y + 10, sz.Width + 18, sz.Height + 6);
            using (GraphicsPath p = Ui.RoundRect(new RectangleF(pill.X, pill.Y, pill.Width, pill.Height), pill.Height / 2f))
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 8, 10, 16)))
                    g.FillPath(sb, p);
                Ui.GlassBorder(g, new RectangleF(pill.X, pill.Y, pill.Width, pill.Height),
                    pill.Height / 2f, 54, 16);
            }
            TextRenderer.DrawText(g, tag, f, pill, Color.FromArgb(238, 244, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        g.Restore(st);
    }

    // ---------- пейзаж ----------
    // Рисованный «майнкрафт-закат»: небо в три цвета, солнце с гало,
    // облака, дальние горы, замок на горизонте, вода с бликами и
    // тёмные деревья по краям. hue задаёт настроение карточки,
    // поэтому у каждой версии свой пейзаж.
    public static void Landscape(Graphics g, Rectangle r, float hue, int seed)
    {
        Landscape(g, r, hue, seed, false);
    }

    // alwaysCastle: витрина на главной должна быть «полной» — без замка
    // и флага баннер выглядит пустым.
    public static void Landscape(Graphics g, Rectangle r, float hue, int seed, bool alwaysCastle)
    {
        if (r.Width <= 8 || r.Height <= 8) return;
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.SetClip(r);

        // Палитра приведена к бренд-диапазону: раньше hue приходил
        // сырым (сумма кодов символов версии, 0..359) и карточки
        // получались зелёными, красными, жёлтыми — вне темы. Теперь
        // hue сжимается в индиго-фиолетово-синий коридор 232..300.
        hue = 232f + (Math.Abs(hue) % 69f);

        Random rnd = new Random(seed * 7919 + 13);

        // --- композиция зависит от seed: иначе каждая карточка выглядит
        // как копия (солнце всегда на 63%, замок всегда в центре,
        // деревья всегда в трёх одних местах) ---
        bool sunLeft = rnd.Next(3) == 0;
        bool sunRight = !sunLeft && rnd.Next(2) == 0;
        float sunFrac = sunLeft ? (0.22f + (float)rnd.NextDouble() * 0.10f)
                     : sunRight ? (0.74f + (float)rnd.NextDouble() * 0.08f)
                     : (0.55f + (float)rnd.NextDouble() * 0.14f);
        float horizonJit = (float)(rnd.NextDouble() - 0.5) * r.Height * 0.05f;

        // --- небо ---
        // Сверху глубокий сине-фиолетовый, у горизонта тёплый закат.
        // Тёплый низ даёт «свет» и карточки перестают быть плоскими.
        Color skyTop = Ui.Hsb(hue, 0.70f, 0.20f);
        Color skyMid = Ui.Hsb((hue + 26f) % 360f, 0.58f, 0.44f);
        Color skyLow = Ui.Hsb((hue + 60f) % 360f, 0.46f, 0.78f);
        Color skyGlow = Color.FromArgb(255, 255, 196, 138);
        float horizon = r.Y + r.Height * 0.62f + horizonJit;
        RectangleF sky = new RectangleF(r.X, r.Y, r.Width, horizon - r.Y);
        using (LinearGradientBrush b = new LinearGradientBrush(sky, skyTop, skyGlow, 90f))
        {
            ColorBlend cb = new ColorBlend(4);
            cb.Colors = new Color[] { skyTop, skyMid, skyLow, skyGlow };
            cb.Positions = new float[] { 0f, 0.44f, 0.80f, 1f };
            b.InterpolationColors = cb;
            g.FillRectangle(b, sky);
        }

        // --- звёзды сверху ---
        for (int i = 0; i < 18; i++)
        {
            float sx = r.X + (float)rnd.NextDouble() * r.Width;
            float sy = r.Y + (float)rnd.NextDouble() * r.Height * 0.30f;
            int al = 26 + rnd.Next(46);
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(al, 235, 238, 255)))
                g.FillEllipse(sb, sx, sy, 1.4f, 1.4f);
        }

        // --- солнце ---
        float sunX = r.X + r.Width * sunFrac;
        float sunY = horizon - r.Height * 0.055f;
        float sunR = r.Height * 0.20f;
        Ui.Glow2(g, new RectangleF(sunX - sunR, sunY - sunR, sunR * 2f, sunR * 2f),
            Color.FromArgb(255, 232, 176), 190);
        using (SolidBrush sb = new SolidBrush(Color.FromArgb(245, 255, 246, 214)))
            g.FillEllipse(sb, sunX - sunR * 0.32f, sunY - sunR * 0.32f, sunR * 0.64f, sunR * 0.64f);

        // --- облака ---
        for (int i = 0; i < 8; i++)
        {
            float cy = r.Y + r.Height * (0.08f + (float)rnd.NextDouble() * 0.34f);
            float cw = r.Width * (0.16f + (float)rnd.NextDouble() * 0.26f);
            float cx = r.X + (float)rnd.NextDouble() * (r.Width - cw);
            float chh = Math.Max(3f, r.Height * (0.020f + (float)rnd.NextDouble() * 0.026f));
            // у горизонта облака розовее — свет заката
            float t = (cy - r.Y) / Math.Max(1f, horizon - r.Y);
            int rC = (int)(255 - t * 40), gC = (int)(206 + t * 20), bC = (int)(196 + t * 40);
            int al = 34 + rnd.Next(44);
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(al, rC, gC, bC)))
            {
                g.FillEllipse(sb, cx, cy, cw, chh * 2.4f);
                g.FillEllipse(sb, cx + cw * 0.18f, cy - chh * 0.9f, cw * 0.58f, chh * 2.2f);
                g.FillEllipse(sb, cx + cw * 0.52f, cy + chh * 0.2f, cw * 0.46f, chh * 1.8f);
            }
        }

        // --- дальние горы ---
        DrawRidge(g, r, horizon, 0.34f, 0.55f, Ui.Hsb((hue + 14f) % 360f, 0.42f, 0.30f), 190, rnd);
        DrawRidge(g, r, horizon, 0.22f, 0.72f, Ui.Hsb((hue + 8f) % 360f, 0.46f, 0.20f), 220, rnd);

        // --- замок (есть не на всех карточках, и стоит в разных местах) ---
        bool hasCastle = alwaysCastle || rnd.Next(4) != 0;   // ~75% карточек
        if (hasCastle)
            DrawCastle(g, r, horizon, hue, rnd, sunFrac);

        // --- вода ---
        RectangleF water = new RectangleF(r.X, horizon, r.Width, r.Bottom - horizon);
        using (LinearGradientBrush b = new LinearGradientBrush(water,
            Ui.Hsb((hue + 40f) % 360f, 0.62f, 0.52f), Ui.Hsb((hue + 10f) % 360f, 0.62f, 0.13f), 90f))
            g.FillRectangle(b, water);

        // отражение солнца и блики
        for (int i = 0; i < 26; i++)
        {
            float yy = horizon + (float)rnd.NextDouble() * (r.Bottom - horizon);
            float w = r.Width * (0.02f + (float)rnd.NextDouble() * 0.14f);
            float xx = sunX - w * 0.5f + (float)(rnd.NextDouble() - 0.5) * r.Width * 0.22f;
            int al = 18 + rnd.Next(52);
            using (Pen p = new Pen(Color.FromArgb(al, 255, 232, 190), 1.2f))
                g.DrawLine(p, xx, yy, xx + w, yy);
        }

        // --- деревья по краям: число и высота зависят от seed ---
        int treeCount = 2 + rnd.Next(2);        // 2..3
        float[] treeX = new float[] { 0.03f, 0.13f, 0.93f };
        float[] treeH = new float[] { 0.30f, 0.20f, 0.26f };
        for (int i = 0; i < treeCount; i++)
        {
            float tx = r.X + r.Width * treeX[i];
            float ty = horizon + r.Height * (0.02f + 0.06f * i);
            float ts = r.Height * treeH[i] * (0.82f + (float)rnd.NextDouble() * 0.36f);
            DrawTree(g, r, tx, ty, ts, hue, rnd);
        }

        // --- нижняя тень, чтобы текст читался ---
        using (LinearGradientBrush b = new LinearGradientBrush(
            new RectangleF(r.X, r.Bottom - r.Height * 0.46f, r.Width, r.Height * 0.46f),
            Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 6, 8, 16), 90f))
            g.FillRectangle(b, r.X, r.Bottom - r.Height * 0.46f, r.Width, r.Height * 0.46f);

        g.Restore(st);
    }

    static void DrawRidge(Graphics g, Rectangle r, float horizon, float baseH, float rough,
        Color col, int alpha, Random rnd)
    {
        GraphicsPath p = new GraphicsPath();
        p.StartFigure();
        p.AddLine(r.X, horizon, r.X, horizon - r.Height * baseH * 0.45f);
        int steps = 7;
        for (int i = 1; i <= steps; i++)
        {
            float x = r.X + r.Width * i / steps;
            float h = horizon - r.Height * baseH * (0.35f + (float)rnd.NextDouble() * rough);
            p.AddLine(r.X + r.Width * (i - 1) / steps, horizon - r.Height * baseH * 0.45f, x, h);
        }
        p.AddLine(r.X + r.Width, horizon - r.Height * baseH * 0.45f, r.X + r.Width, horizon);
        p.CloseFigure();
        using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, col)))
            g.FillPath(b, p);
        p.Dispose();
    }

    static void DrawCastle(Graphics g, Rectangle r, float horizon, float hue, Random rnd, float sunFrac)
    {
        // Замок стоит на берегу: чуть выше горизонта, светлый силуэт
        // с тёплой подсветкой со стороны солнца. Позиция по seed —
        // со стороны солнца (подсветка граней тогда читается), но не
        // под солнцем: если солнце близко к центру, замок смещается.
        float cx = r.X + r.Width * (sunFrac < 0.45f ? 0.62f + (float)rnd.NextDouble() * 0.08f
                                    : sunFrac > 0.60f ? 0.28f + (float)rnd.NextDouble() * 0.08f
                                    : 0.5f);
        float baseW = r.Width * 0.22f;
        float h = r.Height * 0.20f;
        Color body = Color.FromArgb(235, Ui.Hsb((hue + 12f) % 360f, 0.34f, 0.24f));
        Color lit = Color.FromArgb(245, Ui.Hsb((hue + 46f) % 360f, 0.40f, 0.46f));
        Color roof = Color.FromArgb(240, Ui.Hsb((hue + 24f) % 360f, 0.46f, 0.16f));

        float ground = horizon + r.Height * 0.005f;
        // подсветка грани, обращённой к солнцу: слева/справа
        bool litLeft = sunFrac < cx - r.X;
        using (SolidBrush b = new SolidBrush(body))
        {
            g.FillRectangle(b, cx - baseW * 0.5f, ground - h * 0.52f, baseW, h * 0.52f);
            // стена с зубцами
            for (int i = 0; i < 7; i++)
                g.FillRectangle(b, cx - baseW * 0.5f + baseW * i / 7f, ground - h * 0.62f, baseW / 12f, h * 0.12f);
            // башни
            g.FillRectangle(b, cx - baseW * 0.52f, ground - h, baseW * 0.15f, h);
            g.FillRectangle(b, cx + baseW * 0.36f, ground - h * 0.84f, baseW * 0.14f, h * 0.84f);
            g.FillRectangle(b, cx - baseW * 0.04f, ground - h * 1.22f, baseW * 0.13f, h * 1.22f);
        }
        // подсвеченные солнцем грани: со стороны солнца, а не всегда справа
        using (SolidBrush b = new SolidBrush(lit))
        {
            if (litLeft)
            {
                g.FillRectangle(b, cx - baseW * 0.52f, ground - h * 0.84f, baseW * 0.06f, h * 0.84f);
                g.FillRectangle(b, cx - baseW * 0.07f, ground - h * 1.22f, baseW * 0.03f, h * 1.22f);
                g.FillRectangle(b, cx - baseW * 0.50f, ground - h * 0.52f, baseW * 0.10f, h * 0.52f);
            }
            else
            {
                g.FillRectangle(b, cx + baseW * 0.44f, ground - h * 0.84f, baseW * 0.06f, h * 0.84f);
                g.FillRectangle(b, cx + baseW * 0.06f, ground - h * 1.22f, baseW * 0.03f, h * 1.22f);
                g.FillRectangle(b, cx + baseW * 0.40f, ground - h * 0.52f, baseW * 0.10f, h * 0.52f);
            }
        }
        using (SolidBrush b = new SolidBrush(roof))
        {
            Roof(g, b, cx - baseW * 0.52f, ground - h, baseW * 0.15f, h * 0.36f);
            Roof(g, b, cx + baseW * 0.36f, ground - h * 0.84f, baseW * 0.14f, h * 0.30f);
            Roof(g, b, cx - baseW * 0.04f, ground - h * 1.22f, baseW * 0.13f, h * 0.42f);
        }
        // флаг на главной башне
        using (Pen p = new Pen(Color.FromArgb(210, 236, 200, 255), 1f))
            g.DrawLine(p, cx + baseW * 0.025f, ground - h * 1.56f, cx + baseW * 0.025f, ground - h * 1.22f);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 196, 150, 255)))
            g.FillPolygon(b, new PointF[] {
                new PointF(cx + baseW * 0.025f, ground - h * 1.56f),
                new PointF(cx + baseW * 0.12f, ground - h * 1.49f),
                new PointF(cx + baseW * 0.025f, ground - h * 1.42f) });
    }

    static void Roof(Graphics g, SolidBrush b, float x, float y, float w, float h)
    {
        g.FillPolygon(b, new PointF[] {
            new PointF(x, y), new PointF(x + w * 0.5f, y - h), new PointF(x + w, y) });
    }

    static void DrawTree(Graphics g, Rectangle r, float x, float groundY, float size, float hue, Random rnd)
    {
        Color trunk = Color.FromArgb(230, Ui.Hsb((hue + 10f) % 360f, 0.45f, 0.09f));
        Color leaf = Color.FromArgb(235, Ui.Hsb((hue + 96f) % 360f, 0.48f, 0.13f));
        using (SolidBrush b = new SolidBrush(trunk))
            g.FillRectangle(b, x - size * 0.055f, groundY - size * 0.55f, size * 0.11f, size * 0.55f);
        using (SolidBrush b = new SolidBrush(leaf))
        {
            g.FillEllipse(b, x - size * 0.34f, groundY - size * 0.98f, size * 0.68f, size * 0.52f);
            g.FillEllipse(b, x - size * 0.26f, groundY - size * 1.14f, size * 0.52f, size * 0.44f);
            g.FillEllipse(b, x - size * 0.42f, groundY - size * 0.78f, size * 0.40f, size * 0.34f);
        }
    }

    // Обложка карточки версии: пейзаж + тёмная подложка под текст
    public static void CardCover(Graphics g, Rectangle r, string ver, float hue, string tag)
    {
        if (r.Width <= 4 || r.Height <= 4) return;
        GraphicsState st = g.Save();
        // seed из текста версии, а не из hue: «26.3» и «26.2» дают
        // почти равные hue (295/294), из-за чего соседние обложки были
        // копиями. Хеш строки разводит композиции по-настоящему.
        int seed = 17;
        if (!string.IsNullOrEmpty(ver)) foreach (char c in ver) seed = seed * 31 + c;
        // Обложка версии — фото (два кадра чередуются по seed, чтобы лента
        // версий не выглядела копиями). Векторный пейзаж — запасной вариант.
        string cover = (seed & 1) == 0 ? "p_cover" : "p_cover2";
        bool photo = PhotoFill(g, r, cover, 0.30f + (Math.Abs(seed) % 4) * 0.06f, 170, 0);
        if (!photo) Landscape(g, r, hue, seed);
        g.SetClip(r);

        if (!string.IsNullOrEmpty(ver))
        {
            float baseSize = Math.Max(13f, r.Height * 0.40f);
            Font f = Ui.Font(baseSize, FontStyle.Bold);
            float avail = r.Width - 20f;
            Size m0 = TextRenderer.MeasureText(ver, f);
            if (m0.Width > avail && m0.Width > 0)
                f = Ui.Font(Math.Max(9f, baseSize * avail / m0.Width), FontStyle.Bold);
            Size sz = TextRenderer.MeasureText(ver, f);
            int tx = r.X + 10;
            int ty = r.Bottom - sz.Height - 5;
            TextRenderer.DrawText(g, ver, f, new Point(tx + 1, ty + 1), Color.FromArgb(120, 0, 0, 0));
            TextRenderer.DrawText(g, ver, f, new Point(tx, ty), Color.FromArgb(248, 255, 255, 255));
        }

        if (!string.IsNullOrEmpty(tag))
        {
            Font f = Ui.Font(7f, FontStyle.Bold);
            Size sz = TextRenderer.MeasureText(tag, f);
            Rectangle pill = new Rectangle(r.X + 8, r.Y + 8, sz.Width + 14, sz.Height + 4);
            using (GraphicsPath p = Ui.RoundRect(new RectangleF(pill.X, pill.Y, pill.Width, pill.Height), pill.Height / 2f))
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(165, 6, 8, 16)))
                    g.FillPath(sb, p);
            }
            TextRenderer.DrawText(g, tag, f, pill, Color.FromArgb(232, 238, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        g.Restore(st);
    }

    // Фон сайдбара: пейзаж живёт в нижней полосе (между меню и футером),
    // сверху он растворяется в цвете панели — как в референсе.
    static readonly Dictionary<string, Bitmap> sideCache = new Dictionary<string, Bitmap>();

    public static void SideArt(Graphics g, Rectangle r)
    {
        if (r.Width <= 8 || r.Height <= 8) return;
        // Панель сайдбара перерисовывается при каждом ресайзе и при
        // перерисовке пунктов меню. Кэшируем готовый фон по размеру.
        string key = r.Width + "x" + r.Height;
        Bitmap cached;
        if (sideCache.TryGetValue(key, out cached) && cached != null)
        {
            g.DrawImageUnscaled(cached, r.X, r.Y);
            return;
        }
        try
        {
            using (Bitmap bmp = new Bitmap(r.Width, r.Height))
            {
                using (Graphics sg = Graphics.FromImage(bmp))
                    PaintSide(sg, new Rectangle(0, 0, r.Width, r.Height));
                if (sideCache.Count > 6)
                {
                    foreach (KeyValuePair<string, Bitmap> kv in sideCache)
                    { try { if (kv.Value != null) kv.Value.Dispose(); } catch { } }
                    sideCache.Clear();
                }
                sideCache[key] = new Bitmap(bmp);
                g.DrawImageUnscaled(sideCache[key], r.X, r.Y);
            }
        }
        catch
        {
            // кэш не сложился — рисуем напрямую, фон не пропадает
            PaintSide(g, r);
        }
    }

    static void PaintSide(Graphics g, Rectangle r)
    {
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.SetClip(r);

        using (SolidBrush b = new SolidBrush(Ui.Side))
            g.FillRectangle(b, r);

        // Полоса арта: от низа меню до низа панели. Сверху она
        // растворяется в цвете сайдбара, снизу остаётся видимой —
        // кнопки футера ложатся на неё тёмными плашками, как в
        // референсе.
        int artTop = 556;
        if (artTop > r.Bottom - 120) artTop = r.Bottom - 120;
        Rectangle art = new Rectangle(r.X - 1, artTop, r.Width + 2, r.Bottom - artTop + 1);
        if (art.Height > 12)
        {
            GraphicsState st2 = g.Save();
            g.SetClip(art);
            // Сайдбар: ночное небо с Млечным Путём (CC BY 2.0).
            // Затемнение сверху — чтобы полоса растворялась в панели.
            if (!PhotoFill(g, art, "p_side", 0.35f, 0, 190))
                Landscape(g, art, 268f, 5);
            g.Restore(st2);
        }

        // Арт сильно притушен: он служит фоном, а не картинкой. Без
        // этого деревья и замок читаются как тёмные кляксы и спорят
        // с кнопками «Настройки»/«Аккаунты».
        using (LinearGradientBrush fog = new LinearGradientBrush(
            art, Color.FromArgb(196, Ui.Side), Color.FromArgb(172, Ui.Side), 90f))
        {
            fog.WrapMode = WrapMode.TileFlipX;
            g.FillRectangle(fog, art);
        }

        // Верх полосы плавно уходит в цвет панели. Градиент растянут на
        // всю панель: так у него нет собственной границы, от которой
        // GDI+ оставлял бы светлые штрихи по краям.
        float artTopF = (float)artTop / Math.Max(1, r.Height);
        using (LinearGradientBrush b = new LinearGradientBrush(
            new Rectangle(r.X, r.Y, Math.Max(2, r.Width), Math.Max(2, r.Height)),
            Ui.Side, Ui.Side, 90f))
        {
            b.WrapMode = WrapMode.TileFlipX;
            ColorBlend cb = new ColorBlend(5);
            cb.Colors = new Color[] {
                Ui.Side,
                Ui.Side,
                Color.FromArgb(186, Ui.Side),
                Color.FromArgb(72, Ui.Side),
                Color.FromArgb(24, Ui.Side) };
            float p0 = Math.Max(0f, artTopF - 0.02f);
            cb.Positions = new float[] {
                0f,
                p0,
                Math.Min(1f, p0 + 0.06f),
                Math.Min(1f, p0 + 0.16f),
                1f };
            b.InterpolationColors = cb;
            g.FillRectangle(b, r);
        }
        // и лёгкое затемнение у самого низа — под подписями футера
        using (LinearGradientBrush b = new LinearGradientBrush(
            new Rectangle(r.X, r.Bottom - 96, Math.Max(2, r.Width), 96),
            Color.FromArgb(0, 8, 10, 20), Color.FromArgb(186, 8, 10, 20), 90f))
        {
            b.WrapMode = WrapMode.TileFlipX;
            g.FillRectangle(b, r.X, r.Bottom - 96, r.Width, 96);
        }

        g.Restore(st);
    }

    // ---------- корона (карточка «Премиум») ----------
    // Полупрозрачная векторная корона: три зубца, обод, камешки.
    // Все размеры берём от прямоугольника панели.
    public static void Crown(Graphics g, Rectangle r, Color col)
    {
        if (r.Width < 10 || r.Height < 10) return;
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        float top = y + h * 0.16f;
        float mid = y + h * 0.50f;
        float baseY = y + h * 0.72f;

        Color fill = Color.FromArgb(col.A, 246, 200, 120);
        Color edge = Color.FromArgb(Math.Min(255, col.A + 60), 255, 238, 186);

        PointF[] body = new PointF[] {
            new PointF(x + w * 0.10f, baseY),
            new PointF(x + w * 0.10f, top + h * 0.12f),
            new PointF(x + w * 0.31f, mid),
            new PointF(x + w * 0.50f, top),
            new PointF(x + w * 0.69f, mid),
            new PointF(x + w * 0.90f, top + h * 0.12f),
            new PointF(x + w * 0.90f, baseY)
        };
        using (SolidBrush b = new SolidBrush(fill))
            g.FillPolygon(b, body);
        using (Pen pen = new Pen(edge, 1.6f))
            g.DrawPolygon(pen, body);

        // обод короны
        using (SolidBrush b = new SolidBrush(edge))
            g.FillRectangle(b, x + w * 0.08f, baseY, w * 0.84f, h * 0.13f);

        // камешки на зубцах
        using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(255, col.A + 80), 255, 252, 232)))
        {
            g.FillEllipse(b, x + w * 0.46f, top + h * 0.04f, w * 0.08f, h * 0.08f);
            g.FillEllipse(b, x + w * 0.27f, mid + h * 0.02f, w * 0.07f, h * 0.07f);
            g.FillEllipse(b, x + w * 0.66f, mid + h * 0.02f, w * 0.07f, h * 0.07f);
        }

        g.Restore(st);
    }

    // ---------- плитка мода ----------
    // Градиент по «семени» + диагональный блик + инициалы.
    public static void ModTile(Graphics g, Rectangle r, string seed, string text)
    {
        if (r.Width <= 3 || r.Height <= 3) return;
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // оттенок берётся из бренд-коридора (см. Ui.BrandHue): раньше
        // хеш названия давал 175/59/347 — зелёные, жёлтые и красные
        // плитки в тёмно-индиго списке модов
        float h = Ui.BrandHue(seed);
        Color c1 = Ui.Hsb(h, 0.55f, 0.58f);
        // второй стоп держим в коридоре: +30 от 294 уходило в розовый
        Color c2 = Ui.Hsb(Math.Min(305f, h + 30f), 0.62f, 0.26f);

        RectangleF rf = new RectangleF(r.X, r.Y, r.Width, r.Height);
        float rad = r.Width * 0.28f;

        using (GraphicsPath p = Ui.RoundRect(rf, rad))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(rf, c1, c2, 124f))
                g.FillPath(b, p);

            // диагональный блик
            using (SolidBrush hi = new SolidBrush(Color.FromArgb(38, 255, 255, 255)))
            {
                GraphicsPath hp = new GraphicsPath();
                hp.AddPolygon(new PointF[] {
                    new PointF(r.X, r.Y + r.Height),
                    new PointF(r.X + r.Width * 0.75f, r.Y),
                    new PointF(r.X + r.Width * 1.2f, r.Y),
                    new PointF(r.X, r.Y + r.Height * 1.6f) });
                hp.CloseFigure();
                RectangleF old = g.ClipBounds;
                g.SetClip(p);
                g.FillPath(hi, hp);
                g.SetClip(old);
                hp.Dispose();
            }

            Ui.Dots(g, new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), 9,
                Color.FromArgb(22, 255, 255, 255));
            Ui.GlassBorder(g, rf, rad, 66, 8);
        }

        Font f = Ui.Font(Math.Max(9f, r.Height * 0.40f), FontStyle.Bold);
        Size sz = TextRenderer.MeasureText(text, f);
        Point tp = new Point(r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2);
        TextRenderer.DrawText(g, text, f, new Point(tp.X + 1, tp.Y + 1), Color.FromArgb(90, 0, 0, 0));
        TextRenderer.DrawText(g, text, f, tp, Color.FromArgb(246, 255, 255, 255));

        g.Restore(st);
    }

    // ---------- герой ----------
    // Игрок спиной к нам на фоне заката: блочная фигура в цвете
    // (рубашка, штаны, кожа), тёплая кромка от солнца, рядом волк.
    public static void PlayerSilhouette(Graphics g, Rectangle r, float phase)
    {
        if (r.Width <= 10 || r.Height <= 10) return;
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float u = r.Height / 100f;          // единица сетки
        float bob = (float)Math.Sin(phase * 1.1f) * u * 1.1f;
        float cx = r.X + r.Width * 0.5f;
        float baseY = r.Bottom + bob;

        Color skin = Color.FromArgb(255, 202, 158, 118);
        Color hair = Color.FromArgb(255, 74, 46, 32);
        Color shirt = Color.FromArgb(255, 46, 154, 148);
        Color shirtD = Color.FromArgb(255, 30, 116, 114);
        Color pants = Color.FromArgb(255, 58, 74, 158);
        Color pantsD = Color.FromArgb(255, 40, 52, 118);
        Color shoe = Color.FromArgb(255, 46, 44, 48);
        Color rim = Color.FromArgb(190, 255, 214, 168);

        // ноги
        using (SolidBrush b = new SolidBrush(pants))
        {
            g.FillRectangle(b, cx - 15 * u, baseY - 26 * u, 13 * u, 26 * u);
            g.FillRectangle(b, cx + 2 * u, baseY - 26 * u, 13 * u, 26 * u);
        }
        using (SolidBrush b = new SolidBrush(pantsD))
        {
            g.FillRectangle(b, cx - 15 * u, baseY - 26 * u, 3 * u, 26 * u);
            g.FillRectangle(b, cx + 2 * u, baseY - 26 * u, 3 * u, 26 * u);
        }
        using (SolidBrush b = new SolidBrush(shoe))
        {
            g.FillRectangle(b, cx - 16 * u, baseY - 5 * u, 15 * u, 5 * u);
            g.FillRectangle(b, cx + 1 * u, baseY - 5 * u, 15 * u, 5 * u);
        }

        // корпус (рубашка) и тень на левой грани
        using (SolidBrush b = new SolidBrush(shirt))
            g.FillRectangle(b, cx - 17 * u, baseY - 62 * u, 34 * u, 38 * u);
        using (SolidBrush b = new SolidBrush(shirtD))
            g.FillRectangle(b, cx - 17 * u, baseY - 62 * u, 5 * u, 38 * u);
        // ворот
        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 226, 188, 146)))
            g.FillRectangle(b, cx - 6 * u, baseY - 63 * u, 12 * u, 4 * u);

        // руки: сверху рукав, ниже кожа
        using (SolidBrush b = new SolidBrush(shirt))
        {
            g.FillRectangle(b, cx - 26 * u, baseY - 61 * u, 9 * u, 16 * u);
            g.FillRectangle(b, cx + 17 * u, baseY - 61 * u, 9 * u, 16 * u);
        }
        using (SolidBrush b = new SolidBrush(skin))
        {
            g.FillRectangle(b, cx - 26 * u, baseY - 45 * u, 9 * u, 19 * u);
            g.FillRectangle(b, cx + 17 * u, baseY - 45 * u, 9 * u, 19 * u);
        }

        // голова: волосы сверху и по бокам, шея-кожа
        using (SolidBrush b = new SolidBrush(skin))
            g.FillRectangle(b, cx - 4 * u, baseY - 66 * u, 8 * u, 5 * u);
        using (SolidBrush b = new SolidBrush(skin))
            g.FillRectangle(b, cx - 12 * u, baseY - 84 * u, 24 * u, 20 * u);
        using (SolidBrush b = new SolidBrush(hair))
        {
            g.FillRectangle(b, cx - 13 * u, baseY - 87 * u, 26 * u, 9 * u);
            g.FillRectangle(b, cx - 13 * u, baseY - 84 * u, 3 * u, 14 * u);
            g.FillRectangle(b, cx + 10 * u, baseY - 84 * u, 3 * u, 14 * u);
        }

        // тёплая кромка справа (солнце за спиной)
        using (Pen p = new Pen(rim, Math.Max(1f, 1.5f * u)))
        {
            g.DrawLine(p, cx + 12 * u, baseY - 87 * u, cx + 12 * u, baseY - 64 * u);
            g.DrawLine(p, cx + 17 * u, baseY - 62 * u, cx + 17 * u, baseY - 24 * u);
            g.DrawLine(p, cx + 26 * u, baseY - 61 * u, cx + 26 * u, baseY - 26 * u);
            g.DrawLine(p, cx + 15 * u, baseY - 26 * u, cx + 15 * u, baseY - 5 * u);
        }

        // волк рядом, чуть позади и левее — тёмный силуэт с кромкой
        Color wolf = Color.FromArgb(255, 58, 60, 74);
        using (SolidBrush b = new SolidBrush(wolf))
        {
            g.FillRectangle(b, cx - 62 * u, baseY - 16 * u, 26 * u, 11 * u);
            g.FillRectangle(b, cx - 40 * u, baseY - 21 * u, 10 * u, 10 * u);
            g.FillRectangle(b, cx - 59 * u, baseY - 6 * u, 5 * u, 6 * u);
            g.FillRectangle(b, cx - 45 * u, baseY - 6 * u, 5 * u, 6 * u);
            g.FillPolygon(b, new PointF[] {
                new PointF(cx - 41 * u, baseY - 21 * u),
                new PointF(cx - 38 * u, baseY - 27 * u),
                new PointF(cx - 35 * u, baseY - 21 * u) });
            // хвост
            g.FillRectangle(b, cx - 66 * u, baseY - 20 * u, 6 * u, 8 * u);
        }
        using (Pen p = new Pen(Color.FromArgb(150, 255, 208, 160), Math.Max(1f, 1.2f * u)))
        {
            g.DrawLine(p, cx - 36 * u, baseY - 21 * u, cx - 36 * u, baseY - 15 * u);
            g.DrawLine(p, cx - 36 * u, baseY - 16 * u, cx - 42 * u, baseY - 16 * u);
        }

        // мягкая тень под ногами
        using (SolidBrush b = new SolidBrush(Color.FromArgb(78, 0, 0, 0)))
            g.FillEllipse(b, cx - 70 * u, baseY - 3 * u, 150 * u, 7 * u);

        g.Restore(st);
    }

    // Абстрактная композиция: мягкие пятна акцента, дуги, парящие
    // плитки. phase сдвигает всё медленно — панель «дышит».
    // Изометрический блок: три грани, светлая кромка и блик.
    // Маленькая векторная «моделька» для баннера главного экрана.
    static void IsoCube(Graphics g, float cx, float cy, float s, float h,
        Color topA, Color topB, Color lfA, Color lfB, Color rtA, Color rtB)
    {
        float w = s, dh = s * 0.5f;
        PointF N = new PointF(cx, cy - dh), E = new PointF(cx + w, cy);
        PointF S = new PointF(cx, cy + dh), W = new PointF(cx - w, cy);
        PointF SL = new PointF(S.X, S.Y + h), WL = new PointF(W.X, W.Y + h), EL = new PointF(E.X, E.Y + h);

        // верхняя грань
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new PointF[] { N, E, S, W });
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(cx - w, cy - dh, w * 2f, dh * 2f), topA, topB, 66f))
                g.FillPath(b, p);
        }
        // левая грань
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new PointF[] { W, S, SL, WL });
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(W.X, W.Y, w, h + dh), lfA, lfB, 90f))
                g.FillPath(b, p);
        }
        // правая грань
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new PointF[] { S, E, EL, SL });
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(S.X, S.Y, w, h + dh), rtA, rtB, 90f))
                g.FillPath(b, p);
        }
        // блик на верхней грани
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new PointF[] { N, E, S, W });
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(cx - w, cy - dh, w * 2f, dh * 2f),
                Color.FromArgb(96, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 42f))
                g.FillPath(b, p);
        }
        // светлая кромка
        using (Pen pen = new Pen(Color.FromArgb(112, 233, 238, 255), 1.15f))
        {
            g.DrawLine(pen, N, W);
            g.DrawLine(pen, N, E);
            g.DrawLine(pen, W, WL);
            g.DrawLine(pen, S, SL);
            g.DrawLine(pen, E, EL);
        }
    }

    public static void HeroArt(Graphics g, Rectangle r, float phase)
    {
        if (r.Width <= 40 || r.Height <= 20) return;
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // пятна
        Ui.Glow2(g, new RectangleF(r.Right - r.Height * 1.4f, r.Y - r.Height * 0.5f,
                 r.Height * 1.8f, r.Height * 1.8f), Ui.Accent1, 54);
        Ui.Glow2(g, new RectangleF(r.Right - r.Height * 2.3f, r.Bottom - r.Height * 0.9f,
                 r.Height * 1.5f, r.Height * 1.5f), Ui.Accent2, 40);

        // концентрические дуги
        float ccx = r.Right - r.Height * 0.62f;
        float ccy = r.Y + r.Height * 0.5f;
        for (int i = 0; i < 5; i++)
        {
            float rr = r.Height * (0.34f + i * 0.17f);
            int al = 30 - i * 5;
            if (al <= 0) break;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddArc(ccx - rr, ccy - rr, rr * 2f, rr * 2f,
                    200f + phase * 8f, 120f);
                using (Pen pen = new Pen(Color.FromArgb(al, 190, 210, 255), 1.2f))
                    g.DrawPath(pen, p);
            }
        }

        // парящие изометрические блоки — вместо абстрактных «пустых» плиток
        float H = r.Height;
        float bob = (float)Math.Sin(phase * 2.2f) * H * 0.030f;
        float cs = H * 0.205f;                 // половина ширины ромба
        float ch = cs * 1.34f;                 // высота боковой грани
        float cx = r.Right - H * 0.90f;
        float cy = r.Y + H * 0.435f + bob;

        // мягкая тень под большим блоком
        using (SolidBrush sb = new SolidBrush(Color.FromArgb(52, 0, 0, 0)))
            g.FillEllipse(sb, cx - cs * 0.92f, cy + ch + cs * 0.40f, cs * 1.84f, cs * 0.44f);

        IsoCube(g, cx, cy, cs, ch,
            Color.FromArgb(236, 158, 176, 255), Color.FromArgb(198, 98, 120, 255),
            Color.FromArgb(228, 74, 88, 212), Color.FromArgb(200, 54, 64, 176),
            Color.FromArgb(216, 46, 54, 152), Color.FromArgb(190, 30, 36, 120));

        // два малых блока рядом, качаются вразнобой
        float b1 = (float)Math.Sin(phase * 2.0f + 1.4f) * H * 0.042f;
        IsoCube(g, r.Right - H * 1.26f, r.Y + H * 0.695f + b1, H * 0.102f, H * 0.136f,
            Color.FromArgb(198, 196, 212, 255), Color.FromArgb(156, 122, 136, 238),
            Color.FromArgb(176, 100, 110, 214), Color.FromArgb(146, 70, 80, 184),
            Color.FromArgb(164, 62, 70, 164), Color.FromArgb(136, 42, 48, 134));

        float b2 = (float)Math.Sin(phase * 1.7f + 3.1f) * H * 0.048f;
        IsoCube(g, r.Right - H * 0.44f, r.Y + H * 0.755f + b2, H * 0.081f, H * 0.108f,
            Color.FromArgb(190, 226, 200, 255), Color.FromArgb(140, 156, 130, 235),
            Color.FromArgb(168, 128, 168, 206), Color.FromArgb(138, 92, 128, 176),
            Color.FromArgb(152, 74, 116, 152), Color.FromArgb(124, 50, 84, 122));

        // искры
        for (int i = 0; i < 8; i++)
        {
            float t = phase * 0.85f + i * 0.83f;
            float fx = r.Right - H * (0.30f + 0.92f * (((i * 37) % 100) / 100f));
            float fy = r.Y + H * (0.10f + 0.78f * (float)((Math.Sin(t) + 1) * 0.5));
            int al = (int)(32 + 46 * ((Math.Sin(t * 1.6f) + 1) * 0.5));
            float sz = 1.5f + (i % 3) * 0.9f;
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(al, 214, 224, 255)))
                g.FillEllipse(sb, fx, fy, sz, sz);
        }

        // мелкая сетка под всем этим
        Ui.Dots(g, r, 26, Color.FromArgb(12, 255, 255, 255));
        g.Restore(st);
    }
}

// ============================================================
//  Мелкие контролы поверх темы
// ============================================================

// Одна векторная иконка как контрол (строка поиска, шапки карточек)
class IconView : Control
{
    public Ico Icon = Ico.None;
    public Color Col = Ui.Gray;
    public float Stroke = 1.7f;

    public IconView()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Size = new Size(16, 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Icons.Draw(e.Graphics, Icon, ClientRectangle, Col, Stroke);
    }
}

// Верхняя «витрина» главного экрана: пейзаж во весь баннер, слева
// затемнение под заголовок, кнопку игры и выбор сборки.
class HeroPanel : RoundedPanel
{
    float phase = 0f;
    public bool Animate = true;
    // Пейзаж статичен: держим его в кэше и перерисовываем только при
    // смене размера. Раньше он рисовался каждый кадр анимации (50 мс) —
    // десятки градиентов и фигур на 20 fps впустую.
    Bitmap skyCache;
    int skyW = -1, skyH = -1;

    public HeroPanel()
    {
        Radius = 16;
        Gradient = false;
        BackColor = Color.FromArgb(12, 14, 26);
        Glass = false;
        Deco = PaintDeco;
        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        t.Interval = 50;
        t.Tick += delegate
        {
            if (IsDisposed || !IsHandleCreated || !Animate || !Visible) return;
            if (Anim.Scrolling) return;
            phase += 0.018f;
            Invalidate();
        };
        t.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && skyCache != null) { skyCache.Dispose(); skyCache = null; }
        base.Dispose(disposing);
    }

    void PaintDeco(Graphics g, Rectangle r)
    {
        // --- статичный слой: пейзаж + затемнение слева ---
        if (skyCache == null || skyW != r.Width || skyH != r.Height)
        {
            if (skyCache != null) { skyCache.Dispose(); skyCache = null; }
            skyW = r.Width; skyH = r.Height;
            try
            {
                skyCache = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
                using (Graphics sg = Graphics.FromImage(skyCache))
                {
                    sg.SmoothingMode = SmoothingMode.AntiAlias;
                    // Витрина: реальное фото гор (CC BY 2.0, Jeff P),
                    // подготовленное с брендовым затемнением снизу. Если
                    // ресурса нет — падаем на векторный пейзаж, чтобы
                    // билд без ассетов не остался с пустым баннером.
                    if (!Art.PhotoFill(sg, new Rectangle(0, 0, r.Width, r.Height), "p_banner", 0.30f, 150, 0))
                        Art.Landscape(sg, new Rectangle(0, 0, r.Width, r.Height), 268f, 5, true);

                    // Затемнение слева: на нём живут заголовок и кнопки.
                    // Градиент тянем на всю ширину баннера и гасим альфу
                    // ровно к 0.80 — иначе на границе кисти GDI+
                    // подхватывает соседний тайл и оставляет вертикальный шов.
                    using (LinearGradientBrush b = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(2, r.Width), Math.Max(2, r.Height)),
                        Color.FromArgb(232, 9, 11, 22), Color.FromArgb(0, 9, 11, 22), 0f))
                    {
                        b.WrapMode = WrapMode.TileFlipX;
                        // последняя позиция обязана быть 1.0 (требование GDI+),
                        // поэтому прозрачность держим на нуле до самого конца
                        ColorBlend cb = new ColorBlend(5);
                        cb.Colors = new Color[] {
                            Color.FromArgb(242, 9, 11, 22),
                            Color.FromArgb(206, 9, 11, 22),
                            Color.FromArgb(96, 9, 11, 22),
                            Color.FromArgb(0, 9, 11, 22),
                            Color.FromArgb(0, 9, 11, 22) };
                        cb.Positions = new float[] { 0f, 0.40f, 0.66f, 0.80f, 1f };
                        b.InterpolationColors = cb;
                        sg.FillRectangle(b, 0, 0, r.Width, r.Height);
                    }
                }
            }
            catch { skyCache = null; }
        }
        if (skyCache != null) g.DrawImageUnscaled(skyCache, r.X, r.Y);

        // --- живой слой: игрок в позе «сидит на камне» ---
        // Раньше здесь стоял нарисованный «Стив» из прямоугольников.
        // Теперь — настоящий скин игрока (PlayerSkin), натянутый на
        // объёмную модель по игровой UV-развёртке: видно скин, позу и
        // медленное дыхание. Каждый кадр рисуется только фигура —
        // пейзаж лежит в кэше выше.
        int figH = (int)(r.Height * 0.90f);
        int figW = (int)(figH * 1.05f);
        SitFigure.Paint(g, new Rectangle(
            r.X + (int)(r.Width * 0.655f), r.Y + (int)(r.Height * 0.09f), figW, figH), phase);
    }
}

