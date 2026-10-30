"""Writes Client/assets/sounds/cube-left.wav: the Hidden Cube "out of sight" sound, three falling soft low notes (the arrival chime rises, this one falls,
so the player hears at once that he is walking away from the cube). Usage: make_cube_left_sound.py <out.wav>"""
import math
import struct
import sys
import wave

RATE = 22050
NOTES = [(523.25, 0.17), (415.30, 0.17), (311.13, 0.30)]  # C5, G#4, D#4: falling, minor-ish

frames = bytearray()
for freq, length in NOTES:
    n = int(RATE * length)
    for i in range(n):
        env = min(1.0, i / (RATE * 0.01)) * math.exp(-4.0 * i / n)
        s = math.sin(2 * math.pi * freq * i / RATE) + 0.3 * math.sin(4 * math.pi * freq * i / RATE)
        frames += struct.pack("<h", int(9000 * env * s))

with wave.open(sys.argv[1], "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes(bytes(frames))
