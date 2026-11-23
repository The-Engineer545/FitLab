using System.Diagnostics;
using static FitLab.Theme;

namespace FitLab;

/// <summary>Formulario para subir o editar una prenda.</summary>
public class GarmentEditorForm : Form
{
    readonly long userId;
    readonly Prenda prenda;
    readonly bool isNew;
    Image? newImage;
    Image? shownCopy; // copia propia de la foto ya guardada, para no depender de la caché de miniaturas
    /// <summary>Id de la prenda guardada, o 0 si el formulario se canceló o la prenda se eliminó.</summary>
    public long SavedId { get; private set; }
    bool preferCameraRoll;
    string? colorHex;
    bool autoName = true;

    readonly ScrollHost scroll = new() { Dock = DockStyle.Fill };
    readonly PhotoPicker photo;
    readonly TextBlock photoError = Err();
    readonly TextBlock cameraHint = new("", 9f, FontStyle.Regular, Muted);
    readonly InputBox name = new("Ej.: Remera blanca básica", maxLength: 60);
    readonly TextBlock nameError = Err();
    readonly ChipGroup category = new();
    readonly TextBlock categoryError = Err();
    readonly WrapPanel swatches = new() { GapX = D(4), GapY = D(4) };
    readonly ColorPreview preview = new();
    readonly InputBox colorName = new("Nombre del color", maxLength: 30);
    readonly TextBlock colorError = Err();
    readonly ChipGroup season = new();
    readonly InputBox notes = new("Ej.: talle M, comprada en 2025, lavar en frío…", multiline: true, maxLength: 300);
    readonly TextBlock summary = Err();

    static TextBlock Err() => new("", 9f, FontStyle.Regular, Danger);
    static TextBlock Label(string t) => new(t, 9.5f, FontStyle.Bold);

    public GarmentEditorForm(long userId, Prenda? existing, Category? preset)
    {
        this.userId = userId;
        isNew = existing == null;
        prenda = existing ?? new Prenda { UsuarioId = userId, Temporada = Season.TodoElAnio };

        Text = isNew ? "Nueva prenda" : "Editar prenda";
        BackColor = Bg;
        Font = F(9.75f);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 900);
        ClientSize = new Size(Math.Min(D(460), wa.Width - 40), Math.Min(D(820), wa.Height - 60));
        MinimumSize = new Size(D(360), D(480));

        var s = scroll.Stack;
        s.MaxContentWidth = D(560);
        s.Gap = D(8);
        s.Controls.Add(new TextBlock(isNew ? "Nueva prenda" : "Editar prenda", 16f, FontStyle.Bold));
        s.Controls.Add(new TextBlock("Los campos con * son obligatorios.", 9f, FontStyle.Regular, Muted));
        s.Controls.Add(Ui.Space(D(4)));

        s.Controls.Add(Label("Foto *"));
        photo = new PhotoPicker();
        photo.Clicked += () => ChooseFile();
        photo.FileDropped += f => LoadImage(f);
        s.Controls.Add(photo);
        var gallery = new FlatButton("Elegir de la galería", BtnKind.Secondary) { PreferredHeight = D(40) };
        var camera = new FlatButton("Usar la cámara", BtnKind.Ghost) { PreferredHeight = D(40) };
        gallery.Click += (_, _) => ChooseFile();
        camera.Click += (_, _) => OpenCamera();
        s.Controls.Add(new HRow(gallery, camera) { Gap = D(8) });
        s.Controls.Add(cameraHint);
        s.Controls.Add(photoError);

        s.Controls.Add(Ui.Space(D(6)));
        s.Controls.Add(Label("Nombre *"));
        s.Controls.Add(name);
        s.Controls.Add(nameError);

        s.Controls.Add(Ui.Space(D(6)));
        s.Controls.Add(Label("Categoría *"));
        foreach (var c in Cats.All) category.Add(Cats.Label(c), c);
        category.Changed += () => categoryError.Text = "";
        s.Controls.Add(category);
        s.Controls.Add(categoryError);

