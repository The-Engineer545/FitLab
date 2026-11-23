using static FitLab.Theme;

namespace FitLab;

/// <summary>Inicio de sesión, registro y recuperación de contraseña con código.</summary>
public class LoginView : ScrollHost, IView
{
    enum Mode { Login, Register, Recover }

    readonly Action<Usuario> done;
    readonly ChipGroup mode = new();
    readonly TextBlock recoverTitle = new("Recuperar la contraseña", 12f, FontStyle.Bold);
    readonly TextBlock recoverInfo = new("Escribí tu correo y el código de recuperación que te dimos al crear la cuenta. Después elegí una contraseña nueva.", 9f, FontStyle.Regular, Muted);
    readonly TextBlock nameLbl = Label("Nombre");
    readonly InputBox name = new("¿Cómo te llamás?", maxLength: 60);
    readonly InputBox email = new("nombre@correo.com", maxLength: 120);
    readonly TextBlock codeLbl = Label("Código de recuperación");
    readonly InputBox code = new("XXXX-XXXX-XXXX", maxLength: 20);
    readonly TextBlock passLbl = Label("Contraseña");
    readonly InputBox pass = new("Tu contraseña", password: true, maxLength: 100);
    readonly TextBlock confirmLbl = Label("Repetí la contraseña");
    readonly InputBox confirm = new("Escribila de nuevo", password: true, maxLength: 100);
    readonly TextBlock error = new("", 9.5f, FontStyle.Regular, Danger);
    readonly FlatButton submit = new("Iniciar sesión");
    readonly FlatButton forgot = new("¿Olvidaste tu contraseña?", BtnKind.Ghost) { PreferredHeight = D(38) };
    readonly TextBlock hint = new("", 9f, FontStyle.Regular, Muted) { Align = TextFormatFlags.HorizontalCenter };
    Mode current = Mode.Login;

    static TextBlock Label(string t) => new(t, 9.5f, FontStyle.Bold);

    InputBox[] Boxes => new[] { name, email, code, pass, confirm };

