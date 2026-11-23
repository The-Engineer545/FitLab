#if DEBUG
using System.Runtime.InteropServices;
#endif
using static FitLab.Theme;

namespace FitLab;

/// <summary>Datos del usuario con sesión iniciada, compartidos por todas las pantallas.</summary>
public class AppState
{
    /// <summary>Persona con sesión iniciada, o null en la pantalla de inicio.</summary>
    public Usuario? User;
    /// <summary>Id de la persona con sesión iniciada. Solo se usa desde pantallas que existen después del inicio de sesión.</summary>
    public long UserId => User?.Id ?? throw new InvalidOperationException("No hay una sesión iniciada.");
    public List<Prenda> Prendas = new();
    public List<Outfit> Outfits = new();
    public readonly Dictionary<Category, Prenda> Fitting = new();
    public event Action? Changed;

    public void Reload()
    {
        if (User == null) return;
        Prendas = Db.ListPrendas(User.Id);
        var map = Prendas.ToDictionary(p => p.Id);
        Outfits = Db.ListOutfits(User.Id, map);
        foreach (var k in Fitting.Keys.ToList())
        {
            if (map.TryGetValue(Fitting[k].Id, out var np) && np.Categoria == k) Fitting[k] = np;
            else Fitting.Remove(k);
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        User = null;
        Prendas = new();
        Outfits = new();
        Fitting.Clear();
    }
}

public interface IView
{
    /// <summary>Se llama cada vez que la pantalla pasa a estar visible, para refrescar lo que muestra.</summary>
    void ShowView();
}

public class MainForm : Form
{
#if DEBUG
    readonly string? shotPath;
#endif
    public readonly AppState State = new();
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly Panel content = new() { Dock = DockStyle.Fill };
    readonly Header header;
    readonly NavBar nav;
    LoginView? login;
    // Las pantallas de la app se crean la primera vez que alguien inicia sesión.
    WardrobeView? wardrobe;
    FitterView? fitter;
    OutfitsView? outfits;
    GuideView? guide;
    Control? current;

    public MainForm(string? screenshotPath)
    {
#if DEBUG
        shotPath = screenshotPath;
#endif
        Text = "FitLab · Probador virtual";
        BackColor = Bg;
        Font = F(9.75f);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 900);
        ClientSize = new Size(Math.Min(D(440), wa.Width - 40), Math.Min(D(860), wa.Height - 60));
        MinimumSize = new Size(D(360), D(560));
        DoubleBuffered = true;

        header = new Header();
        header.LogoutClicked += Logout;
        nav = new NavBar();
        nav.Selected += ShowTab;

        root.BackColor = Bg;
        root.Controls.Add(content);
        root.Controls.Add(header);
        root.Controls.Add(nav);
        content.BringToFront();
        Controls.Add(root);

        ShowLogin();
    }

    // ---------- Navegación ----------

    void Swap(Control view)
    {
        content.SuspendLayout();
        if (current != null) current.Visible = false;
        if (!content.Controls.Contains(view)) { view.Dock = DockStyle.Fill; content.Controls.Add(view); }
        view.Visible = true;
        view.BringToFront();
        current = view;
        content.ResumeLayout();
        (view as IView)?.ShowView();
    }

    void ShowLogin()
    {
        header.Visible = false;
        nav.Visible = false;
        login ??= new LoginView(OnLoggedIn);
        login.Reset();
        Swap(login);
    }

    void OnLoggedIn(Usuario u)
    {
        State.Clear();
        State.User = u;
        State.Reload();
        header.UserName = u.Nombre;
        header.Visible = true;
        nav.Visible = true;
        wardrobe ??= new WardrobeView(this);
        fitter ??= new FitterView(this);
        outfits ??= new OutfitsView(this);
        guide ??= new GuideView(this);
        nav.Index = 0;
        ShowTab(0);
    }

    void Logout()
    {
        if (!Ui.Confirm(this, "¿Querés cerrar la sesión?", "Cerrar sesión")) return;
        State.Clear();
        Photos.Clear();
        ShowLogin();
    }

    public void ShowTab(int i)
    {
        Control? view = i switch { 0 => wardrobe, 1 => fitter, 2 => outfits, _ => guide };
        if (view == null) return; // todavía no hay sesión iniciada
        nav.Index = i;
        Swap(view);
    }

    public void TryOn(Prenda p)
    {
        State.Fitting[p.Categoria] = p;
        ShowTab(1);
    }

    public void OpenOutfit(Outfit o)
    {
        State.Fitting.Clear();
        foreach (var kv in o.Prendas) State.Fitting[kv.Key] = kv.Value;
        ShowTab(1);
    }

