using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace FitLab;

/// <summary>Base de datos SQLite: usuarios, prendas, outfits y outfit_prendas. Las fotos van a archivos.</summary>
public static class Db
{
    public static string DataDir = "";
    public static string PhotosDir = "";
    public static string DbPath = "";
    static string connStr = "";

    /// <summary>Vueltas de PBKDF2 para las contraseñas nuevas (recomendación de OWASP para SHA-256).</summary>
    public const int Iterations = 600_000;
    /// <summary>Vueltas con las que se guardaron las cuentas creadas antes de la versión 1 del esquema.</summary>
    public const int LegacyIterations = 120_000;
    public const int MinPassword = 8;

    public static void Init()
    {
        var dir = Environment.GetEnvironmentVariable("FITLAB_DATA");
        DataDir = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FitLab")
            : dir;
        Directory.CreateDirectory(DataDir);
        PhotosDir = Path.Combine(DataDir, "fotos");
        Directory.CreateDirectory(PhotosDir);
        DbPath = Path.Combine(DataDir, "fitlab.db");
        connStr = new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

        using var c = Open();
        Exec(c, @"
CREATE TABLE IF NOT EXISTS usuarios (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    email TEXT NOT NULL UNIQUE COLLATE NOCASE,
    nombre TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    creado TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS prendas (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    usuario_id INTEGER NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    nombre TEXT NOT NULL,
    categoria TEXT NOT NULL CHECK (categoria IN ('superior','inferior','calzado','abrigo','accesorio')),
    color_hex TEXT NOT NULL,
    color_nombre TEXT NOT NULL,
    temporada TEXT NOT NULL CHECK (temporada IN ('todo_el_anio','calor','frio')),
    notas TEXT,
    foto_path TEXT NOT NULL,
    creado TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_prendas_usuario ON prendas(usuario_id);
CREATE TABLE IF NOT EXISTS outfits (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    usuario_id INTEGER NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    nombre TEXT NOT NULL,
    ocasion TEXT NOT NULL CHECK (ocasion IN ('casual','universidad','trabajo','salida','deporte')),
    favorito INTEGER NOT NULL DEFAULT 0,
    creado TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_outfits_usuario ON outfits(usuario_id);
CREATE TABLE IF NOT EXISTS outfit_prendas (
    outfit_id INTEGER NOT NULL REFERENCES outfits(id) ON DELETE CASCADE,
    prenda_id INTEGER NOT NULL REFERENCES prendas(id) ON DELETE CASCADE,
    ranura TEXT NOT NULL,
    PRIMARY KEY (outfit_id, ranura)
);");
        Migrate(c);
    }

    /// <summary>
    /// Cambios de esquema posteriores a la primera versión. El número guardado en PRAGMA user_version
    /// indica cuáles ya se aplicaron; cada cambio nuevo va en un "if (v &lt; N)" propio. No retorna nada.
    /// </summary>
    static void Migrate(SqliteConnection c)
    {
        long v;
        using (var q = Cmd(c, "PRAGMA user_version")) v = Convert.ToInt64(q.ExecuteScalar());
        if (v < 1)
        {
            // v1: cada cuenta recuerda con cuántas vueltas se calculó su contraseña.
            using var tx = c.BeginTransaction();
            Exec(c, $"ALTER TABLE usuarios ADD COLUMN password_iter INTEGER NOT NULL DEFAULT {LegacyIterations}; PRAGMA user_version = 1;", tx);
            tx.Commit();
        }
        if (v < 2)
        {
            // v2: código de recuperación (se guarda solo su hash, igual que la contraseña).
            using var tx = c.BeginTransaction();
            Exec(c, "ALTER TABLE usuarios ADD COLUMN recovery_hash TEXT; ALTER TABLE usuarios ADD COLUMN recovery_salt TEXT; PRAGMA user_version = 2;", tx);
            tx.Commit();
        }
    }

    static SqliteConnection Open()
    {
        var c = new SqliteConnection(connStr);
        c.Open();
        Exec(c, "PRAGMA foreign_keys = ON;");
        return c;
    }

    static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    static SqliteCommand Cmd(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }

    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    // ---------- Usuarios ----------

    static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    public static Usuario Register(string? nombre, string? email, string? password, string? confirm) =>
        Register(nombre, email, password, confirm, out _);

    /// <summary>
    /// Crea la cuenta. Entradas: nombre, correo, contraseña y su repetición.
    /// Salida: la persona creada; en recoveryCode, el código de recuperación (hay que mostrarlo una sola vez).
    /// </summary>
    public static Usuario Register(string? nombre, string? email, string? password, string? confirm, out string recoveryCode)
    {
        nombre = (nombre ?? "").Trim();
        email = (email ?? "").Trim().ToLowerInvariant();
        if (nombre.Length == 0) throw new UserError("Contanos tu nombre para crear la cuenta.", "nombre");
        if (nombre.Length > 60) throw new UserError("El nombre es muy largo (máximo 60 caracteres).", "nombre");
        if (email.Length == 0) throw new UserError("Ingresá tu correo electrónico.", "email");
        if (!EmailRx.IsMatch(email)) throw new UserError("Ese correo no parece válido. Revisalo, por ejemplo: nombre@correo.com", "email");
        var pass = CheckNewPassword(password, confirm);

        using var c = Open();
        using (var chk = Cmd(c, "SELECT COUNT(*) FROM usuarios WHERE email = $e", ("$e", email)))
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0)
                throw new UserError("Ya existe una cuenta con ese correo. Probá iniciar sesión.", "email");

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Hash(pass, salt, Iterations);
        recoveryCode = NewCode();
        var rsalt = RandomNumberGenerator.GetBytes(16);
        using var ins = Cmd(c, "INSERT INTO usuarios (email, nombre, password_hash, password_salt, password_iter, recovery_hash, recovery_salt, creado) VALUES ($e,$n,$h,$s,$i,$rh,$rs,$c); SELECT last_insert_rowid();",
            ("$e", email), ("$n", nombre), ("$h", Convert.ToBase64String(hash)), ("$s", Convert.ToBase64String(salt)), ("$i", Iterations),
            ("$rh", Convert.ToBase64String(Hash(NormalizeCode(recoveryCode), rsalt, Iterations))), ("$rs", Convert.ToBase64String(rsalt)), ("$c", Now()));
        long id = Convert.ToInt64(ins.ExecuteScalar());
        return new Usuario { Id = id, Email = email, Nombre = nombre, TieneRecuperacion = true };
    }

    /// <summary>Valida una contraseña nueva y su repetición. Devuelve la contraseña ya comprobada.</summary>
    static string CheckNewPassword(string? password, string? confirm)
    {
        password ??= "";
        if (password.Length < MinPassword) throw new UserError($"La contraseña tiene que tener al menos {MinPassword} caracteres.", "password");
        if (password != confirm) throw new UserError("Las contraseñas no coinciden. Escribilas de nuevo.", "confirm");
        return password;
    }

    // ---------- Código de recuperación ----------

    // Sin letras ni números que se confundan al copiarlos a mano (0/O, 1/I/L).
    const string CodeChars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Código nuevo al azar con el formato XXXX-XXXX-XXXX (unos 59 bits de azar).</summary>
    static string NewCode()
    {
        var chars = RandomNumberGenerator.GetItems<char>(CodeChars, 12);
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    /// <summary>Quita guiones y espacios y pasa a mayúsculas, para que "abcd efgh-jkmn" valga igual que "ABCD-EFGH-JKMN".</summary>
    public static string NormalizeCode(string? code) =>
        new string((code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Crea (o reemplaza) el código de recuperación de una cuenta. Entrada: id de la persona.
    /// Salida: el código nuevo, que hay que mostrar una sola vez; el anterior deja de servir.
    /// </summary>
    public static string NewRecoveryCode(long userId)
    {
        var code = NewCode();
        var salt = RandomNumberGenerator.GetBytes(16);
        using var c = Open();
        using var cmd = Cmd(c, "UPDATE usuarios SET recovery_hash=$h, recovery_salt=$s WHERE id=$id",
            ("$h", Convert.ToBase64String(Hash(NormalizeCode(code), salt, Iterations))), ("$s", Convert.ToBase64String(salt)), ("$id", userId));
        if (cmd.ExecuteNonQuery() == 0) throw new UserError("No encontramos tu cuenta.");
        return code;
    }

    /// <summary>
    /// Cambia la contraseña olvidada usando el código de recuperación. Entradas: correo, código, contraseña nueva y su repetición.
    /// Salida: la persona (con sesión lista para iniciar); en newCode, un código nuevo, porque cada código sirve una sola vez.
    /// </summary>
    public static Usuario ResetPassword(string? email, string? code, string? password, string? confirm, out string newCode)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        var given = NormalizeCode(code);
        if (email.Length == 0 || given.Length == 0)
            throw new UserError("Completá tu correo y tu código de recuperación.", "recuperacion");
        var pass = CheckNewPassword(password, confirm);

        // El mismo mensaje para "no existe el correo", "la cuenta no tiene código" y "el código no coincide".
        const string Wrong = "El correo o el código de recuperación no son correctos.";
        using var c = Open();
        Usuario u;
        byte[] salt, expected;
        using (var cmd = Cmd(c, "SELECT id, email, nombre, recovery_hash, recovery_salt FROM usuarios WHERE email = $e", ("$e", email)))
        using (var r = cmd.ExecuteReader())
        {
            if (!r.Read() || r.IsDBNull(3) || r.IsDBNull(4)) throw new UserError(Wrong, "recuperacion");
            u = new Usuario { Id = r.GetInt64(0), Email = r.GetString(1), Nombre = r.GetString(2), TieneRecuperacion = true };
            expected = Convert.FromBase64String(r.GetString(3));
            salt = Convert.FromBase64String(r.GetString(4));
        }
        if (!CryptographicOperations.FixedTimeEquals(Hash(given, salt, Iterations), expected))
            throw new UserError(Wrong, "recuperacion");

        newCode = NewCode();
        var psalt = RandomNumberGenerator.GetBytes(16);
        var rsalt = RandomNumberGenerator.GetBytes(16);
        using var up = Cmd(c, "UPDATE usuarios SET password_hash=$h, password_salt=$s, password_iter=$i, recovery_hash=$rh, recovery_salt=$rs WHERE id=$id",
            ("$h", Convert.ToBase64String(Hash(pass, psalt, Iterations))), ("$s", Convert.ToBase64String(psalt)), ("$i", Iterations),
            ("$rh", Convert.ToBase64String(Hash(NormalizeCode(newCode), rsalt, Iterations))), ("$rs", Convert.ToBase64String(rsalt)), ("$id", u.Id));
        up.ExecuteNonQuery();
        return u;
    }

    public static Usuario Login(string? email, string? password)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0 || string.IsNullOrEmpty(password))
            throw new UserError("Completá tu correo y tu contraseña.", "credenciales");
        using var c = Open();
        Usuario u;
        byte[] salt, expected;
        int iter;
        using (var cmd = Cmd(c, "SELECT id, email, nombre, password_hash, password_salt, password_iter, recovery_hash IS NOT NULL FROM usuarios WHERE email = $e", ("$e", email)))
        using (var r = cmd.ExecuteReader())
        {
            if (!r.Read()) throw new UserError("El correo o la contraseña no son correctos.", "credenciales");
            u = new Usuario { Id = r.GetInt64(0), Email = r.GetString(1), Nombre = r.GetString(2), TieneRecuperacion = r.GetInt64(6) != 0 };
            expected = Convert.FromBase64String(r.GetString(3));
            salt = Convert.FromBase64String(r.GetString(4));
            iter = r.GetInt32(5);
        }
        if (!CryptographicOperations.FixedTimeEquals(Hash(password, salt, iter), expected))
            throw new UserError("El correo o la contraseña no son correctos.", "credenciales");

        if (iter < Iterations)
        {
            // La cuenta es anterior: ya que conocemos la contraseña correcta, la guardamos con la protección actual.
            var newSalt = RandomNumberGenerator.GetBytes(16);
            using var up = Cmd(c, "UPDATE usuarios SET password_hash=$h, password_salt=$s, password_iter=$i WHERE id=$id",
                ("$h", Convert.ToBase64String(Hash(password, newSalt, Iterations))), ("$s", Convert.ToBase64String(newSalt)), ("$i", Iterations), ("$id", u.Id));
            up.ExecuteNonQuery();
        }
        return u;
    }

    static byte[] Hash(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    // ---------- Prendas ----------

    public static List<Prenda> ListPrendas(long userId)
    {
        var list = new List<Prenda>();
        using var c = Open();
        using var cmd = Cmd(c, "SELECT id, nombre, categoria, color_hex, color_nombre, temporada, notas, foto_path, creado FROM prendas WHERE usuario_id = $u ORDER BY id DESC", ("$u", userId));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Prenda
            {
                Id = r.GetInt64(0),
                UsuarioId = userId,
                Nombre = r.GetString(1),
                Categoria = Cats.Parse(r.GetString(2)),
                ColorHex = r.GetString(3),
                ColorNombre = r.GetString(4),
                Temporada = Cats.ParseSeason(r.GetString(5)),
                Notas = r.IsDBNull(6) ? "" : r.GetString(6),
                FotoPath = r.GetString(7),
                Creado = DateTime.TryParse(r.GetString(8), out var d) ? d : DateTime.Now,
            });
        }
        return list;
    }

    public static void SavePrenda(Prenda p)
    {
        using var c = Open();
        var args = new (string, object?)[]
        {
            ("$u", p.UsuarioId), ("$n", p.Nombre), ("$cat", Cats.Key(p.Categoria)), ("$hex", p.ColorHex),
            ("$cn", p.ColorNombre), ("$t", Cats.SeasonKey(p.Temporada)), ("$no", p.Notas), ("$f", p.FotoPath), ("$id", p.Id), ("$c", Now()),
        };
        if (p.Id == 0)
        {
            using var cmd = Cmd(c, "INSERT INTO prendas (usuario_id, nombre, categoria, color_hex, color_nombre, temporada, notas, foto_path, creado) VALUES ($u,$n,$cat,$hex,$cn,$t,$no,$f,$c); SELECT last_insert_rowid();", args);
            p.Id = Convert.ToInt64(cmd.ExecuteScalar());
            return;
        }

        using var tx = c.BeginTransaction();
        using (var cmd = Cmd(c, "UPDATE prendas SET nombre=$n, categoria=$cat, color_hex=$hex, color_nombre=$cn, temporada=$t, notas=$no, foto_path=$f WHERE id=$id AND usuario_id=$u", args))
        {
            cmd.Transaction = tx;
            if (cmd.ExecuteNonQuery() == 0) throw new UserError("No encontramos esa prenda. Puede que ya se haya eliminado.");
        }
        // Si cambió de categoría, en cada outfit pasa a la ranura nueva. Si esa ranura ya está ocupada,
        // la prenda sale de ese outfit; y un outfit que queda sin prendas se elimina.
        using (var move = Cmd(c, "UPDATE OR IGNORE outfit_prendas SET ranura=$cat WHERE prenda_id=$id AND ranura<>$cat", ("$cat", Cats.Key(p.Categoria)), ("$id", p.Id)))
        { move.Transaction = tx; move.ExecuteNonQuery(); }
        using (var drop = Cmd(c, "DELETE FROM outfit_prendas WHERE prenda_id=$id AND ranura<>$cat", ("$cat", Cats.Key(p.Categoria)), ("$id", p.Id)))
        { drop.Transaction = tx; drop.ExecuteNonQuery(); }
        DeleteEmptyOutfits(c, tx, p.UsuarioId);
        tx.Commit();
    }

    static void DeleteEmptyOutfits(SqliteConnection c, SqliteTransaction tx, long userId)
    {
        using var clean = Cmd(c, "DELETE FROM outfits WHERE usuario_id = $u AND id NOT IN (SELECT outfit_id FROM outfit_prendas)", ("$u", userId));
        clean.Transaction = tx;
        clean.ExecuteNonQuery();
    }

    public static int OutfitsUsing(long userId, long prendaId)
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT COUNT(DISTINCT op.outfit_id) FROM outfit_prendas op JOIN outfits o ON o.id = op.outfit_id WHERE op.prenda_id = $p AND o.usuario_id = $u", ("$p", prendaId), ("$u", userId));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void DeletePrenda(long userId, Prenda p)
    {
        using (var c = Open())
        using (var tx = c.BeginTransaction())
        {
            using (var cmd = Cmd(c, "DELETE FROM prendas WHERE id = $id AND usuario_id = $u", ("$id", p.Id), ("$u", userId)))
            { cmd.Transaction = tx; cmd.ExecuteNonQuery(); }
            // Los outfits que quedaron sin prendas se eliminan también.
            DeleteEmptyOutfits(c, tx, userId);
            tx.Commit();
        }
        Photos.Delete(p.FotoPath);
    }

    // ---------- Outfits ----------

    public static List<Outfit> ListOutfits(long userId, IReadOnlyDictionary<long, Prenda> prendas)
    {
        var list = new List<Outfit>();
        var byId = new Dictionary<long, Outfit>();
        using var c = Open();
        using (var cmd = Cmd(c, "SELECT id, nombre, ocasion, favorito, creado FROM outfits WHERE usuario_id = $u ORDER BY favorito DESC, id DESC", ("$u", userId)))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var o = new Outfit
                {
                    Id = r.GetInt64(0), Nombre = r.GetString(1), Ocasion = r.GetString(2), Favorito = r.GetInt64(3) != 0,
                    Creado = DateTime.TryParse(r.GetString(4), out var d) ? d : DateTime.Now,
                };
                list.Add(o); byId[o.Id] = o;
            }
        }
        using (var cmd = Cmd(c, "SELECT op.outfit_id, op.prenda_id, op.ranura FROM outfit_prendas op JOIN outfits o ON o.id = op.outfit_id WHERE o.usuario_id = $u", ("$u", userId)))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (byId.TryGetValue(r.GetInt64(0), out var o) && prendas.TryGetValue(r.GetInt64(1), out var p))
                    o.Prendas[Cats.Parse(r.GetString(2))] = p;
            }
        }
        return list;
    }

    public static long AddOutfit(long userId, string nombre, string ocasion, bool favorito, IReadOnlyDictionary<Category, Prenda> prendas)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        long id;
        using (var cmd = Cmd(c, "INSERT INTO outfits (usuario_id, nombre, ocasion, favorito, creado) VALUES ($u,$n,$o,$f,$c); SELECT last_insert_rowid();",
            ("$u", userId), ("$n", nombre), ("$o", ocasion), ("$f", favorito ? 1 : 0), ("$c", Now())))
        {
            cmd.Transaction = tx;
            id = Convert.ToInt64(cmd.ExecuteScalar());
        }
        foreach (var kv in prendas)
        {
            using var cmd = Cmd(c, "INSERT INTO outfit_prendas (outfit_id, prenda_id, ranura) SELECT $o, id, $r FROM prendas WHERE id = $p AND usuario_id = $u",
                ("$o", id), ("$p", kv.Value.Id), ("$r", Cats.Key(kv.Key)), ("$u", userId));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    public static void SetFavorito(long userId, long outfitId, bool fav)
    {
        using var c = Open();
        using var cmd = Cmd(c, "UPDATE outfits SET favorito = $f WHERE id = $id AND usuario_id = $u", ("$f", fav ? 1 : 0), ("$id", outfitId), ("$u", userId));
        cmd.ExecuteNonQuery();
    }

    public static void DeleteOutfit(long userId, long outfitId)
    {
        using var c = Open();
        using var cmd = Cmd(c, "DELETE FROM outfits WHERE id = $id AND usuario_id = $u", ("$id", outfitId), ("$u", userId));
        cmd.ExecuteNonQuery();
    }
}
