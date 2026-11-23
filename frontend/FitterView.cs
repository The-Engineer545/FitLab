using static FitLab.Theme;

namespace FitLab;

/// <summary>Módulo 2: probador virtual con un maniquí que se viste con las prendas, por categoría.</summary>
public class FitterView : ScrollHost, IView
{
    readonly MainForm main;
    readonly FitterCanvas canvas = new();
    readonly HarmonyPanel harmony = new();
    readonly EmptyState noClothes = new("Todavía no tenés prendas para probarte", "Subí algunas prendas a tu guardarropa y volvé para armar combinaciones.", "Ir al guardarropa");
    readonly TextBlock status = new("", 9.5f, FontStyle.Bold, Success);
    readonly Random rng = new();

    AppState S => main.State;

    public FitterView(MainForm m)
    {
        main = m;
        Stack.MaxContentWidth = D(980);
        Stack.Controls.Add(new TextBlock("Probador", 17f, FontStyle.Bold));
        Stack.Controls.Add(new TextBlock("Tocá una etiqueta o una parte del maniquí para vestirlo con una prenda de tu guardarropa.", 9.5f, FontStyle.Regular, Muted));
        Stack.Controls.Add(noClothes);

        var left = new VStack { Gap = D(10) };
        left.Controls.Add(canvas);
        var random = new FlatButton("Aleatorio", BtnKind.Secondary);
        var clear = new FlatButton("Limpiar", BtnKind.Ghost);
        left.Controls.Add(new HRow(random, clear) { Gap = D(8) });
        var save = new FlatButton("Guardar outfit");
        left.Controls.Add(save);
        left.Controls.Add(status);

        var right = new VStack { Gap = D(10) };
        right.Controls.Add(harmony);
        Stack.Controls.Add(new Responsive2(left, right) { LeftWidth = D(420), Breakpoint = D(800) });

        canvas.Get = c => S.Fitting.TryGetValue(c, out var p) ? p : null;
        canvas.SlotClicked += Pick;
        canvas.SlotCleared += c => { S.Fitting.Remove(c); Changed(); };
        random.Click += (_, _) => Randomize();
        clear.Click += (_, _) => { S.Fitting.Clear(); Changed(); };
        save.Click += (_, _) => SaveOutfit();
        noClothes.Action.Click += (_, _) => main.ShowTab(0);
        main.State.Changed += () => { if (Visible) ShowView(); };
    }

    public void ShowView()
    {
        noClothes.Visible = S.Prendas.Count == 0;
        Changed(keepStatus: true);
    }

    void Changed(bool keepStatus = false)
    {
        if (!keepStatus) status.Text = "";
        canvas.Invalidate();
        harmony.Set(ColorTools.Analyze(S.Fitting), S.Fitting);
        Relayout();
    }

    void Pick(Category c)
    {
        S.Fitting.TryGetValue(c, out var current);
        using var dlg = new GarmentPickerForm(c, S.Prendas.Where(p => p.Categoria == c).ToList(), current);
        var res = dlg.ShowDialog(this);
        if (res == DialogResult.OK && dlg.Chosen != null) { S.Fitting[c] = dlg.Chosen; Changed(); }
        else if (res == DialogResult.Abort) { S.Fitting.Remove(c); Changed(); }
        else if (res == DialogResult.Retry)
        {
            if (main.EditGarment(null, c, out long savedId))
            {
                // Solo se pone la prenda recién creada, y solo si quedó en la categoría de esta ranura.
                var created = S.Prendas.FirstOrDefault(p => p.Id == savedId && p.Categoria == c);
                if (created != null) { S.Fitting[c] = created; Changed(); }
            }
        }
    }

    void Randomize()
    {
        var missing = Cats.Required.Where(c => !S.Prendas.Any(p => p.Categoria == c)).ToList();
        if (missing.Count > 0)
        {
            Ui.Info(this, "Para armar un outfit aleatorio necesitás al menos una prenda de cada una de estas categorías: "
                + string.Join(", ", missing.Select(c => Cats.Label(c).ToLowerInvariant())) + ".\n\nSubilas desde el Guardarropa.", "Faltan prendas");
            return;
        }

        // Se prueban varias combinaciones al azar y se elige una al azar entre las que mejor armonizan.
        var tries = new List<(Dictionary<Category, Prenda> combo, HarmonyLevel level, bool same)>();
        for (int i = 0; i < 16; i++)
        {
            var combo = new Dictionary<Category, Prenda>();
            foreach (var c in Cats.Required)
            {
                var opts = S.Prendas.Where(p => p.Categoria == c).ToList();
                combo[c] = opts[rng.Next(opts.Count)];
            }
            bool same = Cats.Required.All(c => S.Fitting.TryGetValue(c, out var cur) && cur.Id == combo[c].Id) && S.Fitting.Count == 3;
            tries.Add((combo, ColorTools.Analyze(combo).Level, same));
        }
        var pool = tries.Where(t => !t.same && t.level == HarmonyLevel.Buena).ToList();
        if (pool.Count == 0) pool = tries.Where(t => !t.same).ToList();
        if (pool.Count == 0) pool = tries;
        var pick = pool[rng.Next(pool.Count)].combo;

        S.Fitting.Clear();
        foreach (var kv in pick) S.Fitting[kv.Key] = kv.Value;
        Changed();
    }

