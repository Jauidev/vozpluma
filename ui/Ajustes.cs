using System.IO;
using System.Text.Json;

namespace VoiceAgent;

/// Ajustes persistidos en ajustes.json en la raíz del proyecto.
public class Ajustes
{
    // --- General ---
    public int MicIndex { get; set; } = -1;        // -1 = automático
    public bool EspacioFinal { get; set; } = true; // espacio tras cada frase dictada
    public string Idioma { get; set; } = "";       // "" = usar el idioma del sistema
    public bool UsarWhisper { get; set; } = true;

    // --- Rendimiento ---
    // La grabación la paras tú; esto es solo un tope de seguridad.
    public int MaxSeg { get; set; } = 300;         // duración máxima de una grabación
    public bool ForzarCpu { get; set; } = false;   // equipos sin gráfica NVIDIA

    private static string Ruta =>
        Path.Combine(MainWindow.RaizProyecto(), "ajustes.json");

    public static Ajustes Actual { get; private set; } = Cargar();

    private static Ajustes Cargar()
    {
        try
        {
            return JsonSerializer.Deserialize<Ajustes>(File.ReadAllText(Ruta)) ?? new();
        }
        catch
        {
            return new(); // sin archivo o corrupto: valores por defecto
        }
    }

    public void Guardar()
    {
        File.WriteAllText(Ruta,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        Actual = this;
    }
}
