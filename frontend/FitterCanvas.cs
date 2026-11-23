using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static FitLab.Theme;

namespace FitLab;

/// <summary>
/// Maniquí con volumen (luces y sombras) al que se le "ponen" las prendas: cada foto se recorta sin fondo
/// y se coloca sobre su zona del cuerpo, en capas. A los costados hay etiquetas por categoría para elegir
/// o quitar prendas. Todo se dibuja en un espacio de diseño de DW x DH que se escala al ancho disponible.
/// </summary>
public class FitterCanvas : PaintedControl, IMeasure
{
    public Func<Category, Prenda?> Get = _ => null;
    public event Action<Category>? SlotClicked;
    public event Action<Category>? SlotCleared;

    Category? hover;
    bool hoverClose;
    Category kbSlot = Category.Superior; // categoría activa al usar el teclado

    const float DW = 420, DH = 580, CX = DW / 2;

    enum Rest { Top, Bottom }

    /// <summary>
    /// Caja del cuerpo donde entra cada prenda, desde dónde se apoya y cuánto se permite estirarla a lo largo.
    /// Stretch 1 = la foto nunca se deforma. Los pantalones fotografiados planos son más anchos que una pierna,
    /// así que se permite afinarlos hasta 1,4 veces para que lleguen a los tobillos (un short sigue quedando corto).
    /// </summary>
    static readonly Dictionary<Category, (RectangleF Box, Rest Side, float Stretch)> Fit = new()
    {
        [Category.Superior] = (new RectangleF(130, 104, 160, 186), Rest.Top, 1f),
        [Category.Abrigo] = (new RectangleF(120, 100, 180, 214), Rest.Top, 1f),
        [Category.Inferior] = (new RectangleF(135, 236, 150, 288), Rest.Top, 1.4f),
        [Category.Calzado] = (new RectangleF(150, 494, 120, 62), Rest.Bottom, 1f),
        [Category.Accesorio] = (new RectangleF(270, 270, 74, 86), Rest.Top, 1f),
    };

    /// <summary>Etiqueta de cada categoría a los costados y el punto del cuerpo al que apunta.</summary>
    static readonly Dictionary<Category, (RectangleF Pill, PointF Target)> Tags = new()
    {
        [Category.Superior] = (new RectangleF(4, 150, 106, 40), new PointF(172, 170)),
        [Category.Inferior] = (new RectangleF(4, 370, 106, 40), new PointF(192, 390)),
        [Category.Calzado] = (new RectangleF(4, 506, 106, 40), new PointF(186, 528)),
        [Category.Abrigo] = (new RectangleF(310, 126, 106, 40), new PointF(256, 146)),
        [Category.Accesorio] = (new RectangleF(310, 396, 106, 40), new PointF(306, 330)),
    };

    /// <summary>Orden de las capas, de abajo hacia arriba: la camisa va sobre el pantalón y el abrigo sobre la camisa.</summary>
    static readonly Category[] Layers = { Category.Inferior, Category.Calzado, Category.Superior, Category.Abrigo, Category.Accesorio };

    // Colores del maniquí (tipo maniquí de vidriera, mate y claro).
    static readonly Color SkinEdge = Color.FromArgb(196, 190, 183);
    static readonly Color SkinMid = Color.FromArgb(226, 222, 216);
    static readonly Color SkinLight = Color.FromArgb(247, 245, 241);

    static string Short(Category c) => c switch
    {
        Category.Superior => "Superior",
        Category.Inferior => "Inferior",
        _ => Cats.Label(c),
    };

    public FitterCanvas()
    {
        Cursor = Cursors.Hand;
        MakeFocusable(AccessibleRole.Grouping, "Maniquí: flechas para moverte entre las prendas, Enter para elegir una y Suprimir para quitarla.", keyActivates: false);
        Describe();
    }

    // ---------- Geometría ----------

    float K(int width) => Math.Min(width / DW, Df(1.25f));

    public int MeasureHeight(int width) => (int)(DH * K(width)) + D(8);

    (float k, float ox, float oy) Frame()
    {
        float k = K(Width);
        return (k, (Width - DW * k) / 2, D(4));
    }

