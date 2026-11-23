using static FitLab.Theme;

namespace FitLab;

/// <summary>Base para los diálogos: contenido con desplazamiento y barra de acciones fija abajo.</summary>
public class SheetForm : Form
{
    protected readonly ScrollHost Body = new() { Dock = DockStyle.Fill };
    protected readonly Panel Footer = new() { Dock = DockStyle.Bottom, BackColor = Surface };

    public SheetForm(string title, int width, int height)
    {
        Text = title;
        BackColor = Bg;
        Font = F(9.75f);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 900);
        ClientSize = new Size(Math.Min(width, wa.Width - 40), Math.Min(height, wa.Height - 60));
        MinimumSize = new Size(D(340), D(360));
        Footer.Height = D(72);
        Footer.Padding = new Padding(D(16), D(14), D(16), D(14));
        Footer.Paint += (_, e) => { using var pen = new Pen(Border); e.Graphics.DrawLine(pen, 0, 0, Footer.Width, 0); };
        Controls.Add(Body);
        Controls.Add(Footer);
        Body.BringToFront();
    }

    protected void SetFooter(params Control[] buttons)
    {
        var row = new HRow(buttons) { Dock = DockStyle.Fill, Gap = D(8) };
        Footer.Controls.Add(row);
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); Body.Relayout(); }

    /// <summary>Esc cierra el diálogo como "Cancelar".</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>Elegir una prenda del guardarropa para una ranura del probador.</summary>
public class GarmentPickerForm : SheetForm
{
    public Prenda? Chosen;
    readonly List<Prenda> options;
    readonly Prenda? current;
    readonly GarmentGrid grid = new() { MinCardWidth = D(130) };
    readonly InputBox search = new("Buscar por nombre…", maxLength: 60);
    readonly TextBlock none = new("", 9.5f, FontStyle.Regular, Muted);

    public GarmentPickerForm(Category cat, List<Prenda> options, Prenda? current)
        : base("Elegir " + Cats.Label(cat).ToLowerInvariant(), D(460), D(720))
    {
        this.options = options;
        this.current = current;
        var s = Body.Stack;
        s.MaxContentWidth = D(760);
        s.Controls.Add(new TextBlock(Cats.Label(cat), 16f, FontStyle.Bold));

        if (options.Count == 0)
        {
            var empty = new EmptyState($"Todavía no tenés prendas de {Cats.Label(cat).ToLowerInvariant()}",
                "Subí una al guardarropa y la vas a poder probar en el maniquí.", "Agregar una ahora");
            empty.Action.Click += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
            s.Controls.Add(empty);
        }
        else
        {
            s.Controls.Add(new TextBlock("Tocá la prenda que querés probarte.", 9.5f, FontStyle.Regular, Muted));
            if (options.Count > 6) s.Controls.Add(search);
            s.Controls.Add(none);
            s.Controls.Add(grid);
            search.Box.TextChanged += (_, _) => Fill();
            Fill();
        }

        var cancel = new FlatButton("Cancelar", BtnKind.Ghost);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        if (current != null)
        {
            var remove = new FlatButton("Quitar del maniquí", BtnKind.Danger);
            remove.Click += (_, _) => { DialogResult = DialogResult.Abort; Close(); };
            SetFooter(cancel, remove);
        }
        else if (options.Count > 0)
        {
            var add = new FlatButton("+ Nueva prenda", BtnKind.Secondary);
            add.Click += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
            SetFooter(cancel, add);
        }
        else SetFooter(cancel);
    }

    void Fill()
    {
        var t = search.Value.Trim();
        var list = options.Where(p => t.Length == 0 || p.Nombre.Contains(t, StringComparison.CurrentCultureIgnoreCase)).ToList();
        none.Text = list.Count == 0 ? "No encontramos prendas con ese nombre." : "";
        grid.SetCards(list.Select(p =>
        {
            var card = new GarmentCard(p) { Badge = current != null && current.Id == p.Id ? "Puesta" : null };
            card.Click += (_, _) => { Chosen = p; DialogResult = DialogResult.OK; Close(); };
            return card;
        }));
        Body.Relayout();
    }
}