    void SaveOutfit()
    {
        if (S.Fitting.Count == 0)
        {
            Ui.Info(this, "Elegí al menos una prenda en el maniquí antes de guardar el outfit.", "Outfit vacío");
            return;
        }
        var missing = Cats.Required.Where(c => !S.Fitting.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            Ui.Info(this, "Para guardar el outfit todavía falta elegir: "
                + ColorTools.JoinNames(missing.Select(Cats.Phrase)) + ".", "Faltan prendas obligatorias");
            return;
        }
        using var dlg = new SaveOutfitForm(S.Outfits.Count + 1);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Db.AddOutfit(S.UserId, dlg.OutfitName, dlg.Occasion, dlg.Favorite, S.Fitting);
            S.Reload();
            status.Text = $"¡Listo! Guardaste «{dlg.OutfitName}». Lo encontrás en Outfits.";
            Relayout();
        }
        catch (Exception ex) { Ui.Error(this, "No pudimos guardar el outfit. Probá de nuevo. (" + ex.Message + ")"); }
    }
}

/// <summary>Indicador de armonía de colores del outfit.</summary>
public class HarmonyPanel : PaintedControl, IMeasure
{
    HarmonyResult result = new();
    List<(Category, Prenda)> items = new();

    public void Set(HarmonyResult r, IReadOnlyDictionary<Category, Prenda> fit)
    {
        result = r;
        items = Cats.All.Where(fit.ContainsKey).Select(c => (c, fit[c])).ToList();
        Invalidate();
    }

    public int MeasureHeight(int width) => Render(null, width);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Behind);
        HQ(e.Graphics);
        Render(e.Graphics, Width);
    }

    int Render(Graphics? g, int width)
    {
        int pad = D(18);
        int inner = width - pad * 2;
        int h = 0;
        if (g != null)
        {
            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            FillRound(g, Surface, card, Df(16));
            DrawRound(g, Border, card, Df(16), Df(1));
        }
        int y = pad;
        if (g != null) DrawText(g, "ARMONÍA DE COLORES", F(8.25f, FontStyle.Bold), Muted, new Rectangle(pad, y, inner, D(18)), TextFormatFlags.SingleLine);
        y += D(24);

        var lc = LevelColor(result.Level);
        var tf = F(10.5f, FontStyle.Bold);
        int bw = TextWidth(result.Title, tf) + D(26);
        if (g != null)
        {
            var br = new RectangleF(pad, y, bw, D(32));
            FillRound(g, LevelSoft(result.Level), br, br.Height / 2);
            using (var dot = new SolidBrush(lc)) g.FillEllipse(dot, br.X + Df(10), br.Y + (br.Height - Df(8)) / 2, Df(8), Df(8));
            DrawText(g, result.Title, tf, lc, Rectangle.Round(new RectangleF(br.X + Df(14), br.Y, br.Width - Df(14), br.Height)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (!string.IsNullOrEmpty(result.Verdict))
                DrawText(g, result.Verdict, F(9.5f, FontStyle.Bold), Theme.Text, new Rectangle(pad + bw + D(10), y, Math.Max(0, inner - bw - D(10)), D(32)), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        y += D(32) + D(12);

        if (items.Count > 0)
        {
            int x = pad, rowH = D(28);
            var cf = F(8.5f);
            foreach (var (c, p) in items)
            {
                int w = D(22) + TextWidth(p.ColorNombre, cf) + D(14);
                if (x > pad && x + w > pad + inner) { x = pad; y += rowH + D(6); }
                if (g != null)
                {
                    var r = new RectangleF(x, y, w, rowH);
                    FillRound(g, Bg, r, r.Height / 2);
                    float s = Df(14);
                    Swatch(g, p.Color, new RectangleF(r.X + Df(7), r.Y + (r.Height - s) / 2, s, s));
                    DrawText(g, p.ColorNombre, cf, Theme.Text, Rectangle.Round(new RectangleF(r.X + Df(24), r.Y, r.Width - Df(26), r.Height)), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                x += w + D(6);
            }
            y += rowH + D(12);
        }

        var ef = F(9.75f);
        int eh = TextHeight(result.Explanation, ef, inner);
        if (g != null) DrawText(g, result.Explanation, ef, Theme.Text, new Rectangle(pad, y, inner, eh), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        y += eh;

        if (!string.IsNullOrEmpty(result.Suggestion))
        {
            y += D(12);
            var sf = F(9.5f);
            int sp = D(12);
            int sh = TextHeight(result.Suggestion, sf, inner - sp * 2);
            int boxH = sp + D(20) + sh + sp;
            if (g != null)
            {
                var sr = new RectangleF(pad, y, inner, boxH);
                FillRound(g, PrimarySoft, sr, Df(12));
                DrawText(g, "Sugerencia", F(9f, FontStyle.Bold), Primary, new Rectangle(pad + sp, y + sp, inner - sp * 2, D(20)), TextFormatFlags.SingleLine);
                DrawText(g, result.Suggestion, sf, Theme.Text, new Rectangle(pad + sp, y + sp + D(20), inner - sp * 2, sh), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            }
            y += boxH;
        }
        h = y + pad;
        return h;
    }
}
