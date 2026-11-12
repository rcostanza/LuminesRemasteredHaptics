"""Play a 4ch WAV on the DualSense's USB audio device via WASAPI.
  play_dualsense.py --list          show output devices
  play_dualsense.py file.wav [--device "Wireless Controller"]
"""
import argparse, sys, wave
import numpy as np, sounddevice as sd

def find_device(name):
    apis = sd.query_hostapis()
    for i, d in enumerate(sd.query_devices()):
        if name.lower() in d["name"].lower() and d["max_output_channels"] >= 4 \
           and "WASAPI" in apis[d["hostapi"]]["name"]:
            return i
    return None

ap = argparse.ArgumentParser()
ap.add_argument("wav", nargs="?"); ap.add_argument("--list", action="store_true")
ap.add_argument("--device", default="Wireless Controller")
a = ap.parse_args()
if a.list or not a.wav:
    apis = sd.query_hostapis()
    for i, d in enumerate(sd.query_devices()):
        if d["max_output_channels"]:
            print(i, d["name"], "|", apis[d["hostapi"]]["name"], "| out ch:", d["max_output_channels"])
    sys.exit()
dev = find_device(a.device)
if dev is None:
    sys.exit(f"No 4-channel WASAPI output matching '{a.device}'. Is the DualSense on USB? Try --list.")
with wave.open(a.wav) as w:
    sr, ch = w.getframerate(), w.getnchannels()
    pcm = np.frombuffer(w.readframes(w.getnframes()), np.int16).reshape(-1, ch)
print(f"Playing on [{dev}] {sd.query_devices(dev)['name']} ({len(pcm)/sr:.2f}s)")
sd.play(pcm.astype(np.float32) / 32768, sr, device=dev)
sd.wait()
