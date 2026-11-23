using static FitLab.Theme;

namespace FitLab;

/// <summary>Módulo 3: guía de colores con rueda interactiva y prendas propias que combinan.</summary>
public class GuideView : ScrollHost, IView
{
    readonly MainForm main;
    readonly ColorWheel wheel = new();
    readonly WrapPanel neutrals = new();
    readonly TextBlock mineLbl = new("De tus prendas", 9.5f, FontStyle.Bold);
    readonly WrapPanel mine = new();
    readonly CardStack info = new() { Gap = D(8) };
    readonly TextBlock matchTitle = new("Prendas de tu guardarropa que combinan", 13f, FontStyle.Bold);
    readonly TextBlock matchSub = new("", 9.5f, FontStyle.Regular, Muted);
    readonly GarmentGrid grid = new() { MinCardWidth = D(140) };
    readonly EmptyState noMatch = new("", "");
    Color chosen;
    string chosenName = "";

    AppState S => main.State;

    public GuideView(MainForm m)
    {
        main = m;
        Stack.MaxContentWidth = D(1000);
        Stack.Controls.Add(new TextBlock("Guía de colores", 17f, FontStyle.Bold));
        Stack.Controls.Add(new TextBlock("Tocá un color de la rueda para ver con qué colores combina y qué prendas de tu guardarropa encajan.", 9.5f, FontStyle.Regular, Muted));

        var left = new VStack { Gap = D(10) };
        left.Controls.Add(wheel);
        left.Controls.Add(new TextBlock("Neutros", 9.5f, FontStyle.Bold));
        foreach (var n in new[] { "Negro", "Blanco", "Gris", "Gris oscuro", "Beige", "Crema", "Camel", "Marrón", "Azul marino" })
        {
            var hex = ColorTools.Palette.First(p => p.Name == n).Hex;
            neutrals.Controls.Add(MakeSwatch(ColorTools.FromHex(hex), n));
        }
        left.Controls.Add(neutrals);
        left.Controls.Add(mineLbl);
        left.Controls.Add(mine);

        Stack.Controls.Add(new Responsive2(left, info) { LeftWidth = D(380), Breakpoint = D(760) });
        Stack.Controls.Add(Ui.Space(D(4)));
        Stack.Controls.Add(matchTitle);
        Stack.Controls.Add(matchSub);
        Stack.Controls.Add(noMatch);
        Stack.Controls.Add(grid);

        wheel.Picked += (c, n) => Choose(c, n, fromWheel: true);
        main.State.Changed += () => { if (Visible) ShowView(); };
        Choose(ColorWheel.ColorAt(7, 1), "Azul", fromWheel: false);
        wheel.SelectAt(7, 1);
    }

    SwatchButton MakeSwatch(Color c, string name)
    {
        var sw = new SwatchButton(c, name, D(36));
        Ui.Tip(sw, name);
        sw.Click += (_, _) => Choose(sw.Color, sw.ColorName, fromWheel: false);
        return sw;
    }

    public void ShowView()
    {
        var old = mine.Controls.Cast<Control>().ToList();
        mine.Controls.Clear();
        foreach (var c in old) c.Dispose();
        foreach (var g in S.Prendas.GroupBy(p => p.ColorHex.ToUpperInvariant()).Take(24))
            mine.Controls.Add(MakeSwatch(g.First().Color, g.First().ColorNombre));
        mineLbl.Visible = mine.Visible = mine.Controls.Count > 0;
        Choose(chosen, chosenName, fromWheel: true);
    }

    void Choose(Color c, string name, bool fromWheel)
    {
        chosen = c;
        chosenName = string.IsNullOrWhiteSpace(name) ? ColorTools.NearestName(c) : name;
        if (!fromWheel) wheel.SelectColor(c);
        foreach (var sw in neutrals.Controls.OfType<SwatchButton>().Concat(mine.Controls.OfType<SwatchButton>()))
            sw.Selected = ColorTools.ToHex(sw.Color) == ColorTools.ToHex(c);
        BuildInfo();
        BuildMatches();
        Relayout();
    }