/// <summary>Guardar la combinación actual como outfit.</summary>
public class SaveOutfitForm : SheetForm
{
    public string OutfitName = "";
    public string Occasion = "casual";
    public bool Favorite;
    readonly InputBox name = new("Ej.: Lunes de facultad", maxLength: 50);
    readonly TextBlock nameError = new("", 9f, FontStyle.Regular, Danger);
    readonly ChipGroup occasion = new();
    readonly ChipGroup fav = new() { AllowNone = true };

    public SaveOutfitForm(int number) : base("Guardar outfit", D(440), D(520))
    {
        var s = Body.Stack;
        s.Gap = D(8);
        s.Controls.Add(new TextBlock("Guardar outfit", 16f, FontStyle.Bold));
        s.Controls.Add(new TextBlock("Ponele un nombre y elegí para qué ocasión es.", 9.5f, FontStyle.Regular, Muted));
        s.Controls.Add(Ui.Space(D(6)));
        s.Controls.Add(new TextBlock("Nombre *", 9.5f, FontStyle.Bold));
        s.Controls.Add(name);
        s.Controls.Add(nameError);
        s.Controls.Add(Ui.Space(D(6)));
        s.Controls.Add(new TextBlock("Ocasión *", 9.5f, FontStyle.Bold));
        foreach (var (k, l) in Cats.Occasions) occasion.Add(l, k);
        occasion.SelectedIndex = 0;
        s.Controls.Add(occasion);
        s.Controls.Add(Ui.Space(D(6)));
        fav.Add("★  Marcar como favorito", true);
        s.Controls.Add(fav);

        name.Box.AccessibleName = "Nombre del outfit";
        name.Value = $"Outfit {number}";
        name.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); } };
        var cancel = new FlatButton("Cancelar", BtnKind.Ghost);
        var save = new FlatButton("Guardar");
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        save.Click += (_, _) => Save();
        SetFooter(cancel, save);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        name.Box.Focus();
        name.Box.SelectAll();
    }

    void Save()
    {
        var n = name.Value.Trim();
        if (n.Length == 0)
        {
            nameError.Text = "Poné un nombre para encontrar el outfit después.";
            name.HasError = true;
            name.Invalidate();
            Body.Relayout();
            return;
        }
        OutfitName = n;
        Occasion = occasion.SelectedValue as string ?? "casual";
        Favorite = fav.SelectedIndex == 0;
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Muestra una sola vez el código de recuperación de la cuenta, con opción de copiarlo.</summary>
public class RecoveryCodeForm : SheetForm
{
    readonly TextBlock copied = new("", 9f, FontStyle.Bold, Success);

    /// <summary>Entradas: el código a mostrar y un texto que explica por qué aparece ahora.</summary>
    public RecoveryCodeForm(string code, string intro) : base("Código de recuperación", D(440), D(560))
    {
        var s = Body.Stack;
        s.Gap = D(10);
        s.Controls.Add(new TextBlock("Guardá tu código de recuperación", 16f, FontStyle.Bold));
        s.Controls.Add(new TextBlock(intro, 9.5f, FontStyle.Regular, Muted));
        s.Controls.Add(Ui.Space(D(4)));

        var box = new InputBox("", maxLength: 20);
        box.Box.ReadOnly = true;
        box.Box.TabStop = true;
        box.Box.Font = F(15f, FontStyle.Bold);
        box.Box.TextAlign = HorizontalAlignment.Center;
        box.Box.AccessibleName = "Código de recuperación";
        box.Value = code;
        s.Controls.Add(box);

        var copy = new FlatButton("Copiar código", BtnKind.Secondary) { PreferredHeight = D(40) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(code); copied.Text = "Copiado. Pegalo en un lugar seguro."; }
            catch (System.Runtime.InteropServices.ExternalException) { copied.Text = "No pudimos copiarlo. Seleccioná el código y anotalo a mano."; }
            Body.Relayout();
        };
        s.Controls.Add(copy);
        s.Controls.Add(copied);
        s.Controls.Add(Ui.Space(D(4)));
        s.Controls.Add(new TextBlock("Si olvidás la contraseña, en la pantalla de inicio tocá «¿Olvidaste tu contraseña?» y escribí este código. "
            + "Sirve una sola vez: al usarlo te damos uno nuevo.", 9.5f));
        s.Controls.Add(new TextBlock("Anotalo en papel o en tu gestor de contraseñas: no lo vamos a volver a mostrar.", 9.5f, FontStyle.Bold, Danger));

        var ok = new FlatButton("Ya lo guardé");
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        SetFooter(ok);
    }
}