    RectangleF Map(RectangleF r)
    {
        var (k, ox, oy) = Frame();
        return new RectangleF(ox + r.X * k, oy + r.Y * k, r.Width * k, r.Height * k);
    }

    PointF Map(PointF p)
    {
        var (k, ox, oy) = Frame();
        return new PointF(ox + p.X * k, oy + p.Y * k);
    }

    /// <summary>Rectángulo en pantalla donde se dibuja la prenda de una categoría.</summary>
    RectangleF Place(Category c, Image img)
    {
        var (box, rest, stretch) = Fit[c];
        float s = Math.Min(box.Width / img.Width, box.Height / img.Height);
        float w = img.Width * s, h = img.Height * s;
        float tall = img.Width * box.Height / img.Height; // ancho que tendría si ocupara todo el alto
        if (stretch > 1 && tall > box.Width && tall / stretch <= box.Width) (w, h) = (box.Width, box.Height);
        else if (stretch > 1 && tall <= box.Width) (w, h) = (tall, box.Height);
        float x = box.X + (box.Width - w) / 2;
        float y = rest == Rest.Top ? box.Y : box.Bottom - h;
        return Map(new RectangleF(x, y, w, h));
    }

    RectangleF CloseRect(RectangleF pill)
    {
        float s = Math.Min(Df(22), pill.Height - Df(10));
        return new RectangleF(pill.Right - s - Df(6), pill.Y + (pill.Height - s) / 2, s, s);
    }

    /// <summary>Qué hay bajo el puntero: primero las etiquetas, después las prendas puestas (la de más arriba), después las zonas vacías.</summary>
    (Category? cat, bool close) HitTest(Point p)
    {
        foreach (var kv in Tags)
        {
            var r = Map(kv.Value.Pill);
            if (r.Contains(p)) return (kv.Key, Get(kv.Key) != null && CloseRect(r).Contains(p));
        }
        for (int i = Layers.Length - 1; i >= 0; i--)
        {
            var c = Layers[i];
            var img = Photos.GetCutout(Get(c)?.FotoPath);
            if (img == null) continue;
            var r = Place(c, img);
            if (!r.Contains(p)) continue;
            int ix = Math.Clamp((int)((p.X - r.X) / r.Width * img.Width), 0, img.Width - 1);
            int iy = Math.Clamp((int)((p.Y - r.Y) / r.Height * img.Height), 0, img.Height - 1);
            if (img.GetPixel(ix, iy).A > 40) return (c, false);
        }
        foreach (var c in new[] { Category.Accesorio, Category.Calzado, Category.Superior, Category.Inferior })
            if (Get(c) == null && Map(Fit[c].Box).Contains(p)) return (c, false);
        return (null, false);
    }