    void BuildInfo()
    {
        info.SuspendLayout();
        var old = info.Controls.Cast<Control>().ToList();
        info.Controls.Clear();
        foreach (var c in old) c.Dispose();

        info.Controls.Add(new ChosenHeader(chosen, chosenName));
        bool neutral = ColorTools.IsNeutral(chosen);
        var (h, s, v) = ColorTools.Hsv(chosen);

        if (neutral)
        {
            AddGroup("Neutro: combina con todo", "Los neutros son la base de cualquier outfit. Sumales un color protagonista o combinalos entre sí.",
                ("Rojo", ColorTools.FromHex("#D2312F")), ("Azul", ColorTools.FromHex("#2F6FD0")), ("Verde", ColorTools.FromHex("#2E9B57")),
                ("Amarillo", ColorTools.FromHex("#F2CC3B")), ("Rosa", ColorTools.FromHex("#F2A7C0")), ("Violeta", ColorTools.FromHex("#7A4FC2")));
            AddGroup("Otros neutros", "Mezclá claros y oscuros para que el look no se vea plano.",
                ("Blanco", ColorTools.FromHex("#F8F8F6")), ("Negro", ColorTools.FromHex("#1C1C1E")), ("Beige", ColorTools.FromHex("#D9C7A7")), ("Azul marino", ColorTools.FromHex("#1F2A48")));
        }
        else
        {
            double sv = Math.Max(0.55, s);
            Color comp = ColorTools.FromHsv(h + 180, sv, v);
            AddGroup("Complementario", "El color opuesto en la rueda. Genera contraste y energía: usalo como acento.", Named(comp));
            AddGroup("Análogos", "Los vecinos en la rueda. Combinan de forma suave y natural.",
                Named(ColorTools.FromHsv(h - 30, sv, v)), Named(ColorTools.FromHsv(h + 30, sv, v)));
            AddGroup("Tríada", "Tres colores repartidos en la rueda. Más atrevido: dejá que uno domine.",
                Named(ColorTools.FromHsv(h + 120, sv, v)), Named(ColorTools.FromHsv(h + 240, sv, v)));
            AddGroup("Monocromático", "El mismo color en tonos más claros y más oscuros.",
                Named(ColorTools.FromHsv(h, Math.Max(0.2, s * 0.45), Math.Min(1, v + 0.1))), Named(ColorTools.FromHsv(h, s, Math.Max(0.25, v * 0.6))), Named(ColorTools.FromHsv(h, Math.Min(1, s * 1.1), Math.Max(0.2, v * 0.4))));
            AddGroup("Neutros que siempre suman", "Con estos nunca falla.",
                ("Blanco", ColorTools.FromHex("#F8F8F6")), ("Negro", ColorTools.FromHex("#1C1C1E")), ("Gris", ColorTools.FromHex("#9A9A9E")), ("Beige", ColorTools.FromHex("#D9C7A7")), ("Azul marino", ColorTools.FromHex("#1F2A48")));
        }
        info.ResumeLayout();
    }

    static (string, Color) Named(Color c) => (ColorTools.NearestName(c), c);

    void AddGroup(string title, string desc, params (string Name, Color C)[] colors)
    {
        info.Controls.Add(Ui.Space(D(4)));
        info.Controls.Add(new TextBlock(title, 10f, FontStyle.Bold));
        info.Controls.Add(new TextBlock(desc, 9f, FontStyle.Regular, Muted));
        var wrap = new WrapPanel { BackColor = Surface, GapX = D(6) };
        foreach (var (n, c) in colors)
        {
            var chip = new ChipButton(n, c, c) { BackColor = Surface };
            Ui.Tip(chip, "Ver con qué combina el " + n.ToLowerInvariant());
            chip.Click += (_, _) => BeginInvoke(new Action(() => Choose(c, n, fromWheel: false)));
            wrap.Controls.Add(chip);
        }
        info.Controls.Add(wrap);
    }

    void BuildMatches()
    {
        if (S.Prendas.Count == 0)
        {
            matchSub.Text = "";
            noMatch.Title.Text = "Todavía no tenés prendas";
            noMatch.Body.Text = "Subí prendas a tu guardarropa y acá vas a ver cuáles combinan con el color que elijas.";
            noMatch.Visible = true;
            grid.Visible = false;
            grid.SetCards(Array.Empty<GarmentCard>());
            return;
        }
        static int Rank(string r) => r switch { "Mismo tono" => 0, "Análogo" => 1, "Complementario" => 2, "Tríada" => 3, "Va con neutros" => 4, _ => 5 };
        var found = new List<(Prenda p, string reason)>();
        foreach (var p in S.Prendas)
            if (ColorTools.MatchReason(chosen, p.Color) is string reason) found.Add((p, reason));
        var matches = found.OrderBy(t => Rank(t.reason)).ThenBy(t => t.p.Categoria).ToList();
        matchSub.Text = matches.Count == 0 ? "" : $"Encontramos {matches.Count} {(matches.Count == 1 ? "prenda" : "prendas")} que van con el {chosenName.ToLowerInvariant()}. Tocá una para probarla en el maniquí.";
        noMatch.Visible = matches.Count == 0;
        noMatch.Title.Text = "Ninguna prenda encaja con este color";
        noMatch.Body.Text = "Probá con otro color de la rueda, o sumá prendas neutras (negro, blanco, gris o beige): combinan con todo.";
        grid.Visible = matches.Count > 0;
        grid.SetCards(matches.Select(t =>
        {
            var card = new GarmentCard(t.p) { Badge = t.reason };
            card.Click += (_, _) => BeginInvoke(new Action(() => main.TryOn(t.p)));
            return card;
        }));
    }

