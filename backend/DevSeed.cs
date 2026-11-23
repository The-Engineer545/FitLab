#if DEBUG
using System.Drawing.Drawing2D;

namespace FitLab;

/// <summary>
/// Solo para pruebas internas (variable de entorno FITLAB_DEMO=1): crea una cuenta de ejemplo con prendas
/// dibujadas y abre la pestaña pedida en FITLAB_TAB. La app normal nunca lo usa.
/// </summary>
public static class DevSeed
{
    public static Usuario Run()
    {
        Usuario u;
        try { u = Db.Login("demo@fitlab.test", "demo1234"); }
        catch (UserError) { u = Db.Register("Valentina Demo", "demo@fitlab.test", "demo1234", "demo1234"); }
        if (Db.ListPrendas(u.Id).Count > 0) return u;

        var items = new (string n, Category c, string hex, string cn, Season s)[]
        {
            ("Remera básica", Category.Superior, "#F8F8F6", "Blanco", Season.TodoElAnio),
            ("Camisa de lino", Category.Superior, "#8EC5EC", "Celeste", Season.Calor),
            ("Buzo oversize", Category.Superior, "#EE7D2B", "Naranja", Season.Frio),
            ("Jean recto", Category.Inferior, "#2F5DA8", "Azul", Season.TodoElAnio),
            ("Pantalón sastrero", Category.Inferior, "#D9C7A7", "Beige", Season.TodoElAnio),
            ("Zapatillas urbanas", Category.Calzado, "#F8F8F6", "Blanco", Season.TodoElAnio),
            ("Botas de cuero", Category.Calzado, "#6B4A32", "Marrón", Season.Frio),
            ("Campera de jean", Category.Abrigo, "#1F2A48", "Azul marino", Season.Frio),
            ("Bolso tote", Category.Accesorio, "#D2312F", "Rojo", Season.TodoElAnio),
        };
        foreach (var it in items)
        {
            using var img = Draw(it.c, ColorTools.FromHex(it.hex));
            var p = new Prenda
            {
                UsuarioId = u.Id, Nombre = it.n, Categoria = it.c, ColorHex = it.hex, ColorNombre = it.cn, Temporada = it.s,
                FotoPath = Photos.Save(img, u.Id),
            };
            Db.SavePrenda(p);
        }
        var all = Db.ListPrendas(u.Id);
        Prenda P(string n) => all.First(p => p.Nombre == n);
        Db.AddOutfit(u.Id, "Lunes de facultad", "universidad", true, new Dictionary<Category, Prenda>
        { [Category.Superior] = P("Remera básica"), [Category.Inferior] = P("Jean recto"), [Category.Calzado] = P("Zapatillas urbanas"), [Category.Abrigo] = P("Campera de jean") });
        Db.AddOutfit(u.Id, "Cena con amigas", "salida", false, new Dictionary<Category, Prenda>
        { [Category.Superior] = P("Camisa de lino"), [Category.Inferior] = P("Pantalón sastrero"), [Category.Calzado] = P("Botas de cuero"), [Category.Accesorio] = P("Bolso tote") });
        return u;
    }

    static Bitmap Draw(Category c, Color col)
    {
        var bmp = new Bitmap(600, 600);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 600, 600), Color.FromArgb(238, 235, 230), Color.FromArgb(222, 218, 212), 90f)) g.FillRectangle(bg, 0, 0, 600, 600);
        using var b = new SolidBrush(col);
        using var pen = new Pen(Color.FromArgb((int)(col.R * 0.65), (int)(col.G * 0.65), (int)(col.B * 0.65)), 6) { LineJoin = LineJoin.Round };
        var path = new GraphicsPath();
        switch (c)
        {
            case Category.Superior:
                path.AddPolygon(new PointF[] { new(210, 110), new(250, 130), new(350, 130), new(390, 110), new(500, 180), new(460, 260), new(420, 240), new(420, 500), new(180, 500), new(180, 240), new(140, 260), new(100, 180) });
                break;
            case Category.Inferior:
                path.AddPolygon(new PointF[] { new(190, 90), new(410, 90), new(450, 520), new(340, 520), new(300, 220), new(260, 520), new(150, 520) });
                break;
            case Category.Calzado:
                path.AddPath(Shoe(110, 260), false);
                path.AddPath(Shoe(300, 330), false);
                break;
            case Category.Abrigo:
                path.AddPolygon(new PointF[] { new(220, 90), new(300, 140), new(380, 90), new(500, 160), new(520, 470), new(450, 480), new(430, 280), new(420, 530), new(180, 530), new(170, 280), new(150, 480), new(80, 470), new(100, 160) });
                break;
            default:
                path.AddRectangle(new RectangleF(160, 240, 280, 250));
                path.AddArc(new RectangleF(220, 120, 160, 220), 180, 180);
                break;
        }
        g.FillPath(b, path);
        g.DrawPath(pen, path);
        if (c == Category.Abrigo) g.DrawLine(pen, 300, 140, 300, 530);
        return bmp;
    }

    static GraphicsPath Shoe(float x, float y)
    {
        var p = new GraphicsPath();
        p.AddPolygon(new PointF[] { new(x, y), new(x + 70, y), new(x + 110, y + 60), new(x + 200, y + 80), new(x + 210, y + 130), new(x, y + 130) });
        return p;
    }
}
#endif