        s.Controls.Add(Ui.Space(D(6)));
        s.Controls.Add(Label("Color principal *"));
        s.Controls.Add(new TextBlock("Tocá un color o elegí «Otro color» para buscar el tono exacto.", 9f, FontStyle.Regular, Muted));
        foreach (var (n, hex) in ColorTools.Palette)
        {
            var sw = new SwatchButton(ColorTools.FromHex(hex), n, D(38));
            Ui.Tip(sw, n);
            sw.Click += (_, _) => PickColor(sw.Color, n, fromPalette: true);
            swatches.Controls.Add(sw);
        }
        var other = new ChipButton("Otro color…");
        other.Click += (_, _) => CustomColor();
        swatches.Controls.Add(other);
        s.Controls.Add(swatches);
        preview.Click += (_, _) => CustomColor();
        s.Controls.Add(new HRow(preview, colorName) { Weights = new[] { 1f, 5f }, Gap = D(8) });
        colorName.Box.TextChanged += (_, _) => { if (colorName.Box.Focused) autoName = false; colorError.Text = ""; };
        s.Controls.Add(colorError);

        s.Controls.Add(Ui.Space(D(6)));
        s.Controls.Add(Label("Temporada"));
        season.Add("Todo el año", Season.TodoElAnio);
        season.Add("Calor", Season.Calor);
        season.Add("Frío", Season.Frio);
        s.Controls.Add(season);

        s.Controls.Add(Ui.Space(D(6)));
        s.Controls.Add(Label("Notas (opcional)"));
        s.Controls.Add(notes);
        s.Controls.Add(summary);

        if (!isNew)
        {
            s.Controls.Add(Ui.Space(D(10)));
            var del = new FlatButton("Eliminar prenda", BtnKind.Danger) { PreferredHeight = D(40) };
            del.Click += (_, _) => ConfirmDelete(this, userId, prenda, () => { DialogResult = DialogResult.Abort; Close(); });
            s.Controls.Add(del);
        }

        // Barra inferior fija con las acciones principales.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = D(72), BackColor = Surface, Padding = new Padding(D(16), D(14), D(16), D(14)) };
        footer.Paint += (_, e) => { using var pen = new Pen(Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        var cancel = new FlatButton("Cancelar", BtnKind.Ghost);
        var save = new FlatButton(isNew ? "Guardar prenda" : "Guardar cambios");
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        save.Click += (_, _) => Save();
        var row = new HRow(cancel, save) { Dock = DockStyle.Fill, Weights = new[] { 1f, 1.6f } };
        footer.Controls.Add(row);

        Controls.Add(scroll);
        Controls.Add(footer);
        scroll.BringToFront();

        // Valores iniciales
        name.Value = prenda.Nombre;
        notes.Value = prenda.Notas;
        if (!isNew) category.SelectedValue = prenda.Categoria;
        else if (preset is Category pc) category.SelectedValue = pc;
        season.SelectedValue = prenda.Temporada;
        if (!isNew)
        {
            autoName = false;
            PickColor(prenda.Color, prenda.ColorNombre, fromPalette: false);
            var cached = Photos.Get(prenda.FotoPath);
            if (cached != null) photo.Image = shownCopy = new Bitmap(cached);
        }
        else preview.Value = null;

        name.Box.AccessibleName = "Nombre de la prenda";
        colorName.Box.AccessibleName = "Nombre del color";
        notes.Box.AccessibleName = "Notas";
    }

    /// <summary>Esc cierra el formulario como "Cancelar".</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        scroll.Relayout();
        if (isNew) name.Box.Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        newImage?.Dispose();
        shownCopy?.Dispose();
    }

    void PickColor(Color c, string n, bool fromPalette)
    {
        colorHex = ColorTools.ToHex(c);
        preview.Value = c;
        foreach (var sw in swatches.Controls.OfType<SwatchButton>()) sw.Selected = ColorTools.ToHex(sw.Color) == colorHex;
        if (fromPalette || autoName || colorName.Value.Trim().Length == 0)
        {
            colorName.Value = n;
            autoName = true;
        }
        colorError.Text = "";
        scroll.Relayout();
    }

    void CustomColor()
    {
        using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
        if (colorHex != null) dlg.Color = ColorTools.FromHex(colorHex);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            PickColor(dlg.Color, ColorTools.NearestName(dlg.Color), fromPalette: false);
    }