    class ChosenHeader : PaintedControl, IMeasure
    {
        readonly Color c;
        readonly string name;
        public ChosenHeader(Color c, string name) { this.c = c; this.name = name; }
        public int MeasureHeight(int width) => D(64);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            var r = new RectangleF(1, D(2), D(58), D(58));
            FillRound(g, c, r, Df(14));
            DrawRound(g, Color.FromArgb(50, 0, 0, 0), r, Df(14), Df(1));
            DrawText(g, "COLOR ELEGIDO", F(8f, FontStyle.Bold), Muted, new Rectangle(D(72), D(6), Width - D(72), D(18)), TextFormatFlags.SingleLine);
            DrawText(g, name, F(14f, FontStyle.Bold), Theme.Text, new Rectangle(D(72), D(22), Width - D(72), D(28)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            DrawText(g, ColorTools.ToHex(c) + (ColorTools.IsNeutral(c) ? " · neutro" : ""), F(8.5f), Muted, new Rectangle(D(72), D(46), Width - D(72), D(18)), TextFormatFlags.SingleLine);
        }
    }
}

/// <summary>Rueda de 12 tonos en 3 intensidades (claro, vivo, oscuro).</summary>
public class ColorWheel : PaintedControl, IMeasure
{
    public event Action<Color, string>? Picked;
    int selHue = -1, selBand = -1;
    int hovHue = -1, hovBand = -1;
    Color? external;

    static readonly (double S, double V)[] Bands = { (0.36, 1.0), (0.78, 0.92), (0.82, 0.58) };
    static readonly float[] Radii = { 1f, 0.79f, 0.58f };
    const float Hole = 0.37f;

    public static Color ColorAt(int hue, int band) => ColorTools.FromHsv(hue * 30, Bands[band].S, Bands[band].V);

    public ColorWheel()
    {
        Cursor = Cursors.Hand;
        MakeFocusable(AccessibleRole.Grouping, "Rueda de color: flechas izquierda y derecha cambian el tono; arriba y abajo, la intensidad.", keyActivates: false);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int h = selHue < 0 ? 0 : selHue, b = selBand < 0 ? 1 : selBand;
        switch (e.KeyCode)
        {
            case Keys.Left: h = (h + 11) % 12; break;
            case Keys.Right: h = (h + 1) % 12; break;
            case Keys.Up: b = Math.Max(0, b - 1); break;   // hacia afuera: más claro
            case Keys.Down: b = Math.Min(2, b + 1); break; // hacia el centro: más oscuro
            default: return;
        }
        e.Handled = true;
        SelectAt(h, b);
        var col = ColorAt(h, b);
        AccessibleDescription = "Color elegido: " + ColorTools.NearestName(col);
        Picked?.Invoke(col, ColorTools.NearestName(col));
    }

    public int MeasureHeight(int width) => Math.Min(width, D(360));

    (PointF c, float R) Geo()
    {
        float size = Math.Min(Width, Height) - Df(8);
        return (new PointF(Width / 2f, Height / 2f), size / 2);
    }

    public void SelectAt(int hue, int band) { selHue = hue; selBand = band; external = null; Invalidate(); }

    public void SelectColor(Color c)
    {
        for (int h = 0; h < 12; h++)
            for (int b = 0; b < 3; b++)
                if (ColorTools.ToHex(ColorAt(h, b)) == ColorTools.ToHex(c)) { SelectAt(h, b); return; }
        selHue = selBand = -1;
        external = c;
        Invalidate();
    }

