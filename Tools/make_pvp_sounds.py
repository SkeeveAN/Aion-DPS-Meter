"""Writes Client/assets/sounds/pvp-near.wav and pvp-targeted.wav, the two sounds of the PvP alerts in the Abyss (own work, plain sine tones):
pvp-near = an enemy comes into view: two short falling notes; pvp-targeted = an enemy marks the player: three quick low-high beeps, sharper than anything else the meter plays.
Usage: make_pvp_sounds.py <sounds folder>"""
import math
import os
import struct
import sys
import wave

RATE = 22050


def tone(freq, length, volume=9000, harmonics=0.25):
    n = int(RATE * length)
    out = bytearray()
    for i in range(n):
        env = min(1.0, i / (RATE * 0.008)) * min(1.0, (n - i) / (RATE * 0.02))
        s = math.sin(2 * math.pi * freq * i / RATE) + harmonics * math.sin(4 * math.pi * freq * i / RATE)
        out += struct.pack("<h", int(volume * env * s))
    return out


def silence(length):
    return bytearray(int(RATE * length) * 2)


def write(path, data):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(bytes(data))


folder = sys.argv[1]
near = tone(784.0, 0.13) + silence(0.02) + tone(587.33, 0.22)  # G5, D5: falling
targeted = bytearray()
for _ in range(3):
    targeted += tone(440.0, 0.07, 10000, 0.1) + silence(0.01) + tone(1318.5, 0.07, 10000, 0.1) + silence(0.05)  # A4, E6
write(os.path.join(folder, "pvp-near.wav"), near)
write(os.path.join(folder, "pvp-targeted.wav"), targeted)
