# VozPluma

Dictado por voz **100% local** para Windows: habla y el texto se escribe solo en cualquier aplicación — chats, Word, el navegador, tu editor de código. Tu voz nunca sale de tu ordenador.

## Características

- **Transcripción local con IA**: Whisper en cinco tamaños — de `tiny` (el más rápido) a `large-v3-turbo` (máxima calidad) — o NVIDIA Nemotron 3.5 ASR, se cambia desde la interfaz
- **Modo widget**: pastilla flotante que escribe lo que dictas directamente donde esté el cursor, en cualquier app, sin robar el foco; se abre al instante — un círculo de carga gira mientras el motor arranca y el punto verde avisa de que ya puedes dictar
- **Tú controlas la grabación**: pulsa para empezar, habla a tu ritmo — con las pausas que quieras — y pulsa otra vez para parar y transcribir; sin cortes automáticos por silencio
- **7 idiomas**: español, inglés, francés, alemán, italiano, portugués y árabe
- **Instalación sin pasos y rápida**: abres el exe y el primer arranque instala Python y todas las dependencias por sí solo, con barra de progreso — sin scripts que ejecutar; desde la v1.2.1 usa [uv](https://github.com/astral-sh/uv) y descarga los paquetes en paralelo, en una fracción del tiempo que tardaba pip
- **Atajo de teclado global** (Ctrl+Alt+D por defecto): empieza o para el dictado desde cualquier aplicación, incluso con VozPluma oculto
- **Interfaz Windows 11** (WPF + tema Fluent con Mica) con ajustes de **General**, **Inicio** (arrancar con Windows, abrir en modo widget) y **Rendimiento**
- **Funciona en cualquier PC y con cualquier gráfica**: NVIDIA (CUDA), **AMD e Intel (DirectML)** o solo CPU — el modo *automático* elige la mejor disponible; el modelo se calienta al cargar para que hasta la primera transcripción sea instantánea
- **Ligero en segundo plano**: tras un tiempo de inactividad configurable el motor entra en reposo y libera los GB que ocupa el modelo; pulsar el micro o el atajo lo despierta
- **Cerrar es cerrar**: al salir de la app se libera toda la memoria — nada queda en segundo plano
- **Privado por diseño**: sin nube, sin cuentas, sin telemetría — internet solo para descargar los modelos la primera vez

## Instalación rápida (usuarios)

1. Descarga `VozPluma-v1.2.1.zip` desde [Releases](https://github.com/Jauidev/vozpluma/releases) y descomprímelo
2. Abre `VozPluma.exe` — **y ya está**. La primera vez instala Python y todas las dependencias por sí sola, con una barra de progreso, y después descarga el modelo de voz (~1.5 GB). Todo esto ocurre una sola vez; los siguientes arranques van directos a la app.

El paquete descomprimido queda así:

```
VozPluma\
├── VozPluma.exe        ← la aplicación (autocontenida, no necesita .NET)
├── LEEME.txt           ← instrucciones y solución de problemas
├── engine.py, talk.py, transcribe.py
└── requirements.txt
```

**Requisitos**: Windows 10/11 x64 · 8 GB RAM · No necesita gráfica: funciona en cualquier PC usando el procesador. Las **GPU NVIDIA** se usan automáticamente (CUDA); las **AMD e Intel** aceleran Whisper mediante DirectML (el instalador lo configura cuando no detecta NVIDIA).

## Instalación desde el código (desarrolladores)

```bat
git clone https://github.com/Jauidev/vozpluma.git
cd vozpluma
:: el entorno de Python lo crea la propia app al abrirla por primera vez;
:: a mano sería: python -m venv .venv && .venv\Scripts\pip install torch torchaudio -r requirements.txt
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
│  VozPluma.exe       │ ◄───────────────────────► │  engine.py           │
│  C# WPF (Fluent)    │   rec / stop / quit       │  Python              │
│  · ventana principal│   ready / listening /     │  · captura de audio  │
│  · widget flotante  │   transcribing / text     │  · VAD por energía   │
│  · ajustes          │                           │  · Nemotron / Whisper│
│  · SendInput Unicode│                           │  · mic persistente   │
└─────────────────────┘                           └──────────────────────┘
```

- El widget usa `WS_EX_NOACTIVATE` para no robar el foco y `SendInput` con caracteres Unicode para escribir en la app activa
- El motor mantiene el micrófono abierto entre grabaciones y transcribe en un hilo aparte; la grabación dura hasta que pulsas parar (con un tope de seguridad configurable)
- La grabación se recorta al segmento con voz antes de transcribir para evitar alucinaciones del modelo sobre el ruido
- Los ajustes se guardan en `ajustes.json` y se pasan al motor como argumentos (`--mic`, `--maxseg`, `--wmodel`, `--accel`)
- El acelerador se elige en Ajustes: automático (CUDA → DirectML → CPU), NVIDIA, AMD/Intel o CPU; DirectML ejecuta Whisper con ONNX Runtime y pesos fp16 (los fp32 dan texto basura en algunas GPU)

## Hoja de ruta

- [x] Aceleración en gráficas AMD e Intel vía DirectML *(disponible desde la v1.2.0)*
- [ ] Soporte para macOS y Linux (el motor Python ya es multiplataforma; falta portar la interfaz y la escritura en otras apps)
- [ ] Respuestas habladas: conectar la transcripción a un LLM y leer la respuesta en voz alta

¿Ideas o contribuciones? Abre un issue o un pull request.

## Licencia

MIT — ver [LICENSE](LICENSE). Los modelos se descargan de Hugging Face bajo sus propias licencias: [Nemotron 3.5 ASR](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b) (NVIDIA Open Model License) y [Whisper](https://huggingface.co/openai/whisper-large-v3-turbo) (MIT).