    (int hue, int band) Hit(Point p)
    {
        var (c, R) = Geo();
        float dx = p.X - c.X, dy = p.Y - c.Y;
        float d = (float)Math.Sqrt(dx * dx + dy * dy) / R;
        if (d > 1 || d < Hole) return (-1, -1);
        int band = d > Radii[1] ? 0 : d > Radii[2] ? 1 : 2;
        double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
        double a = ((ang + 90 + 15) % 360 + 360) % 360;
        return ((int)(a / 30) % 12, band);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (h, b) = Hit(e.Location);
        if (h != hovHue || b != hovBand) { hovHue = h; hovBand = b; Invalidate(); }
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e) { hovHue = hovBand = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var (h, b) = Hit(e.Location);
        if (h < 0) return;
        SelectAt(h, b);
        var col = ColorAt(h, b);
        Picked?.Invoke(col, ColorTools.NearestName(col));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        HQ(g);
        var (c, R) = Geo();
        for (int b = 0; b < 3; b++)
        {
            float r = R * Radii[b];
            var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
            for (int h = 0; h < 12; h++)
            {
                using var br = new SolidBrush(ColorAt(h, b));
                g.FillPie(br, rect.X, rect.Y, rect.Width, rect.Height, -90 + h * 30 - 15, 30);
            }
        }
        // Separadores blancos entre segmentos
        using (var sep = new Pen(BackColor, Df(2.5f)))
        {
            for (int h = 0; h < 12; h++)
            {
                double a = (-90 + h * 30 - 15) * Math.PI / 180;
                g.DrawLine(sep, c.X + (float)Math.Cos(a) * R * Hole, c.Y + (float)Math.Sin(a) * R * Hole, c.X + (float)Math.Cos(a) * (R + 1), c.Y + (float)Math.Sin(a) * (R + 1));
            }
            for (int b = 1; b < 3; b++)
            {
                float r = R * Radii[b];
                g.DrawEllipse(sep, c.X - r, c.Y - r, r * 2, r * 2);
            }
        }

        // Marcas de armonía: complementario (●), análogos y tríada (○)
        if (selHue >= 0)
        {
            void Mark(int hue, bool strong)
            {
                float rm = R * (Radii[selBand] + (selBand == 2 ? Hole : Radii[selBand + 1])) / 2;
                double a = (-90 + ((hue % 12 + 12) % 12) * 30) * Math.PI / 180;
                float x = c.X + (float)Math.Cos(a) * rm, y = c.Y + (float)Math.Sin(a) * rm, s = Df(strong ? 12 : 9);
                using var wb = new SolidBrush(Color.White);
                g.FillEllipse(wb, x - s / 2, y - s / 2, s, s);
                using var pen = new Pen(Theme.Text, Df(1.5f));
                g.DrawEllipse(pen, x - s / 2, y - s / 2, s, s);
                if (strong) using (var db = new SolidBrush(Theme.Text)) g.FillEllipse(db, x - s / 4, y - s / 4, s / 2, s / 2);
            }
            Mark(selHue + 6, true);
            Mark(selHue + 1, false); Mark(selHue - 1, false);
            Mark(selHue + 4, false); Mark(selHue - 4, false);
        }

        void Outline(int h, int b, Color col, float w)
        {
            float ro = R * Radii[b], ri = b == 2 ? R * Hole : R * Radii[b + 1];
            using var p = new System.Drawing.Drawing2D.GraphicsPath();
            float start = -90 + h * 30 - 15;
            p.AddArc(c.X - ro, c.Y - ro, ro * 2, ro * 2, start, 30);
            p.AddArc(c.X - ri, c.Y - ri, ri * 2, ri * 2, start + 30, -30);
            p.CloseFigure();
            using var pen = new Pen(col, w) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            g.DrawPath(pen, p);
        }
        if (hovHue >= 0 && (hovHue != selHue || hovBand != selBand)) Outline(hovHue, hovBand, Color.FromArgb(180, 255, 255, 255), Df(2.5f));
        if (selHue >= 0)
        {
            Outline(selHue, selBand, Color.White, Df(5f));
            Outline(selHue, selBand, Theme.Text, Df(2f));
        }

        if (FocusCue)
            using (var fp = new Pen(Primary, Df(2))) g.DrawEllipse(fp, c.X - R - Df(2), c.Y - R - Df(2), (R + Df(2)) * 2, (R + Df(2)) * 2);

        // Centro con el color elegido
        float hr = R * Hole - Df(6);
        var cur = selHue >= 0 ? ColorAt(selHue, selBand) : external ?? Color.White;
        using (var cb = new SolidBrush(cur)) g.FillEllipse(cb, c.X - hr, c.Y - hr, hr * 2, hr * 2);
        using (var cp = new Pen(Color.FromArgb(40, 0, 0, 0), Df(1))) g.DrawEllipse(cp, c.X - hr, c.Y - hr, hr * 2, hr * 2);
        DrawText(g, ColorTools.NearestName(cur), F(9f, FontStyle.Bold), OnColor(cur), Rectangle.Round(new RectangleF(c.X - hr, c.Y - hr, hr * 2, hr * 2)),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}
