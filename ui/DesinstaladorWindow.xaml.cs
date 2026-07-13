using System.Diagnostics;
using System.IO;
using System.Windows;
using Application = System.Windows.Application;

namespace VoiceAgent;

public partial class DesinstaladorWindow
{
    private string _raiz = "";

    public DesinstaladorWindow()
    {
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        try { _raiz = MainWindow.RaizProyecto(); }
        catch { _raiz = Path.GetDirectoryName(Environment.ProcessPath) ?? "."; }
    }

    private async void Desinstalar_Click(object sender, RoutedEventArgs e)
    {
        BotonDesinstalar.IsEnabled = false;
        BotonCancelar.IsEnabled = false;
        Estado.Visibility = Visibility.Visible;

        // espera a que la app principal se cierre (suelta el mutex de instancia única)
        Estado.Text = "Esperando a que VozPluma se cierre…";
        var libre = await Task.Run(() =>
        {
            for (int i = 0; i < 60; i++)
            {
                var m = new Mutex(true, "VozPlumaApp", out bool creado);
                if (creado) return m; // lo retenemos: nadie puede arrancar mientras
                m.Dispose();
                Thread.Sleep(500);
            }
            return null;
        });
        if (libre is null)
        {
            Estado.Text = "VozPluma sigue abierto. Cierra su ventana y vuelve a intentarlo.";
            BotonDesinstalar.IsEnabled = true;
            BotonCancelar.IsEnabled = true;
            return;
        }

        var cacheHub = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "huggingface", "hub");
        // por patrón y no con una lista fija: cubre los cinco tamaños de Whisper
        // (Systran/mobiuslabs), sus variantes ONNX de DirectML y Nemotron
        var objetivos = new List<(string ruta, string nombre)>();
        if (Directory.Exists(cacheHub))
            foreach (var d in Directory.GetDirectories(cacheHub))
            {
                var n = Path.GetFileName(d).ToLowerInvariant();
                if (n.Contains("whisper") || n.Contains("nemotron"))
                    objetivos.Add((d, "modelo " + Path.GetFileName(d)
                        .Replace("models--", "").Replace("--", "/")));
            }
        objetivos.Add((Path.Combine(_raiz, ".venv"), "entorno de Python"));
        objetivos.Add((Path.Combine(_raiz, "__pycache__"), "archivos temporales"));

        foreach (var (ruta, nombre) in objetivos)
        {
            if (!Directory.Exists(ruta)) continue;
            Estado.Text = $"Eliminando {nombre}…";
            await Task.Run(() =>
            {
                try { Directory.Delete(ruta, recursive: true); }
                catch
                {
                    Thread.Sleep(2000); // algún archivo aún bloqueado: reintenta una vez
                    try { Directory.Delete(ruta, recursive: true); } catch { }
                }
            });
        }
        var ajustes = Path.Combine(_raiz, "ajustes.json");
        if (File.Exists(ajustes))
            try { File.Delete(ajustes); } catch { }

        Estado.Text = "Listo. Para terminar, borra la carpeta de VozPluma a mano.";
        BotonTerminar.Visibility = Visibility.Visible;
    }

    private void Terminar_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", $"/select,\"{_raiz}\"");
        Application.Current.Shutdown();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();
}
