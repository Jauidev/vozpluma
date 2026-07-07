using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;

namespace VoiceAgent;

public partial class MainWindow
{
    private Process? _engine;
    private Storyboard _pulso = null!;
    private bool _listo;
    private bool _grabando; // el botón de micro está en modo "parar"
    private WidgetWindow? _widget;
    private System.Windows.Forms.NotifyIcon? _bandeja;
    private bool _salir;
    private bool _avisoBandeja;

    /// (evento, texto|mensaje) — el widget se suscribe para reaccionar al motor
    public event Action<string, string?>? MotorEvento;

    public MainWindow(EventWaitHandle? mostrar = null)
    {
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        CrearIconoBandeja();

        // otra instancia del exe nos pide mostrarnos (ver Program.Main)
        if (mostrar is not null)
        {
            var hilo = new Thread(() =>
            {
                while (mostrar.WaitOne())
                    Dispatcher.Invoke(Restaurar);
            }) { IsBackground = true };
            hilo.Start();
        }

        var idiomas = new[] { "es-ES", "en-US", "fr-FR", "de-DE", "it-IT", "pt-BR", "ar-SA" };
        foreach (var l in idiomas)
            IdiomaCombo.Items.Add(l);

        // idioma guardado; si no hay, el del sistema; si no está soportado, inglés
        var sistema = System.Globalization.CultureInfo.CurrentUICulture.Name;
        IdiomaCombo.SelectedItem =
            idiomas.FirstOrDefault(l => l == Ajustes.Actual.Idioma)
            ?? idiomas.FirstOrDefault(l => l == sistema)
            ?? idiomas.FirstOrDefault(l => l.StartsWith(sistema.Split('-')[0]))
            ?? "en-US";
        ModeloCombo.SelectedIndex = Ajustes.Actual.UsarWhisper ? 0 : 1;

        _pulso = (Storyboard)FindResource("Pulso");

        Loaded += (_, _) => IniciarMotor();
        Closed += (_, _) =>
        {
            _bandeja?.Dispose();
            _widget?.Close();
            PararMotor();
        };
    }

    // ---------- Bandeja del sistema ----------

