using System.Runtime.InteropServices;

namespace VoiceAgent;

/// Escribe texto en la ventana con foco simulando pulsaciones de teclado
/// Unicode (SendInput), como si el usuario lo tecleara.
public static class Teclado
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi; // asegura el tamaño correcto de la unión
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void Escribir(string texto)
    {
        if (texto.Length == 0) return;

        var entradas = new INPUT[texto.Length * 2];
        for (int i = 0; i < texto.Length; i++)
        {
            // cada unidad UTF-16 se envía como pulsación + liberación Unicode
            entradas[i * 2] = Tecla(texto[i], soltar: false);
            entradas[i * 2 + 1] = Tecla(texto[i], soltar: true);
        }
        SendInput((uint)entradas.Length, entradas, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Tecla(char c, bool soltar) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (soltar ? KEYEVENTF_KEYUP : 0),
            },
        },
    };
}