    void ChooseFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Elegí la foto de la prenda",
            // Se arma con la misma lista que acepta el backend, para que nunca queden desalineadas.
            Filter = "Imágenes (JPG, PNG, WebP…)|" + string.Join(";", Photos.Allowed.Select(e => "*" + e)) + "|Todos los archivos|*.*",
        };
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var roll = Path.Combine(pics, "Camera Roll");
        var alt = Path.Combine(pics, "Álbum de cámara");
        dlg.InitialDirectory = preferCameraRoll && Directory.Exists(roll) ? roll : preferCameraRoll && Directory.Exists(alt) ? alt : pics;
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadImage(dlg.FileName);
    }

    void LoadImage(string file)
    {
        try
        {
            var img = Photos.LoadForImport(file);
            newImage?.Dispose();
            newImage = img;
            photo.Image = img;
            photoError.Text = "";
            photo.HasError = false;
            if (name.Value.Trim().Length == 0)
            {
                var guess = Path.GetFileNameWithoutExtension(file);
                if (guess.Length <= 40 && !guess.StartsWith("IMG", StringComparison.OrdinalIgnoreCase) && !guess.StartsWith("WIN_", StringComparison.OrdinalIgnoreCase))
                    name.Value = guess.Replace('_', ' ').Replace('-', ' ');
            }
        }
        catch (UserError ex)
        {
            photoError.Text = ex.Message;
            photo.HasError = true;
        }
        photo.Invalidate();
        scroll.Relayout();
    }

    void OpenCamera()
    {
        try
        {
            Process.Start(new ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true });
            preferCameraRoll = true;
            cameraHint.Text = "Se abrió la app Cámara. Cuando saques la foto, tocá «Elegir de la galería»: se abre en la carpeta de fotos de la cámara.";
        }
        catch
        {
            cameraHint.Text = "No encontramos una cámara en esta computadora. Podés sacar la foto con el celular, pasarla a la computadora y elegirla desde la galería.";
        }
        scroll.Relayout();
    }

    void Save()
    {
        bool ok = true;
        photoError.Text = nameError.Text = categoryError.Text = colorError.Text = summary.Text = "";
        if (newImage == null && string.IsNullOrEmpty(prenda.FotoPath))
        { photoError.Text = "Subí una foto de la prenda para poder verla en el probador."; photo.HasError = true; ok = false; }
        var n = name.Value.Trim();
        if (n.Length == 0) { nameError.Text = "Poné un nombre para reconocer la prenda."; name.HasError = true; ok = false; }
        var cat = category.SelectedValue as Category?;
        if (cat == null) { categoryError.Text = "Elegí una categoría."; ok = false; }
        var cn = colorName.Value.Trim();
        if (colorHex == null) { colorError.Text = "Elegí el color principal de la prenda."; ok = false; }
        else if (cn.Length == 0) { colorError.Text = "Escribí un nombre para el color (por ejemplo: azul marino)."; colorName.HasError = true; ok = false; }

        if (!ok || cat == null || colorHex == null)
        {
            summary.Text = "Revisá los campos marcados en rojo.";
            name.Invalidate(); colorName.Invalidate(); photo.Invalidate();
            scroll.Relayout();
            return;
        }

        // Se guarda una copia: la prenda original (que comparten las pantallas) no cambia si algo falla.
        string? savedPhoto = null;
        try
        {
            if (newImage != null) savedPhoto = Photos.Save(newImage, userId);
            var edit = new Prenda
            {
                Id = prenda.Id,
                UsuarioId = userId,
                Creado = prenda.Creado,
                FotoPath = savedPhoto ?? prenda.FotoPath,
                Nombre = n,
                Categoria = cat.Value,
                ColorHex = colorHex,
                ColorNombre = char.ToUpper(cn[0]) + cn.Substring(1),
                Temporada = (Season)(season.SelectedValue ?? Season.TodoElAnio),
                Notas = notes.Value.Trim(),
            };
            Db.SavePrenda(edit);
            if (savedPhoto != null && !string.IsNullOrEmpty(prenda.FotoPath)) Photos.Delete(prenda.FotoPath);
            SavedId = edit.Id;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (UserError ex)
        {
            if (savedPhoto != null) Photos.Delete(savedPhoto);
            summary.Text = ex.Message;
            scroll.Relayout();
        }
        catch (Exception ex)
        {
            if (savedPhoto != null) Photos.Delete(savedPhoto);
            summary.Text = "No pudimos guardar la prenda. Revisá que haya espacio en el disco y probá de nuevo. (" + ex.Message + ")";
            scroll.Relayout();
        }
    }

    public static void ConfirmDelete(IWin32Window owner, long userId, Prenda p, Action after)
    {
        int used = Db.OutfitsUsing(userId, p.Id);
        var msg = $"¿Eliminar «{p.Nombre}»? Esta acción no se puede deshacer.";
        if (used > 0)
            msg += (used == 1 ? "\n\nTambién se va a quitar de 1 outfit guardado." : $"\n\nTambién se va a quitar de {used} outfits guardados.")
                + " Si un outfit queda sin prendas, se elimina.";
        if (!Ui.Confirm(owner, msg, "Eliminar prenda")) return;
        try { Db.DeletePrenda(userId, p); after(); }
        catch (Exception ex) { Ui.Error(owner, "No pudimos eliminar la prenda. Probá de nuevo. (" + ex.Message + ")"); }
    }

    /// <summary>Muestra la foto elegida o una invitación a elegirla. Acepta arrastrar un archivo.</summary>
    class PhotoPicker : PaintedControl, IMeasure
    {
        public event Action? Clicked;
        public event Action<string>? FileDropped;
        public Image? Image;
        public bool HasError;
        bool hover;

        public PhotoPicker()
        {
            Cursor = Cursors.Hand;
            AllowDrop = true;
            MakeFocusable(AccessibleRole.PushButton, "Elegir la foto de la prenda");
        }

        public int MeasureHeight(int width) => Math.Min(D(260), Math.Max(D(180), width * 2 / 3));

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Clicked?.Invoke(); }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) FileDropped?.Invoke(files[0]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Behind);
            HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            FillRound(g, Image != null ? Surface : (hover ? PrimarySoft : Surface), r, Df(14));
            if (Image != null)
            {
                float k = Math.Min((r.Width - Df(16)) / Image.Width, (r.Height - Df(16)) / Image.Height);
                float w = Image.Width * k, h = Image.Height * k;
                DrawCover(g, Image, new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h), Df(10));
                var chip = new RectangleF(r.Right - Df(118), r.Bottom - Df(38), Df(108), Df(28));
                FillRound(g, Color.FromArgb(225, 255, 255, 255), chip, chip.Height / 2);
                DrawText(g, "Cambiar foto", F(8.5f, FontStyle.Bold), Primary, Rectangle.Round(chip), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                float s = Df(50);
                var ir = new RectangleF((Width - s) / 2, Height / 2f - Df(50), s, s);
                using (var b = new SolidBrush(PrimarySoft)) g.FillEllipse(b, ir);
                using (var pen = new Pen(Primary, Df(2.2f)))
                {
                    g.DrawLine(pen, ir.X + s / 2, ir.Y + Df(14), ir.X + s / 2, ir.Bottom - Df(14));
                    g.DrawLine(pen, ir.X + Df(14), ir.Y + s / 2, ir.Right - Df(14), ir.Y + s / 2);
                }
                DrawText(g, "Tocá para elegir una foto", F(10f, FontStyle.Bold), Theme.Text, new Rectangle(0, (int)ir.Bottom + D(8), Width, D(22)), TextFormatFlags.HorizontalCenter);
                DrawText(g, "o arrastrala acá · JPG, PNG o WebP, máximo 5 MB", F(8.75f), Muted, new Rectangle(0, (int)ir.Bottom + D(30), Width, D(20)), TextFormatFlags.HorizontalCenter);
            }
            DrawRound(g, HasError ? Danger : hover || FocusCue ? Primary : Outline, r, Df(14), FocusCue ? Df(2.5f) : Df(1.4f), Image == null ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid);
        }
    }

    /// <summary>Muestra el color elegido.</summary>
    class ColorPreview : PaintedControl, IMeasure
    {
        public Color? Value { get => value; set { this.value = value; Invalidate(); } }
        Color? value;

        public ColorPreview() { Cursor = Cursors.Hand; MakeFocusable(AccessibleRole.PushButton, "Elegir otro color"); }

        public int MeasureHeight(int width) => D(46);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Behind);
            HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (value.HasValue) FillRound(g, value.Value, r, Df(11));
            else
            {
                FillRound(g, Surface, r, Df(11));
                DrawText(g, "?", F(12f, FontStyle.Bold), Muted, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            DrawRound(g, FocusCue ? Primary : Outline, r, Df(11), FocusCue ? Df(2.5f) : Df(1));
        }
    }
}
