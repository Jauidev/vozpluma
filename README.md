# Agente de Voz 🎤

Dictado por voz **100% local** para Windows: habla y el texto se escribe solo en cualquier aplicación — chats, Word, el navegador, tu editor de código. Tu voz nunca sale de tu ordenador.

## Características

- **Transcripción local con IA**: NVIDIA Nemotron 3.5 ASR (rápido, ligero) o Whisper large-v3-turbo en int8 (máxima calidad) — tú eliges desde la interfaz
- **Modo widget**: pastilla flotante que escribe lo que dictas directamente donde esté el cursor, en cualquier app, sin robar el foco
- **Dictado continuo manos libres**: habla frase tras frase; detecta tus pausas y transcribe mientras ya está escuchando la siguiente
- **7 idiomas**: español, inglés, francés, alemán, italiano, portugués y árabe
- **Interfaz Windows 11** (WPF + tema Fluent con Mica) y ajustes de micrófono y rendimiento
- **Privado por diseño**: sin nube, sin cuentas, sin telemetría — internet solo para descargar los modelos la primera vez

## Instalación rápida (usuarios)

1. Descarga `AgenteDeVoz-v1.0.zip` desde [Releases](../../releases) y descomprímelo
2. Doble clic en `instalar.bat` (instala Python y las dependencias; si instala Python, vuelve a ejecutarlo)
3. Abre `ui\VoiceAgent.exe` — la primera vez descarga el modelo (~1.5 GB)

**Requisitos**: Windows 10/11 x64 · 8 GB RAM · GPU NVIDIA opcional (sin ella funciona por CPU, más lento)

## Instalación desde el código (desarrolladores)

```bat
git clone <este-repo>
cd <carpeta>
instalar.bat
:: la interfaz necesita el SDK de .NET 9 para compilar:
dotnet build ui\VoiceAgent.csproj -c Release
```

También puedes usar el motor sin interfaz gráfica:

```bat
.venv\Scripts\python.exe talk.py es-ES          :: dictado en consola (Nemotron)
.venv\Scripts\python.exe talk.py es-ES whisper  :: con Whisper int8
.venv\Scripts\python.exe transcribe.py audio.mp3 es-ES  :: transcribir un archivo
```

## Arquitectura

```
┌─────────────────────┐   JSON por stdin/stdout   ┌──────────────────────┐
│  VoiceAgent.exe     │ ◄───────────────────────► │  engine.py           │
│  C# WPF (Fluent)    │   rec / stop / quit       │  Python              │
│  · ventana principal│   ready / listening /     │  · captura de audio  │
│  · widget flotante  │   transcribing / text     │  · VAD por energía   │
│  · SendInput Unicode│                           │  · Nemotron / Whisper│
└─────────────────────┘                           └──────────────────────┘
```

- El widget usa `WS_EX_NOACTIVATE` para no robar el foco y `SendInput` con caracteres Unicode para escribir en la app activa
- El motor mantiene el micrófono abierto y transcribe en un hilo aparte: escucha tu siguiente frase mientras la GPU procesa la anterior
- La grabación se recorta al segmento con voz antes de transcribir para evitar alucinaciones del modelo sobre el ruido

## Licencia

MIT — ver [LICENSE](LICENSE). Los modelos se descargan de Hugging Face bajo sus propias licencias: [Nemotron 3.5 ASR](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b) (NVIDIA Open Model License) y [Whisper](https://huggingface.co/openai/whisper-large-v3-turbo) (MIT).
