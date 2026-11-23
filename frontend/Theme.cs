using System.Drawing.Drawing2D;

namespace FitLab;

/// <summary>Colores, tipografías y utilidades de dibujo compartidas.</summary>
public static class Theme
{
    public static float Scale = 1f;
    public static int D(float v) => (int)Math.Round(v * Scale);
    public static float Df(float v) => v * Scale;

    public static readonly Color Bg = Color.FromArgb(246, 244, 241);
    public static readonly Color Surface = Color.White;
    public static readonly Color Primary = Color.FromArgb(91, 75, 219);
    public static readonly Color PrimarySoft = Color.FromArgb(237, 234, 252);
    public static readonly Color Text = Color.FromArgb(31, 29, 43);
    public static readonly Color Muted = Color.FromArgb(112, 108, 126);
    public static readonly Color Border = Color.FromArgb(226, 222, 216);
    /// <summary>Borde de controles que se pueden tocar o escribir: contraste de al menos 3:1 sobre blanco.</summary>
    public static readonly Color Outline = Color.FromArgb(138, 134, 150);
    public static readonly Color Danger =Color.FromArgb(200, 60, 60);
    public static readonly Color DangerSoft = Color.FromArgb(252, 236, 236);
    public static readonly Color Success = Color.FromArgb(34, 140, 94);
    public static readonly Color SuccessSoft = Color.FromArgb(226, 245, 236);
    public static readonly Color Warning = Color.FromArgb(196, 124, 20);
    public static readonly Color WarningSoft = Color.FromArgb(253, 243, 224);
    public static readonly Color Mannequin = Color.FromArgb(232, 228, 240);

    static readonly Dictionary<(float, FontStyle), Font> fonts = new();
    public static Font F(float size, FontStyle style = FontStyle.Regular)
    {
        if (!fonts.TryGetValue((size, style), out var f))
        {
            f = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            fonts[(size, style)] = f;
        }
        return f;
    }

    public static void HQ(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using var p = Round(r, rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, Color c, RectangleF r, float rad, float width = 1f, DashStyle dash = DashStyle.Solid)
    {
        using var p = Round(r, rad);
        using var pen = new Pen(c, width) { DashStyle = dash };
        g.DrawPath(pen, p);
    }

    /// <summary>Dibuja la imagen cubriendo el rectángulo (recorte centrado) con esquinas redondeadas.</summary>
    public static void DrawCover(Graphics g, Image img, RectangleF dest, float rad)
    {
        if (img == null || dest.Width < 1 || dest.Height < 1) return;
        float sr = (float)img.Width / img.Height, dr = dest.Width / dest.Height;
        RectangleF src;
        if (sr > dr) { float w = img.Height * dr; src = new RectangleF((img.Width - w) / 2, 0, w, img.Height); }
        else { float h = img.Width / dr; src = new RectangleF(0, (img.Height - h) / 2, img.Width, h); }
        var state = g.Save();
        using (var p = Round(dest, rad)) g.SetClip(p, CombineMode.Intersect);
        g.DrawImage(img, dest, src, GraphicsUnit.Pixel);
        g.Restore(state);
    }

    public static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags = TextFormatFlags.Left)
    {
        TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix);
    }

    public static int TextHeight(string s, Font f, int width)
    {
        if (string.IsNullOrEmpty(s) || width <= 0) return 0;
        var sz = TextRenderer.MeasureText(s, f, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        return sz.Height;
    }

    public static int TextWidth(string s, Font f) =>
        TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

    /// <summary>Color legible (negro o blanco) sobre un fondo.</summary>
    public static Color OnColor(Color bg)
    {
        double l = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return l > 0.62 ? Text : Color.White;
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void Swatch(Graphics g, Color c, RectangleF r)
    {
        using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
        using var pen = new Pen(Color.FromArgb(60, 0, 0, 0), Math.Max(1f, Df(1)));
        g.DrawEllipse(pen, r);
    }

    public static Color LevelColor(HarmonyLevel l) => l switch
    {
        HarmonyLevel.Buena => Success,
        HarmonyLevel.Media => Warning,
        HarmonyLevel.Baja => Danger,
        _ => Muted,
    };

    public static Color LevelSoft(HarmonyLevel l) => l switch
    {
        HarmonyLevel.Buena => SuccessSoft,
        HarmonyLevel.Media => WarningSoft,
        HarmonyLevel.Baja => DangerSoft,
        _ => Bg,
    };
}
