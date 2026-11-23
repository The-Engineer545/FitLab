namespace FitLab;

public enum HarmonyKind { Vacio, Neutros, Monocromatico, Analogos, Complementarios, Choque }
public enum HarmonyLevel { Ninguno, Buena, Media, Baja }

public class HarmonyResult
{
    public HarmonyKind Kind;
    public HarmonyLevel Level;
    public string Title = "";
    public string Verdict = "";
    public string Explanation = "";
    public string Suggestion = "";
}

public static class ColorTools
{
    /// <summary>Paleta de colores con nombre en español, usada en el selector y para nombrar colores.</summary>
    public static readonly (string Name, string Hex)[] Palette =
    {
        ("Negro", "#1C1C1E"), ("Blanco", "#F8F8F6"), ("Gris", "#9A9A9E"), ("Gris oscuro", "#4A4A50"),
        ("Beige", "#D9C7A7"), ("Crema", "#F1E7D0"), ("Marrón", "#6B4A32"), ("Camel", "#B9894F"),
        ("Azul marino", "#1F2A48"), ("Azul", "#2F6FD0"), ("Celeste", "#8EC5EC"), ("Turquesa", "#2BB3B1"),
        ("Verde", "#2E9B57"), ("Verde oliva", "#6B7A3A"), ("Verde menta", "#A8DCC0"), ("Amarillo", "#F2CC3B"),
        ("Mostaza", "#C99A2E"), ("Naranja", "#EE7D2B"), ("Coral", "#F07B6E"), ("Rojo", "#D2312F"),
        ("Bordó", "#7A1F2E"), ("Rosa", "#F2A7C0"), ("Fucsia", "#D63D8E"), ("Violeta", "#7A4FC2"),
        ("Lila", "#C2A9E6"),
    };

