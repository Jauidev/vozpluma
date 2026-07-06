# Transcripción con NVIDIA Nemotron 3.5 ASR Streaming 0.6B
# Modelo oficial: https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b
# GitHub oficial: https://github.com/NVIDIA-NeMo/Speech
#
# Uso: python transcribe.py <archivo_audio> [codigo_idioma]
#   ej: python transcribe.py audio.wav es-ES
import sys

from transformers import AutoModelForRNNT, AutoProcessor
from transformers.audio_utils import load_audio

MODEL_ID = "nvidia/nemotron-3.5-asr-streaming-0.6b"


def main():
    if len(sys.argv) < 2:
        print("Uso: python transcribe.py <archivo_audio> [idioma, ej. es-ES]")
        sys.exit(1)

    audio_path = sys.argv[1]
    language = sys.argv[2] if len(sys.argv) > 2 else "es-ES"

    print("Cargando modelo (la primera vez descarga ~1.2 GB)...")
    processor = AutoProcessor.from_pretrained(MODEL_ID)
    model = AutoModelForRNNT.from_pretrained(MODEL_ID, device_map="auto")

    sr = processor.feature_extractor.sampling_rate
    audio = load_audio(audio_path, sampling_rate=sr)

    inputs = processor(audio, sampling_rate=sr, language=language)
    inputs.to(model.device, dtype=model.dtype)

    print(f"Transcribiendo {audio_path} ({language})...")
    output = model.generate(**inputs, return_dict_in_generate=True)
    print("\n--- Transcripción ---")
    print(processor.decode(output.sequences, skip_special_tokens=True))


if __name__ == "__main__":
    main()
