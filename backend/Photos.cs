using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;
using Color = System.Drawing.Color;

namespace FitLab;

/// <summary>Guarda las fotos de las prendas como archivos (no en la base de datos) y las cachea en memoria.</summary>
public static class Photos
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxSide = 1000;
    /// <summary>Tope de miniaturas en memoria (cada una ocupa hasta ~0,7 MB).</summary>
    const int MaxCached = 300;
    /// <summary>Extensiones que se intentan abrir. WebP, HEIC y AVIF los decodifica Windows (no GDI+).</summary>
    public static readonly string[] Allowed = { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".webp", ".heic", ".heif", ".avif", ".tif", ".tiff" };
    const string Formats = "JPG, PNG o WebP";
    static readonly Dictionary<string, Image> cache = new();

    public static string FullPath(string rel) => Path.Combine(Db.PhotosDir, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Valida una imagen elegida por la persona y la devuelve cargada en memoria.</summary>
    public static Image LoadForImport(string source)
    {
        if (!File.Exists(source)) throw new UserError("No encontramos ese archivo. Probá elegirlo de nuevo.");
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (!Allowed.Contains(ext)) throw new UserError($"Ese formato no es compatible. Elegí una foto {Formats}.");
        var len = new FileInfo(source).Length;
        if (len > MaxBytes)
            throw new UserError($"La imagen pesa {len / 1024.0 / 1024.0:0.0} MB y el máximo es 5 MB. Elegí una foto más liviana.");

        byte[] bytes;
        try { bytes = File.ReadAllBytes(source); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new UserError("No pudimos abrir ese archivo. Revisá que no esté abierto en otro programa y probá de nuevo."); }

        // La extensión no garantiza el formato real (muchas fotos descargadas de tiendas son WebP con
        // nombre .jpg), así que se prueba primero con GDI+ y, si no la entiende, con el lector de Windows.
        var img = DecodeWithGdi(bytes) ?? DecodeWithWindows(bytes);
        if (img != null) return img;
        if (ext is ".heic" or ".heif")
            throw new UserError("Windows no pudo abrir esta foto HEIC (las del iPhone). Instalá «Extensiones de imagen HEIF» desde Microsoft Store, o pasá la foto a JPG y elegila de nuevo.");
        throw new UserError($"No pudimos leer esa imagen. Probá con otra foto en formato {Formats}.");
    }

    /// <summary>Decodifica con GDI+ (JPG, PNG, BMP, GIF, TIFF). Devuelve la foto ya girada y achicada, o null si no la entiende.</summary>
    static Bitmap? DecodeWithGdi(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var raw = Image.FromStream(ms);
            ApplyOrientation(raw);
            return Resize(raw, MaxSide);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            return null; // GDI+ responde "memoria insuficiente" cuando no conoce el formato
        }
    }

    /// <summary>
    /// Decodifica con el lector de imágenes de Windows (WIC), que también abre WebP y, si están instaladas
    /// sus extensiones, HEIC y AVIF. Devuelve la foto girada, achicada y sobre fondo blanco, o null si no puede.
    /// </summary>
    static Bitmap? DecodeWithWindows(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            // PreservePixelFormat: sin esto el decodificador de WebP descarta la transparencia y el fondo queda negro.
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            int orientation = ReadOrientation(frame);

            double k = Math.Min(1.0, (double)MaxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (k < 1) frame = new TransformedBitmap(frame, new ScaleTransform(k, k));
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

            using var bmp = new Bitmap(bgra.PixelWidth, bgra.PixelHeight, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { bgra.CopyPixels(Int32Rect.Empty, data.Scan0, data.Stride * bmp.Height, data.Stride); }
            finally { bmp.UnlockBits(data); }

            var flip = FlipFor(orientation);
            if (flip != RotateFlipType.RotateNoneFlipNone) bmp.RotateFlip(flip);
            return Resize(bmp, MaxSide); // también reemplaza la transparencia por blanco
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException
                                       or InvalidOperationException or COMException or OverflowException)
        {
            return null;
        }
    }

    static int ReadOrientation(BitmapSource frame)
    {
        try { return (frame.Metadata as BitmapMetadata)?.GetQuery("System.Photo.Orientation") is ushort o ? o : 1; }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or COMException) { return 1; }
    }

    /// <summary>Guarda la imagen ya validada en la carpeta de fotos del usuario y devuelve su ruta relativa.</summary>
    public static string Save(Image img, long userId)
    {
        var dir = Path.Combine(Db.PhotosDir, "u" + userId);
        Directory.CreateDirectory(dir);
        var name = Guid.NewGuid().ToString("N") + ".jpg";
        SaveJpeg(img, Path.Combine(dir, name), 88);
        return "u" + userId + "/" + name;
    }

    /// <summary>
    /// Devuelve la miniatura de una foto (o null si no existe). La imagen pertenece a la caché:
    /// quien la recibe la usa para dibujar en el momento y no la guarda ni la libera.
    /// </summary>
    public static Image? Get(string? rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (cache.TryGetValue(rel, out var img)) return img;
        try
        {
            var path = FullPath(rel);
            if (!File.Exists(path)) return null;
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var raw = Image.FromStream(ms);
            img = Resize(raw, 480);
            if (cache.Count >= MaxCached) Clear();
            cache[rel] = img;
            return img;
        }
        catch { return null; }
    }

    /// <summary>Libera todas las miniaturas en memoria (al cerrar sesión o al llegar al límite). No retorna nada.</summary>
    public static void Clear()
    {
        foreach (var img in cache.Values) img.Dispose();
        cache.Clear();
        foreach (var img in cutouts.Values) img.Dispose();
        cutouts.Clear();
    }

    // ---------- Recorte del fondo (para "vestir" el maniquí del probador) ----------

    static readonly Dictionary<string, Bitmap> cutouts = new();

    /// <summary>
    /// Devuelve la prenda de la foto sin el fondo (transparente) y recortada a su contorno, o null si no hay foto.
    /// Igual que Get: la imagen pertenece a la caché, se usa para dibujar y no se libera.
    /// </summary>
    public static Bitmap? GetCutout(string? rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (cutouts.TryGetValue(rel, out var cut)) return cut;
        var src = Get(rel);
        if (src == null) return null;
        cut = MakeCutout(src);
        if (cutouts.Count >= MaxCached) { foreach (var c in cutouts.Values) c.Dispose(); cutouts.Clear(); }
        cutouts[rel] = cut;
        return cut;
    }

    /// <summary>
    /// Quita el fondo de una foto de producto: parte de los bordes y va "pintando" como fondo los píxeles
    /// parecidos al color del borde (acepta fondos con degradé suave). Suaviza el contorno y recorta lo que sobra.
    /// Si el fondo no es parejo (por ejemplo, una foto en la calle) o la prenda es del mismo color que el fondo,
    /// devuelve la foto entera, sin recortar, para no romperla. Entrada: la foto; salida: una imagen nueva ARGB.
    /// </summary>
    public static Bitmap MakeCutout(Image src)
    {
        using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
        int w = bmp.Width, h = bmp.Height, n = w * h;
        var px = new int[n];
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(data.Scan0, px, 0, n);
        bmp.UnlockBits(data);

        // Color promedio del borde y cuánto varía.
        double sr = 0, sg = 0, sb = 0; int count = 0;
        void Acc(int i) { int c = px[i]; sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255; count++; }
        for (int x = 0; x < w; x++) { Acc(x); Acc((h - 1) * w + x); }
        for (int y = 1; y < h - 1; y++) { Acc(y * w); Acc(y * w + w - 1); }
        double ar = sr / count, ag = sg / count, ab = sb / count, spread = 0;
        static double Dist(int c, double r, double g, double b)
        {
            double dr = ((c >> 16) & 255) - r, dg = ((c >> 8) & 255) - g, db = (c & 255) - b;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }
        for (int x = 0; x < w; x++) spread += Dist(px[x], ar, ag, ab) + Dist(px[(h - 1) * w + x], ar, ag, ab);
        for (int y = 1; y < h - 1; y++) spread += Dist(px[y * w], ar, ag, ab) + Dist(px[y * w + w - 1], ar, ag, ab);
        spread /= count;
        if (spread > 28) return new Bitmap(bmp); // fondo con objetos o texturas: no se recorta

        double globalTol = 46 + spread * 2;   // qué tan distinto al fondo puede ser un píxel de fondo
        const double StepTol = 16;            // salto máximo entre vecinos (sigue degradés, frena en el borde de la prenda)
        var bg = new bool[n];
        var queue = new Queue<int>();
        void Seed(int i) { if (!bg[i] && Dist(px[i], ar, ag, ab) < globalTol) { bg[i] = true; queue.Enqueue(i); } }
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 1; y < h - 1; y++) { Seed(y * w); Seed(y * w + w - 1); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w, c = px[i];
            double cr = (c >> 16) & 255, cg = (c >> 8) & 255, cb = c & 255;
            void Try(int j) { if (!bg[j] && Dist(px[j], ar, ag, ab) < globalTol && Dist(px[j], cr, cg, cb) < StepTol) { bg[j] = true; queue.Enqueue(j); } }
            if (x > 0) Try(i - 1);
            if (x < w - 1) Try(i + 1);
            if (y > 0) Try(i - w);
            if (y < h - 1) Try(i + w);
        }

        int bgCount = bg.Count(b => b);
        if (bgCount < n * 0.02 || bgCount > n * 0.97) return new Bitmap(bmp); // no se encontró una prenda clara

        // Transparencia con borde suave (promedio 3x3) y caja que encierra la prenda.
        int minX = w, minY = h, maxX = -1, maxY = -1;
        var outPx = new int[n];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int fg = 0, tot = 0;
                for (int yy = Math.Max(0, y - 1); yy <= Math.Min(h - 1, y + 1); yy++)
                    for (int xx = Math.Max(0, x - 1); xx <= Math.Min(w - 1, x + 1); xx++) { tot++; if (!bg[yy * w + xx]) fg++; }
                int a = fg * 255 / tot, i = y * w + x;
                outPx[i] = (a << 24) | (px[i] & 0xFFFFFF);
                if (a > 0) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            }

        using var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var od = full.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(outPx, 0, od.Scan0, n);
        full.UnlockBits(od);
        return full.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), PixelFormat.Format32bppArgb);
    }

    public static void Delete(string? rel)
    {
        if (string.IsNullOrEmpty(rel)) return;
        if (cache.Remove(rel, out var img)) img.Dispose();
        if (cutouts.Remove(rel, out var cut)) cut.Dispose();
        try { var p = FullPath(rel); if (File.Exists(p)) File.Delete(p); } catch { }
    }

    static void ApplyOrientation(Image img)
    {
        const int OrientationId = 0x0112;
        if (!img.PropertyIdList.Contains(OrientationId)) return;
        var prop = img.GetPropertyItem(OrientationId);
        if (prop?.Value == null || prop.Value.Length == 0) return;
        var flip = FlipFor(prop.Value[0]);
        if (flip != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(flip);
    }

    /// <summary>Giro que corresponde a la orientación EXIF (1 = derecha, 6 = foto vertical del celular, etc.).</summary>
    static RotateFlipType FlipFor(int orientation) => orientation switch
    {
        3 => RotateFlipType.Rotate180FlipNone,
        6 => RotateFlipType.Rotate90FlipNone,
        8 => RotateFlipType.Rotate270FlipNone,
        2 => RotateFlipType.RotateNoneFlipX,
        4 => RotateFlipType.Rotate180FlipX,
        5 => RotateFlipType.Rotate90FlipX,
        7 => RotateFlipType.Rotate270FlipX,
        _ => RotateFlipType.RotateNoneFlipNone,
    };

    public static Bitmap Resize(Image src, int maxSide)
    {
        double k = Math.Min(1.0, (double)maxSide / Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, w, h);
        return bmp;
    }

    static void SaveJpeg(Image img, string path, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        img.Save(path, codec, ep);
    }
}
