#!/usr/bin/env python3
"""
Orpheus TTS via LM Studio + SNAC decoder.

Usage:
    python3 orpheus_tts.py <text> <output.mp3> [voice] [lm_studio_endpoint] [model]

Output:
    MP3 file at <output.mp3>

Dependencies:
    pip install openai torch snac

Prompt format:
    <custom_token_3><|begin_of_text|>{voice}: {text}<|eot_id|><custom_token_4><custom_token_5><custom_token_1>

The model outputs <custom_token_XXXXX> strings. Each is decoded as:
    code = int(N) - 10 - ((index % 7) * 4096)
grouped in 7-token frames → SNAC decoder → PCM 24kHz/16-bit/mono → MP3 via ffmpeg.
"""

import sys
import os
import struct
import subprocess
import tempfile
import json
import re
import urllib.request


def build_prompt(text: str, voice: str) -> str:
    return (
        f"<custom_token_3><|begin_of_text|>{voice}: {text}"
        f"<|eot_id|><custom_token_4><custom_token_5><custom_token_1>"
    )


def turn_token_into_id(token_string: str, index: int) -> int | None:
    """Extract SNAC code from a <custom_token_N> string using the index-based formula."""
    start = token_string.rfind("<custom_token_")
    if start == -1:
        return None
    end = token_string.find(">", start)
    if end == -1:
        return None
    try:
        raw_id = int(token_string[start + len("<custom_token_"):end])
        return raw_id - 10 - ((index % 7) * 4096)
    except ValueError:
        return None


def call_lm_studio_streaming(text: str, voice: str, endpoint: str, model: str) -> list[int]:
    """Stream completions from LM Studio and collect SNAC token IDs."""
    prompt = build_prompt(text, voice)
    payload = json.dumps({
        "model": model,
        "prompt": prompt,
        "max_tokens": 2000,
        "temperature": 0.6,
        "top_p": 0.9,
        "repeat_penalty": 1.1,
        "stream": True,
    }).encode()

    url = f"{endpoint}/v1/completions"
    req = urllib.request.Request(url, data=payload,
                                  headers={"Content-Type": "application/json"})

    token_ids: list[int] = []
    idx = 0

    with urllib.request.urlopen(req, timeout=120) as resp:
        for raw_line in resp:
            line = raw_line.decode("utf-8").strip()
            if not line.startswith("data: "):
                continue
            data_str = line[len("data: "):]
            if data_str == "[DONE]":
                break
            try:
                chunk = json.loads(data_str)
                token_text = chunk["choices"][0].get("text", "")
            except (json.JSONDecodeError, KeyError, IndexError):
                continue

            if "<custom_token_" not in token_text:
                continue

            code = turn_token_into_id(token_text, idx)
            if code is not None and 0 <= code < 4096:
                token_ids.append(code)
                idx += 1

    return token_ids


def decode_snac_tokens(token_ids: list[int]) -> bytes:
    """
    Decode SNAC codes to raw PCM bytes (24kHz, 16-bit, mono).

    7-token frame layout: [c0, c1a, c2a, c2b, c1b, c2c, c2d]
    Maps to SNAC layers: 1 coarse, 2 mid, 4 fine per frame.
    """
    import torch
    from snac import SNAC

    if len(token_ids) < 7:
        raise ValueError(f"Too few SNAC tokens: {len(token_ids)} (need ≥7)")

    # Trim to a multiple of 7
    token_ids = token_ids[: (len(token_ids) // 7) * 7]

    layer_0, layer_1, layer_2 = [], [], []
    for i in range(0, len(token_ids), 7):
        layer_0.append(token_ids[i])
        layer_1.append(token_ids[i + 1])
        layer_2.append(token_ids[i + 2])
        layer_2.append(token_ids[i + 3])
        layer_1.append(token_ids[i + 4])
        layer_2.append(token_ids[i + 5])
        layer_2.append(token_ids[i + 6])

    snac_model = SNAC.from_pretrained("hubertsiuzdak/snac_24khz").eval()

    codes = [
        torch.tensor([layer_0], dtype=torch.long),
        torch.tensor([layer_1], dtype=torch.long),
        torch.tensor([layer_2], dtype=torch.long),
    ]

    with torch.no_grad():
        audio = snac_model.decode(codes)

    audio_np = audio.squeeze().cpu().numpy()
    audio_np = audio_np.clip(-1.0, 1.0)
    return (audio_np * 32767).astype("int16").tobytes()


def wrap_pcm_as_wav(pcm_bytes: bytes, sample_rate: int = 24000,
                    bits_per_sample: int = 16, channels: int = 1) -> bytes:
    byte_rate = sample_rate * channels * bits_per_sample // 8
    block_align = channels * bits_per_sample // 8
    data_size = len(pcm_bytes)
    header = struct.pack(
        "<4sI4s4sIHHIIHH4sI",
        b"RIFF", 36 + data_size, b"WAVE",
        b"fmt ", 16, 1, channels, sample_rate, byte_rate, block_align, bits_per_sample,
        b"data", data_size,
    )
    return header + pcm_bytes


def find_ffmpeg() -> str:
    for candidate in ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg"]:
        if candidate == "ffmpeg" or os.path.exists(candidate):
            return candidate
    raise FileNotFoundError("ffmpeg not found. Install via: brew install ffmpeg")


def convert_wav_to_mp3(wav_bytes: bytes, output_path: str) -> None:
    ffmpeg = find_ffmpeg()
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(wav_bytes)
        tmp_path = tmp.name
    try:
        result = subprocess.run(
            [ffmpeg, "-y", "-i", tmp_path, "-codec:a", "libmp3lame", "-q:a", "2", output_path],
            capture_output=True, text=True,
        )
        if result.returncode != 0:
            raise RuntimeError(f"ffmpeg failed: {result.stderr[:300]}")
    finally:
        os.unlink(tmp_path)


def main():
    if len(sys.argv) < 3:
        print("Usage: orpheus_tts.py <text> <output.mp3> [voice] [lm_studio_endpoint] [model]",
              file=sys.stderr)
        sys.exit(1)

    text = sys.argv[1]
    output_path = sys.argv[2]
    voice = sys.argv[3] if len(sys.argv) > 3 else "tara"
    endpoint = sys.argv[4] if len(sys.argv) > 4 else "http://localhost:1234"
    model = sys.argv[5] if len(sys.argv) > 5 else "orpheus-3b-0.1-ft"

    print(f"[orpheus_tts] voice={voice}, model={model}, text={text[:60]}", file=sys.stderr)

    token_ids = call_lm_studio_streaming(text, voice, endpoint, model)
    print(f"[orpheus_tts] got {len(token_ids)} SNAC codes", file=sys.stderr)

    if len(token_ids) < 7:
        raise RuntimeError(
            f"Not enough SNAC codes ({len(token_ids)}). "
            "Ensure orpheus-3b-0.1-ft is loaded in LM Studio."
        )

    pcm_bytes = decode_snac_tokens(token_ids)
    print(f"[orpheus_tts] decoded {len(pcm_bytes)} PCM bytes", file=sys.stderr)

    wav_bytes = wrap_pcm_as_wav(pcm_bytes)
    convert_wav_to_mp3(wav_bytes, output_path)
    print(f"[orpheus_tts] saved MP3 → {output_path}", file=sys.stderr)


if __name__ == "__main__":
    main()