    public static Color FromHex(string? hex)
    {
        try
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length != 6) return Color.Gray;
            return Color.FromArgb(Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
        }
        catch { return Color.Gray; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static (double H, double S, double V) Hsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    /// <summary>Negro, blanco, grises, beige/camel/marrón y azul marino se tratan como neutros de moda.</summary>
    public static bool IsNeutral(Color c)
    {
        var (h, s, v) = Hsv(c);
        if (s < 0.16 || v < 0.2) return true;
        if (h >= 18 && h <= 50 && s < 0.42) return true;              // beige, crema, camel claro
        if (h >= 15 && h <= 45 && v < 0.5 && s < 0.7) return true;    // marrones
        if (h >= 205 && h <= 250 && v < 0.36) return true;             // azul marino
        return false;
    }

    public static double HueDist(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    public static double MeanHue(IEnumerable<double> hues)
    {
        double x = 0, y = 0;
        foreach (var h in hues) { x += Math.Cos(h * Math.PI / 180); y += Math.Sin(h * Math.PI / 180); }
        double a = Math.Atan2(y, x) * 180 / Math.PI;
        return a < 0 ? a + 360 : a;
    }

    public static string NearestName(Color c)
    {
        string best = "Color";
        double bd = double.MaxValue;
        var a = Lab(c);
        foreach (var (name, hex) in Palette)
        {
            var b = Lab(FromHex(hex));
            double d = (a.L - b.L) * (a.L - b.L) * 0.6 + (a.A - b.A) * (a.A - b.A) + (a.B - b.B) * (a.B - b.B);
            if (d < bd) { bd = d; best = name; }
        }
        return best;
    }

    /// <summary>Color en espacio CIELAB (D65), para comparar colores como los percibe el ojo.</summary>
    public static (double L, double A, double B) Lab(Color c)
    {
        static double Lin(int v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
        double X = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        double Y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        double Z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
        static double Fn(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        double fx = Fn(X), fy = Fn(Y), fz = Fn(Z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>"a", "a y b", "a, b y c".</summary>
    public static string JoinNames(IEnumerable<string> names)
    {
        var l = names.Distinct().ToList();
        if (l.Count <= 1) return l.FirstOrDefault() ?? "";
        return string.Join(", ", l.Take(l.Count - 1)) + " y " + l[^1];
    }

    /// <summary>Nombre corto de la familia de un tono, para explicaciones.</summary>
    public static string HueFamily(double h) => h switch
    {
        < 15 => "rojos",
        < 40 => "naranjas",
        < 70 => "amarillos",
        < 160 => "verdes",
        < 200 => "turquesas",
        < 255 => "azules",
        < 290 => "violetas",
        < 335 => "rosas y fucsias",
        _ => "rojos",
    };

    static double MaxSpread(IList<double> hues)
    {
        double m = 0;
        for (int i = 0; i < hues.Count; i++)
            for (int j = i + 1; j < hues.Count; j++)
                m = Math.Max(m, HueDist(hues[i], hues[j]));
        return m;
    }

    static bool IsComplementary(IList<double> hues)
    {
        if (hues.Count < 2) return false;
        foreach (var center in hues)
        {
            var a = hues.Where(h => HueDist(h, center) <= 35).ToList();
            var b = hues.Where(h => HueDist(h, center) > 35).ToList();
            if (a.Count == 0 || b.Count == 0) continue;
            if (MaxSpread(a) > 40 || MaxSpread(b) > 40) continue;
            double d = HueDist(MeanHue(a), MeanHue(b));
            if (d >= 145 && d <= 215) return true;
        }
        return false;
    }

    static HarmonyKind Classify(IList<double> hues)
    {
        if (hues.Count == 0) return HarmonyKind.Neutros;
        if (hues.Count == 1) return HarmonyKind.Neutros;
        double spread = MaxSpread(hues);
        if (spread <= 18) return HarmonyKind.Monocromatico;
        if (spread <= 65) return HarmonyKind.Analogos;
        if (IsComplementary(hues)) return HarmonyKind.Complementarios;
        return HarmonyKind.Choque;
    }

    /// <summary>Analiza los colores principales de las prendas del outfit.</summary>
    public static HarmonyResult Analyze(IReadOnlyDictionary<Category, Prenda> items)
    {
        var r = new HarmonyResult();
        if (items.Count == 0)
        {
            r.Kind = HarmonyKind.Vacio; r.Level = HarmonyLevel.Ninguno;
            r.Title = "Sin prendas todavía";
            r.Explanation = "Elegí prendas en las ranuras del maniquí y acá vas a ver si sus colores combinan.";
            return r;
        }

        var chrom = items.Where(kv => !IsNeutral(kv.Value.Color)).ToList();
        var hues = chrom.Select(kv => Hsv(kv.Value.Color).H).ToList();
        var kind = Classify(hues);
        r.Kind = kind;

        switch (kind)
        {
            case HarmonyKind.Neutros when chrom.Count == 0:
                r.Level = HarmonyLevel.Buena;
                r.Title = "Neutros";
                r.Verdict = "Combinación segura";
                r.Explanation = "Todas las prendas son de colores neutros (negro, blanco, gris, beige, marrón o marino). Los neutros combinan entre sí y dan un look prolijo.";
                r.Suggestion = items.ContainsKey(Category.Accesorio)
                    ? "Si querés más personalidad, probá cambiar el accesorio por uno de color."
                    : "Sumá un accesorio de color para darle un toque de vida.";
                break;

            case HarmonyKind.Neutros:
                {
                    var accent = chrom[0];
                    r.Level = HarmonyLevel.Buena;
                    r.Title = "Neutros con acento";
                    r.Verdict = "Combinan muy bien";
                    r.Explanation = $"Un solo color protagonista ({accent.Value.ColorNombre.ToLowerInvariant()} en {Cats.Phrase(accent.Key)}) sobre una base neutra: destaca sin competir con nada.";
                    r.Suggestion = items.Count < 3
                        ? "Completá con prendas neutras para mantener el equilibrio."
                        : "Podés repetir el color de acento en un accesorio para unir el look.";
                    break;
                }

            case HarmonyKind.Monocromatico:
                {
                    var (_, _, v0) = Hsv(chrom[0].Value.Color);
                    bool sameValue = chrom.All(kv => Math.Abs(Hsv(kv.Value.Color).V - v0) < 0.12);
                    r.Level = HarmonyLevel.Buena;
                    r.Title = "Monocromático";
                    r.Verdict = "Combinan muy bien";
                    r.Explanation = $"Las prendas de color están en la misma familia de {HueFamily(MeanHue(hues))}. Un look monocromático se ve elegante y alarga la figura.";
                    r.Suggestion = sameValue
                        ? "Mezclá un tono más claro y uno más oscuro del mismo color para darle profundidad."
                        : "Buena variación de tonos. Un calzado neutro termina de equilibrarlo.";
                    break;
                }

            case HarmonyKind.Analogos:
                r.Level = HarmonyLevel.Buena;
                r.Title = "Análogos";
                r.Verdict = "Combinan bien";
                r.Explanation = $"Los colores están vecinos en la rueda de color ({JoinNames(chrom.Select(kv => kv.Value.ColorNombre.ToLowerInvariant()))}), por eso se ven armónicos y naturales.";
                r.Suggestion = "Dejá que un color domine y usá el otro en menor cantidad, por ejemplo en un accesorio.";
                break;

            case HarmonyKind.Complementarios:
                r.Level = HarmonyLevel.Buena;
                r.Title = "Complementarios";
                r.Verdict = "Contraste que funciona";
                r.Explanation = $"Hay colores opuestos en la rueda ({JoinNames(chrom.Select(kv => kv.Value.ColorNombre.ToLowerInvariant()))}). El contraste da energía y llama la atención.";
                r.Suggestion = items.Count - chrom.Count == 0
                    ? "Usá un color como protagonista y el otro como acento; sumar una prenda neutra ayuda a equilibrar."
                    : "Mantené un color como protagonista y el otro en menor cantidad.";
                break;

            default:
                {
                    r.Level = chrom.Count >= 3 ? HarmonyLevel.Baja : HarmonyLevel.Media;
                    r.Title = "Colores que compiten";
                    r.Verdict = chrom.Count >= 3 ? "No combinan del todo" : "Combinación arriesgada";
                    var names = chrom.Select(kv => kv.Value.ColorNombre.ToLowerInvariant()).Distinct().ToList();
                    r.Explanation = $"Los colores ({JoinNames(names)}) no son vecinos ni opuestos en la rueda, así que compiten entre sí por la atención.";
                    r.Suggestion = SuggestFix(chrom);
                    break;
                }
        }
        return r;
    }

    static string SuggestFix(List<KeyValuePair<Category, Prenda>> chrom)
    {
        // Busca la prenda que, si fuera neutra, dejaría el resto en armonía. Se prefiere cambiar lo más fácil.
        var order = new[] { Category.Calzado, Category.Accesorio, Category.Abrigo, Category.Inferior, Category.Superior };
        foreach (var cat in order)
        {
            var item = chrom.FirstOrDefault(kv => kv.Key == cat);
            if (item.Value == null) continue;
            var rest = chrom.Where(kv => kv.Key != cat).Select(kv => Hsv(kv.Value.Color).H).ToList();
            if (Classify(rest) != HarmonyKind.Choque)
                return $"Cambiá {Cats.Phrase(cat)} ({item.Value.ColorNombre.ToLowerInvariant()}) por una prenda neutra: negro, blanco, gris o beige.";
        }
        return "Dejá un solo color protagonista y pasá el resto de las prendas a neutros (negro, blanco, gris o beige).";
    }

    /// <summary>Relación de una prenda con un color elegido en la guía, o null si no encaja.</summary>
    public static string? MatchReason(Color chosen, Color garment)
    {
        bool gn = IsNeutral(garment), cn = IsNeutral(chosen);
        if (gn) return "Neutro";
        if (cn) return "Va con neutros";
        double hc = Hsv(chosen).H, hg = Hsv(garment).H;
        double d = HueDist(hc, hg);
        if (d <= 18) return "Mismo tono";
        if (d <= 45) return "Análogo";
        if (d >= 155) return "Complementario";
        if (Math.Abs(d - 120) <= 15) return "Tríada";
        return null;
    }
}