    public LoginView(Action<Usuario> onDone)
    {
        done = onDone;
        Stack.MaxContentWidth = D(420);
        Stack.Pad = new Padding(D(20), D(36), D(20), D(28));
        Stack.Gap = D(18);

        Stack.Controls.Add(new Logo());

        var card = new CardStack { Gap = D(10), Pad = new Padding(D(20)) };
        mode.Add("Iniciar sesión", Mode.Login);
        mode.Add("Crear cuenta", Mode.Register);
        mode.Changed += () => SetMode(mode.SelectedValue is Mode m ? m : Mode.Login);
        card.Controls.Add(mode);
        card.Controls.Add(Ui.Space(D(2)));
        card.Controls.Add(recoverTitle);
        card.Controls.Add(recoverInfo);
        card.Controls.Add(nameLbl);
        card.Controls.Add(name);
        card.Controls.Add(Label("Correo electrónico"));
        card.Controls.Add(email);
        card.Controls.Add(codeLbl);
        card.Controls.Add(code);
        card.Controls.Add(passLbl);
        card.Controls.Add(pass);
        card.Controls.Add(confirmLbl);
        card.Controls.Add(confirm);
        card.Controls.Add(error);
        card.Controls.Add(Ui.Space(D(2)));
        card.Controls.Add(submit);
        card.Controls.Add(forgot);
        Stack.Controls.Add(card);
        Stack.Controls.Add(hint);

        name.Box.AccessibleName = "Nombre";
        email.Box.AccessibleName = "Correo electrónico";
        code.Box.AccessibleName = "Código de recuperación";
        code.Box.CharacterCasing = CharacterCasing.Upper;
        confirm.Box.AccessibleName = "Repetí la contraseña";
        submit.Click += (_, _) => Submit();
        forgot.Click += (_, _) => SetMode(Mode.Recover);
        foreach (var box in Boxes)
            box.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); } };
        SetMode(Mode.Login);
    }

    void SetMode(Mode m)
    {
        current = m;
        bool reg = m == Mode.Register, rec = m == Mode.Recover;
        // En el modo de recuperación no queda marcada ninguna pestaña; tocar una vuelve a ese modo.
        mode.SelectedIndex = rec ? -1 : reg ? 1 : 0;
        recoverTitle.Visible = recoverInfo.Visible = codeLbl.Visible = code.Visible = rec;
        nameLbl.Visible = name.Visible = reg;
        confirmLbl.Visible = confirm.Visible = reg || rec;
        forgot.Visible = m == Mode.Login;
        passLbl.Text = rec ? "Contraseña nueva" : "Contraseña";
        pass.Box.AccessibleName = passLbl.Text;
        pass.Box.PlaceholderText = m == Mode.Login ? "Tu contraseña" : $"Mínimo {Db.MinPassword} caracteres";
        submit.Text = m switch { Mode.Register => "Crear mi cuenta", Mode.Recover => "Cambiar la contraseña", _ => "Iniciar sesión" };
        hint.Text = m switch
        {
            Mode.Register => "Tu guardarropa y tus outfits se guardan solo en esta computadora. Al crear la cuenta te damos un código para recuperar la contraseña si la olvidás.",
            Mode.Recover => "¿Te acordaste? Tocá «Iniciar sesión» arriba para volver.",
            _ => "¿Primera vez en FitLab? Tocá «Crear cuenta».",
        };
        error.Text = "";
        foreach (var b in Boxes) { b.HasError = false; b.Invalidate(); }
        Relayout();
        if (IsHandleCreated && Visible) BeginInvoke(new Action(FocusFirst));
    }

    void FocusFirst()
    {
        var first = current == Mode.Register && name.Value.Length == 0 ? name
            : current == Mode.Recover && email.Value.Length > 0 ? code
            : email;
        first.Box.Focus();
    }

    public void Reset()
    {
        pass.Value = confirm.Value = code.Value = "";
        error.Text = "";
        if (current == Mode.Recover) SetMode(Mode.Login);
        Relayout();
    }

    public void ShowView()
    {
        Relayout();
        ScrollTop();
        if (IsHandleCreated) BeginInvoke(new Action(FocusFirst));
    }

    void Submit()
    {
        error.Text = "";
        try
        {
            Usuario u;
            string? newCode = null, intro = null;
            switch (current)
            {
                case Mode.Register:
                    u = Db.Register(name.Value, email.Value, pass.Value, confirm.Value, out var c1);
                    newCode = c1;
                    intro = "Tu cuenta está lista. Si algún día olvidás la contraseña, vas a necesitar este código para elegir una nueva.";
                    break;
                case Mode.Recover:
                    u = Db.ResetPassword(email.Value, code.Value, pass.Value, confirm.Value, out var c2);
                    newCode = c2;
                    intro = "Listo, cambiamos tu contraseña. El código que usaste ya no sirve: este es el nuevo.";
                    break;
                default:
                    u = Db.Login(email.Value, pass.Value);
                    if (!u.TieneRecuperacion)
                    {
                        // Cuentas creadas antes de que existiera el código: se les genera uno al entrar.
                        newCode = Db.NewRecoveryCode(u.Id);
                        u.TieneRecuperacion = true;
                        intro = "Ahora FitLab te permite recuperar la contraseña si la olvidás. Este es el código de tu cuenta.";
                    }
                    break;
            }
            pass.Value = confirm.Value = code.Value = "";
            if (newCode != null)
            {
                using var dlg = new RecoveryCodeForm(newCode, intro ?? "");
                dlg.ShowDialog(FindForm());
            }
            if (current == Mode.Recover) SetMode(Mode.Login);
            done(u);
        }
        catch (UserError ex)
        {
            error.Text = ex.Message;
            name.HasError = ex.Field == "nombre";
            email.HasError = ex.Field is "email" or "credenciales" or "recuperacion";
            code.HasError = ex.Field == "recuperacion";
            pass.HasError = ex.Field is "password" or "credenciales";
            confirm.HasError = ex.Field == "confirm";
            foreach (var b in Boxes) b.Invalidate();
            Relayout();
        }
        catch (Exception ex)
        {
            error.Text = "No pudimos conectarnos con la base de datos. Probá de nuevo en un momento. (" + ex.Message + ")";
            Relayout();
        }
    }

    class Logo : PaintedControl, IMeasure
    {
        public int MeasureHeight(int width) => D(150);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            float s = Df(68);
            var r = new RectangleF((Width - s) / 2, 0, s, s);
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(r, Primary, Color.FromArgb(160, 92, 230), 45f))
            using (var p = Round(r, Df(20))) g.FillPath(br, p);
            Icons.Hanger(g, RectangleF.Inflate(r, -Df(16), -Df(18)), Color.White, Df(3));
            DrawText(g, "FitLab", F(22f, FontStyle.Bold), Theme.Text, new Rectangle(0, D(74), Width, D(44)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            DrawText(g, "Tu guardarropa digital, probador virtual y guía de colores", F(9.5f), Muted,
                new Rectangle(0, D(116), Width, D(34)), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }
    }
}
