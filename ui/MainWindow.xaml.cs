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

    /// (evento, texto|mensaje) — el widget se suscribe para reaccionar al motor
    public event Action<string, string?>? MotorEvento;

    public MainWindow(EventWaitHandle? mostrar = null)
    {
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

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
        // el combo mezcla los tamaños de Whisper y Nemotron; se elige por Tag
        var tagGuardado = Ajustes.Actual.UsarWhisper
            ? Ajustes.Actual.ModeloWhisper : "nemotron";
        ModeloCombo.SelectedItem =
            ModeloCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Tag == tagGuardado)
            ?? ModeloCombo.Items[0];

        _pulso = (Storyboard)FindResource("Pulso");

        // atajo global grabar/parar (funciona con la ventana oculta o el widget)
        SourceInitialized += (_, _) => RegistrarAtajo();

        // reposo: el modelo ocupa GB; tras N min sin dictar se para el motor
        _reposo = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMinutes(1) };
        _reposo.Tick += (_, _) => ComprobarReposo();
        _reposo.Start();

        // primer arranque: si falta el entorno de Python, se instala solo
        Loaded += (_, _) =>
        {
            if (File.Exists(Path.Combine(RaizProyecto(), ".venv", "Scripts", "python.exe")))
                IniciarMotor();
            else
                _ = InstalarAsync();
        };
        // cerrar la ventana cierra la app de verdad: motor Python incluido,
        // nada queda consumiendo memoria en segundo plano
        Closed += (_, _) =>
        {
            _widget?.Close();
            PararMotor();
        };
    }

    /// Cierra la app (alias histórico; el cierre normal ya termina todo).
    public void SalirDeVerdad() => Close();

    private void Restaurar()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
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
        var tagModelo = (string)((ComboBoxItem)ModeloCombo.SelectedItem).Tag;
        var usarWhisper = tagModelo != "nemotron"
            ? $" whisper --wmodel={tagModelo}" : "";
        var idioma = (string)IdiomaCombo.SelectedItem;
        var aj = Ajustes.Actual;

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"-u engine.py {idioma}{usarWhisper} --mic={aj.MicIndex}" +
                        $" --maxseg={aj.MaxSeg} --accel={aj.Acelerador}",
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

            _ultimaActividad = DateTime.Now; // cualquier evento del motor cuenta como uso

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
                    // arrancar directo en modo widget (solo la primera vez, no
                    // al despertar del reposo ni al cambiar de modelo)
                    if (Ajustes.Actual.AbrirEnWidget && !_autoWidgetHecho && _widget is null)
                    {
                        _autoWidgetHecho = true;
                        BotonWidget_Click(this, new RoutedEventArgs());
                    }
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
        _ultimaActividad = DateTime.Now;
        if (_engine is { HasExited: false })
            _engine.StandardInput.WriteLine(orden);
        else if (orden == "rec" && !_reiniciando && !_instalando)
            ReiniciarMotor(); // motor en reposo: se reactiva (dicta cuando cargue)
    }

    private void BotonAjustes_Click(object sender, RoutedEventArgs e)
    {
        var ventana = new SettingsWindow { Owner = this };
        if (ventana.ShowDialog() == true)
        {
            RegistrarAtajo(); // por si cambió el atajo global
            ReiniciarMotor(); // aplica micrófono y parámetros de rendimiento nuevos
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
        // recuerda la elección para la próxima sesión
        var a = Ajustes.Actual;
        a.Idioma = (string)IdiomaCombo.SelectedItem;
        var tag = (string)((ComboBoxItem)ModeloCombo.SelectedItem).Tag;
        a.UsarWhisper = tag != "nemotron";
        if (a.UsarWhisper)
            a.ModeloWhisper = tag;
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
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        pila.Children.Add(new TextBlock
        {
            Text = texto,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
        });
        tarjeta.Child = pila;
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

    // ---------- Atajo global (grabar/parar desde cualquier aplicación) ----------

    private const int WM_HOTKEY = 0x0312;
    private const int ID_ATAJO = 0xB0CA;
    private System.Windows.Interop.HwndSource? _gancho;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public void RegistrarAtajo()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        if (_gancho is null)
        {
            _gancho = System.Windows.Interop.HwndSource.FromHwnd(h);
            _gancho?.AddHook(AlMensaje);
        }
        UnregisterHotKey(h, ID_ATAJO);
        var (mods, vk) = TraducirAtajo(Ajustes.Actual.Atajo);
        if (vk != 0)
            RegisterHotKey(h, ID_ATAJO, mods | 0x4000, vk); // MOD_NOREPEAT
    }

    private static (uint mods, uint vk) TraducirAtajo(string atajo) => atajo switch
    {
        "ctrl+alt+d" => (0x0002 | 0x0001, 'D'),      // MOD_CONTROL | MOD_ALT
        "ctrl+alt+space" => (0x0002 | 0x0001, 0x20),
        "f9" => (0, 0x78),
        _ => (0u, 0u),                               // "" = desactivado
    };

    private IntPtr AlMensaje(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY && w.ToInt32() == ID_ATAJO)
        {
            AlternarDictado();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void AlternarDictado()
    {
        if (_instalando) return;
        if (_engine is null) { EnviarOrden("rec"); return; } // en reposo: despierta
        if (!_listo && !_grabando) return;                   // aún cargando
        EnviarOrden(_grabando ? "stop" : "rec");
    }

    // ---------- Reposo: parar el motor tras N min sin usar (ahorra GB de RAM) ----------

    private System.Windows.Threading.DispatcherTimer _reposo = null!;
    private DateTime _ultimaActividad = DateTime.Now;
    private bool _autoWidgetHecho;

    private void ComprobarReposo()
    {
        var min = Ajustes.Actual.ReposoMin;
        if (min <= 0 || _engine is null || !_listo || _grabando) return;
        if ((DateTime.Now - _ultimaActividad).TotalMinutes < min) return;

        PararMotor();
        _listo = false;
        Grabar(false);
        BotonMic.IsEnabled = true; // pulsarlo reactiva el motor
        Estado_("En reposo para ahorrar memoria — pulsa el micrófono o el atajo para seguir dictando");
        MotorEvento?.Invoke("reposo", null);
    }

    // ---------- Instalación integrada (primer arranque, sin instalar.bat) ----------

    private bool _instalando;

    private async Task InstalarAsync()
    {
        _instalando = true;
        BotonMic.Visibility = Visibility.Collapsed;
        Carga.Visibility = Visibility.Visible;
        BarraInstalacion.Visibility = Visibility.Visible;
        ModeloCombo.IsEnabled = IdiomaCombo.IsEnabled = false;
        BotonAjustes.IsEnabled = false;
        try
        {
            var raiz = RaizProyecto();
            Progreso_(3, "Primera vez: preparando VozPluma (instalación única, no cierres la app)…");

            var python = await BuscarPythonAsync();
            if (python is null)
            {
                Progreso_(6, "Instalando Python 3.12…");
                await EjecutarAsync("winget",
                    "install -e --id Python.Python.3.12 --accept-source-agreements " +
                    "--accept-package-agreements", null, 6, 14);
                python = await BuscarPythonAsync();
            }
            if (python is null)
                throw new InvalidOperationException("no se pudo instalar Python");

            Progreso_(15, "Creando el entorno de Python…");
            var venvPy = Path.Combine(raiz, ".venv", "Scripts", "python.exe");
            if (!File.Exists(venvPy))
                await EjecutarOFallar(python.Value.exe,
                    $"{python.Value.args} -m venv .venv".TrimStart(), raiz);

            Progreso_(20, "Detectando la gráfica…");
            var hayNvidia = await EjecutarAsync("nvidia-smi", "", null) == 0;

            Progreso_(24, hayNvidia
                ? "Descargando PyTorch con CUDA (~3 GB, puede tardar varios minutos)…"
                : "Descargando PyTorch para CPU…");
            await EjecutarOFallar(venvPy, "-m pip install torch torchaudio" +
                (hayNvidia ? " --index-url https://download.pytorch.org/whl/cu126" : ""),
                raiz, 24, 62);

            Progreso_(62, "Instalando dependencias de audio y modelos…");
            await EjecutarOFallar(venvPy, "-m pip install -r requirements.txt",
                raiz, 62, 88);

            if (!hayNvidia)
            {
                Progreso_(88, "Añadiendo soporte para GPU AMD/Intel (DirectML)…");
                await EjecutarAsync(venvPy, "-m pip uninstall -y onnxruntime", raiz);
                await EjecutarAsync(venvPy,
                    "-m pip install onnxruntime-directml optimum-onnx", raiz, 90, 98);
            }

            Progreso_(100, "Instalación completada");
            BarraInstalacion.Visibility = Visibility.Collapsed;
            RestaurarTrasInstalar();
            IniciarMotor(); // la primera carga descargará el modelo de voz
        }
        catch (Exception ex)
        {
            BarraInstalacion.Visibility = Visibility.Collapsed;
            Carga.Visibility = Visibility.Collapsed;
            RestaurarTrasInstalar();
            Estado_($"Error al instalar ({ex.Message}). Comprueba tu conexión y " +
                    "vuelve a abrir VozPluma, o ejecuta instalar.bat.", esError: true);
        }
    }

    private void RestaurarTrasInstalar()
    {
        _instalando = false;
        ModeloCombo.IsEnabled = IdiomaCombo.IsEnabled = true;
        BotonAjustes.IsEnabled = true;
    }

    private void Progreso_(double pct, string texto)
    {
        BarraInstalacion.Value = pct;
        Estado_(texto);
    }

    /// Localiza un Python del sistema ("py" o "python"); tras instalarlo con
    /// winget aún no está en el PATH de este proceso, así que también se busca
    /// en la carpeta de instalación por defecto.
    private async Task<(string exe, string args)?> BuscarPythonAsync()
    {
        foreach (var (exe, args) in new[] { ("py", "-3 "), ("python", "") })
            if (await EjecutarAsync(exe, args + "--version", null) == 0)
                return (exe, args);
        var carpeta = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
        if (Directory.Exists(carpeta))
            foreach (var d in Directory.GetDirectories(carpeta).OrderDescending())
                if (File.Exists(Path.Combine(d, "python.exe")))
                    return (Path.Combine(d, "python.exe"), "");
        return null;
    }

    /// Ejecuta un proceso oculto mostrando su salida en la barra de estado.
    /// Entre desde/hasta la barra avanza un poco con cada línea (progreso
    /// aproximado: pip no da porcentajes utilizables). -1 si el exe no existe.
    private async Task<int> EjecutarAsync(string exe, string args, string? cwd,
                                          double desde = -1, double hasta = -1)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = cwd ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        Process p;
        try { p = Process.Start(psi)!; }
        catch { return -1; } // "nvidia-smi"/"winget"/"py" pueden no existir
        p.OutputDataReceived += (_, e) => MostrarLineaInstalacion(e.Data, desde, hasta);
        p.ErrorDataReceived += (_, e) => MostrarLineaInstalacion(e.Data, desde, hasta);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    private void MostrarLineaInstalacion(string? linea, double desde, double hasta)
    {
        if (string.IsNullOrWhiteSpace(linea)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (desde >= 0 && BarraInstalacion.Value < hasta - 1)
                BarraInstalacion.Value += (hasta - desde) / 250.0;
            var texto = linea.Trim();
            Estado_(texto.Length > 90 ? texto[..90] + "…" : texto);
        });
    }

    private async Task EjecutarOFallar(string exe, string args, string? cwd,
                                       double desde = -1, double hasta = -1)
    {
        if (await EjecutarAsync(exe, args, cwd, desde, hasta) != 0)
            throw new InvalidOperationException(
                $"falló «{Path.GetFileName(exe)} {args}»");
    }
}
