using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace VoiceAgent;

public partial class WidgetWindow
{
    private readonly MainWindow _principal;
    private bool _continuo; // dictado manos libres: re-escucha tras cada frase
    private int _transcribiendo; // frases pendientes de transcribir (van en paralelo)

    private static readonly Brush Gris = Brushes.Gray;
    private static readonly Brush Rojo = new SolidColorBrush(Color.FromRgb(0xE8, 0x4A, 0x4A));
    private static readonly Brush Ambar = new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D));
    private static readonly Brush Verde = new SolidColorBrush(Color.FromRgb(0x4C, 0xB0, 0x6E));

    public WidgetWindow(MainWindow principal)
    {
        InitializeComponent();
        _principal = principal;
        _principal.MotorEvento += AlEventoDelMotor;

        // WS_EX_NOACTIVATE: pulsar el widget no roba el foco a la app donde
        // estás escribiendo; WS_EX_TOOLWINDOW lo saca del Alt+Tab.
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(h, GWL_EXSTYLE,
                GetWindowLongPtr(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

        // esquina inferior derecha del área de trabajo
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 24;
            Top = area.Bottom - ActualHeight - 24;
        };

        Closed += (_, _) =>
        {
            _principal.MotorEvento -= AlEventoDelMotor;
            if (_principal.IsLoaded) // no re-mostrar si la app entera se está cerrando
                _principal.Show();
        };
    }

    private void AlEventoDelMotor(string evento, string? dato)
    {
        switch (evento)
        {
            case "ready":
                Punto.Fill = Verde;
                BotonHablar.IsEnabled = true;
                BotonParar.IsEnabled = false;
                break;
            case "listening":
                Punto.Fill = Rojo;
                BotonHablar.IsEnabled = false;
                BotonParar.IsEnabled = true;
                break;
            case "transcribing":
                Punto.Fill = Ambar;
                BotonParar.IsEnabled = _continuo;
                _transcribiendo++;
                Progreso.Visibility = Visibility.Visible;
                if (_continuo)
                    _principal.EnviarOrden("rec"); // re-escucha YA: transcribe en paralelo
                break;
            case "text":
                if (_transcribiendo > 0 && --_transcribiendo == 0)
                    Progreso.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(dato))
                    Teclado.Escribir(dato + (Ajustes.Actual.EspacioFinal ? " " : ""));
                if (_continuo)
                {
                    // si el ciclo terminó sin voz (timeout), re-arma la escucha
                    if (string.IsNullOrEmpty(dato))
                        _principal.EnviarOrden("rec");
                }
                else
                {
                    Punto.Fill = Verde;
                    BotonHablar.IsEnabled = true;
                    BotonParar.IsEnabled = false;
                }
                break;
            case "error":
                _continuo = false;
                _transcribiendo = 0;
                Progreso.Visibility = Visibility.Collapsed;
                Punto.Fill = Gris;
                BotonHablar.IsEnabled = true;
                BotonParar.IsEnabled = false;
                break;
        }
    }

    private void Hablar_Click(object sender, RoutedEventArgs e)
    {
        _continuo = true;
        _principal.EnviarOrden("rec");
    }

    private void Parar_Click(object sender, RoutedEventArgs e)
    {
        _continuo = false;
        _principal.EnviarOrden("stop");
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private void Arrastrar(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    // ---------- Win32 ----------

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong);
}
