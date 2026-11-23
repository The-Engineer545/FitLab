using static FitLab.Theme;

namespace FitLab;

/// <summary>Lista de outfits guardados, con favoritos.</summary>
public class OutfitsView : ScrollHost, IView
{
    readonly MainForm main;
    readonly TextBlock count = new("", 9.5f, FontStyle.Regular, Muted);
    readonly ChipGroup filter = new();
    readonly VStack list = new() { Gap = D(12) };
    readonly EmptyState empty = new("Todavía no guardaste outfits", "Armá una combinación en el Probador y tocá «Guardar outfit». Acá vas a ver todos tus looks.", "Ir al probador");
    readonly EmptyState noFav = new("No tenés favoritos todavía", "Tocá la estrella de un outfit para marcarlo como favorito.");

    AppState S => main.State;

    public OutfitsView(MainForm m)
    {
        main = m;
        Stack.MaxContentWidth = D(760);
        Stack.Controls.Add(new TextBlock("Mis outfits", 17f, FontStyle.Bold));
        Stack.Controls.Add(count);
        filter.Add("Todos", false);
        filter.Add("★ Favoritos", true);
        filter.SelectedIndex = 0;
        filter.Changed += Fill;
        Stack.Controls.Add(filter);
        Stack.Controls.Add(empty);
        Stack.Controls.Add(noFav);
        Stack.Controls.Add(list);
        empty.Action.Click += (_, _) => main.ShowTab(1);
        main.State.Changed += () => { if (Visible) Fill(); };
    }

    public void ShowView() => Fill();

    void Fill()
    {
        int n = S.Outfits.Count, favs = S.Outfits.Count(o => o.Favorito);
        count.Text = n == 0 ? "Tus combinaciones guardadas aparecen acá." : $"{n} {(n == 1 ? "outfit guardado" : "outfits guardados")} · {favs} {(favs == 1 ? "favorito" : "favoritos")}";
        bool onlyFav = filter.SelectedValue is true;
        var items = S.Outfits.Where(o => !onlyFav || o.Favorito).ToList();
        empty.Visible = n == 0;
        filter.Visible = n > 0;
        noFav.Visible = n > 0 && items.Count == 0;

        list.SuspendLayout();
        var old = list.Controls.Cast<Control>().ToList();
        list.Controls.Clear();
        foreach (var c in old) c.Dispose();
        foreach (var o in items)
        {
            var card = new OutfitCard(o);
            card.FavToggled += () => BeginInvoke(new Action(() =>
            {
                Db.SetFavorito(S.UserId, o.Id, !o.Favorito);
                S.Reload();
            }));
            card.DeleteClicked += () => BeginInvoke(new Action(() =>
            {
                if (!Ui.Confirm(this, $"¿Eliminar el outfit «{o.Nombre}»? Las prendas siguen en tu guardarropa.", "Eliminar outfit")) return;
                Db.DeleteOutfit(S.UserId, o.Id);
                S.Reload();
            }));
            card.Opened += () => BeginInvoke(new Action(() => main.OpenOutfit(o)));
            list.Controls.Add(card);
        }
        list.ResumeLayout();
        Relayout();
    }
}

public class OutfitCard : PaintedControl, IMeasure
{
    readonly Outfit o;
    readonly HarmonyResult harmony;
    public event Action? FavToggled, DeleteClicked, Opened;
    int hoverZone; // 0 nada, 1 tarjeta, 2 estrella, 3 papelera

