using static FitLab.Theme;

namespace FitLab;

/// <summary>Controles que saben cuánto alto necesitan para un ancho dado.</summary>
public interface IMeasure { int MeasureHeight(int width); }

public abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        // Por defecto no reciben foco: solo los que llaman a MakeFocusable entran en el recorrido de Tab.
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }
    protected Color Behind => Parent?.BackColor ?? Bg;

    /// <summary>Si es true, Enter y Espacio hacen lo mismo que un clic.</summary>
    protected bool KeyActivates;

    /// <summary>True cuando hay que dibujar el indicador de foco (la persona está usando el teclado).</summary>
    protected bool FocusCue => Focused && ShowFocusCues;

    /// <summary>
    /// Vuelve al control alcanzable con Tab y lo describe para los lectores de pantalla.
    /// Entradas: rol, nombre (null = usar el texto del control) y si Enter/Espacio lo activan. No retorna nada.
    /// </summary>
    protected void MakeFocusable(AccessibleRole role, string? name = null, bool keyActivates = true)
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        KeyActivates = keyActivates;
        AccessibleRole = role;
        if (name != null) AccessibleName = name;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); Ui.Reveal(this); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyActivates && !e.Handled && e.KeyCode is Keys.Enter or Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); }
    }
}

public static class Ui
{
    /// <summary>Vuelve a acomodar el contenedor con desplazamiento que contiene al control.</summary>
    public static void Relayout(Control c)
    {
        for (var p = c; p != null; p = p.Parent)
            if (p is ScrollHost sh) { sh.Relayout(); return; }
    }

