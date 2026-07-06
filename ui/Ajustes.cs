using System.IO;
using System.Text.Json;

namespace VoiceAgent;

/// Ajustes persistidos en ajustes.json en la raíz del proyecto.
public class Ajustes
{
    // --- General ---
    public int MicIndex { get; set; } = -1;        // -1 = automático
    public bool EspacioFinal { get; set; } = true; // espacio tras cada frase dictada

    // --- Rendimiento ---
    public double SilencioFin { get; set; } = 0.9; // seg. de silencio que cortan la frase
    public int EsperaVoz { get; set; } = 10;       // seg. esperando voz antes de rendirse
    public int MaxSeg { get; set; } = 60;          // duración máxima de una frase

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
