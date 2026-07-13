# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

VozPluma — local voice dictation for Windows. Public repo: https://github.com/Jauidev/vozpluma (Release v1.0 published). The user communicates in Spanish; UI strings, code comments, and commit messages are in Spanish. README.md is Spanish-only (the English README was removed in v1.2.0).

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

# Uninstaller UI (lives inside the same exe)
ui\bin\Release\net9.0-windows\VozPluma.exe --desinstalar
```

There is no test suite; changes are verified by driving the engine protocol (see smoke test above) and launching the app.

### Release pipeline (run after every user-visible change)

1. Publish single-file exe: `"C:\Program Files\dotnet\dotnet.exe" publish ui\VoiceAgent.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o dist\pub`
2. Move `dist\pub\VozPluma.exe` into the staging folder (a clean copy of the package: exe + engine.py/talk.py/transcribe.py + requirements.txt + LEEME.txt; the .bat installers were removed in v1.2.1 — install/uninstall live inside the exe). **Never zip `dist\VozPluma` directly** — it contains a multi-GB `.venv` from local use.
3. Zip with **WinRAR** (user's explicit preference; Compress-Archive takes minutes, WinRAR seconds): `& "C:\Program Files\WinRAR\WinRAR.exe" a -afzip -ibck -ep1 <zip> <stagingFolder>` (via `Start-Process -Wait`).
4. Replace the GitHub release asset (release id 349843137) via the REST API; the auth token comes from Git Credential Manager: `printf "protocol=https\nhost=github.com\n" | git credential fill` (gh CLI is not installed). Delete the old asset, then POST the zip to uploads.github.com.
5. Commit (anonymous author, see above) and `git push`.

## Architecture

Two processes talking JSON-lines over stdio:

- **`ui/` — C# WPF (.NET 9 + WPF-UI 3.0.5 Fluent theme)**. `AssemblyName` is `VozPluma` but the csproj file and C# namespace remain `VoiceAgent`. `MainWindow` owns the Python engine process and re-broadcasts engine events via its `MotorEvento` event; `WidgetWindow` (floating dictation pill) and `SettingsWindow` subscribe/act through `MainWindow.EnviarOrden()`. `Teclado.cs` types text into the focused app via `SendInput` (Unicode). The widget window sets `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` so clicking it never steals focus.
- **Lifecycle**: closing the main window exits for real — engine process included; nothing stays in the background (no tray icon; `UseWindowsForms` was removed from the csproj). Single instance via Mutex `"VozPlumaApp"`; a second launch signals EventWaitHandle `"VozPlumaMostrar"` to restore the window. `Program.Main` dispatches `--desinstalar` (→ `DesinstaladorWindow`, which waits for the mutex to free, then deletes the 3 HF model caches, `.venv`, `__pycache__`, `ajustes.json`) **before** the mutex logic. Engine restarts (`ReiniciarMotor`) stop the old engine on a background task — never block the UI thread waiting for process exit.
- **`engine.py` — Python engine**. Orders on stdin (`rec`, `stop`, `quit`); events on stdout (`ready`, `listening`, `transcribing`, `text`, `error`). Anything `talk.py` prints is redirected to stderr to keep stdout JSON-clean. **Recording does not auto-stop on silence**: `rec` records until the user sends `stop` (or `--maxseg` as a safety cap). `stop` is handled by a stdin-reader thread that sets a `threading.Event` mid-recording **and drains any pending `rec` from the queue**; the `parar.clear()` happens in that reader thread when a `rec` is enqueued, so a later `stop` is never swallowed. Transcription runs in a worker thread (so a long transcription doesn't block the next order). There is no hands-free re-arming — one `rec` = one recording; the UI starts the next one on the user's press.
- **`talk.py` — core audio/model library** (also a standalone console app). Key pieces: `buscar_microfono()` (device autodetection, excludes Stereo Mix/Steam virtual devices), `MicrofonoContinuo.escuchar()` (persistent stream + cached energy threshold; records until `stop_event`/`max_seg`, no silence cut), `_procesar()` (trims audio to the voiced segment — untrimmed noise makes models hallucinate words; mid-recording pauses are kept), `cargar_transcriptor()` (returns a `text = f(audio)` closure for either model). `grabar()` is the separate console-only path that still auto-cuts on silence. Heavy imports (transformers, torch, librosa) are **lazy** — keep them that way; the torch DLL dir for ctranslate2 is located with `importlib.util.find_spec("torch")` *without importing torch* (importing it costs ~10 s).
- **UI recording control**: the main window's mic button and the widget's mic/stop buttons all follow start→stop. The main-window mic button toggles (`Grabar(bool)` swaps the `Mic24`/`Stop24` icon); the widget has separate `BotonHablar`/`BotonParar`. Button state is driven by engine events, not optimistically.
- **Settings**: `ajustes.json` at repo root ↔ `ui/Ajustes.cs` (static `Ajustes.Actual`) → passed to the engine as `--mic/--maxseg` plus the flag `--cpu` when `ForzarCpu` is set (format doubles with InvariantCulture). `MaxSeg` is only a safety cap now (silence-cut and voice-wait settings were removed). `--cpu` loads models directly on CPU (`cpu_threads=os.cpu_count()` for faster-whisper, `device_map="cpu"` for Nemotron) and skips the CUDA attempt and torch DLL lookup. Dictation language defaults to the system language on first run; language and model choices persist.

Models (downloaded from Hugging Face on first use): Nemotron 3.5 ASR 0.6B via transformers (`AutoModelForRNNT`), Whisper large-v3-turbo via faster-whisper CT2 int8 with CUDA→CPU fallback.

## Hardware/platform gotchas (hard-won, do not re-learn)

- The dev machine's GTX 1650 (4 GB) produces **garbage output with Whisper in fp16** (even with eager attention). Whisper must run via faster-whisper `int8_float32` (≈1.4 GB VRAM) or transformers fp32. NeMo toolkit does not install on Python 3.14 (dep pins numpy<2).
- Windows audio: in non-interactive sessions MME/DirectSound/WASAPI streams may fail with PaErrorCode -9999; WDM-KS works but **only in callback mode** (blocking reads raise "Blocking API not supported yet"). The Realtek mic has hardware echo cancellation: audio played through the PC's own speakers gets suppressed, so speaker→mic loopback is unreliable for testing transcription quality.
- Single-file publish: `AppContext.BaseDirectory` points to a temp extraction dir — project root discovery must use `Environment.ProcessPath` (see `MainWindow.RaizProyecto()`).
- Existing code-behind files keep `using` aliases (`Brush`, `Clipboard`, `Application`…) from the era when `UseWindowsForms` made those names ambiguous; the aliases are harmless — don't reintroduce WinForms.
- Console output: `sys.stdout.reconfigure(encoding="utf-8", errors="replace")` is required before printing emoji (cp1252 consoles crash otherwise).
- The system locale is French — audio device names appear in French ("Réseau de microphones" = mic array).
- Long compound PowerShell commands that mention `"C:\Program Files\..."` can be misparsed by the sandbox permission layer — split them into smaller calls.