    public static void Info(IWin32Window owner, string msg, string title = "FitLab") =>
        MessageBox.Show(owner, msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Error(IWin32Window owner, string msg) =>
        MessageBox.Show(owner, msg, "Ups, algo no salió bien", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static bool Confirm(IWin32Window owner, string msg, string title) =>
        MessageBox.Show(owner, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    public static Spacer Space(int h) => new Spacer(h);

    /// <summary>Desplaza el contenedor para que el control que recibió el foco quede a la vista.</summary>
    public static void Reveal(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p is ScrollHost sh) { sh.EnsureVisible(c); return; }
    }

    static readonly ToolTip Tips = new();

    /// <summary>Texto de ayuda al pasar el mouse. Usa un único ToolTip para toda la app y lo suelta cuando el control se destruye.</summary>
    public static void Tip(Control c, string text)
    {
        Tips.SetToolTip(c, text);
        c.Disposed += (_, _) => Tips.SetToolTip(c, null);
    }
}

public class Spacer : Control, IMeasure
{
    readonly int h;
    public Spacer(int h) { this.h = h; SetStyle(ControlStyles.Selectable, false); TabStop = false; }
    public int MeasureHeight(int width) => h;
}

public class TextBlock : PaintedControl, IMeasure
{
    public TextFormatFlags Align = TextFormatFlags.Left;
    public bool SingleLine;

    public TextBlock(string text, float size = 10f, FontStyle style = FontStyle.Regular, Color? color = null)
    {
        Text = text;
        Font = F(size, style);
        ForeColor = color ?? Theme.Text;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public int MeasureHeight(int width)
    {
        if (string.IsNullOrEmpty(Text)) return 0;
        if (SingleLine) return TextRenderer.MeasureText("Ag", Font).Height;
        return TextHeight(Text, Font, width) + 1;
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var flags = Align | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
        flags |= SingleLine ? TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis : TextFormatFlags.WordBreak;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
    }
}

public enum BtnKind { Primary, Secondary, Ghost, Danger }

public class FlatButton : PaintedControl, IMeasure
{
    public BtnKind Kind;
    public int PreferredHeight = D(44);
    bool hover, down;

    public FlatButton(string text, BtnKind kind = BtnKind.Primary)
    {
        Text = text;
        Kind = kind;
        Font = F(10f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        MakeFocusable(AccessibleRole.PushButton);
        Height = PreferredHeight;
    }

    public int MeasureHeight(int width) => PreferredHeight;

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        HQ(g);
        Color bg, fg, border = Color.Empty;
        switch (Kind)
        {
            case BtnKind.Primary: bg = Primary; fg = Color.White; break;
            case BtnKind.Secondary: bg = PrimarySoft; fg = Primary; break;
            case BtnKind.Danger: bg = DangerSoft; fg = Danger; break;
            default: bg = Surface; fg = Theme.Text; border = Outline; break;
        }
        if (!Enabled) { bg = Color.FromArgb(232, 230, 236); fg = Muted; border = Color.Empty; }
        else if (down) bg = Mix(bg, Color.Black, 0.12f);
        else if (hover) bg = Mix(bg, Kind == BtnKind.Primary ? Color.Black : Primary, 0.07f);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, bg, r, Df(11));
        if (border != Color.Empty) DrawRound(g, border, r, Df(11), Df(1));
        if (FocusCue) DrawRound(g, Kind == BtnKind.Primary ? Color.White : Primary, RectangleF.Inflate(r, -Df(2.5f), -Df(2.5f)), Df(9), Df(1.5f));
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

public class ChipButton : PaintedControl
{
    bool selected, hover;
    public object? Value;
    public Color? Dot;

    public ChipButton(string text, object? value = null, Color? dot = null)
    {
        Text = text;
        Value = value;
        Dot = dot;
        Font = F(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        int w = TextWidth(text, Font) + D(28) + (dot.HasValue ? D(20) : 0);
        Size = new Size(w, D(36));
        MakeFocusable(AccessibleRole.PushButton);
    }

    public bool Selected { get => selected; set { selected = value; AccessibleDescription = value ? "Seleccionado" : null; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (selected) FillRound(g, Primary, r, r.Height / 2);
        else
        {
            FillRound(g, hover ? Mix(Surface, Primary, 0.06f) : Surface, r, r.Height / 2);
            DrawRound(g, Outline, r, r.Height / 2, Df(1));
        }
        if (FocusCue) DrawRound(g, selected ? Color.White : Primary, RectangleF.Inflate(r, -Df(2.5f), -Df(2.5f)), r.Height / 2, Df(1.5f));
        var tr = ClientRectangle;
        if (Dot.HasValue)
        {
            float s = Df(14);
            Swatch(g, Dot.Value, new RectangleF(D(12), (Height - s) / 2, s, s));
            tr = new Rectangle(D(20), 0, Width - D(20), Height);
        }
        TextRenderer.DrawText(g, Text, Font, tr, selected ? Color.White : Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Acomoda a los hijos de izquierda a derecha y salta de línea cuando no entran.</summary>
public class WrapPanel : Panel, IMeasure
{
    public int GapX = D(8), GapY = D(8);

    public WrapPanel() { DoubleBuffered = true; }

    public int MeasureHeight(int width) => Arrange(width, false);

    protected override void OnLayout(LayoutEventArgs levent) { Arrange(Width, true); }

    int Arrange(int width, bool apply)
    {
        int x = 0, y = 0, rowH = 0;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            if (x > 0 && x + c.Width > width) { x = 0; y += rowH + GapY; rowH = 0; }
            if (apply) c.Location = new Point(x, y);
            x += c.Width + GapX;
            rowH = Math.Max(rowH, c.Height);
        }
        return Controls.Count == 0 ? 0 : y + rowH;
    }
}

/// <summary>Grupo de opciones con una sola elegida.</summary>
public class ChipGroup : WrapPanel
{
    public readonly List<ChipButton> Chips = new();
    public event Action? Changed;
    int selected = -1;
    public bool AllowNone;

    public ChipButton Add(string text, object value, Color? dot = null)
    {
        var chip = new ChipButton(text, value, dot);
        int idx = Chips.Count;
        chip.Click += (_, _) => { SelectedIndex = (AllowNone && selected == idx) ? -1 : idx; Changed?.Invoke(); };
        Chips.Add(chip);
        Controls.Add(chip);
        return chip;
    }

    public int SelectedIndex
    {
        get => selected;
        set { selected = value; for (int i = 0; i < Chips.Count; i++) Chips[i].Selected = i == value; }
    }

    public object? SelectedValue
    {
        get => selected >= 0 && selected < Chips.Count ? Chips[selected].Value : null;
        set => SelectedIndex = Chips.FindIndex(c => Equals(c.Value, value));
    }
}

/// <summary>Columna: hijos uno debajo del otro, a todo el ancho (con ancho máximo centrado).</summary>
public class VStack : Panel, IMeasure
{
    public int Gap = D(12);
    public int MaxContentWidth;
    public Padding Pad = Padding.Empty;

    public VStack() { DoubleBuffered = true; BackColor = Bg; }

    static int H(Control c, int w) => c is IMeasure m ? m.MeasureHeight(w) : c.Height;

    int ContentX(int width, out int cw)
    {
        int avail = Math.Max(0, width - Pad.Horizontal);
        cw = MaxContentWidth > 0 ? Math.Min(avail, MaxContentWidth) : avail;
        return Pad.Left + (avail - cw) / 2;
    }

    public int MeasureHeight(int width) => Arrange(width, false);

    public void DoLayout() => Arrange(Width, true);

    protected override void OnLayout(LayoutEventArgs levent) { Arrange(Width, true); }

    int Arrange(int width, bool apply)
    {
        int x = ContentX(width, out int cw);
        int y = Pad.Top;
        bool first = true;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            int h = H(c, cw);
            if (h <= 0) { if (apply) c.SetBounds(x, y, cw, 0); continue; }
            if (!first) y += Gap;
            first = false;
            if (apply) c.SetBounds(x, y, cw, h);
            y += h;
        }
        return y + Pad.Bottom;
    }
}

/// <summary>Columna dibujada como tarjeta blanca con esquinas redondeadas.</summary>
public class CardStack : VStack
{
    public CardStack()
    {
        BackColor = Surface;
        Pad = new Padding(D(16));
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Bg);
        HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, Surface, r, Df(16));
        DrawRound(g, Border, r, Df(16), Df(1));
    }
}

/// <summary>Fila: hijos lado a lado con anchos proporcionales.</summary>
public class HRow : Panel, IMeasure
{
    public int Gap = D(10);
    public float[]? Weights;

    public HRow(params Control[] children)
    {
        DoubleBuffered = true;
        foreach (var c in children) Controls.Add(c);
    }

    IEnumerable<Control> Shown() => Controls.Cast<Control>().Where(c => c.Visible);

    float[] Ws(int n) => Weights != null && Weights.Length == n ? Weights : Enumerable.Repeat(1f, n).ToArray();

    public int MeasureHeight(int width)
    {
        var kids = Shown().ToList();
        if (kids.Count == 0) return 0;
        var ws = Ws(kids.Count);
        float total = ws.Sum();
        int avail = width - Gap * (kids.Count - 1);
        int h = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            int w = (int)(avail * ws[i] / total);
            h = Math.Max(h, kids[i] is IMeasure m ? m.MeasureHeight(w) : kids[i].Height);
        }
        return h;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        var kids = Shown().ToList();
        if (kids.Count == 0) return;
        var ws = Ws(kids.Count);
        float total = ws.Sum();
        int avail = Width - Gap * (kids.Count - 1);
        int x = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            int w = i == kids.Count - 1 ? Width - x : (int)(avail * ws[i] / total);
            kids[i].SetBounds(x, 0, w, Height);
            x += w + Gap;
        }
    }
}

/// <summary>Dos bloques: lado a lado en pantallas anchas, uno debajo del otro en angostas.</summary>
public class Responsive2 : Panel, IMeasure
{
    readonly Control a, b;
    public int Breakpoint = D(780);
    public int LeftWidth = D(400);
    public int Gap = D(16);

    public Responsive2(Control a, Control b)
    {
        this.a = a; this.b = b;
        DoubleBuffered = true;
        Controls.Add(a);
        Controls.Add(b);
    }

    static int H(Control c, int w) => c is IMeasure m ? m.MeasureHeight(w) : c.Height;
    bool Wide(int w) => w >= Breakpoint;

    public int MeasureHeight(int width)
    {
        if (Wide(width)) return Math.Max(H(a, LeftWidth), H(b, width - LeftWidth - Gap));
        int hb = H(b, width);
        return H(a, width) + (hb > 0 ? Gap + hb : 0);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int w = Width;
        if (Wide(w))
        {
            a.SetBounds(0, 0, LeftWidth, H(a, LeftWidth));
            b.SetBounds(LeftWidth + Gap, 0, w - LeftWidth - Gap, H(b, w - LeftWidth - Gap));
        }
        else
        {
            int ha = H(a, w);
            a.SetBounds(0, 0, w, ha);
            b.SetBounds(0, ha + Gap, w, H(b, w));
        }
    }
}

/// <summary>Área con desplazamiento vertical propia (rueda del mouse y barra).</summary>
public class ScrollHost : Panel
{
    public readonly VStack Stack = new();
    readonly VScrollBar bar = new() { Visible = false };
    int offset, contentH;
    bool busy;

    public ScrollHost()
    {
        BackColor = Bg;
        DoubleBuffered = true;
        Stack.Pad = new Padding(D(16), D(16), D(16), D(24));
        Controls.Add(Stack);
        Controls.Add(bar);
        bar.Scroll += (_, _) => { offset = bar.Value; Place(); };
        bar.ValueChanged += (_, _) => { offset = bar.Value; Place(); };
    }

    protected override void OnResize(EventArgs eventargs) { base.OnResize(eventargs); Relayout(); }

    public void Relayout()
    {
        if (busy) return;
        int W = ClientSize.Width, H = ClientSize.Height;
        if (W <= 0 || H <= 0) return;
        busy = true;
        try
        {
            int ch = Stack.MeasureHeight(W);
            bool need = ch > H;
            int bw = need ? SystemInformation.VerticalScrollBarWidth : 0;
            int w = W - bw;
            if (need) ch = Stack.MeasureHeight(w);
            contentH = ch;
            if (need)
            {
                offset = Math.Max(0, Math.Min(offset, ch - H));
                bar.SetBounds(W - bw, 0, bw, H);
                bar.Minimum = 0;
                bar.Maximum = Math.Max(0, ch - 1);
                bar.LargeChange = Math.Max(1, H);
                bar.SmallChange = D(48);
                bar.Value = offset;
                bar.Visible = true;
                bar.BringToFront();
            }
            else { offset = 0; bar.Visible = false; }
            Stack.Size = new Size(w, Math.Max(ch, H));
            Place();
            Stack.DoLayout();
        }
        finally { busy = false; }
    }

    void Place() => Stack.Location = new Point(0, -offset);

    /// <summary>Desplaza lo necesario para que el control (un descendiente) quede dentro del área visible. No retorna nada.</summary>
    public void EnsureVisible(Control c)
    {
        if (!bar.Visible || !IsHandleCreated || !c.IsHandleCreated || !Stack.Contains(c)) return;
        int top = Stack.PointToClient(c.PointToScreen(Point.Empty)).Y, bottom = top + c.Height;
        int h = ClientSize.Height, margin = D(12), o = offset;
        if (top - margin < o) o = top - margin;
        else if (bottom + margin > o + h) o = bottom + margin - h;
        o = Math.Max(0, Math.Min(o, Math.Max(0, contentH - h)));
        if (o == offset) return;
        offset = o;
        bar.Value = offset;
        Place();
    }

    public void ScrollTop() { offset = 0; if (bar.Visible) bar.Value = 0; Place(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!bar.Visible) return;
        int max = Math.Max(0, contentH - ClientSize.Height);
        offset = Math.Max(0, Math.Min(max, offset - e.Delta * D(90) / 120));
        bar.Value = offset;
        Place();
    }
}

/// <summary>Campo de texto con borde redondeado y texto de ayuda.</summary>
public class InputBox : PaintedControl, IMeasure
{
    public readonly TextBox Box = new();
    readonly bool multiline;
    public bool HasError;

    public InputBox(string placeholder, bool password = false, bool multiline = false, int maxLength = 200)
    {
        this.multiline = multiline;
        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = Surface;
        Box.ForeColor = Theme.Text;
        Box.Font = F(10.5f);
        Box.PlaceholderText = placeholder;
        Box.UseSystemPasswordChar = password;
        Box.Multiline = multiline;
        Box.MaxLength = maxLength;
        if (multiline) Box.ScrollBars = ScrollBars.Vertical;
        Box.AccessibleName = placeholder;
        Box.GotFocus += (_, _) => { Invalidate(); Ui.Reveal(this); };
        Box.LostFocus += (_, _) => Invalidate();
        Box.TextChanged += (_, _) => { if (HasError) { HasError = false; Invalidate(); } };
        Controls.Add(Box);
        Cursor = Cursors.IBeam;
        Height = MeasureHeight(0);
    }

    public string Value { get => Box.Text; set => Box.Text = value ?? ""; }

    public int MeasureHeight(int width) => multiline ? D(92) : D(46);

    protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int px = D(14);
        if (multiline) Box.SetBounds(px, D(10), Width - px * 2, Height - D(20));
        else
        {
            int h = Box.PreferredHeight;
            Box.SetBounds(px, (Height - h) / 2, Width - px * 2, h);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Behind);
        HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, Surface, r, Df(11));
        var bc = HasError ? Danger : Box.Focused ? Primary : Outline;
        DrawRound(g, bc, r, Df(11), Box.Focused || HasError ? Df(1.6f) : Df(1));
    }
}

/// <summary>Círculo de color clicable.</summary>
public class SwatchButton : PaintedControl
{
    public readonly Color Color;
    public readonly string ColorName;
    bool selected, hover;

