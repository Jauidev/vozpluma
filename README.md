# VozPluma

**English** · [Español](README.es.md)

**100% local** voice dictation for Windows: speak and the text types itself into any application — chats, Word, your browser, your code editor. Your voice never leaves your computer.

## Features

- **Local AI transcription**: NVIDIA Nemotron 3.5 ASR (fast, lightweight) or Whisper large-v3-turbo in int8 (best quality) — switchable from the UI
- **Widget mode**: a floating pill that types what you dictate right where your cursor is, in any app, without stealing focus
- **You control the recording**: press to start, speak at your own pace — pauses included — and press again to stop and transcribe; no silence-based auto-cutoff
- **7 languages**: Spanish, English, French, German, Italian, Portuguese and Arabic
- **Windows 11 interface** (WPF + Fluent theme with Mica) with separate **General** (microphone, behavior) and **Performance** settings
- **Runs on any PC**: accelerated with an NVIDIA GPU; without one (or with AMD/Intel) there is a **CPU mode** that uses all cores and skips the GPU probing for faster startup
- **Closes for real**: quitting the app frees all memory — nothing stays running in the background
- **Private by design**: no cloud, no accounts, no telemetry — internet is only needed to download the models the first time

## Quick install (users)

1. Download `VozPluma-v1.1.0.zip` from [Releases](https://github.com/Jauidev/vozpluma/releases) and unzip it
2. Double-click `instalar.bat` — it installs Python and all dependencies by itself
   (if it says it just installed Python, close it and run it again: only happens the first time)
3. Open `VozPluma.exe` — the first run downloads the speech model (~1.5 GB)

The unzipped package looks like this:

```
VozPluma\
├── VozPluma.exe        ← the app (self-contained, no .NET required)
├── instalar.bat        ← run this first, once
├── LEEME.txt           ← instructions and troubleshooting (Spanish)
├── engine.py, talk.py, transcribe.py
└── requirements.txt
```

**Requirements**: Windows 10/11 x64 · 8 GB RAM · No GPU needed: it runs on any PC using the CPU. If you have an **NVIDIA GPU** it is used automatically and runs much faster (AMD/Intel cards don't accelerate — with those it runs on CPU as well).

## Install from source (developers)

```bat
git clone https://github.com/Jauidev/vozpluma.git
cd vozpluma
instalar.bat
:: the UI needs the .NET 9 SDK to build:
dotnet build ui\VoiceAgent.csproj -c Release
```

You can also use the engine without the GUI:

```bat
.venv\Scripts\python.exe talk.py es-ES          :: console dictation (Nemotron)
.venv\Scripts\python.exe talk.py es-ES whisper  :: with Whisper int8
.venv\Scripts\python.exe transcribe.py audio.mp3 es-ES  :: transcribe a file
```

## Architecture

```
┌─────────────────────┐   JSON over stdin/stdout  ┌──────────────────────┐
│  VozPluma.exe       │ ◄───────────────────────► │  engine.py           │
│  C# WPF (Fluent)    │   rec / stop / quit       │  Python              │
│  · main window      │   ready / listening /     │  · audio capture     │
│  · floating widget  │   transcribing / text     │  · energy-based VAD  │
│  · settings         │                           │  · Nemotron / Whisper│
│  · SendInput Unicode│                           │  · persistent mic    │
└─────────────────────┘                           └──────────────────────┘
```

- The widget uses `WS_EX_NOACTIVATE` to never steal focus and `SendInput` with Unicode characters to type into the active app
- The engine keeps the microphone open between recordings and transcribes on a separate thread; recording runs until you press stop (with a configurable safety cap)
- Recordings are trimmed to the voiced segment before transcription to prevent the model from hallucinating words out of noise
- Settings are stored in `ajustes.json` and passed to the engine as arguments (`--mic`, `--maxseg`, `--cpu`)

## Roadmap

- [ ] AMD and Intel GPU acceleration (via whisper.cpp/Vulkan or DirectML); today those GPUs run on CPU
- [ ] macOS and Linux support (the Python engine is already cross-platform; the UI and text injection need porting)
- [ ] Spoken replies: connect the transcription to an LLM and read the answer out loud

Ideas or contributions? Open an issue or a pull request.

## License

MIT — see [LICENSE](LICENSE). Models are downloaded from Hugging Face under their own licenses: [Nemotron 3.5 ASR](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b) (NVIDIA Open Model License) and [Whisper](https://huggingface.co/openai/whisper-large-v3-turbo) (MIT).
