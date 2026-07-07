# Habla con el agente por el micrófono usando Nemotron 3.5 ASR 0.6B (o Whisper)
# Pulsa Enter, habla, y al callarte transcribe automáticamente (detección de silencio).
#
# Uso: python talk.py [codigo_idioma] [whisper]
#   ej: python talk.py es-ES            -> Nemotron (por defecto)
#       python talk.py es-ES whisper    -> Whisper large-v3-turbo
import queue
import sys
import time

# consolas Windows con cp1252 no soportan emojis
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

import numpy as np
import sounddevice as sd

# transformers, torch y librosa se importan solo cuando hacen falta:
# son lentísimos de cargar y retrasaban cada arranque de la app

MODEL_ID = "nvidia/nemotron-3.5-asr-streaming-0.6b"
MODEL_SR = 16000  # el modelo espera 16 kHz


EXCLUIR = ("mixage", "stereo mix", "steam streaming", "loopback", "mappeur", "sound mapper")


def probar_dispositivo(device):
    """Devuelve un samplerate utilizable para ese dispositivo, o None si no abre."""
    rates = [MODEL_SR, 48000, 44100,
             int(sd.query_devices(device)["default_samplerate"])]
    for rate in rates:
        try:
            # WDM-KS solo admite modo callback, no lectura bloqueante
            with sd.InputStream(samplerate=rate, channels=1, dtype="float32",
                                device=device, callback=lambda *a: None):
                pass
            return rate
        except Exception:
            continue
    return None


def buscar_microfono():
    """Devuelve (device, samplerate) del primer micrófono real que se pueda abrir."""
    entradas = [(i, d["name"].lower()) for i, d in enumerate(sd.query_devices())
                if d["max_input_channels"] > 0
                and not any(x in d["name"].lower() for x in EXCLUIR)]
    # primero el dispositivo por defecto, luego los que se llaman "mic", luego el resto
    defecto = sd.default.device[0]
    candidatos = [defecto] if defecto is not None and defecto >= 0 else []
    candidatos += [i for i, n in entradas if "mic" in n]
    candidatos += [i for i, n in entradas if "mic" not in n]
    for device in candidatos:
        rates = [MODEL_SR, 48000, 44100,
                 int(sd.query_devices(device)["default_samplerate"])]
        for rate in rates:
            try:
                # WDM-KS solo admite modo callback, no lectura bloqueante
                with sd.InputStream(samplerate=rate, channels=1,
                                    dtype="float32", device=device,
                                    callback=lambda *a: None):
                    pass
                return device, rate
            except sd.PortAudioError:
                continue
    raise RuntimeError("No se encontró ningún micrófono utilizable")


def grabar(device, rate, max_seg=60, silencio_fin=1.2, espera_voz=10,
           stop_event=None):
    """Graba hasta detectar ~1.2s de silencio tras la voz (o max_seg segundos).
    Si se pasa stop_event (threading.Event), activarlo termina la grabación."""
    bloques = queue.Queue()
    dur_bloque = 0.1
    with sd.InputStream(samplerate=rate, channels=1, dtype="float32",
                        device=device, blocksize=int(dur_bloque * rate),
                        callback=lambda indata, n, t, s: bloques.put(indata.copy())):
        # calibra el ruido de fondo con el primer medio segundo (mediana,
        # para que un pico suelto no infle el umbral por encima de la voz)
        ruido = [bloques.get() for _ in range(5)]
        piso = float(np.median([np.abs(b).max() for b in ruido]))
        umbral = min(max(4 * piso, 0.012), 0.035)

        print("🎤 Habla ahora...")
        frames = list(ruido)
        niveles = [float(np.abs(b).max()) for b in ruido]
        hablando = False
        silencio = 0.0
        inicio = time.time()
        while True:
            bloque = bloques.get()
            frames.append(bloque)
            nivel = float(np.abs(bloque).max())
            niveles.append(nivel)
            if stop_event is not None and stop_event.is_set():
                break
            if not hablando:
                if nivel > umbral:
                    hablando = True
                elif time.time() - inicio > espera_voz:
                    print("(no se detectó voz)")
                    break
            else:
                # histéresis: para cortar exige caer bastante por debajo del umbral
                silencio = silencio + dur_bloque if nivel < 0.7 * umbral else 0.0
                if silencio >= silencio_fin or time.time() - inicio > max_seg:
                    break

    return _procesar(frames, niveles, umbral, rate)


def _procesar(frames, niveles, umbral, rate):
    """Recorta al segmento con voz, remuestrea y normaliza el volumen."""
    # recorta al segmento con voz: el ruido de los extremos, amplificado por la
    # normalización, hace que el modelo "invente" palabras que no se dijeron
    voz = [i for i, n in enumerate(niveles) if n > umbral]
    if not voz:
        return np.zeros(0, dtype="float32")
    ini = max(voz[0] - 3, 0)
    fin = min(voz[-1] + 4, len(frames))
    audio = np.concatenate(frames[ini:fin])[:, 0]

    if rate != MODEL_SR:
        import librosa
        audio = librosa.resample(audio, orig_sr=rate, target_sr=MODEL_SR)
    # normaliza el volumen: la señal del micro llega muy baja
    pico = np.abs(audio).max()
    if pico < 0.03:
        print(f"⚠️  Tu voz llega muy baja (nivel {pico:.3f}). Acércate al micrófono "
              "o sube su volumen en Configuración de Windows > Sonido > Entrada.")
    if pico > 0.001:
        audio = audio * (0.95 / pico)
    return audio


