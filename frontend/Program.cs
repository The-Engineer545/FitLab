namespace FitLab;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string? shot = null;
#if DEBUG
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--screenshot" && i + 1 < args.Length) shot = Path.GetFullPath(args[i + 1]);
#endif

        ApplicationConfiguration.Initialize();
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = Math.Max(1f, g.DpiX / 96f);

        try { Db.Init(); }
        catch (Exception ex)
        {
            MessageBox.Show("No pudimos abrir la base de datos de FitLab.\n\n" + ex.Message, "FitLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.ThreadException += (_, e) =>
        {
            var msg = e.Exception is UserError ue ? ue.Message : "Ocurrió un error inesperado. Probá de nuevo.\n\nDetalle: " + e.Exception.Message;
            MessageBox.Show(msg, "Ups, algo no salió bien", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        Application.Run(new MainForm(shot));
    }
}
