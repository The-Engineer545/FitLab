using Microsoft.Data.Sqlite;

namespace FitLab.Tests;

/// <summary>
/// Pruebas de cuentas, prendas y outfits contra una base de datos real en una carpeta temporal
/// (variable FITLAB_DATA). Cada prueba usa correos distintos, así que no dependen del orden.
/// </summary>
public class DbTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "fitlab-tests-" + Guid.NewGuid().ToString("N"));
    const string Clave = "clave-segura-1";

    public DbTests()
    {
        Environment.SetEnvironmentVariable("FITLAB_DATA", dir);
        Db.Init();
    }

    public void Dispose()
    {
        Photos.Clear();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }

    static Usuario Alta(string email) => Db.Register("Persona de Prueba", email, Clave, Clave);

    static Prenda Prenda(Usuario u, string nombre, Category cat, string hex = "#2F6FD0")
    {
        var p = new Prenda { UsuarioId = u.Id, Nombre = nombre, Categoria = cat, ColorHex = hex, ColorNombre = "Azul", FotoPath = "sin-foto.jpg" };
        Db.SavePrenda(p);
        return p;
    }

    static List<Outfit> Outfits(Usuario u) => Db.ListOutfits(u.Id, Db.ListPrendas(u.Id).ToDictionary(p => p.Id));

    // ---------- Cuentas ----------

    [Fact]
    public void Registrarse_y_luego_iniciar_sesion()
    {
        var u = Alta("Ana@Correo.com");
        var again = Db.Login("  ana@correo.COM ", Clave);
        Assert.Equal(u.Id, again.Id);
        Assert.Equal("ana@correo.com", again.Email);
    }

    [Fact]
    public void Contrasena_incorrecta_no_entra()
    {
        Alta("beto@correo.com");
        var ex = Assert.Throws<UserError>(() => Db.Login("beto@correo.com", "otra-clave-99"));
        Assert.Equal("credenciales", ex.Field);
    }

    [Fact]
    public void Un_correo_que_no_existe_da_el_mismo_error_que_una_contrasena_mala()
    {
        Alta("caro@correo.com");
        var a = Assert.Throws<UserError>(() => Db.Login("nadie@correo.com", Clave));
        var b = Assert.Throws<UserError>(() => Db.Login("caro@correo.com", "equivocada-1"));
        Assert.Equal(a.Message, b.Message);
    }

    [Theory]
    [InlineData("", "a@b.com", Clave, Clave, "nombre")]
    [InlineData("Ana", "", Clave, Clave, "email")]
    [InlineData("Ana", "sin-arroba", Clave, Clave, "email")]
    [InlineData("Ana", "a@b.com", "corta", "corta", "password")]
    [InlineData("Ana", "a@b.com", Clave, "distinta-123", "confirm")]
    public void El_registro_indica_que_campo_esta_mal(string nombre, string email, string pass, string confirm, string campo)
    {
        var ex = Assert.Throws<UserError>(() => Db.Register(nombre, email, pass, confirm));
        Assert.Equal(campo, ex.Field);
    }

    [Fact]
    public void No_se_puede_registrar_dos_veces_el_mismo_correo()
    {
        Alta("dani@correo.com");
        var ex = Assert.Throws<UserError>(() => Alta("DANI@correo.com"));
        Assert.Equal("email", ex.Field);
    }

    [Fact]
    public void Una_cuenta_antigua_entra_y_se_actualiza_a_la_proteccion_nueva()
    {
        var u = Alta("eva@correo.com");
        // Simula una cuenta creada antes de la versión 1: hash con las vueltas antiguas.
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(Clave, salt, Db.LegacyIterations, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        using (var c = new SqliteConnection("Data Source=" + Db.DbPath))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE usuarios SET password_hash=$h, password_salt=$s, password_iter=$i WHERE id=$id";
            cmd.Parameters.AddWithValue("$h", Convert.ToBase64String(hash));
            cmd.Parameters.AddWithValue("$s", Convert.ToBase64String(salt));
            cmd.Parameters.AddWithValue("$i", Db.LegacyIterations);
            cmd.Parameters.AddWithValue("$id", u.Id);
            cmd.ExecuteNonQuery();
        }

        Assert.Equal(u.Id, Db.Login("eva@correo.com", Clave).Id);

        using (var c = new SqliteConnection("Data Source=" + Db.DbPath))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT password_iter FROM usuarios WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", u.Id);
            Assert.Equal((long)Db.Iterations, cmd.ExecuteScalar());
        }
        Assert.Equal(u.Id, Db.Login("eva@correo.com", Clave).Id);
    }

    // ---------- Recuperación de contraseña ----------

    [Fact]
    public void Con_el_codigo_se_cambia_la_contrasena_y_el_codigo_se_renueva()
    {
        var u = Db.Register("Pía", "pia@correo.com", Clave, Clave, out var code);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", code);

        // Se acepta en minúsculas y sin guiones.
        var again = Db.ResetPassword("PIA@correo.com", code.ToLowerInvariant().Replace("-", " "), "nueva-clave-22", "nueva-clave-22", out var next);
        Assert.Equal(u.Id, again.Id);
        Assert.NotEqual(code, next);

        Assert.Equal(u.Id, Db.Login("pia@correo.com", "nueva-clave-22").Id);
        Assert.Throws<UserError>(() => Db.Login("pia@correo.com", Clave));

        // El código usado ya no sirve; el nuevo sí.
        var used = Assert.Throws<UserError>(() => Db.ResetPassword("pia@correo.com", code, "otra-clave-33", "otra-clave-33", out _));
        Assert.Equal("recuperacion", used.Field);
        Db.ResetPassword("pia@correo.com", next, "otra-clave-33", "otra-clave-33", out _);
        Assert.Equal(u.Id, Db.Login("pia@correo.com", "otra-clave-33").Id);
    }

    [Fact]
    public void Un_codigo_equivocado_o_un_correo_ajeno_no_cambian_nada()
    {
        Db.Register("Quique", "quique@correo.com", Clave, Clave, out var code);
        Db.Register("Rosa", "rosa@correo.com", Clave, Clave, out _);

        var a = Assert.Throws<UserError>(() => Db.ResetPassword("quique@correo.com", "AAAA-BBBB-CCCC", "nueva-clave-22", "nueva-clave-22", out _));
        var b = Assert.Throws<UserError>(() => Db.ResetPassword("rosa@correo.com", code, "nueva-clave-22", "nueva-clave-22", out _));
        var c = Assert.Throws<UserError>(() => Db.ResetPassword("nadie@correo.com", code, "nueva-clave-22", "nueva-clave-22", out _));
        Assert.Equal(a.Message, b.Message);
        Assert.Equal(a.Message, c.Message);

        Assert.NotNull(Db.Login("quique@correo.com", Clave));
        Assert.NotNull(Db.Login("rosa@correo.com", Clave));
    }

    [Fact]
    public void La_contrasena_nueva_tambien_se_valida()
    {
        Db.Register("Santi", "santi@correo.com", Clave, Clave, out var code);
        Assert.Equal("password", Assert.Throws<UserError>(() => Db.ResetPassword("santi@correo.com", code, "corta", "corta", out _)).Field);
        Assert.Equal("confirm", Assert.Throws<UserError>(() => Db.ResetPassword("santi@correo.com", code, "nueva-clave-22", "distinta-22", out _)).Field);
        // Como falló la validación, el código sigue sirviendo.
        Db.ResetPassword("santi@correo.com", code, "nueva-clave-22", "nueva-clave-22", out _);
    }

    [Fact]
    public void Una_cuenta_sin_codigo_lo_obtiene_y_el_viejo_deja_de_servir()
    {
        var u = Alta("tomi@correo.com");
        using (var c = new SqliteConnection("Data Source=" + Db.DbPath))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE usuarios SET recovery_hash=NULL, recovery_salt=NULL WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", u.Id);
            cmd.ExecuteNonQuery();
        }
        Assert.False(Db.Login("tomi@correo.com", Clave).TieneRecuperacion);
        Assert.Throws<UserError>(() => Db.ResetPassword("tomi@correo.com", "ABCD-EFGH-JKMN", "nueva-clave-22", "nueva-clave-22", out _));

        var first = Db.NewRecoveryCode(u.Id);
        Assert.True(Db.Login("tomi@correo.com", Clave).TieneRecuperacion);
        var second = Db.NewRecoveryCode(u.Id);
        Assert.Throws<UserError>(() => Db.ResetPassword("tomi@correo.com", first, "nueva-clave-22", "nueva-clave-22", out _));
        Db.ResetPassword("tomi@correo.com", second, "nueva-clave-22", "nueva-clave-22", out _);
    }

    [Fact]
    public void Init_se_puede_repetir_sin_romper_la_base()
    {
        var u = Alta("fede@correo.com");
        Db.Init();
        Db.Init();
        Assert.Equal(u.Id, Db.Login("fede@correo.com", Clave).Id);
    }

    // ---------- Prendas ----------

    [Fact]
    public void Guardar_editar_y_listar_una_prenda()
    {
        var u = Alta("gabi@correo.com");
        var p = Prenda(u, "Remera", Category.Superior);
        Assert.True(p.Id > 0);

        p.Nombre = "Remera blanca";
        p.Temporada = Season.Calor;
        Db.SavePrenda(p);

        var saved = Assert.Single(Db.ListPrendas(u.Id));
        Assert.Equal("Remera blanca", saved.Nombre);
        Assert.Equal(Season.Calor, saved.Temporada);
        Assert.Equal(Category.Superior, saved.Categoria);
    }

    [Fact]
    public void Cada_persona_solo_ve_y_modifica_sus_prendas()
    {
        var a = Alta("hugo@correo.com");
        var b = Alta("ines@correo.com");
        var p = Prenda(a, "Jean", Category.Inferior);

        Assert.Empty(Db.ListPrendas(b.Id));

        var ajena = new Prenda { Id = p.Id, UsuarioId = b.Id, Nombre = "Robada", Categoria = Category.Inferior, ColorHex = "#000000", ColorNombre = "Negro", FotoPath = "x.jpg" };
        Assert.Throws<UserError>(() => Db.SavePrenda(ajena));
        Db.DeletePrenda(b.Id, p);
        Assert.Equal("Jean", Assert.Single(Db.ListPrendas(a.Id)).Nombre);
    }

    // ---------- Outfits ----------

    [Fact]
    public void Guardar_un_outfit_y_marcarlo_favorito()
    {
        var u = Alta("juan@correo.com");
        var sup = Prenda(u, "Remera", Category.Superior);
        var inf = Prenda(u, "Jean", Category.Inferior);
        long id = Db.AddOutfit(u.Id, "Lunes", "casual", false, new Dictionary<Category, Prenda> { [Category.Superior] = sup, [Category.Inferior] = inf });

        var o = Assert.Single(Outfits(u));
        Assert.Equal(2, o.Prendas.Count);
        Assert.False(o.Favorito);

        Db.SetFavorito(u.Id, id, true);
        Assert.True(Assert.Single(Outfits(u)).Favorito);

        Db.DeleteOutfit(u.Id, id);
        Assert.Empty(Outfits(u));
        Assert.Equal(2, Db.ListPrendas(u.Id).Count);
    }

    [Fact]
    public void Un_outfit_no_puede_incluir_prendas_de_otra_persona()
    {
        var a = Alta("kari@correo.com");
        var b = Alta("leo@correo.com");
        var ajena = Prenda(a, "Campera", Category.Abrigo);
        var propia = Prenda(b, "Botas", Category.Calzado);
        Db.AddOutfit(b.Id, "Mezcla", "casual", false, new Dictionary<Category, Prenda> { [Category.Abrigo] = ajena, [Category.Calzado] = propia });

        var o = Assert.Single(Outfits(b));
        Assert.Equal(new[] { Category.Calzado }, o.Prendas.Keys.ToArray());
    }

    [Fact]
    public void Al_eliminar_una_prenda_sale_de_los_outfits_y_los_vacios_se_borran()
    {
        var u = Alta("mora@correo.com");
        var sup = Prenda(u, "Remera", Category.Superior);
        var inf = Prenda(u, "Jean", Category.Inferior);
        Db.AddOutfit(u.Id, "Completo", "casual", false, new Dictionary<Category, Prenda> { [Category.Superior] = sup, [Category.Inferior] = inf });
        Db.AddOutfit(u.Id, "Solo remera", "casual", false, new Dictionary<Category, Prenda> { [Category.Superior] = sup });
        Assert.Equal(2, Db.OutfitsUsing(u.Id, sup.Id));

        Db.DeletePrenda(u.Id, sup);

        var o = Assert.Single(Outfits(u));
        Assert.Equal("Completo", o.Nombre);
        Assert.Equal(new[] { Category.Inferior }, o.Prendas.Keys.ToArray());
    }

    [Fact]
    public void Al_cambiar_la_categoria_la_prenda_pasa_a_la_ranura_nueva_del_outfit()
    {
        var u = Alta("nico@correo.com");
        var camisa = Prenda(u, "Camisa", Category.Superior);
        var jean = Prenda(u, "Jean", Category.Inferior);
        Db.AddOutfit(u.Id, "Oficina", "trabajo", false, new Dictionary<Category, Prenda> { [Category.Superior] = camisa, [Category.Inferior] = jean });

        camisa.Categoria = Category.Abrigo;
        Db.SavePrenda(camisa);

        var o = Assert.Single(Outfits(u));
        Assert.False(o.Prendas.ContainsKey(Category.Superior));
        Assert.Equal(camisa.Id, o.Prendas[Category.Abrigo].Id);
        Assert.Equal(jean.Id, o.Prendas[Category.Inferior].Id);
    }

    [Fact]
    public void Si_la_ranura_nueva_esta_ocupada_la_prenda_sale_del_outfit()
    {
        var u = Alta("olga@correo.com");
        var camisa = Prenda(u, "Camisa", Category.Superior);
        var campera = Prenda(u, "Campera", Category.Abrigo);
        Db.AddOutfit(u.Id, "Frío", "casual", false, new Dictionary<Category, Prenda> { [Category.Superior] = camisa, [Category.Abrigo] = campera });
        Db.AddOutfit(u.Id, "Solo camisa", "casual", false, new Dictionary<Category, Prenda> { [Category.Superior] = camisa });

        camisa.Categoria = Category.Abrigo;
        Db.SavePrenda(camisa);

        var list = Outfits(u);
        Assert.Equal(2, list.Count);
        var frio = list.Single(o => o.Nombre == "Frío");
        Assert.Equal(campera.Id, Assert.Single(frio.Prendas).Value.Id);
        var solo = list.Single(o => o.Nombre == "Solo camisa");
        Assert.Equal(camisa.Id, solo.Prendas[Category.Abrigo].Id);
    }
}

