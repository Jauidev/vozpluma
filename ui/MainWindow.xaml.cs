using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VoiceAgent;

public partial class MainWindow
{
    private Process? _engine;
    private Storyboard _pulso = null!;
    private bool _listo;
    private WidgetWindow? _widget;

    /// (evento, texto|mensaje) — el widget se suscribe para reaccionar al motor
    public event Action<string, string?>? MotorEvento;

    public MainWindow()
    {
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        foreach (var l in new[] { "es-ES", "en-US", "fr-FR", "de-DE", "it-IT", "pt-BR", "ar-SA" })
            IdiomaCombo.Items.Add(l);
        IdiomaCombo.SelectedIndex = 0;

        _pulso = (Storyboard)FindResource("Pulso");

        Loaded += (_, _) => IniciarMotor();
        Closed += (_, _) =>
        {
            _widget?.Close();
            PararMotor();
        };
    }

    // ---------- Motor Python ----------

    internal static string RaizProyecto()
    {
        // ProcessPath y no BaseDirectory: en publicación de archivo único,
        // BaseDirectory apunta a una carpeta temporal de extracción
        var origen = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var dir = new DirectoryInfo(origen);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "engine.py")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new FileNotFoundException("No se encontró engine.py subiendo desde " + origen);
    }

    private void IniciarMotor()
    {
        _listo = false;
        BotonMic.IsEnabled = false;
        BotonWidget.IsEnabled = false;
        Estado_("Cargando modelo… (puede tardar un poco la primera vez)");

        var raiz = RaizProyecto();
        var python = Path.Combine(raiz, ".venv", "Scripts", "python.exe");
        var usarWhisper = ModeloCombo.SelectedIndex == 0 ? " whisper" : "";
        var idioma = (string)IdiomaCombo.SelectedItem;
        var aj = Ajustes.Actual;
        var silencio = aj.SilencioFin.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"-u engine.py {idioma}{usarWhisper} --mic={aj.MicIndex}" +
                        $" --silencio={silencio} --espera={aj.EsperaVoz} --maxseg={aj.MaxSeg}",
            WorkingDirectory = raiz,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        try
        {
            _engine = Process.Start(psi);
        }
        catch (Exception e)
        {
            Estado_("Error al iniciar el motor: " + e.Message, esError: true);
            return;
        }

        _engine!.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Dispatcher.Invoke(() => ProcesarEvento(e.Data));
        };
        _engine.BeginOutputReadLine();
        _engine.BeginErrorReadLine();
    }

    private void PararMotor()
    {
        try
        {
            if (_engine is { HasExited: false })
            {
                _engine.StandardInput.WriteLine("quit");
                if (!_engine.WaitForExit(2000))
                    _engine.Kill(entireProcessTree: true);
            }
        }
        catch { /* cerrando la app; el proceso muere igualmente */ }
        _engine = null;
    }

    private void ProcesarEvento(string linea)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(linea); }
        catch { return; }

        using (doc)
        {
            var ev = doc.RootElement.GetProperty("event").GetString();

            var dato = ev switch
            {
                "text" => doc.RootElement.GetProperty("text").GetString(),
                "error" => doc.RootElement.GetProperty("message").GetString(),
                _ => null,
            };
            MotorEvento?.Invoke(ev ?? "", dato);

            switch (ev)
            {
                case "ready":
                    _listo = true;
                    BotonMic.IsEnabled = true;
                    BotonWidget.IsEnabled = true;
                    Estado_($"Listo · {doc.RootElement.GetProperty("model").GetString()}" +
                            $" · {doc.RootElement.GetProperty("mic").GetString()}");
                    break;
                case "listening":
                    Grabando(true);
                    Estado_("Escuchando… habla y se corta solo al callarte");
                    break;
                case "transcribing":
                    Grabando(false);
                    Estado_("Transcribiendo…");
                    break;
                case "text":
                    Grabando(false);
                    var texto = doc.RootElement.GetProperty("text").GetString() ?? "";
                    if (texto.Length == 0)
                        Estado_("No se detectó voz — pulsa el micrófono e inténtalo de nuevo");
                    else
                    {
                        AgregarTarjeta(texto);
                        Estado_("Listo — toca una tarjeta para copiar su texto");
                    }
                    BotonMic.IsEnabled = true;
                    break;
                case "error":
                    Grabando(false);
                    Estado_("Error: " + doc.RootElement.GetProperty("message").GetString(),
                            esError: true);
                    BotonMic.IsEnabled = _listo;
                    break;
            }
        }
    }

    // ---------- Estado visual "grabando" ----------

    private void Grabando(bool activo)
    {
        if (activo)
            _pulso.Begin();
        else
        {
            _pulso.Stop();
            Anillo.Opacity = 0;
        }
    }

    // ---------- Acciones ----------

    private void BotonMic_Click(object sender, RoutedEventArgs e)
    {
        BotonMic.IsEnabled = false;
        EnviarOrden("rec");
    }

    public void EnviarOrden(string orden)
    {
        if (_engine is { HasExited: false })
            _engine.StandardInput.WriteLine(orden);
    }

    private void BotonAjustes_Click(object sender, RoutedEventArgs e)
    {
        var ventana = new SettingsWindow { Owner = this };
        if (ventana.ShowDialog() == true)
        {
            PararMotor();
            IniciarMotor(); // aplica micrófono y parámetros de rendimiento nuevos
        }
    }

    private void BotonWidget_Click(object sender, RoutedEventArgs e)
    {
        _widget = new WidgetWindow(this);
        _widget.Closed += (_, _) => _widget = null;
        _widget.Show();
        Hide();
    }

    private void Selector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        PararMotor();
        IniciarMotor();
    }

    private void AgregarTarjeta(string texto)
    {
        var tarjeta = new Border
        {
            Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = DateTime.Now.ToString("HH:mm"),
                        FontSize = 11,
                        Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                    new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                },
            },
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        tarjeta.MouseLeftButtonUp += (_, _) =>
        {
            Clipboard.SetText(texto);
            Estado_("Copiado al portapapeles ✓");
        };
        Placeholder.Visibility = Visibility.Collapsed;
        Lista.Children.Add(tarjeta);
        Scroll.ScrollToBottom();
    }

    private void Estado_(string texto, bool esError = false)
    {
        Estado.Text = texto;
        Estado.SetResourceReference(TextBlock.ForegroundProperty,
            esError ? "PaletteRedBrush" : "TextFillColorSecondaryBrush");
    }
}