    public SwatchButton(Color c, string name, int size)
    {
        Color = c;
        ColorName = name;
        Size = new Size(size, size);
        Cursor = Cursors.Hand;
        MakeFocusable(AccessibleRole.PushButton, "Color " + name);
    }

    public bool Selected { get => selected; set { selected = value; Invalidate(); } }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        HQ(g);
        float pad = Df(4);
        var r = new RectangleF(pad, pad, Width - pad * 2 - 1, Height - pad * 2 - 1);
        Swatch(g, Color, r);
        if (selected || hover || FocusCue)
        {
            using var pen = new Pen(selected ? Primary : FocusCue ? Theme.Text : Border, Df(2));
            g.DrawEllipse(pen, Df(1), Df(1), Width - Df(2) - 1, Height - Df(2) - 1);
        }
    }
}

/// <summary>Tarjeta de prenda: foto, nombre, color, categoría y temporada.</summary>
public class GarmentCard : PaintedControl
{
    public readonly Prenda P;
    public string? Badge;
    bool hover;

    public GarmentCard(Prenda p)
    {
        P = p;
        Cursor = Cursors.Hand;
        MakeFocusable(AccessibleRole.PushButton, $"{p.Nombre}, {p.ColorNombre}, {Cats.Label(p.Categoria)}, {Cats.SeasonLabel(p.Temporada)}");
    }

