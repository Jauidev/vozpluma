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
    public bool IniciarConWindows { get; set; } = false; // clave Run de HKCU
    public bool AbrirEnWidget { get; set; } = false;     // arrancar directo en el widget
    public string Atajo { get; set; } = "ctrl+alt+d";    // atajo global grabar/parar; "" = sin atajo

    // --- Rendimiento ---
    // La grabación la paras tú; esto es solo un tope de seguridad.
    public int MaxSeg { get; set; } = 300;         // duración máxima de una grabación
    public string ModeloWhisper { get; set; } = "large-v3-turbo"; // tiny|base|small|medium|large-v3-turbo
    public string Acelerador { get; set; } = "auto"; // auto|cuda|dml|cpu (dml = GPU AMD/Intel)
    public bool ForzarCpu { get; set; } = false;   // v1.1: migrado a Acelerador en Cargar()
    // el modelo ocupa GB en memoria: tras N min sin dictar se para el motor
    // y se reactiva al pulsar el micro (0 = no parar nunca)
    public int ReposoMin { get; set; } = 10;

    private static string Ruta =>
        Path.Combine(MainWindow.RaizProyecto(), "ajustes.json");

    public static Ajustes Actual { get; private set; } = Cargar();

    private static Ajustes Cargar()
    {
        try
        {
            var a = JsonSerializer.Deserialize<Ajustes>(File.ReadAllText(Ruta)) ?? new();
            if (a.ForzarCpu && a.Acelerador == "auto")
                a.Acelerador = "cpu"; // ajustes de la v1.1 (casilla "acelerar con CPU")
            return a;
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
