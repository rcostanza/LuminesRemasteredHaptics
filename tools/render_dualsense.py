"""Render a .bnvib to a 4-channel 48 kHz WAV for the DualSense (haptics on ch3/ch4).

  render_dualsense.py in.bnvib.bytes out.wav [--gain 1.0] [--low-scale 1.0] [--high-scale 1.0]
                      [--fmin 0] [--fmax 1e9] [--side both|left|right]
"""
import argparse, pathlib, struct, sys
import numpy as np
sys.path.insert(0, str(pathlib.Path(__file__).parent)); import bnvib

SR = 48000

def shape_amps(al, ah, curve=1.0):
    """Amplitude curve (<1 lifts quiet detail = 'stronger' feel), then keep aL+aH <= 1 (no clipping)."""
    al, ah = al ** curve, ah ** curve
    s = al + ah
    k = np.where(s > 1.0, 1.0 / np.maximum(s, 1e-9), 1.0)
    return al * k, ah * k

def synth(v: bnvib.Bnvib, gain=1.0, low_scale=1.0, high_scale=1.0, fmin=0.0, fmax=1e9, curve=1.0) -> np.ndarray:
    """Phase-continuous two-band sine synthesis; params linearly interpolated between 200 Hz frames."""
    n = len(v.frames)
    t_frame = (np.arange(n) + 0.5) / v.rate            # frame centres
    t = np.arange(int(np.ceil(n / v.rate * SR))) / SR
    out = np.zeros_like(t)
    al, ah = shape_amps(v.amp_low, v.amp_high, curve)
    for amp, freq, scale in ((al, v.freq_low, low_scale), (ah, v.freq_high, high_scale)):
        f = np.clip(np.interp(t, t_frame, freq * scale), fmin, fmax)
        a = np.interp(t, t_frame, amp)
        # fade the very first/last frame in/out so file edges don't click
        edge = int(SR / v.rate)
        a[:edge] *= np.linspace(0, 1, edge)[: len(a[:edge])]
        a[-edge:] *= np.linspace(1, 0, edge)[-len(a[-edge:]):]
        phase = 2 * np.pi * np.cumsum(f) / SR
        out += a * np.sin(phase)
    return np.clip(out * gain, -1.0, 1.0)

def write_wav4(path, ch3, ch4):
    """16-bit WAVE_FORMAT_EXTENSIBLE, quad layout (FL FR BL BR = mask 0x33)."""
    pcm = np.zeros((len(ch3), 4), np.int16)
    pcm[:, 2] = (ch3 * 32767).astype(np.int16)
    pcm[:, 3] = (ch4 * 32767).astype(np.int16)
    data = pcm.tobytes()
    fmt = struct.pack("<HHIIHHHHIH14s", 0xFFFE, 4, SR, SR * 8, 8, 16, 22, 16, 0x33, 1,
                      b"\x00\x00\x00\x00\x10\x00\x80\x00\x00\xaa\x00\x38\x9b\x71")  # PCM subformat GUID
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 4 + 8 + len(fmt) + 8 + len(data)) + b"WAVE")
        f.write(b"fmt " + struct.pack("<I", len(fmt)) + fmt)
        f.write(b"data" + struct.pack("<I", len(data)) + data)

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("inp"); ap.add_argument("out")
    ap.add_argument("--gain", type=float, default=1.0)
    ap.add_argument("--low-scale", type=float, default=1.0)
    ap.add_argument("--high-scale", type=float, default=1.0)
    ap.add_argument("--fmin", type=float, default=0.0)
    ap.add_argument("--fmax", type=float, default=1e9)
    ap.add_argument("--amp-curve", type=float, default=0.6)  # tuned 2026-11-11 by feel
    ap.add_argument("--side", choices=["both", "left", "right"], default="both")
    a = ap.parse_args()
    v = bnvib.load(a.inp)
    s = synth(v, a.gain, a.low_scale, a.high_scale, a.fmin, a.fmax, a.amp_curve)
    z = np.zeros_like(s)
    write_wav4(a.out, s if a.side != "right" else z, s if a.side != "left" else z)
    print(f"{a.out}: {v.duration:.2f}s, peak {np.abs(s).max():.2f}")