class MicrofonoContinuo:
    """Micrófono siempre abierto para dictado continuo: evita el coste de
    reabrir el stream y recalibrar el ruido en cada frase."""

    def __init__(self, device, rate):
        self.rate = rate
        self.dur_bloque = 0.1
        self.bloques = queue.Queue()
        self.umbral = None
        self.stream = sd.InputStream(
            samplerate=rate, channels=1, dtype="float32", device=device,
            blocksize=int(self.dur_bloque * rate),
            callback=lambda indata, n, t, s: self.bloques.put(indata.copy()))
        self.stream.start()

    def cerrar(self):
        self.stream.stop()
        self.stream.close()

    def _vaciar(self):
        try:
            while True:
                self.bloques.get_nowait()
        except queue.Empty:
            pass

    def escuchar(self, max_seg=300, stop_event=None):
        """Graba hasta que se pulse parar (stop_event) o se alcance max_seg,
        sin cortar por silencio: la frase la termina el usuario, no una pausa.
        Devuelve el audio recortado al tramo con voz."""
        self._vaciar()  # descarta lo acumulado mientras no escuchábamos

        frames, niveles = [], []
        if self.umbral is None:  # calibra el ruido de fondo la primera vez
            frames = [self.bloques.get() for _ in range(5)]
            niveles = [float(np.abs(b).max()) for b in frames]
            piso = float(np.median(niveles))
            self.umbral = min(max(4 * piso, 0.012), 0.035)
        umbral = self.umbral

        inicio = time.time()
        while True:
            if stop_event is not None and stop_event.is_set():
                break
            bloque = self.bloques.get()
            frames.append(bloque)
            niveles.append(float(np.abs(bloque).max()))
            if time.time() - inicio > max_seg:
                break

        return _procesar(frames, niveles, umbral, self.rate)


def cargar_transcriptor(usar_whisper, language):
    """Devuelve una función audio->texto con el modelo elegido."""
    if usar_whisper:
        import importlib.util
        import os
        # ctranslate2 necesita las DLL de CUDA que trae torch; localizamos la
        # carpeta sin importar torch (importarlo tarda ~10 s y no hace falta)
        spec = importlib.util.find_spec("torch")
        if spec and spec.submodule_search_locations:
            os.add_dll_directory(
                os.path.join(spec.submodule_search_locations[0], "lib"))
        from faster_whisper import WhisperModel
        # int8: ~1.4 GB de VRAM y misma calidad; fp16 puro da basura en la GTX 1650
        try:
            fw = WhisperModel("large-v3-turbo", device="cuda",
                              compute_type="int8_float32")
        except Exception:
            # sin GPU NVIDIA utilizable: CPU en int8 (más lento pero funciona)
            fw = WhisperModel("large-v3-turbo", device="cpu", compute_type="int8")
        idioma = language.split("-")[0]

        def transcribir(audio):
            # vad_filter descarta tramos sin voz: evita palabras inventadas en el ruido
            segments, _ = fw.transcribe(audio, language=idioma, vad_filter=True)
            return " ".join(s.text.strip() for s in segments)

        return "Whisper large-v3-turbo (int8)", transcribir

    from transformers import AutoModelForRNNT, AutoProcessor
    processor = AutoProcessor.from_pretrained(MODEL_ID)
    model = AutoModelForRNNT.from_pretrained(MODEL_ID, device_map="auto")

    def transcribir(audio):
        inputs = processor(audio, sampling_rate=MODEL_SR, language=language)
        inputs.to(model.device, dtype=model.dtype)
        output = model.generate(**inputs, return_dict_in_generate=True)
        return processor.decode(output.sequences, skip_special_tokens=True)[0].strip()

    return "Nemotron 3.5 ASR 0.6B", transcribir


def main():
    language = sys.argv[1] if len(sys.argv) > 1 else "es-ES"
    usar_whisper = "whisper" in [a.lower() for a in sys.argv[2:]]

    device, rate = buscar_microfono()
    nombre = sd.query_devices(device)["name"]
    print(f"Micrófono: [{device}] {nombre} @ {rate} Hz")

    print("Cargando modelo...")
    nombre_modelo, transcribir = cargar_transcriptor(usar_whisper, language)
    print(f"Listo. Modelo: {nombre_modelo}. Idioma: {language}. Ctrl+C para salir.\n")

    while True:
        input("Pulsa Enter y habla (se corta solo al callarte)...")
        audio = grabar(device, rate)
        if len(audio) < MODEL_SR / 2:
            print("(grabación demasiado corta, inténtalo de nuevo)\n")
            continue

        texto = transcribir(audio)
        print(f"\n🗣️  Tú dijiste: {texto}\n")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nAdiós.")
