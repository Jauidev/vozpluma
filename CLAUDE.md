# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

VozPluma — local voice dictation for Windows. Public repo: https://github.com/Jauidev/vozpluma. The user communicates in Spanish; UI strings, code comments, and commit messages are in Spanish.

**Anonymity requirement**: never commit with the user's real name or email. Always commit as `git -c user.name="VozPluma" -c user.email="vozpluma@users.noreply.github.com" commit ...`. Never ship `.pdb` files in the distributable (they embed local paths with the username).

## Commands

The Python venv lives at `.venv` (Python 3.14). `dotnet` may not be on PATH — use the full path.

```
# Run the app (dev build)
ui\bin\Release\net9.0-windows\VozPluma.exe

# Build the UI (kill the app first — the exe stays locked while running)
Stop-Process -Name VozPluma -Force
"C:\Program Files\dotnet\dotnet.exe" build ui\VoiceAgent.csproj -c Release

# Engine / CLI without the UI
.venv\Scripts\python.exe talk.py es-ES            # console dictation (Nemotron)
.venv\Scripts\python.exe talk.py es-ES whisper    # Whisper int8
.venv\Scripts\python.exe transcribe.py file.mp3 es-ES
.venv\Scripts\python.exe engine.py --list-mics    # fast: exits before heavy imports

# Smoke-test the engine protocol (PowerShell piping to python stdin is unreliable — use cmd)
cmd /c "echo rec | .venv\Scripts\python.exe -u engine.py es-ES 2>nul"

# Rebuild the distributable (single-file exe at package root)
"C:\Program Files\dotnet\dotnet.exe" publish ui\VoiceAgent.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o dist\pub
# then move dist\pub\VozPluma.exe into dist\VozPluma\, delete dist\pub, re-zip dist\VozPluma → dist\VozPluma-v1.0.zip
```

There is no test suite; changes are verified by driving the engine protocol (see smoke test above) and launching the app.

## Architecture

Two processes talking JSON-lines over stdio:

- **`ui/` — C# WPF (.NET 9 + WPF-UI 3.0.5 Fluent theme)**. `AssemblyName` is `VozPluma` but the csproj file and C# namespace remain `VoiceAgent`. `MainWindow` owns the Python engine process and re-broadcasts engine events via its `MotorEvento` event; `WidgetWindow` (floating dictation pill) and `SettingsWindow` subscribe/act through `MainWindow.EnviarOrden()`. `Teclado.cs` types text into the focused app via `SendInput` (Unicode). The widget window sets `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` so clicking it never steals focus from the target app.
- **`engine.py` — Python engine**. Orders on stdin (`rec`, `stop`, `quit`); events on stdout (`ready`, `listening`, `transcribing`, `text`, `error`). Anything `talk.py` prints is redirected to stderr to keep stdout JSON-clean. `stop` is handled by a stdin-reader thread setting a `threading.Event` mid-recording. Transcription runs in a worker thread so the mic can listen to the next phrase while the previous one transcribes (the widget re-sends `rec` on the `transcribing` event to pipeline hands-free dictation).
- **`talk.py` — core audio/model library** (also a standalone console app). Key pieces: `buscar_microfono()` (device autodetection, excludes Stereo Mix/Steam virtual devices), `MicrofonoContinuo` (persistent stream + cached energy threshold, silence-based end-of-phrase), `_procesar()` (trims audio to the voiced segment before transcribing — untrimmed noise makes models hallucinate words), `cargar_transcriptor()` (returns a `text = f(audio)` closure for either model).
- **Settings**: `ajustes.json` at repo root ↔ `ui/Ajustes.cs` (static `Ajustes.Actual`) → passed to the engine as `--mic/--silencio/--espera/--maxseg` (format doubles with InvariantCulture).

Models (downloaded from Hugging Face on first use): Nemotron 3.5 ASR 0.6B via transformers (`AutoModelForRNNT`), Whisper large-v3-turbo via faster-whisper CT2 int8 with CUDA→CPU fallback.

## Hardware/platform gotchas (hard-won, do not re-learn)

- The dev machine's GTX 1650 (4 GB) produces **garbage output with Whisper in fp16** (even with eager attention). Whisper must run via faster-whisper `int8_float32` (≈1.4 GB VRAM) or transformers fp32. NeMo toolkit does not install on Python 3.14 (dep pins numpy<2).
- Windows audio: in non-interactive sessions MME/DirectSound/WASAPI streams may fail with PaErrorCode -9999; WDM-KS works but **only in callback mode** (blocking reads raise "Blocking API not supported yet"). The Realtek mic has hardware echo cancellation: audio played through the PC's own speakers gets suppressed, so speaker→mic loopback is unreliable for testing transcription quality.
- Single-file publish: `AppContext.BaseDirectory` points to a temp extraction dir — project root discovery must use `Environment.ProcessPath` (see `MainWindow.RaizProyecto()`).
- Console output: `sys.stdout.reconfigure(encoding="utf-8", errors="replace")` is required before printing emoji (cp1252 consoles crash otherwise).
- The system locale is French — audio device names appear in French ("Réseau de microphones" = mic array).