    /// <summary>Abre el formulario de prenda (nueva o existente). Devuelve true si hubo cambios.</summary>
    public bool EditGarment(Prenda? p, Category? preset = null) => EditGarment(p, preset, out _);

    /// <summary>Igual que el anterior; además entrega en savedId el id de la prenda guardada (0 si se canceló o se eliminó).</summary>
    public bool EditGarment(Prenda? p, Category? preset, out long savedId)
    {
        savedId = 0;
        using var dlg = new GarmentEditorForm(State.UserId, p, preset);
        var res = dlg.ShowDialog(this);
        if (res == DialogResult.OK || res == DialogResult.Abort) { savedId = dlg.SavedId; State.Reload(); return true; }
        return false;
    }

#if DEBUG
    // ---------- Captura para la revisión automática (solo en compilaciones de depuración) ----------

    protected override bool ShowWithoutActivation => shotPath != null;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (shotPath != null) cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (shotPath == null) return;
        SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010); // HWND_BOTTOM, NOMOVE|NOSIZE|NOACTIVATE
        if (Environment.GetEnvironmentVariable("FITLAB_DEMO") == "1")
        {
            OnLoggedIn(DevSeed.Run());
            int.TryParse(Environment.GetEnvironmentVariable("FITLAB_TAB"), out int tab);
            if (tab == 1)
                foreach (var p in State.Prendas.Where(p => p.Nombre is "Camisa de lino" or "Jean recto" or "Botas de cuero" or "Bolso tote"))
                    State.Fitting[p.Categoria] = p;
            ShowTab(Math.Clamp(tab, 0, 3));
            Form? dlg = tab switch
            {
                4 => new GarmentEditorForm(State.UserId, State.Prendas.First(p => p.Nombre == "Camisa de lino"), null),
                5 => new SaveOutfitForm(3),
                6 => new GarmentPickerForm(Category.Calzado, State.Prendas.Where(p => p.Categoria == Category.Calzado).ToList(), State.Prendas.First(p => p.Nombre == "Botas de cuero")),
                7 => new GarmentEditorForm(State.UserId, null, null),
                8 => new RecoveryCodeForm("ABCD-EFGH-JKMN", "Tu cuenta está lista. Si algún día olvidás la contraseña, vas a necesitar este código para elegir una nueva."),
                _ => null,
            };
            if (dlg != null) { dlg.StartPosition = FormStartPosition.Manual; dlg.Location = Location; dlg.Show(this); shotTarget = dlg; }
            if (Environment.GetEnvironmentVariable("FITLAB_W") is string w && int.TryParse(w, out int ww)) ClientSize = new Size(ww, ClientSize.Height);
        }
        int frames = 0;
        var t = new System.Windows.Forms.Timer { Interval = 16 };
        t.Tick += (_, _) =>
        {
            frames++;
            root.Invalidate(true);
            if (frames < 120) return;
            t.Stop();
            try { SaveShot(shotPath); }
            catch (Exception ex) { Console.Error.WriteLine("No se pudo guardar la captura: " + ex.Message); }
            Close();
        };
        t.Start();
    }

    Form? shotTarget;

    void SaveShot(string path)
    {
        var target = shotTarget ?? this;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        int cw = target.ClientSize.Width, ch = target.ClientSize.Height;
        Bitmap? shot = null;

        GetWindowRect(target.Handle, out var wr);
        int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
        if (ww > 0 && wh > 0)
        {
            using var full = new Bitmap(ww, wh);
            using (var g = Graphics.FromImage(full))
            {
                var hdc = g.GetHdc();
                bool ok = PrintWindow(target.Handle, hdc, 2);
                g.ReleaseHdc(hdc);
                if (ok)
                {
                    var origin = target.PointToScreen(Point.Empty);
                    shot = full.Clone(new Rectangle(origin.X - wr.Left, origin.Y - wr.Top, cw, ch), full.PixelFormat);
                    if (IsBlank(shot)) { shot.Dispose(); shot = null; }
                }
            }
        }
        if (shot == null)
        {
            shot = new Bitmap(cw, ch);
            (target == this ? (Control)root : target).DrawToBitmap(shot, new Rectangle(0, 0, cw, ch));
        }
        shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        shot.Dispose();
    }

    static bool IsBlank(Bitmap b)
    {
        var first = b.GetPixel(0, 0);
        for (int y = 0; y < b.Height; y += 17)
            for (int x = 0; x < b.Width; x += 13)
                if (b.GetPixel(x, y) != first) return false;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
#endif
}