    public OutfitCard(Outfit o)
    {
        this.o = o;
        harmony = ColorTools.Analyze(o.Prendas);
        Cursor = Cursors.Hand;
        Ui.Tip(this, "Tocá para abrirlo en el probador");
        MakeFocusable(AccessibleRole.PushButton, $"Outfit {o.Nombre}, {Cats.OccasionLabel(o.Ocasion)}{(o.Favorito ? ", favorito" : "")}", keyActivates: false);
        AccessibleDescription = "Enter para abrirlo en el probador, F para marcarlo como favorito, Suprimir para eliminarlo.";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Enter or Keys.Space: Opened?.Invoke(); break;
            case Keys.F: FavToggled?.Invoke(); break;
            case Keys.Delete: DeleteClicked?.Invoke(); break;
            default: return;
        }
        e.Handled = true;
    }

    int Pad => D(16);
    int Thumb(int width) => Math.Min(D(72), (width - Pad * 2 - D(8) * 4) / 5);

    public int MeasureHeight(int width) => Pad + D(26) + D(6) + D(26) + D(12) + Thumb(width) + Pad;

    RectangleF StarRect => new(Width - Pad - D(76), Pad - D(4), D(34), D(34));
    RectangleF TrashRect => new(Width - Pad - D(34), Pad - D(4), D(34), D(34));

    int Zone(Point p) => StarRect.Contains(p) ? 2 : TrashRect.Contains(p) ? 3 : 1;

    protected override void OnMouseMove(MouseEventArgs e) { int z = Zone(e.Location); if (z != hoverZone) { hoverZone = z; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hoverZone = 0; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        switch (Zone(e.Location))
        {
            case 2: FavToggled?.Invoke(); break;
            case 3: DeleteClicked?.Invoke(); break;
            default: Opened?.Invoke(); break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Behind);
        HQ(g);
        var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, Surface, card, Df(16));
        bool hot = hoverZone == 1 || FocusCue;
        DrawRound(g, hot ? Primary : Border, card, Df(16), FocusCue ? Df(2.5f) : hot ? Df(1.5f) : Df(1));

        int pad = Pad, y = pad;
        int titleW = Width - pad * 2 - D(84);
        DrawText(g, o.Nombre, F(11.5f, FontStyle.Bold), Theme.Text, new Rectangle(pad, y, titleW, D(26)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

        var sr = StarRect;
        if (hoverZone == 2) FillRound(g, WarningSoft, sr, sr.Height / 2);
        Icons.Star(g, RectangleF.Inflate(sr, -Df(8), -Df(8)), o.Favorito ? Color.FromArgb(232, 170, 30) : Muted, Df(1.6f), o.Favorito);
        var tr = TrashRect;
        if (hoverZone == 3) FillRound(g, DangerSoft, tr, tr.Height / 2);
        Icons.Trash(g, RectangleF.Inflate(tr, -Df(10), -Df(9)), hoverZone == 3 ? Danger : Muted, Df(1.6f));

        y += D(26) + D(6);
        var occ = Cats.OccasionLabel(o.Ocasion);
        var cf = F(8.5f, FontStyle.Bold);
        int cw = TextWidth(occ, cf) + D(20);
        var chip = new RectangleF(pad, y, cw, D(24));
        FillRound(g, PrimarySoft, chip, chip.Height / 2);
        DrawText(g, occ, cf, Primary, Rectangle.Round(chip), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        var lc = LevelColor(harmony.Level);
        using (var dot = new SolidBrush(lc)) g.FillEllipse(dot, pad + cw + D(10), y + D(8), Df(8), Df(8));
        DrawText(g, harmony.Title + " · " + o.Creado.ToString("dd/MM/yyyy"), F(8.5f), Muted,
            new Rectangle(pad + cw + D(22), y, Width - pad * 2 - cw - D(22), D(24)), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        y += D(26) + D(12);
        int t = Thumb(Width), x = pad;
        foreach (var c in Cats.All)
        {
            var r = new RectangleF(x, y, t, t);
            if (o.Prendas.TryGetValue(c, out var p))
            {
                var img = Photos.Get(p.FotoPath);
                if (img != null) DrawCover(g, img, r, Df(10));
                else FillRound(g, p.Color, r, Df(10));
                DrawRound(g, Border, r, Df(10), Df(1));
                float s = Df(12);
                Swatch(g, p.Color, new RectangleF(r.Right - s - Df(4), r.Bottom - s - Df(4), s, s));
            }
            else
            {
                FillRound(g, Bg, r, Df(10));
                DrawText(g, "—", F(10f), Border, Rectangle.Round(r), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            x += t + D(8);
        }
    }
}
