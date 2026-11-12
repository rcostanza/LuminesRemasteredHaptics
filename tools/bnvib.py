"""Shared .bnvib decoder (output-agnostic: used by DualSense and future Joy-Con backends).

Layout (little endian), per Wexos's wiki & verified on all 1265 Lumines files:
  u32 metaSize; u16 format(=3); u16 rate(=200); [u32 loopStart; u32 loopEnd; [u32 loopInterval]]
  u32 dataSize; dataSize/4 frames of bytes [ampLow, freqLow, ampHigh, freqHigh]
  amplitude = B/255, frequency = 10 * 2**(B/32) Hz
"""
import struct
from dataclasses import dataclass
import numpy as np

@dataclass
class Bnvib:
    rate: int
    frames: np.ndarray           # uint8 [n,4] raw
    loop_start: int | None = None
    loop_end: int | None = None
    loop_interval: int | None = None

    @property
    def amp_low(self):  return self.frames[:, 0] / 255.0
    @property
    def freq_low(self): return 10.0 * 2.0 ** (self.frames[:, 1] / 32.0)
    @property
    def amp_high(self): return self.frames[:, 2] / 255.0
    @property
    def freq_high(self): return 10.0 * 2.0 ** (self.frames[:, 3] / 32.0)
    @property
    def duration(self): return len(self.frames) / self.rate

def parse(data: bytes) -> Bnvib:
    meta_size, fmt, rate = struct.unpack_from("<IHH", data, 0)
    if fmt != 3:
        raise ValueError(f"unsupported bnvib format {fmt}")
    ls = le = li = None
    if meta_size >= 0xC:
        ls, le = struct.unpack_from("<II", data, 8)
    if meta_size >= 0x10:
        (li,) = struct.unpack_from("<I", data, 16)
    off = 4 + meta_size
    (size,) = struct.unpack_from("<I", data, off)
    frames = np.frombuffer(data, np.uint8, size, off + 4).reshape(-1, 4)
    return Bnvib(rate, frames, ls, le, li)

def load(path) -> Bnvib:
    with open(path, "rb") as f:
        return parse(f.read())