    // ---------- Mouse y teclado ----------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (c, close) = HitTest(e.Location);
        if (c != hover || close != hoverClose) { hover = c; hoverClose = close; Invalidate(); }
        Cursor = c.HasValue ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var (c, close) = HitTest(e.Location);
        if (!c.HasValue) return;
        kbSlot = c.Value;
        if (e.Button == MouseButtons.Right || close) { if (Get(c.Value) != null) SlotCleared?.Invoke(c.Value); }
        else if (e.Button == MouseButtons.Left) SlotClicked?.Invoke(c.Value);
    }

    void Describe()
    {
        var p = Get(kbSlot);
        AccessibleDescription = Cats.Label(kbSlot) + ": " + (p != null ? p.Nombre : "vacía");
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnGotFocus(EventArgs e) { Describe(); base.OnGotFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int i = Array.IndexOf(Cats.All, kbSlot), n = Cats.All.Length;
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up: kbSlot = Cats.All[(i + n - 1) % n]; break;
            case Keys.Right or Keys.Down: kbSlot = Cats.All[(i + 1) % n]; break;
            case Keys.Enter or Keys.Space: SlotClicked?.Invoke(kbSlot); break;
            case Keys.Delete or Keys.Back: if (Get(kbSlot) != null) SlotCleared?.Invoke(kbSlot); break;
            default: return;
        }
        e.Handled = true;
        Describe();
        Invalidate();
    }

    // ---------- Dibujo ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Behind);
        HQ(g);
        DrawBackdrop(g);
        DrawMannequin(g);

        var (k, _, _) = Frame();
        foreach (var c in Layers)
        {
            var img = Photos.GetCutout(Get(c)?.FotoPath);
            if (img != null) DrawGarment(g, img, Place(c, img), k);
        }

        // Zona vacía resaltada al pasar el mouse (o con el teclado) por su etiqueta.
        var active = hover ?? (FocusCue ? kbSlot : (Category?)null);
        if (active is Category a && Get(a) == null)
            DrawRound(g, Color.FromArgb(150, Primary), Map(Fit[a].Box), Df(18), Df(1.5f), DashStyle.Dash);

        foreach (var c in Cats.All) DrawTag(g, c);
    }

    /// <summary>Fondo tipo estudio de fotos: degradé suave y un piso apenas marcado.</summary>
    void DrawBackdrop(Graphics g)
    {
        var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Round(card, Df(18)))
        {
            using (var br = new LinearGradientBrush(card, Surface, Color.FromArgb(240, 237, 248), 90f)) g.FillPath(br, path);
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            var floorTop = Map(new PointF(0, 470)).Y;
            var floor = new RectangleF(card.X, floorTop, card.Width, card.Bottom - floorTop);
            using (var fb = new LinearGradientBrush(new RectangleF(floor.X, floor.Y - 1, floor.Width, floor.Height + 2), Color.FromArgb(0, 120, 110, 150), Color.FromArgb(22, 120, 110, 150), 90f))
                g.FillRectangle(fb, floor);
            g.Restore(state);
        }
        DrawRound(g, Border, card, Df(18), Df(1));
    }

    void DrawMannequin(Graphics g)
    {
        var (k, ox, oy) = Frame();
        PointF P(float x, float y) => new(ox + x * k, oy + y * k);
        PointF M(float x, float y) => P(DW - x, y); // lado derecho, en espejo

        // Sombra en el piso.
        using (var sp = new GraphicsPath())
        {
            sp.AddEllipse(ox + 120 * k, oy + 524 * k, 180 * k, 30 * k);
            using var pgb = new PathGradientBrush(sp) { CenterColor = Color.FromArgb(70, 50, 40, 70), SurroundColors = new[] { Color.FromArgb(0, 50, 40, 70) } };
            g.FillPath(pgb, sp);
        }

        // Piernas y pies (detrás del torso).
        foreach (var S in new Func<float, float, PointF>[] { P, M })
        {
            Limb(g, S(189, 272), S(191, 398), 46 * k, 33 * k);
            Limb(g, S(191, 398), S(193, 512), 33 * k, 20 * k);
            using var foot = new GraphicsPath();
            var f1 = S(176, 508); var f2 = S(208, 508);
            foot.AddEllipse(Math.Min(f1.X, f2.X), f1.Y, 32 * k, 24 * k);
            FillShape(g, foot);
        }

        // Brazos.
        foreach (var S in new Func<float, float, PointF>[] { P, M })
        {
            Limb(g, S(157, 126), S(148, 210), 25 * k, 19 * k);
            Limb(g, S(148, 210), S(141, 290), 19 * k, 15 * k);
            Limb(g, S(141, 290), S(139, 312), 16 * k, 12 * k); // mano
        }

        // Cuello.
        Limb(g, P(CX, 82), P(CX, 114), 22 * k, 27 * k);

        // Torso: hombros redondeados, cintura marcada y cadera.
        using (var t = new GraphicsPath())
        {
            t.AddBezier(P(178, 108), P(166, 111), P(150, 114), P(152, 130));
            t.AddBezier(P(152, 130), P(154, 165), P(162, 205), P(173, 238));
            t.AddBezier(P(173, 238), P(166, 252), P(160, 266), P(163, 288));
            t.AddLine(P(163, 288), M(163, 288));
            t.AddBezier(M(163, 288), M(160, 266), M(166, 252), M(173, 238));
            t.AddBezier(M(173, 238), M(162, 205), M(154, 165), M(152, 130));
            t.AddBezier(M(152, 130), M(150, 114), M(166, 111), M(178, 108));
            t.CloseFigure();
            FillShape(g, t);
            using var pen = new Pen(Color.FromArgb(28, 60, 50, 40), Df(1));
            g.DrawPath(pen, t);
        }

        // Cabeza con brillo arriba a la izquierda.
        using (var head = new GraphicsPath())
        {
            var hr = new RectangleF(ox + 185 * k, oy + 26 * k, 50 * k, 62 * k);
            head.AddEllipse(hr);
            using var hb = new PathGradientBrush(head)
            {
                CenterPoint = new PointF(hr.X + hr.Width * 0.38f, hr.Y + hr.Height * 0.3f),
                CenterColor = SkinLight,
                SurroundColors = new[] { SkinEdge },
                FocusScales = new PointF(0.25f, 0.25f),
            };
            g.FillPath(hb, head);
        }
    }

    /// <summary>Pincel con degradé horizontal (oscuro-claro-oscuro) que da volumen de cilindro a cada parte del cuerpo.</summary>
    static LinearGradientBrush BodyBrush(RectangleF bounds)
    {
        var r = RectangleF.Inflate(bounds, 1, 1);
        var br = new LinearGradientBrush(r, SkinEdge, SkinEdge, 0f);
        br.InterpolationColors = new ColorBlend
        {
            Colors = new[] { SkinEdge, SkinMid, SkinLight, SkinMid, SkinEdge },
            Positions = new[] { 0f, 0.22f, 0.42f, 0.72f, 1f },
        };
        return br;
    }

    static void FillShape(Graphics g, GraphicsPath path)
    {
        using var br = BodyBrush(path.GetBounds());
        g.FillPath(br, path);
    }

    /// <summary>Parte del cuerpo en forma de cápsula que se afina de a (ancho wa) hasta b (ancho wb).</summary>
    static void Limb(Graphics g, PointF a, PointF b, float wa, float wb)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Max(0.01f, (float)Math.Sqrt(dx * dx + dy * dy));
        float nx = -dy / len, ny = dx / len;
        var poly = new[]
        {
            new PointF(a.X + nx * wa / 2, a.Y + ny * wa / 2), new PointF(b.X + nx * wb / 2, b.Y + ny * wb / 2),
            new PointF(b.X - nx * wb / 2, b.Y - ny * wb / 2), new PointF(a.X - nx * wa / 2, a.Y - ny * wa / 2),
        };
        var ea = new RectangleF(a.X - wa / 2, a.Y - wa / 2, wa, wa);
        var eb = new RectangleF(b.X - wb / 2, b.Y - wb / 2, wb, wb);
        // Un solo pincel para las tres figuras, así no se notan las uniones.
        using var br = BodyBrush(RectangleF.Union(RectangleF.Union(ea, eb), RectangleF.FromLTRB(poly.Min(p => p.X), poly.Min(p => p.Y), poly.Max(p => p.X), poly.Max(p => p.Y))));
        g.FillPolygon(br, poly);
        g.FillEllipse(br, ea);
        g.FillEllipse(br, eb);
    }

    /// <summary>Dibuja la prenda recortada con una sombra suave debajo, para que parezca apoyada sobre el cuerpo.</summary>
    static void DrawGarment(Graphics g, Bitmap img, RectangleF r, float k)
    {
        using var shadow = new ImageAttributes();
        shadow.SetColorMatrix(new ColorMatrix(new[]
        {
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0.16f, 0 },
            new float[] { 0.12f, 0.1f, 0.18f, 0, 1 },
        }));
        foreach (var (dx, dy) in new[] { (1.5f, 3f), (3.5f, 6.5f) })
        {
            var sr = Rectangle.Round(new RectangleF(r.X + dx * k, r.Y + dy * k, r.Width, r.Height));
            g.DrawImage(img, sr, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, shadow);
        }
        g.DrawImage(img, r);
    }

    /// <summary>Etiqueta lateral: vacía (borde punteado y "+") o con la prenda puesta (color, nombre y ✕ para quitarla).</summary>
    void DrawTag(Graphics g, Category c)
    {
        var (pillD, targetD) = Tags[c];
        var pill = Map(pillD);
        var target = Map(targetD);
        var p = Get(c);
        bool hov = hover == c, focus = FocusCue && kbSlot == c, req = Cats.Required.Contains(c);
        var accent = hov || focus ? Primary : Color.FromArgb(150, Muted);

        // Línea guía hasta el cuerpo.
        var from = new PointF(pill.X > target.X ? pill.X : pill.Right, pill.Y + pill.Height / 2);
        using (var lp = new Pen(accent, Df(1.2f)) { DashStyle = p == null ? DashStyle.Dash : DashStyle.Solid })
            g.DrawLine(lp, from, target);
        using (var db = new SolidBrush(accent)) g.FillEllipse(db, target.X - Df(3), target.Y - Df(3), Df(6), Df(6));

        float rad = pill.Height / 2;
        FillRound(g, Color.FromArgb(hov ? 255 : 238, 255, 255, 255), pill, rad);
        int tx = (int)(pill.X + Df(10));
        if (p == null)
        {
            DrawRound(g, hov || focus ? Primary : req ? Color.FromArgb(160, Primary) : Outline, pill, rad, focus ? Df(2.2f) : Df(1.3f), DashStyle.Dash);
            float s = Math.Min(Df(20), pill.Height - Df(14));
            var pr = new RectangleF(pill.X + Df(8), pill.Y + (pill.Height - s) / 2, s, s);
            using (var pb = new SolidBrush(hov ? Primary : PrimarySoft)) g.FillEllipse(pb, pr);
            using (var pen = new Pen(hov ? Color.White : Primary, Df(1.8f)))
            {
                float m = s * 0.28f;
                g.DrawLine(pen, pr.X + s / 2, pr.Y + m, pr.X + s / 2, pr.Bottom - m);
                g.DrawLine(pen, pr.X + m, pr.Y + s / 2, pr.Right - m, pr.Y + s / 2);
            }
            tx = (int)(pr.Right + Df(6));
            int tw = (int)(pill.Right - Df(8)) - tx;
            DrawText(g, Short(c), F(8.5f, FontStyle.Bold), Theme.Text, new Rectangle(tx, (int)pill.Y + D(3), tw, (int)(pill.Height / 2)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Bottom);
            DrawText(g, req ? "obligatoria" : "opcional", F(7.5f), Muted, new Rectangle(tx, (int)(pill.Y + pill.Height / 2), tw, (int)(pill.Height / 2) - D(2)), TextFormatFlags.SingleLine | TextFormatFlags.Top);
        }
        else
        {
            DrawRound(g, hov || focus ? Primary : Outline, pill, rad, focus ? Df(2.2f) : Df(1.3f));
            float s = Df(12);
            Swatch(g, p.Color, new RectangleF(pill.X + Df(10), pill.Y + (pill.Height - s) / 2, s, s));
            // La ✕ para quitar aparece al pasar el mouse o con el teclado; así el nombre tiene más lugar.
            var cr = CloseRect(pill);
            bool showClose = hov || focus;
            tx = (int)(pill.X + Df(28));
            int tw = (int)((showClose ? cr.X : pill.Right - Df(10)) - Df(2)) - tx;
            DrawText(g, p.Nombre, F(8.25f, FontStyle.Bold), Theme.Text, new Rectangle(tx, (int)pill.Y + D(3), tw, (int)(pill.Height / 2)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Bottom);
            DrawText(g, Short(c), F(7.5f), Muted, new Rectangle(tx, (int)(pill.Y + pill.Height / 2), tw, (int)(pill.Height / 2) - D(2)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Top);

            if (!showClose) return;
            bool hc = hov && hoverClose;
            using (var cb = new SolidBrush(hc ? Danger : Bg)) g.FillEllipse(cb, cr);
            using var xp = new Pen(hc ? Color.White : Muted, Df(1.5f));
            float m = cr.Width * 0.32f;
            g.DrawLine(xp, cr.X + m, cr.Y + m, cr.Right - m, cr.Bottom - m);
            g.DrawLine(xp, cr.Right - m, cr.Y + m, cr.X + m, cr.Bottom - m);
        }
    }
}