    private void CrearIconoBandeja()
    {
        _bandeja = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "VozPluma",
            Visible = true,
        };
        _bandeja.DoubleClick += (_, _) => Restaurar();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => Restaurar());
        menu.Items.Add("Salir", null, (_, _) => SalirDeVerdad());
        _bandeja.ContextMenuStrip = menu;
    }

    /// Cierra la app de verdad (sin quedarse en la bandeja).
    public void SalirDeVerdad()
    {
        _salir = true;
        Close();
    }

    private void Restaurar()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // cerrar la ventana la esconde a la bandeja con el modelo aún cargado;
    // "Salir" del menú de la bandeja cierra de verdad
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_salir)
        {
            e.Cancel = true;
            Hide();
            if (!_avisoBandeja)
            {
                _avisoBandeja = true;
                _bandeja?.ShowBalloonTip(4000, "VozPluma sigue activo",
                    "Queda en la bandeja con el modelo cargado: al reabrirlo no hay espera. " +
                    "Clic derecho en el icono → Salir para cerrarlo del todo.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            return;
        }
        base.OnClosing(e);
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
        BotonMic.Visibility = Visibility.Collapsed;
        Carga.Visibility = Visibility.Visible;
        BotonWidget.IsEnabled = false;
        Estado_("Cargando el modelo en memoria… (solo se descarga la primera vez)");

        var raiz = RaizProyecto();
        var python = Path.Combine(raiz, ".venv", "Scripts", "python.exe");
        var usarWhisper = ModeloCombo.SelectedIndex == 0 ? " whisper" : "";
        var idioma = (string)IdiomaCombo.SelectedItem;
        var aj = Ajustes.Actual;
        var cpu = aj.ForzarCpu ? " --cpu" : "";

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"-u engine.py {idioma}{usarWhisper} --mic={aj.MicIndex}" +
                        $" --maxseg={aj.MaxSeg}{cpu}",
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
        var viejo = _engine;
        _engine = null;
        Parar(viejo);
    }

    private static void Parar(Process? motor)
    {
        try
        {
            if (motor is { HasExited: false })
            {
                motor.StandardInput.WriteLine("quit");
                if (!motor.WaitForExit(2000))
                    motor.Kill(entireProcessTree: true);
            }
        }
        catch { /* cerrando; el proceso muere igualmente */ }
    }

    private bool _reiniciando;

    // parar y arrancar sin congelar la interfaz: la espera al motor viejo
    // (hasta 2 s, debe soltar el micrófono) ocurre en segundo plano
    private async void ReiniciarMotor()
    {
        if (_reiniciando) return;
        _reiniciando = true;
        _listo = false;
        BotonMic.IsEnabled = false;
        BotonMic.Visibility = Visibility.Collapsed;
        Carga.Visibility = Visibility.Visible;
        BotonWidget.IsEnabled = false;
        Estado_("Cambiando…");

        var viejo = _engine;
        _engine = null;
        await Task.Run(() => Parar(viejo));

        IniciarMotor();
        _reiniciando = false;
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
                    Carga.Visibility = Visibility.Collapsed;
                    BotonMic.Visibility = Visibility.Visible;
                    Grabar(false);
                    BotonMic.IsEnabled = true;
                    BotonWidget.IsEnabled = true;
                    Estado_($"Listo · {doc.RootElement.GetProperty("model").GetString()}" +
                            $" · {doc.RootElement.GetProperty("mic").GetString()}");
                    break;
                case "listening":
                    Grabar(true); // el micrófono pasa a ser botón de parar
                    BotonMic.IsEnabled = true;
                    Estado_("Grabando… pulsa el botón para terminar");
                    break;
                case "transcribing":
                    Grabar(false);
                    BotonMic.IsEnabled = false;
                    Estado_("Transcribiendo…");
                    break;
                case "text":
                    Grabar(false);
                    var texto = doc.RootElement.GetProperty("text").GetString() ?? "";
                    if (texto.Length == 0)
                        Estado_("No se grabó voz — pulsa el micrófono e inténtalo de nuevo");
                    else
                    {
                        AgregarTarjeta(texto);
                        Estado_("Listo — toca una tarjeta para copiar su texto");
                    }
                    BotonMic.IsEnabled = true;
                    break;
                case "error":
                    Grabar(false);
                    Carga.Visibility = Visibility.Collapsed;
                    BotonMic.Visibility = Visibility.Visible;
                    Estado_("Error: " + doc.RootElement.GetProperty("message").GetString(),
                            esError: true);
                    BotonMic.IsEnabled = _listo;
                    break;
            }
        }
    }

    // ---------- Estado visual "grabando" ----------

    // el mismo botón alterna entre grabar (micro) y parar (stop)
    private void Grabar(bool activo)
    {
        _grabando = activo;
        IconoMic.Symbol = activo
            ? Wpf.Ui.Controls.SymbolRegular.Stop24
            : Wpf.Ui.Controls.SymbolRegular.Mic24;
        BotonMic.ToolTip = activo
            ? "Pulsa para terminar y transcribir"
            : "Pulsa y habla — cuando termines, pulsa otra vez para parar";
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
        // desactiva hasta que el motor confirme el cambio de estado (evita doble clic)
        BotonMic.IsEnabled = false;
        EnviarOrden(_grabando ? "stop" : "rec");
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
            ReiniciarMotor(); // aplica micrófono y parámetros de rendimiento nuevos
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
        // recuerda la elección para la próxima sesión
        var a = Ajustes.Actual;
        a.Idioma = (string)IdiomaCombo.SelectedItem;
        a.UsarWhisper = ModeloCombo.SelectedIndex == 0;
        a.Guardar();
        ReiniciarMotor();
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
