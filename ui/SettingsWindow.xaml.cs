using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace VoiceAgent;

public partial class SettingsWindow
{
    private class MicInfo
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    public SettingsWindow()
    {
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        var a = Ajustes.Actual;
        EspacioCheck.IsChecked = a.EspacioFinal;
        CpuCheck.IsChecked = a.ForzarCpu;
        MaxSegSlider.Value = a.MaxSeg;
        ActualizarEtiquetas();

        MicCombo.Items.Add(new ComboBoxItem
        {
            Content = "Automático (recomendado)",
            Tag = -1,
            IsSelected = true,
        });
        _ = CargarMicsAsync();
    }

    private async Task CargarMicsAsync()
    {
        try
        {
            var raiz = MainWindow.RaizProyecto();
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(raiz, ".venv", "Scripts", "python.exe"),
                Arguments = "-u engine.py --list-mics",
                WorkingDirectory = raiz,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var json = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();

            foreach (var mic in JsonSerializer.Deserialize<List<MicInfo>>(json) ?? [])
            {
                var item = new ComboBoxItem { Content = mic.Name, Tag = mic.Index };
                MicCombo.Items.Add(item);
                if (mic.Index == Ajustes.Actual.MicIndex)
                    item.IsSelected = true;
            }
        }
        catch
        {
            // si la lista falla, queda solo "Automático" — opción segura
        }
    }

    private void Slider_Changed(object sender,
        RoutedPropertyChangedEventArgs<double> e) => ActualizarEtiquetas();

    private void ActualizarEtiquetas()
    {
        // aún inicializando el XAML: la etiqueta se crea después del slider
        if (MaxSegValor is null) return;
        MaxSegValor.Text = $"{(int)MaxSegSlider.Value} s";
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        new Ajustes
        {
            MicIndex = (int)(((ComboBoxItem?)MicCombo.SelectedItem)?.Tag ?? -1),
            EspacioFinal = EspacioCheck.IsChecked == true,
            ForzarCpu = CpuCheck.IsChecked == true,
            MaxSeg = (int)MaxSegSlider.Value,
            // conserva lo elegido en la ventana principal
            Idioma = Ajustes.Actual.Idioma,
            UsarWhisper = Ajustes.Actual.UsarWhisper,
        }.Guardar();
        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void Desinstalar_Click(object sender, RoutedEventArgs e)
    {
        var respuesta = System.Windows.MessageBox.Show(
            "Se abrirá el desinstalador y VozPluma se cerrará. ¿Continuar?",
            "Desinstalar VozPluma", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (respuesta != MessageBoxResult.Yes) return;

        Process.Start(Environment.ProcessPath!, "--desinstalar");
        DialogResult = false;
        ((MainWindow)Owner).SalirDeVerdad();
    }
}
