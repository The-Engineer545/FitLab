using static FitLab.Theme;

namespace FitLab;

/// <summary>Lista desplegable con borde redondeado.</summary>
public class StyledCombo : PaintedControl, IMeasure
{
    public readonly ComboBox Combo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Surface,
        ForeColor = Theme.Text,
    };

    public StyledCombo(params string[] items)
    {
        Combo.Font = F(9.5f);
        Combo.Items.AddRange(items);
        if (items.Length > 0) { Combo.SelectedIndex = 0; Combo.AccessibleName = "Filtrar por " + items[0].ToLowerInvariant(); }
        Combo.GotFocus += (_, _) => { Invalidate(); Ui.Reveal(this); };
        Combo.LostFocus += (_, _) => Invalidate();
        Controls.Add(Combo);
        Combo.MouseWheel += (_, e) => ((HandledMouseEventArgs)e).Handled = !Combo.DroppedDown;
    }

    public int MeasureHeight(int width) => Math.Max(D(44), Combo.PreferredHeight + D(14));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int px = D(6);
        Combo.SetBounds(px, (Height - Combo.Height) / 2, Width - px * 2, Combo.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Behind);
        HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, Surface, r, Df(11));
        DrawRound(g, Combo.Focused ? Primary : Outline, r, Df(11), Combo.Focused ? Df(1.6f) : Df(1));
    }
}

/// <summary>Módulo 1: guardarropa digital con búsqueda y filtros.</summary>
public class WardrobeView : ScrollHost, IView
{
    readonly MainForm main;
    readonly TextBlock count = new("", 9.5f, FontStyle.Regular, Muted);
    readonly FlatButton add = new("+ Agregar prenda");
    readonly InputBox search = new("Buscar por nombre…", maxLength: 60);
    readonly StyledCombo catBox, colorBox, seasonBox;
    readonly HRow filters;
    readonly FlatButton clear = new("Limpiar filtros", BtnKind.Ghost) { PreferredHeight = D(36) };
    readonly GarmentGrid grid = new();
    readonly EmptyState empty = new("Todavía no tenés prendas", "Subí la primera: sacale una foto o elegila de tu galería, y empezá a armar tu guardarropa digital.", "Subir mi primera prenda");
    readonly EmptyState noMatch = new("No hay prendas con esos filtros", "Probá con otra categoría, color o temporada, o buscá con otro nombre.", "Limpiar filtros");
    bool refreshing;

    public WardrobeView(MainForm m)
    {
        main = m;
        Stack.MaxContentWidth = D(1100);
        Stack.Controls.Add(new TextBlock("Mi guardarropa", 17f, FontStyle.Bold));
        Stack.Controls.Add(count);
        Stack.Controls.Add(add);
        Stack.Controls.Add(search);

        var cats = new List<string> { "Categoría" };
        cats.AddRange(Cats.All.Select(Cats.Label));
        catBox = new StyledCombo(cats.ToArray());
        colorBox = new StyledCombo("Color");
        seasonBox = new StyledCombo("Temporada", "Todo el año", "Calor", "Frío");
        filters = new HRow(catBox, colorBox, seasonBox) { Gap = D(8) };
        Stack.Controls.Add(filters);
        Stack.Controls.Add(clear);
        Stack.Controls.Add(empty);
        Stack.Controls.Add(noMatch);
        Stack.Controls.Add(grid);

        add.Click += (_, _) => { main.EditGarment(null); };
        empty.Action.Click += (_, _) => { main.EditGarment(null); };
        noMatch.Action.Click += (_, _) => ClearFilters();
        clear.Click += (_, _) => ClearFilters();
        search.Box.AccessibleName = "Buscar prendas por nombre";
        search.Box.TextChanged += (_, _) => Apply();
        foreach (var c in new[] { catBox, colorBox, seasonBox })
            c.Combo.SelectedIndexChanged += (_, _) => { if (!refreshing) Apply(); };
        main.State.Changed += () => { if (Visible) ShowView(); };
    }

    void ClearFilters()
    {
        refreshing = true;
        search.Value = "";
        catBox.Combo.SelectedIndex = colorBox.Combo.SelectedIndex = seasonBox.Combo.SelectedIndex = 0;
        refreshing = false;
        Apply();
    }

    public void ShowView()
    {
        refreshing = true;
        var prev = colorBox.Combo.SelectedItem as string;
        colorBox.Combo.Items.Clear();
        colorBox.Combo.Items.Add("Color");
        foreach (var n in main.State.Prendas.Select(p => p.ColorNombre).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n))
            colorBox.Combo.Items.Add(n);
        int idx = prev == null ? 0 : colorBox.Combo.Items.IndexOf(prev);
        colorBox.Combo.SelectedIndex = idx < 0 ? 0 : idx;
        refreshing = false;
        Apply();
    }

    void Apply()
    {
        var all = main.State.Prendas;
        int n = all.Count;
        count.Text = n == 0 ? "Acá vas a ver todas tus prendas." : n == 1 ? "1 prenda en tu guardarropa" : $"{n} prendas en tu guardarropa";

        IEnumerable<Prenda> q = all;
        var text = search.Value.Trim();
        if (text.Length > 0) q = q.Where(p => p.Nombre.Contains(text, StringComparison.CurrentCultureIgnoreCase));
        if (catBox.Combo.SelectedIndex > 0) { var c = Cats.All[catBox.Combo.SelectedIndex - 1]; q = q.Where(p => p.Categoria == c); }
        if (colorBox.Combo.SelectedIndex > 0) { var cn = colorBox.Combo.SelectedItem as string; q = q.Where(p => string.Equals(p.ColorNombre, cn, StringComparison.OrdinalIgnoreCase)); }
        if (seasonBox.Combo.SelectedIndex > 0) { var s = (Season)(seasonBox.Combo.SelectedIndex - 1); q = q.Where(p => p.Temporada == s); }
        var list = q.ToList();

        bool filtering = text.Length > 0 || catBox.Combo.SelectedIndex > 0 || colorBox.Combo.SelectedIndex > 0 || seasonBox.Combo.SelectedIndex > 0;
        empty.Visible = n == 0;
        search.Visible = filters.Visible = n > 0;
        clear.Visible = n > 0 && filtering && list.Count > 0;
        noMatch.Visible = n > 0 && list.Count == 0;
        grid.Visible = list.Count > 0;

        grid.SetCards(list.Select(MakeCard));
        Relayout();
    }

    void Later(Action a) => BeginInvoke(a);

    GarmentCard MakeCard(Prenda p)
    {
        var card = new GarmentCard(p);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Editar", null, (_, _) => Later(() => main.EditGarment(p)));
        menu.Items.Add("Probar en el probador", null, (_, _) => Later(() => main.TryOn(p)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Eliminar", null, (_, _) => Later(() => GarmentEditorForm.ConfirmDelete(main, main.State.UserId, p, () => main.State.Reload())));
        card.ContextMenuStrip = menu;
        card.Disposed += (_, _) => menu.Dispose();
        card.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Later(() => main.EditGarment(p)); };
        card.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) Later(() => main.EditGarment(p)); };
        card.AccessibleDescription = "Enter para editarla. Mayús+F10 abre el menú con más acciones.";
        return card;
    }
}
