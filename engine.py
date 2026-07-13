# Motor de voz para la interfaz: emite eventos JSON por stdout y recibe
# órdenes por stdin ("rec" = grabar y transcribir, "stop" = terminar la
# grabación en curso, "quit" = salir).
#
# La grabación no se corta por silencio: dura hasta que llega "stop".
#
# Uso: python engine.py [codigo_idioma] [whisper] [--mic=N] [--maxseg=300]
#                       [--wmodel=large-v3-turbo]  (tiny|base|small|medium|large-v3-turbo)
#                       [--accel=auto]  (auto|cuda|dml|cpu; dml = GPU AMD/Intel)
#                       [--cpu]  (equivale a --accel=cpu; se mantiene por compatibilidad)
#      python engine.py --list-mics   (lista los micrófonos en JSON y sale)
import contextlib
import json
import queue
import sys
import threading

import sounddevice as sd

salida = sys.stdout
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

EXCLUIR = ("mixage", "stereo mix", "steam streaming", "loopback",
           "mappeur", "sound mapper")


def emitir(evento, **datos):
    print(json.dumps({"event": evento, **datos}, ensure_ascii=False),
          file=salida, flush=True)


def listar_mics():
    mics = []
    for i, d in enumerate(sd.query_devices()):
        if (d["max_input_channels"] > 0
                and not any(x in d["name"].lower() for x in EXCLUIR)):
            api = sd.query_hostapis(d["hostapi"])["name"]
            mics.append({"index": i, "name": f"{d['name']} — {api}"})
    print(json.dumps(mics, ensure_ascii=False), file=salida, flush=True)


def main():
    args = sys.argv[1:]
    if "--list-mics" in args:
        listar_mics()  # antes de importar talk: no carga las librerías pesadas
        return

    import talk

    language = "es-ES"
    usar_whisper = False
    opciones = {}
    for a in args:
        if a.startswith("--"):
            clave, _, valor = a[2:].partition("=")
            opciones[clave] = valor
        elif a.lower() == "whisper":
            usar_whisper = True
        else:
            language = a

    mic_idx = int(opciones.get("mic", -1))
    maxseg = float(opciones.get("maxseg", 300))
    acelerador = "cpu" if "cpu" in opciones else opciones.get("accel", "auto")
    modelo_whisper = opciones.get("wmodel", "large-v3-turbo")

    try:
        # los prints informativos de talk.py van a stderr para no romper el JSON
        with contextlib.redirect_stdout(sys.stderr):
            if mic_idx >= 0:
                rate = talk.probar_dispositivo(mic_idx)
                if rate is not None:
                    device = mic_idx
                else:
                    print(f"mic {mic_idx} no se pudo abrir; usando automático",
                          file=sys.stderr)
                    device, rate = talk.buscar_microfono()
            else:
                device, rate = talk.buscar_microfono()
            nombre_mic = sd.query_devices(device)["name"]
            nombre_modelo, transcribir = talk.cargar_transcriptor(
                usar_whisper, language, acelerador, modelo_whisper)
            mic = talk.MicrofonoContinuo(device, rate)
    except Exception as e:
        emitir("error", message=str(e))
        return

    # hilo trabajador: transcribe en paralelo para poder escuchar la frase
    # siguiente mientras la GPU procesa la anterior
    pendientes = queue.Queue()

    def transcriptor():
        while True:
            audio = pendientes.get()
            if audio is None:
                break
            try:
                with contextlib.redirect_stdout(sys.stderr):
                    texto = transcribir(audio)
                emitir("text", text=texto)
            except Exception as e:
                emitir("error", message=str(e))

    threading.Thread(target=transcriptor, daemon=True).start()

    # hilo lector: "stop" activa el evento al instante aunque se esté grabando
    # y vacía las "rec" pendientes — una sola pulsación de parar cancela
    # también las re-escuchas que la interfaz ya hubiera encolado
    ordenes = queue.Queue()
    parar = threading.Event()

    def leer_stdin():
        for linea in sys.stdin:
            orden = linea.strip().lower()
            if orden == "stop":
                try:
                    while True:
                        ordenes.get_nowait()
                except queue.Empty:
                    pass
                parar.set()
            else:
                if orden == "rec":
                    # limpiar aquí (y no en el bucle principal) conserva el
                    # orden rec/stop: un stop posterior nunca se pierde
                    parar.clear()
                ordenes.put(orden)
        ordenes.put("quit")

    threading.Thread(target=leer_stdin, daemon=True).start()

    emitir("ready", mic=nombre_mic, model=nombre_modelo, language=language)

    while True:
        orden = ordenes.get()
        if orden == "quit":
            break
        if orden != "rec":
            continue
        try:
            emitir("listening")
            with contextlib.redirect_stdout(sys.stderr):
                audio = mic.escuchar(max_seg=maxseg, stop_event=parar)
            if len(audio) < talk.MODEL_SR / 2:
                emitir("text", text="")
                continue
            emitir("transcribing")
            pendientes.put(audio)  # el trabajador emitirá "text" al terminar
        except Exception as e:
            emitir("error", message=str(e))

    mic.cerrar()


if __name__ == "__main__":
    main()