/// <summary>Una base creada por la primera versión de FitLab (sin número de versión) se actualiza sin perder cuentas.</summary>
public class MigrationTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "fitlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Una_base_de_la_version_original_se_migra_y_la_cuenta_sigue_entrando()
    {
        Directory.CreateDirectory(dir);
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("vieja-clave", salt, Db.LegacyIterations, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        using (var c = new SqliteConnection("Data Source=" + Path.Combine(dir, "fitlab.db")))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            // Tabla de usuarios tal como la creaba la primera versión.
            cmd.CommandText = @"CREATE TABLE usuarios (id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL UNIQUE COLLATE NOCASE,
                nombre TEXT NOT NULL, password_hash TEXT NOT NULL, password_salt TEXT NOT NULL, creado TEXT NOT NULL);
                INSERT INTO usuarios (email, nombre, password_hash, password_salt, creado) VALUES ('vieja@correo.com', 'Cuenta vieja', $h, $s, '2026-01-01 10:00:00');";
            cmd.Parameters.AddWithValue("$h", Convert.ToBase64String(hash));
            cmd.Parameters.AddWithValue("$s", Convert.ToBase64String(salt));
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        Environment.SetEnvironmentVariable("FITLAB_DATA", dir);
        Db.Init();

        var u = Db.Login("vieja@correo.com", "vieja-clave");
        Assert.Equal("Cuenta vieja", u.Nombre);
        Assert.False(u.TieneRecuperacion);
        var code = Db.NewRecoveryCode(u.Id);
        Db.ResetPassword("vieja@correo.com", code, "clave-nueva-1", "clave-nueva-1", out _);
        Assert.Equal(u.Id, Db.Login("vieja@correo.com", "clave-nueva-1").Id);

        using var check = new SqliteConnection("Data Source=" + Db.DbPath);
        check.Open();
        using var v = check.CreateCommand();
        v.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, v.ExecuteScalar());
    }
}

