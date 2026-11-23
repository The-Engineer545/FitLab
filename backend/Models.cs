namespace FitLab;

public enum Category { Superior, Inferior, Calzado, Abrigo, Accesorio }
public enum Season { TodoElAnio, Calor, Frio }

public static class Cats
{
    public static readonly Category[] All = { Category.Superior, Category.Inferior, Category.Calzado, Category.Abrigo, Category.Accesorio };
    public static readonly Category[] Required = { Category.Superior, Category.Inferior, Category.Calzado };

    public static string Label(Category c) => c switch
    {
        Category.Superior => "Parte superior",
        Category.Inferior => "Parte inferior",
        Category.Calzado => "Calzado",
        Category.Abrigo => "Abrigo",
        _ => "Accesorio",
    };

    /// <summary>Forma corta para frases: "cambiá el calzado…".</summary>
    public static string Phrase(Category c) => c switch
    {
        Category.Superior => "la parte superior",
        Category.Inferior => "la parte inferior",
        Category.Calzado => "el calzado",
        Category.Abrigo => "el abrigo",
        _ => "el accesorio",
    };

    public static string Key(Category c) => c switch
    {
        Category.Superior => "superior",
        Category.Inferior => "inferior",
        Category.Calzado => "calzado",
        Category.Abrigo => "abrigo",
        _ => "accesorio",
    };

    public static Category Parse(string s) => s switch
    {
        "superior" => Category.Superior,
        "inferior" => Category.Inferior,
        "calzado" => Category.Calzado,
        "abrigo" => Category.Abrigo,
        _ => Category.Accesorio,
    };

    public static string SeasonLabel(Season s) => s switch
    {
        Season.Calor => "Calor",
        Season.Frio => "Frío",
        _ => "Todo el año",
    };

    public static string SeasonKey(Season s) => s switch
    {
        Season.Calor => "calor",
        Season.Frio => "frio",
        _ => "todo_el_anio",
    };

    public static Season ParseSeason(string s) => s switch
    {
        "calor" => Season.Calor,
        "frio" => Season.Frio,
        _ => Season.TodoElAnio,
    };

    public static readonly (string Key, string Label)[] Occasions =
    {
        ("casual", "Casual"), ("universidad", "Universidad"), ("trabajo", "Trabajo"), ("salida", "Salida"), ("deporte", "Deporte"),
    };

    public static string OccasionLabel(string key) =>
        Occasions.FirstOrDefault(o => o.Key == key).Label ?? "Casual";
}

public class Usuario
{
    public long Id;
    public string Email = "";
    public string Nombre = "";
    /// <summary>False en cuentas creadas antes de que existiera el código de recuperación.</summary>
    public bool TieneRecuperacion;
}

public class Prenda
{
    public long Id;
    public long UsuarioId;
    public string Nombre = "";
    public Category Categoria;
    public string ColorHex = "#000000";
    public string ColorNombre = "";
    public Season Temporada;
    public string Notas = "";
    public string FotoPath = "";
    public DateTime Creado;

    public Color Color => ColorTools.FromHex(ColorHex);
}

public class Outfit
{
    public long Id;
    public string Nombre = "";
    public string Ocasion = "casual";
    public bool Favorito;
    public DateTime Creado;
    public Dictionary<Category, Prenda> Prendas = new();
}

/// <summary>Error pensado para mostrarse tal cual a la persona.</summary>
public class UserError : Exception
{
    /// <summary>Campo del formulario que causó el error ("nombre", "email", "password", "confirm", "credenciales") o null.</summary>
    public readonly string? Field;

    public UserError(string message, string? field = null) : base(message) { Field = field; }
}