    public static int HeightFor(int width) => width - D(16) + D(8) + D(78);

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Behind);
        HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, Surface, r, Df(14));
        bool hot = hover || FocusCue;
        DrawRound(g, hot ? Primary : Border, r, Df(14), FocusCue ? Df(2.5f) : hot ? Df(1.5f) : Df(1));

        int pad = D(8);
        int pw = Width - pad * 2;
        var pr = new RectangleF(pad, pad, pw, pw);
        var img = Photos.Get(P.FotoPath);
        if (img != null) DrawCover(g, img, pr, Df(10));
        else
        {
            FillRound(g, Mannequin, pr, Df(10));
            Theme.DrawText(g, "Sin foto", F(9f), Muted, Rectangle.Round(pr), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (!string.IsNullOrEmpty(Badge))
        {
            var bf = F(8f, FontStyle.Bold);
            int bw = TextWidth(Badge, bf) + D(14);
            var br = new RectangleF(pad + D(6), pad + D(6), bw, D(22));
            FillRound(g, Color.FromArgb(235, 255, 255, 255), br, br.Height / 2);
            Theme.DrawText(g, Badge, bf, Primary, Rectangle.Round(br), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        int y = pad + pw + D(8);
        int tx = D(12), tw = Width - D(24);
        Theme.DrawText(g, P.Nombre, F(10f, FontStyle.Bold), Theme.Text, new Rectangle(tx, y, tw, D(22)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        y += D(24);
        float s = Df(13);
        Swatch(g, P.Color, new RectangleF(tx, y + Df(3), s, s));
        Theme.DrawText(g, P.ColorNombre, F(8.75f), Muted, new Rectangle(tx + D(19), y, tw - D(19), D(20)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        y += D(22);
        Theme.DrawText(g, Cats.Label(P.Categoria) + " · " + Cats.SeasonLabel(P.Temporada), F(8.25f), Muted, new Rectangle(tx, y, tw, D(20)), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Cuadrícula de tarjetas de prendas que se adapta al ancho (2 columnas en celular, más en escritorio).</summary>
public class GarmentGrid : Panel, IMeasure
{
    public int MinCardWidth = D(150);
    public int MaxColumns = 6;
    public int Gap = D(12);

    public GarmentGrid() { DoubleBuffered = true; }

    (int cols, int cw, int ch) Dims(int width)
    {
        int cols = Math.Max(2, Math.Min(MaxColumns, (width + Gap) / (MinCardWidth + Gap)));
        int cw = Math.Max(D(100), (width - Gap * (cols - 1)) / cols);
        return (cols, cw, GarmentCard.HeightFor(cw));
    }

    public int MeasureHeight(int width)
    {
        int n = Controls.Count;
        if (n == 0) return 0;
        var (cols, _, ch) = Dims(width);
        int rows = (n + cols - 1) / cols;
        return rows * ch + (rows - 1) * Gap;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        var (cols, cw, ch) = Dims(Width);
        int i = 0;
        foreach (Control c in Controls)
        {
            int col = i % cols, row = i / cols;
            c.SetBounds(col * (cw + Gap), row * (ch + Gap), cw, ch);
            i++;
        }
    }

    public void SetCards(IEnumerable<GarmentCard> cards)
    {
        SuspendLayout();
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        foreach (var c in cards) Controls.Add(c);
        ResumeLayout();
    }
}

/// <summary>Bloque de estado vacío: ícono, título, texto y una acción opcional.</summary>
public class EmptyState : VStack
{
    public readonly TextBlock Title, Body;
    public readonly FlatButton Action;

    /// <summary>Si action es null, el botón existe pero queda oculto (así Action nunca es null).</summary>
    public EmptyState(string title, string body, string? action = null)
    {
        BackColor = Surface;
        Pad = new Padding(D(20), D(24), D(20), D(24));
        Gap = D(8);
        Controls.Add(new EmptyIcon());
        Title = new TextBlock(title, 12f, FontStyle.Bold) { Align = TextFormatFlags.HorizontalCenter };
        Body = new TextBlock(body, 10f, FontStyle.Regular, Muted) { Align = TextFormatFlags.HorizontalCenter };
        Controls.Add(Title);
        Controls.Add(Body);
        if (action != null) Controls.Add(Ui.Space(D(4)));
        Action = new FlatButton(action ?? "") { Visible = action != null };
        Controls.Add(Action);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Bg);
        HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, Surface, r, Df(16));
        DrawRound(g, Border, r, Df(16), Df(1), System.Drawing.Drawing2D.DashStyle.Dash);
    }

    class EmptyIcon : PaintedControl, IMeasure
    {
        public int MeasureHeight(int width) => D(64);
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            float s = Df(60);
            var r = new RectangleF((Width - s) / 2, 2, s, s);
            using (var b = new SolidBrush(PrimarySoft)) g.FillEllipse(b, r);
            Icons.Hanger(g, RectangleF.Inflate(r, -Df(14), -Df(14)), Primary, Df(2.4f));
        }
    }
}

/// <summary>Íconos simples dibujados con líneas.</summary>
public static class Icons
{
    public static void Hanger(Graphics g, RectangleF r, Color c, float w)
    {
        using var pen = new Pen(c, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        float cx = r.X + r.Width / 2;
        float hookR = r.Width * 0.12f;
        g.DrawArc(pen, cx - hookR, r.Y, hookR * 2, hookR * 2, 180, 270);
        float neckY = r.Y + hookR * 2 + r.Height * 0.08f;
        g.DrawLine(pen, cx, r.Y + hookR * 2, cx, neckY);
        float baseY = r.Y + r.Height * 0.78f;
        g.DrawLines(pen, new[] { new PointF(r.X, baseY), new PointF(cx, neckY), new PointF(r.Right, baseY), new PointF(r.X, baseY) });
    }

    public static void Person(Graphics g, RectangleF r, Color c, float w)
    {
        using var pen = new Pen(c, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        float cx = r.X + r.Width / 2, hr = r.Width * 0.18f;
        g.DrawEllipse(pen, cx - hr, r.Y, hr * 2, hr * 2);
        float sy = r.Y + hr * 2 + r.Height * 0.08f;
        g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.12f, r.Bottom), new PointF(r.X + r.Width * 0.2f, sy), new PointF(r.Right - r.Width * 0.2f, sy), new PointF(r.Right - r.Width * 0.12f, r.Bottom) });
    }

    public static void Star(Graphics g, RectangleF r, Color c, float w, bool filled)
    {
        var pts = new PointF[10];
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2 + r.Height * 0.04f, R = r.Width / 2, ri = R * 0.45f;
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            float rr = i % 2 == 0 ? R : ri;
            pts[i] = new PointF(cx + (float)(Math.Cos(a) * rr), cy + (float)(Math.Sin(a) * rr));
        }
        if (filled) using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        using var pen = new Pen(c, w) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        g.DrawPolygon(pen, pts);
    }

    public static void Palette(Graphics g, RectangleF r, Color c, float w)
    {
        using var pen = new Pen(c, w);
        g.DrawEllipse(pen, r);
        float s = r.Width * 0.2f;
        var dots = new[] { (0.3f, 0.3f, Color.FromArgb(230, 90, 80)), (0.62f, 0.25f, Color.FromArgb(240, 190, 60)), (0.72f, 0.55f, Color.FromArgb(70, 160, 110)), (0.38f, 0.65f, Color.FromArgb(80, 120, 220)) };
        foreach (var (dx, dy, col) in dots)
            using (var b = new SolidBrush(col)) g.FillEllipse(b, r.X + r.Width * dx - s / 2, r.Y + r.Height * dy - s / 2, s, s);
    }

    public static void Trash(Graphics g, RectangleF r, Color c, float w)
    {
        using var pen = new Pen(c, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        float top = r.Y + r.Height * 0.22f;
        g.DrawLine(pen, r.X, top, r.Right, top);
        g.DrawLine(pen, r.X + r.Width * 0.35f, r.Y, r.X + r.Width * 0.65f, r.Y);
        g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.12f, top), new PointF(r.X + r.Width * 0.2f, r.Bottom), new PointF(r.Right - r.Width * 0.2f, r.Bottom), new PointF(r.Right - r.Width * 0.12f, top) });
    }
}
