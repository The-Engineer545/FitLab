namespace FitLab.Tests;

/// <summary>Reglas de armonía de colores: la lógica más delicada de la app y la más fácil de romper sin darse cuenta.</summary>
public class ColorToolsTests
{
    static Prenda P(string hex, string nombre = "color") => new() { ColorHex = hex, ColorNombre = nombre };

    static Dictionary<Category, Prenda> Outfit(string? superior = null, string? inferior = null, string? calzado = null)
    {
        var d = new Dictionary<Category, Prenda>();
        if (superior != null) d[Category.Superior] = P(superior);
        if (inferior != null) d[Category.Inferior] = P(inferior);
        if (calzado != null) d[Category.Calzado] = P(calzado);
        return d;
    }

    [Theory]
    [InlineData("#1C1C1E")] // negro
    [InlineData("#F8F8F6")] // blanco
    [InlineData("#9A9A9E")] // gris
    [InlineData("#D9C7A7")] // beige
    [InlineData("#6B4A32")] // marrón
    [InlineData("#1F2A48")] // azul marino
    public void Los_neutros_de_moda_se_reconocen(string hex) => Assert.True(ColorTools.IsNeutral(ColorTools.FromHex(hex)));

    [Theory]
    [InlineData("#D2312F")] // rojo
    [InlineData("#2F6FD0")] // azul
    [InlineData("#2E9B57")] // verde
    [InlineData("#F2CC3B")] // amarillo
    public void Los_colores_vivos_no_son_neutros(string hex) => Assert.False(ColorTools.IsNeutral(ColorTools.FromHex(hex)));

    [Fact]
    public void FromHex_con_texto_invalido_devuelve_gris_sin_fallar()
    {
        Assert.Equal(Color.Gray, ColorTools.FromHex(null));
        Assert.Equal(Color.Gray, ColorTools.FromHex("#12"));
        Assert.Equal(Color.Gray, ColorTools.FromHex("#ZZZZZZ"));
    }

    [Fact]
    public void ToHex_y_FromHex_son_inversos() => Assert.Equal("#2F6FD0", ColorTools.ToHex(ColorTools.FromHex("#2f6fd0")));

    [Fact]
    public void Hsv_y_FromHsv_son_inversos()
    {
        var c = ColorTools.FromHex("#EE7D2B");
        var (h, s, v) = ColorTools.Hsv(c);
        Assert.Equal(c.ToArgb(), ColorTools.FromHsv(h, s, v).ToArgb());
    }

    [Fact]
    public void La_distancia_entre_tonos_da_la_vuelta_a_la_rueda() => Assert.Equal(20, ColorTools.HueDist(350, 10), 6);

    [Fact]
    public void Sin_prendas_el_resultado_es_vacio()
    {
        var r = ColorTools.Analyze(Outfit());
        Assert.Equal(HarmonyKind.Vacio, r.Kind);
        Assert.Equal(HarmonyLevel.Ninguno, r.Level);
    }

    [Fact]
    public void Todo_neutro_combina_bien()
    {
        var r = ColorTools.Analyze(Outfit("#F8F8F6", "#1C1C1E", "#9A9A9E"));
        Assert.Equal(HarmonyKind.Neutros, r.Kind);
        Assert.Equal(HarmonyLevel.Buena, r.Level);
        Assert.Equal("Neutros", r.Title);
    }

    [Fact]
    public void Un_solo_color_sobre_neutros_es_un_acento()
    {
        var r = ColorTools.Analyze(Outfit("#D2312F", "#1C1C1E", "#F8F8F6"));
        Assert.Equal(HarmonyLevel.Buena, r.Level);
        Assert.Equal("Neutros con acento", r.Title);
    }

    [Fact]
    public void Dos_tonos_casi_iguales_son_monocromaticos()
    {
        var r = ColorTools.Analyze(Outfit("#2F6FD0", "#8EC5EC"));
        Assert.Contains(r.Kind, new[] { HarmonyKind.Monocromatico, HarmonyKind.Analogos });
        Assert.Equal(HarmonyLevel.Buena, r.Level);
    }

    [Fact]
    public void Azul_y_naranja_son_complementarios()
    {
        var r = ColorTools.Analyze(Outfit("#2F6FD0", "#EE7D2B"));
        Assert.Equal(HarmonyKind.Complementarios, r.Kind);
        Assert.Equal(HarmonyLevel.Buena, r.Level);
    }

    [Fact]
    public void Tres_colores_sin_relacion_chocan_y_se_sugiere_un_cambio()
    {
        var r = ColorTools.Analyze(Outfit("#D2312F", "#2E9B57", "#7A4FC2"));
        Assert.Equal(HarmonyKind.Choque, r.Kind);
        Assert.Equal(HarmonyLevel.Baja, r.Level);
        Assert.False(string.IsNullOrWhiteSpace(r.Suggestion));
    }

    [Fact]
    public void MatchReason_explica_la_relacion_con_el_color_elegido()
    {
        var azul = ColorTools.FromHex("#2F6FD0");
        Assert.Equal("Neutro", ColorTools.MatchReason(azul, ColorTools.FromHex("#1C1C1E")));
        Assert.Equal("Mismo tono", ColorTools.MatchReason(azul, azul));
        Assert.Equal("Complementario", ColorTools.MatchReason(azul, ColorTools.FromHex("#EE7D2B")));
        Assert.Equal("Va con neutros", ColorTools.MatchReason(ColorTools.FromHex("#1C1C1E"), azul));
        Assert.Null(ColorTools.MatchReason(azul, ColorTools.FromHsv(ColorTools.Hsv(azul).H + 80, 0.8, 0.8)));
    }

    [Fact]
    public void NearestName_devuelve_el_nombre_exacto_de_la_paleta()
    {
        foreach (var (name, hex) in ColorTools.Palette)
            Assert.Equal(name, ColorTools.NearestName(ColorTools.FromHex(hex)));
    }

    [Fact]
    public void JoinNames_arma_la_frase_en_espanol()
    {
        Assert.Equal("", ColorTools.JoinNames(Array.Empty<string>()));
        Assert.Equal("rojo", ColorTools.JoinNames(new[] { "rojo" }));
        Assert.Equal("rojo y azul", ColorTools.JoinNames(new[] { "rojo", "azul" }));
        Assert.Equal("rojo, azul y verde", ColorTools.JoinNames(new[] { "rojo", "azul", "rojo", "verde" }));
    }
}
