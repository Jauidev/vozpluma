using Application = System.Windows.Application;

namespace VoiceAgent;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // el desinstalador vive en este mismo exe (mismo icono, sin programa extra)
        if (Environment.GetCommandLineArgs().Contains("--desinstalar"))
        {
            var desinstalador = new Application();
            desinstalador.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
            desinstalador.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            desinstalador.Run(new DesinstaladorWindow());
            return;
        }

        // instancia única: si ya hay un VozPluma corriendo (aunque esté oculto
        // en modo widget), le pedimos que muestre su ventana y salimos — así
        // nunca se carga el modelo dos veces
        using var unico = new Mutex(true, "VozPlumaApp", out bool somosPrimeros);
        var mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, "VozPlumaMostrar");
        if (!somosPrimeros)
        {
            mostrar.Set();
            return;
        }

        var app = new Application();
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Run(new MainWindow(mostrar));
    }
}