/// <summary>Barra superior: marca, usuario y botón para salir.</summary>
public class Header : PaintedControl
{
    public event Action? LogoutClicked;
    readonly FlatButton logout = new("Salir", BtnKind.Ghost) { PreferredHeight = D(34) };
    string userName = "";

    public Header()
    {
        Dock = DockStyle.Top;
        Height = D(60);
        BackColor = Surface;
        logout.Font = F(9f, FontStyle.Bold);
        logout.Click += (_, _) => LogoutClicked?.Invoke();
        Controls.Add(logout);
    }

    public string UserName { get => userName; set { userName = value ?? ""; Invalidate(); } }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int bw = D(64);
        logout.SetBounds(Width - bw - D(14), (Height - D(34)) / 2, bw, D(34));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Surface);
        HQ(g);
        float s = Df(34);
        var lr = new RectangleF(D(16), (Height - s) / 2, s, s);
        FillRound(g, Primary, lr, Df(10));
        Icons.Hanger(g, RectangleF.Inflate(lr, -Df(8), -Df(9)), Color.White, Df(2));
        DrawText(g, "FitLab", F(14f, FontStyle.Bold), Theme.Text, new Rectangle(D(58), 0, D(120), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (userName.Length > 0)
        {
            float a = Df(32);
            float ax = Width - D(64) - D(14) - D(10) - a;
            var ar = new RectangleF(ax, (Height - a) / 2, a, a);
            using (var b = new SolidBrush(PrimarySoft)) g.FillEllipse(b, ar);
            DrawText(g, userName.Substring(0, 1).ToUpperInvariant(), F(11f, FontStyle.Bold), Primary, Rectangle.Round(ar), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int nameW = (int)ax - D(190);
            if (nameW > D(60))
                DrawText(g, "Hola, " + userName.Split(' ')[0], F(9.5f), Muted, new Rectangle(D(180), 0, nameW, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        using var pen = new Pen(Border);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Navegación inferior, al estilo de las apps de celular.</summary>
public class NavBar : PaintedControl
{
    public event Action<int>? Selected;
    static readonly string[] Labels = { "Guardarropa", "Probador", "Outfits", "Guía" };
    int index, hover = -1;

    public NavBar()
    {
        Dock = DockStyle.Bottom;
        Height = D(68);
        BackColor = Surface;
        Cursor = Cursors.Hand;
        MakeFocusable(AccessibleRole.PageTabList, "Navegación: Guardarropa, Probador, Outfits y Guía. Flechas izquierda y derecha para cambiar de pantalla.", keyActivates: false);
    }

    public int Index { get => index; set { index = value; AccessibleDescription = "Pantalla actual: " + Labels[Math.Clamp(value, 0, Labels.Length - 1)]; Invalidate(); } }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
        if (step == 0) return;
        e.Handled = true;
        int i = Math.Clamp(index + step, 0, Labels.Length - 1);
        if (i == index) return;
        Index = i;
        Selected?.Invoke(i);
    }

    int Hit(Point p) => Math.Max(0, Math.Min(Labels.Length - 1, p.X * Labels.Length / Math.Max(1, Width)));

    protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = Hit(e.Location);
        Index = i;
        Selected?.Invoke(i);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Surface);
        HQ(g);
        using (var pen = new Pen(Border)) g.DrawLine(pen, 0, 0, Width, 0);
        float cell = (float)Width / Labels.Length;
        for (int i = 0; i < Labels.Length; i++)
        {
            bool sel = i == index;
            var col = sel ? Primary : (i == hover ? Theme.Text : Muted);
            float cx = cell * i + cell / 2;
            var pill = new RectangleF(cx - Df(28), Df(8), Df(56), Df(30));
            if (sel) FillRound(g, PrimarySoft, pill, pill.Height / 2);
            if (sel && FocusCue) DrawRound(g, Primary, pill, pill.Height / 2, Df(2));
            var ir = new RectangleF(cx - Df(10), Df(13), Df(20), Df(20));
            switch (i)
            {
                case 0: Icons.Hanger(g, ir, col, Df(1.8f)); break;
                case 1: Icons.Person(g, ir, col, Df(1.8f)); break;
                case 2: Icons.Star(g, ir, col, Df(1.6f), sel); break;
                default: Icons.Palette(g, ir, col, Df(1.6f)); break;
            }
            DrawText(g, Labels[i], F(8.5f, sel ? FontStyle.Bold : FontStyle.Regular), col,
                new Rectangle((int)(cell * i), D(40), (int)cell, D(22)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