/// <summary>Guardado y lectura de fotos en disco.</summary>
public class PhotosTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "fitlab-tests-" + Guid.NewGuid().ToString("N"));

    public PhotosTests()
    {
        Environment.SetEnvironmentVariable("FITLAB_DATA", dir);
        Db.Init();
    }

    public void Dispose()
    {
        Photos.Clear();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Guardar_leer_y_borrar_una_foto()
    {
        using var bmp = new Bitmap(1600, 800);
        var rel = Photos.Save(bmp, 7);
        Assert.StartsWith("u7/", rel);
        Assert.True(File.Exists(Photos.FullPath(rel)));

        var thumb = Photos.Get(rel);
        Assert.NotNull(thumb);
        Assert.Equal(480, Math.Max(thumb!.Width, thumb.Height));
        Assert.Same(thumb, Photos.Get(rel));

        Photos.Delete(rel);
        Assert.False(File.Exists(Photos.FullPath(rel)));
        Assert.Null(Photos.Get(rel));
    }

    [Fact]
    public void Importar_rechaza_archivos_que_no_son_imagenes()
    {
        Directory.CreateDirectory(dir);
        var txt = Path.Combine(dir, "nota.txt");
        File.WriteAllText(txt, "hola");
        var falso = Path.Combine(dir, "falso.jpg");
        File.WriteAllText(falso, "esto no es una imagen");

        Assert.Throws<UserError>(() => Photos.LoadForImport(txt));
        Assert.Throws<UserError>(() => Photos.LoadForImport(falso));
        Assert.Throws<UserError>(() => Photos.LoadForImport(Path.Combine(dir, "no-existe.png")));
    }

    // Muchas tiendas web sirven las fotos en WebP, aunque el archivo descargado diga .jpg o .png.
    [Theory]
    [InlineData("camisa.webp")]
    [InlineData("camisa-webp-con-extension.jpg")]
    public void Importar_acepta_fotos_WebP_aunque_tengan_otra_extension(string fixture)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        using var img = Photos.LoadForImport(file);
        Assert.Equal(Photos.MaxSide, img.Width);
        Assert.Equal(750, img.Height);

        using var bmp = new Bitmap(img);
        var corner = bmp.GetPixel(5, 5);           // el fondo transparente queda blanco
        Assert.Equal((255, 255, 255), (corner.R, corner.G, corner.B));
        var center = bmp.GetPixel(500, 500);       // la camisa sigue siendo azul
        Assert.True(center.B > 150 && center.R < 100, $"Se esperaba azul y llegó {center}");

        // Y se puede guardar como cualquier otra foto.
        var rel = Photos.Save(img, 9);
        Assert.NotNull(Photos.Get(rel));
    }

    // ---------- Recorte del fondo para el maniquí ----------

    static Bitmap Producto(Color fondoArriba, Color fondoAbajo, Color prenda)
    {
        var bmp = new Bitmap(300, 400);
        using var g = Graphics.FromImage(bmp);
        using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 300, 400), fondoArriba, fondoAbajo, 90f))
            g.FillRectangle(br, 0, 0, 300, 400);
        using (var b = new SolidBrush(prenda)) g.FillEllipse(b, 75, 100, 150, 200);
        return bmp;
    }

    [Fact]
    public void El_recorte_quita_un_fondo_blanco_y_deja_solo_la_prenda()
    {
        using var src = Producto(Color.White, Color.White, Color.FromArgb(47, 111, 208));
        using var cut = Photos.MakeCutout(src);
        Assert.InRange(cut.Width, 148, 154);   // recortada al contorno de la prenda (150 x 200)
        Assert.InRange(cut.Height, 198, 204);
        Assert.Equal(0, cut.GetPixel(1, 1).A); // la esquina de la caja ya es fondo transparente
        var centro = cut.GetPixel(cut.Width / 2, cut.Height / 2);
        Assert.Equal(255, centro.A);
        Assert.Equal(208, centro.B);
    }

    [Fact]
    public void El_recorte_sigue_fondos_con_degrade_suave()
    {
        using var src = Producto(Color.FromArgb(240, 237, 232), Color.FromArgb(220, 216, 210), Color.FromArgb(210, 49, 47));
        using var cut = Photos.MakeCutout(src);
        Assert.InRange(cut.Width, 148, 154);
        Assert.Equal(0, cut.GetPixel(1, 1).A);
    }

    [Fact]
    public void Si_el_fondo_no_es_parejo_la_foto_queda_entera()
    {
        using var src = new Bitmap(200, 200);
        var rnd = new Random(1);
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++) src.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
        using var cut = Photos.MakeCutout(src);
        Assert.Equal((200, 200), (cut.Width, cut.Height));
        Assert.Equal(255, cut.GetPixel(0, 0).A);
    }

    [Fact]
    public void Una_prenda_del_mismo_color_que_el_fondo_queda_entera()
    {
        using var src = Producto(Color.White, Color.White, Color.FromArgb(252, 252, 252));
        using var cut = Photos.MakeCutout(src);
        Assert.Equal((300, 400), (cut.Width, cut.Height));
    }

    [Fact]
    public void GetCutout_usa_la_cache_y_se_libera_al_borrar()
    {
        using var src = Producto(Color.White, Color.White, Color.FromArgb(46, 155, 87));
        var rel = Photos.Save(src, 3);
        var a = Photos.GetCutout(rel);
        Assert.NotNull(a);
        Assert.Same(a, Photos.GetCutout(rel));
        Photos.Delete(rel);
        Assert.Null(Photos.GetCutout(rel));
        Assert.Null(Photos.GetCutout(null));
    }

    [Fact]
    public void Importar_acepta_fotos_grandes_de_celular()
    {
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "celular.jpg");
        using (var big = new Bitmap(4000, 3000)) big.Save(file, System.Drawing.Imaging.ImageFormat.Jpeg);
        using var img = Photos.LoadForImport(file);
        Assert.Equal(Photos.MaxSide, img.Width);
    }

    [Fact]
    public void Importar_achica_las_fotos_grandes()
    {
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "grande.png");
        using (var big = new Bitmap(2400, 1200)) big.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        using var img = Photos.LoadForImport(file);
        Assert.Equal(Photos.MaxSide, img.Width);
        Assert.Equal(Photos.MaxSide / 2, img.Height);
    }
}
