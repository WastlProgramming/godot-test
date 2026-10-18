#!/usr/bin/env python3
"""JumpRun audio generator (CONTRACT_V2 §6.1).

Synthesises the background music and all sound effects with the Python standard library only
(no numpy, no external assets). Output: mono, 22 050 Hz, 16-bit PCM WAV files in art/audio/.

Usage (from the project root):
    python3 tools/gen_audio.py            # generate everything
    python3 tools/gen_audio.py --verify   # generate, then run the checks in tools/verify_audio.py

The output is deterministic: every random source uses a fixed seed, so two runs produce identical bytes.

Music: friendly chiptune loop in C major, 105 BPM, 32 bars (= 73.14 s). It is rendered on a *circular*
buffer: note tails and echoes that run past the end wrap around to the beginning, so the loop point is
seamless without any fade.
"""
from __future__ import annotations

import math
import os
import random
import struct
import sys
import wave
from array import array

SR = 22050
TARGET_PEAK_DBFS = -3.0
TARGET_PEAK = 10 ** (TARGET_PEAK_DBFS / 20.0)  # ~0.708
TWO_PI = 2.0 * math.pi

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.environ.get("JUMPRUN_AUDIO_OUT") or os.path.join(ROOT, "art", "audio")


# ------------------------------------------------------------------------------------------------
# Helpers
# ------------------------------------------------------------------------------------------------

def midi_hz(n: float) -> float:
    return 440.0 * 2.0 ** ((n - 69) / 12.0)


def buf(n: int) -> array:
    return array("d", bytes(8 * n))


def remove_dc(x: array) -> None:
    if not x:
        return
    m = sum(x) / len(x)
    for i in range(len(x)):
        x[i] -= m


def normalize(x: array, peak: float = TARGET_PEAK) -> None:
    p = max(abs(v) for v in x) if x else 0.0
    if p <= 1e-12:
        return
    g = peak / p
    for i in range(len(x)):
        x[i] *= g


def edge_fades(x: array, fade_in_s: float = 0.002, fade_out_s: float = 0.012) -> None:
    """Click protection for one-shots: short raised-cosine fade at both ends, ends exactly at 0."""
    n = len(x)
    fi = max(1, int(fade_in_s * SR))
    fo = max(1, int(fade_out_s * SR))
    for i in range(min(fi, n)):
        x[i] *= 0.5 - 0.5 * math.cos(math.pi * i / fi)
    for i in range(min(fo, n)):
        x[n - 1 - i] *= 0.5 - 0.5 * math.cos(math.pi * i / fo)


def write_wav(name: str, x: array) -> str:
    path = os.path.join(OUT_DIR, name + ".wav")
    frames = array("h", bytes(2 * len(x)))
    for i, v in enumerate(x):
        s = int(round(v * 32767.0))
        if s > 32767:
            s = 32767
        elif s < -32768:
            s = -32768
        frames[i] = s
    if sys.byteorder != "little":
        frames.byteswap()
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(frames.tobytes())
    return path


class OnePole:
    """Simple one-pole low-pass; hp() gives the complementary high-pass."""

    def __init__(self, cutoff_hz: float):
        self.set(cutoff_hz)
        self.z = 0.0

    def set(self, cutoff_hz: float) -> None:
        self.a = 1.0 - math.exp(-TWO_PI * max(1.0, cutoff_hz) / SR)

    def lp(self, v: float) -> float:
        self.z += self.a * (v - self.z)
        return self.z

    def hp(self, v: float) -> float:
        return v - self.lp(v)


class SVF:
    """Chamberlin state-variable filter (band-pass / low-pass), cutoff can be modulated per sample."""

    def __init__(self, q: float = 0.7):
        self.low = 0.0
        self.band = 0.0
        self.damp = 1.0 / max(0.5, q)

    def process(self, v: float, cutoff_hz: float) -> tuple[float, float]:
        f = 2.0 * math.sin(math.pi * min(cutoff_hz, SR * 0.2) / SR)
        self.low += f * self.band
        high = v - self.low - self.damp * self.band
        self.band += f * high
        return self.low, self.band


# ------------------------------------------------------------------------------------------------
# Band-limited wavetables (additive, only harmonics below Nyquist -> no aliasing)
# ------------------------------------------------------------------------------------------------

TABLE_SIZE = 2048
_tables: dict[tuple[str, int], array] = {}


def wavetable(kind: str, midi: int) -> array:
    key = (kind, midi)
    t = _tables.get(key)
    if t is not None:
        return t
    f0 = midi_hz(midi)
    max_h = max(1, int((SR * 0.45) / f0))
    t = buf(TABLE_SIZE)
    harmonics: list[tuple[int, float]] = []
    for h in range(1, max_h + 1):
        if kind == "tri":
            if h % 2 == 1:
                harmonics.append((h, (1.0 if (h // 2) % 2 == 0 else -1.0) / (h * h)))
        elif kind == "square":
            if h % 2 == 1:
                harmonics.append((h, 1.0 / h))
        elif kind == "pulse25":
            # pulse with 25 % duty: amplitude sin(pi*h*d)/h
            harmonics.append((h, math.sin(math.pi * h * 0.25) / h))
        elif kind == "soft":  # mellow organ-like
            if h <= 6:
                harmonics.append((h, 1.0 / (h * h)))
        else:
            raise ValueError(kind)
    # gentle roll-off of the top harmonics (smoother timbre)
    for i in range(TABLE_SIZE):
        ph = TWO_PI * i / TABLE_SIZE
        s = 0.0
        for h, a in harmonics:
            roll = 1.0 / (1.0 + (h * f0 / 6000.0) ** 2)
            s += a * roll * math.sin(h * ph)
        t[i] = s
    p = max(abs(v) for v in t)
    for i in range(TABLE_SIZE):
        t[i] /= p
    _tables[key] = t
    return t


# ------------------------------------------------------------------------------------------------
# Music
# ------------------------------------------------------------------------------------------------

BPM = 105
BEAT = SR * 60 // BPM          # 12600 samples, exact
assert BEAT * BPM == SR * 60
BAR = BEAT * 4                 # 50400 samples = 2.2857 s
BARS = 32
MUSIC_LEN = BAR * BARS         # 1 612 800 samples = 73.14 s

# chord symbol -> MIDI pitch classes (root first)
CHORDS = {
    "C": (60, 64, 67), "G": (55, 59, 62), "Am": (57, 60, 64), "F": (53, 57, 60),
    "Em": (52, 55, 59), "Dm": (50, 53, 57),
}

PROGRESSION = (
    ["C", "G", "Am", "F"] * 2 +          # A   (bars 0-7)
    ["Am", "F", "C", "G"] * 2 +          # B   (bars 8-15)
    ["C", "G", "Am", "F"] * 2 +          # A2  (bars 16-23)
    ["F", "G", "Em", "Am", "F", "G", "C", "G"]  # C / bridge + turnaround (bars 24-31)
)
assert len(PROGRESSION) == BARS

# melody phrases: (beat, length in beats, MIDI note); per bar
N = {"G4": 67, "A4": 69, "B4": 71, "C5": 72, "D5": 74, "E5": 76, "F5": 77, "G5": 79, "A5": 81,
     "B5": 83, "C6": 84, "D6": 86}


def ph(*notes):
    return [(b, d, N[n]) for (b, d, n) in notes]


MEL_A = [
    ph((0, 1, "E5"), (1, .5, "G5"), (1.5, .5, "E5"), (2, 1, "D5"), (3, 1, "C5")),
    ph((0, 1.5, "D5"), (1.5, .5, "B4"), (2, 1, "G4"), (3, .5, "B4"), (3.5, .5, "D5")),
    ph((0, 1, "C5"), (1, 1, "E5"), (2, 1.5, "A5"), (3.5, .5, "G5")),
    ph((0, 1, "F5"), (1, .5, "E5"), (1.5, .5, "D5"), (2, 2, "C5")),
]
MEL_A_END = ph((0, .5, "F5"), (.5, .5, "G5"), (1, 1, "A5"), (2, 1, "G5"), (3, 1, "E5"))

MEL_B = [
    ph((0, .5, "A4"), (.5, .5, "C5"), (1, 1, "E5"), (2, .5, "D5"), (2.5, .5, "C5"), (3, 1, "E5")),
    ph((0, 1.5, "F5"), (1.5, .5, "E5"), (2, 1, "C5"), (3, 1, "A4")),
    ph((0, .5, "G4"), (.5, .5, "C5"), (1, .5, "E5"), (1.5, 1.5, "G5"), (3, 1, "E5")),
    ph((0, 2, "D5"), (2.5, .5, "B4"), (3, .5, "D5"), (3.5, .5, "G5")),
]
MEL_B_END = ph((0, 1, "D5"), (1, 1, "F5"), (2, 1.5, "G5"), (3.5, .5, "B4"))

MEL_C = [
    ph((0, 2, "A5"), (2, 1, "G5"), (3, 1, "F5")),
    ph((0, 3, "G5"), (3, 1, "D5")),
    ph((0, 2, "E5"), (2, 1, "G5"), (3, 1, "B5")),
    ph((0, 4, "A5")),
    ph((0, 1, "F5"), (1, 1, "A5"), (2, 2, "C6")),
    ph((0, 2, "B5"), (2, 1, "G5"), (3, 1, "D5")),
    ph((0, 4, "E5")),
    ph((2, .5, "G4"), (2.5, .5, "A4"), (3, .5, "B4"), (3.5, .5, "D5")),
]


C_MAJOR = (0, 2, 4, 5, 7, 9, 11)


def diatonic_third_below(note: int) -> int:
    """Two C-major scale steps below `note` (a diatonic third)."""
    steps = 0
    n = note
    while steps < 2:
        n -= 1
        if n % 12 in C_MAJOR:
            steps += 1
    return n


def melody_for_bar(bar: int):
    if bar < 8:
        return MEL_A_END if bar == 7 else MEL_A[bar % 4]
    if bar < 16:
        return MEL_B_END if bar == 15 else MEL_B[bar % 4]
    if bar < 24:
        return MEL_A_END if bar == 23 else MEL_A[bar % 4]
    return MEL_C[bar - 24]


def render_note(dst: array, start: int, length: int, midi: int, kind: str, gain: float,
                attack: float, decay: float, sustain: float, release: float,
                vibrato: float = 0.0, lp_hz: float = 0.0) -> None:
    """Adds one enveloped wavetable note into the circular buffer `dst`."""
    table = wavetable(kind, midi)
    n_total = len(dst)
    f0 = midi_hz(midi)
    inc = f0 * TABLE_SIZE / SR
    a_n = max(1, int(attack * SR))
    d_n = max(1, int(decay * SR))
    r_n = max(1, int(release * SR))
    hold = max(length, a_n)
    total = hold + r_n
    phase = 0.0
    filt = OnePole(lp_hz) if lp_hz > 0 else None
    vib_inc = TWO_PI * 5.5 / SR
    vib_delay = int(0.18 * SR)
    level = 0.0
    for i in range(total):
        if i < a_n:
            env = i / a_n
        elif i < a_n + d_n:
            env = 1.0 - (1.0 - sustain) * (i - a_n) / d_n
        else:
            env = sustain
        if i >= hold:
            if i == hold:
                level = env
            env = level * (1.0 - (i - hold) / r_n)
        s = table[int(phase) & (TABLE_SIZE - 1)]
        if filt is not None:
            s = filt.lp(s)
        dst[(start + i) % n_total] += s * env * gain
        if vibrato > 0.0 and i > vib_delay:
            phase += inc * (1.0 + vibrato * math.sin(vib_inc * i))
        else:
            phase += inc
        if phase >= TABLE_SIZE:
            phase -= TABLE_SIZE


def make_kick() -> array:
    n = int(0.28 * SR)
    x = buf(n)
    ph_ = 0.0
    for i in range(n):
        t = i / SR
        f = 45.0 + 85.0 * math.exp(-t * 28.0)
        ph_ += TWO_PI * f / SR
        env = math.exp(-t * 11.0)
        click = math.exp(-t * 400.0) * 0.3
        x[i] = math.sin(ph_) * env + click * math.sin(ph_ * 3)
    edge_fades(x, 0.001, 0.02)
    return x


def make_snare(rng: random.Random) -> array:
    n = int(0.18 * SR)
    x = buf(n)
    hp = OnePole(1800.0)
    lp = OnePole(7000.0)
    ph_ = 0.0
    for i in range(n):
        t = i / SR
        noise = lp.lp(hp.hp(rng.uniform(-1, 1)))
        ph_ += TWO_PI * 190.0 / SR
        tone = math.sin(ph_) * math.exp(-t * 35.0)
        x[i] = (noise * 1.4 * math.exp(-t * 22.0)) + tone * 0.6
    edge_fades(x, 0.001, 0.02)
    return x


def make_hat(rng: random.Random, length: float) -> array:
    n = int(length * SR)
    x = buf(n)
    hp = OnePole(6000.0)
    for i in range(n):
        t = i / SR
        x[i] = hp.hp(rng.uniform(-1, 1)) * math.exp(-t * (6.0 / length))
    edge_fades(x, 0.0005, 0.005)
    return x


def mix_in(dst: array, src: array, start: int, gain: float) -> None:
    n = len(dst)
    for i, v in enumerate(src):
        dst[(start + i) % n] += v * gain


def gen_music() -> array:
    rng = random.Random(1105)
    L = MUSIC_LEN
    lead = buf(L)
    harm = buf(L)
    bass = buf(L)
    drums = buf(L)

    kick = make_kick()
    snare = make_snare(rng)
    hat_c = make_hat(rng, 0.045)
    hat_o = make_hat(rng, 0.16)
    six = BEAT // 4  # sixteenth = 3150

    for bar, chord_name in enumerate(PROGRESSION):
        b0 = bar * BAR
        chord = CHORDS[chord_name]
        section = "A" if bar < 8 else "B" if bar < 16 else "A2" if bar < 24 else "C"

        # ---- lead melody ------------------------------------------------------------------
        for beat, dur, note in melody_for_bar(bar):
            st = b0 + int(beat * BEAT)
            ln = int(dur * BEAT * 0.92)
            render_note(lead, st, ln, note, "pulse25", 0.30, 0.008, 0.12, 0.65, 0.09,
                        vibrato=0.004, lp_hz=4200.0)
            if section == "A2":
                # soft counter-voice a sixth below (third an octave lower) for the reprise
                render_note(harm, st, ln, diatonic_third_below(note), "soft", 0.10, 0.01, 0.1, 0.7, 0.08)

        # ---- harmony: pad or arpeggio ------------------------------------------------------
        if section in ("A", "C"):
            for k, p in enumerate(chord):
                render_note(harm, b0, int(BAR * 0.96), p, "soft", 0.075, 0.12, 0.4, 0.75, 0.25)
        else:
            arp = [chord[0], chord[1], chord[2], chord[0] + 12, chord[2], chord[1], chord[0] + 12, chord[2]]
            for s16 in range(16):
                p = arp[s16 % 8] + 12
                render_note(harm, b0 + s16 * six, int(six * 0.55), p, "square", 0.055,
                            0.003, 0.05, 0.4, 0.03, lp_hz=3000.0)

        # ---- bass -------------------------------------------------------------------------------
        root = chord[0] - 24 if chord[0] >= 57 else chord[0] - 12
        root = root if root >= 40 else root + 12  # keep within E2..D#3
        if section == "C":
            pattern = [(0, 2, 0), (2, 1.5, 7), (3.5, .5, 12)]
        else:
            pattern = [(0, .5, 0), (.5, .5, 0), (1, .5, 12), (1.5, .5, 0),
                       (2, .5, 7), (2.5, .5, 0), (3, .5, 12), (3.5, .5, 7)]
        for beat, dur, off in pattern:
            render_note(bass, b0 + int(beat * BEAT), int(dur * BEAT * 0.85), root + off, "tri",
                        0.42, 0.004, 0.08, 0.8, 0.04)

        # ---- drums ------------------------------------------------------------------------------
        if section == "C":
            mix_in(drums, kick, b0, 0.75)
            mix_in(drums, kick, b0 + 2 * BEAT + 2 * six, 0.45)
            mix_in(drums, snare, b0 + 2 * BEAT, 0.28)
            for e in range(8):
                mix_in(drums, hat_c, b0 + e * 2 * six, 0.10 if e % 2 else 0.14)
        else:
            mix_in(drums, kick, b0, 0.85)
            mix_in(drums, kick, b0 + 2 * BEAT, 0.8)
            mix_in(drums, kick, b0 + 2 * BEAT + 2 * six, 0.45)
            mix_in(drums, snare, b0 + BEAT, 0.42)
            mix_in(drums, snare, b0 + 3 * BEAT, 0.42)
            steps = 16 if section == "B" else 8
            for e in range(steps):
                step = e * (16 // steps)
                g = 0.13 if step % 4 == 2 else 0.08
                mix_in(drums, hat_c, b0 + step * six, g)
            if bar % 4 == 3:
                mix_in(drums, hat_o, b0 + 3 * BEAT + 2 * six, 0.12)
        if bar == BARS - 1:  # fill into the loop start
            for s16 in range(12, 16):
                mix_in(drums, snare, b0 + s16 * six, 0.18 + 0.06 * (s16 - 12))

    # ---- lead echo (circular dotted-eighth delay -> wraps seamlessly) ----------------------------
    delay = BEAT * 3 // 4
    echo = buf(L)
    for k, g in ((1, 0.28), (2, 0.13), (3, 0.06)):
        off = k * delay
        for i in range(L):
            echo[i] += lead[(i - off) % L] * g
    lp = OnePole(2500.0)
    # filter the echo circularly: run one warm-up lap, then the real lap
    for i in range(L):
        lp.lp(echo[i])
    for i in range(L):
        echo[i] = lp.lp(echo[i])

    mix = buf(L)
    for i in range(L):
        mix[i] = lead[i] + echo[i] + harm[i] + bass[i] + drums[i]
    remove_dc(mix)
    # gentle soft-clip safety (tanh) before normalising: keeps transients round
    peak = max(abs(v) for v in mix)
    if peak > 0:
        for i in range(L):
            mix[i] = math.tanh(1.2 * mix[i] / peak)
    remove_dc(mix)
    normalize(mix)
    return mix


# ------------------------------------------------------------------------------------------------
# SFX
# ------------------------------------------------------------------------------------------------

def sfx_jump() -> array:
    n = int(0.20 * SR)
    x = buf(n)
    ph_ = 0.0
    for i in range(n):
        t = i / SR
        f = 260.0 + 520.0 * (1.0 - math.exp(-t * 18.0))
        ph_ += TWO_PI * f / SR
        env = math.exp(-t * 14.0) * min(1.0, t / 0.004)
        x[i] = (math.sin(ph_) + 0.35 * math.sin(2 * ph_) + 0.15 * math.sin(3 * ph_)) * env
    return x


def sfx_land(rng: random.Random) -> array:
    n = int(0.22 * SR)
    x = buf(n)
    ph_ = 0.0
    lp = OnePole(900.0)
    for i in range(n):
        t = i / SR
        f = 48.0 + 60.0 * math.exp(-t * 30.0)
        ph_ += TWO_PI * f / SR
        thump = math.sin(ph_) * math.exp(-t * 18.0)
        dust = lp.lp(rng.uniform(-1, 1)) * math.exp(-t * 35.0) * 1.6
        x[i] = thump + dust
    return x


def sfx_swing(rng: random.Random) -> array:
    n = int(0.28 * SR)
    x = buf(n)
    f = SVF(q=2.0)
    for i in range(n):
        t = i / n
        cutoff = 500.0 + 2600.0 * math.sin(math.pi * t) ** 1.5
        _, band = f.process(rng.uniform(-1, 1), cutoff)
        env = math.sin(math.pi * t) ** 2
        x[i] = band * env
    return x


def sfx_crate_break(rng: random.Random) -> array:
    n = int(0.55 * SR)
    x = buf(n)
    hp = OnePole(400.0)
    # main crack
    for i in range(n):
        t = i / SR
        x[i] += hp.hp(rng.uniform(-1, 1)) * math.exp(-t * 16.0) * 0.9
    # wooden body resonances (several splinters)
    for k in range(6):
        st = int(rng.uniform(0.0, 0.18) * SR) if k else 0
        f0 = rng.uniform(140.0, 420.0)
        dec = rng.uniform(18.0, 40.0)
        amp = 0.9 if k == 0 else rng.uniform(0.25, 0.55)
        for i in range(st, n):
            t = (i - st) / SR
            e = math.exp(-t * dec)
            if e < 1e-4:
                break
            x[i] += amp * e * (math.sin(TWO_PI * f0 * t) + 0.5 * math.sin(TWO_PI * f0 * 2.3 * t))
    # small debris ticks
    for k in range(10):
        st = int(rng.uniform(0.05, 0.45) * SR)
        a = rng.uniform(0.1, 0.3)
        for i in range(st, min(n, st + int(0.012 * SR))):
            x[i] += rng.uniform(-a, a) * (1.0 - (i - st) / (0.012 * SR))
    return x


def tone_seq(notes, total: float, kind: str = "bell", gain_decay: float = 7.0) -> array:
    n = int(total * SR)
    x = buf(n)
    for start, midi, amp in notes:
        f = midi_hz(midi)
        st = int(start * SR)
        for i in range(st, n):
            t = (i - st) / SR
            e = math.exp(-t * gain_decay) * min(1.0, t / 0.004) * amp
            if e < 1e-5 and t > 0.01:
                break
            if kind == "bell":
                s = math.sin(TWO_PI * f * t) + 0.3 * math.sin(TWO_PI * f * 2.0 * t) * math.exp(-t * 6) \
                    + 0.12 * math.sin(TWO_PI * f * 3.01 * t) * math.exp(-t * 10)
            elif kind == "square":
                s = sum(math.sin(TWO_PI * f * h * t) / h for h in (1, 3, 5, 7) if f * h < SR * 0.45)
            else:
                s = math.sin(TWO_PI * f * t)
            x[i] += s * e
    return x


def sfx_pickup() -> array:
    return tone_seq([(0.0, 84, 0.8), (0.06, 88, 0.8), (0.12, 91, 1.0)], 0.45, "bell", 9.0)


def sfx_bridge_place(rng: random.Random) -> array:
    n = int(0.5 * SR)
    x = buf(n)
    for k, (st_s, f0) in enumerate(((0.0, 210.0), (0.11, 240.0), (0.22, 270.0))):
        st = int(st_s * SR)
        lp = OnePole(2500.0)
        for i in range(st, n):
            t = (i - st) / SR
            e = math.exp(-t * 28.0)
            if e < 1e-4:
                break
            knock = math.sin(TWO_PI * f0 * t) + 0.4 * math.sin(TWO_PI * f0 * 2.7 * t)
            x[i] += (knock + lp.lp(rng.uniform(-1, 1)) * 0.6 * math.exp(-t * 60)) * e * (0.8 + 0.1 * k)
    return x


def sfx_trampoline_place(rng: random.Random) -> array:
    n = int(0.40 * SR)
    x = buf(n)
    ph_ = 0.0
    lp = OnePole(700.0)
    for i in range(n):
        t = i / SR
        thunk = math.sin(TWO_PI * 85.0 * t) * math.exp(-t * 25.0)
        f = 220.0 + 90.0 * math.sin(TWO_PI * 18.0 * t) * math.exp(-t * 6)
        ph_ += TWO_PI * f / SR
        spring = math.sin(ph_) * math.exp(-t * 9.0) * 0.45 * min(1.0, t / 0.03)
        x[i] = thunk + spring + lp.lp(rng.uniform(-1, 1)) * math.exp(-t * 40) * 0.6
    return x


def sfx_trampoline_bounce() -> array:
    n = int(0.55 * SR)
    x = buf(n)
    ph_ = 0.0
    for i in range(n):
        t = i / SR
        f = 160.0 + 420.0 * (1.0 - math.exp(-t * 9.0)) + 35.0 * math.sin(TWO_PI * 14.0 * t)
        ph_ += TWO_PI * f / SR
        env = math.exp(-t * 5.5) * min(1.0, t / 0.005)
        x[i] = (math.sin(ph_) + 0.25 * math.sin(2 * ph_)) * env
    return x


def sfx_grapple_shoot(rng: random.Random) -> array:
    n = int(0.30 * SR)
    x = buf(n)
    ph_ = 0.0
    f_svf = SVF(q=3.0)
    for i in range(n):
        t = i / SR
        f = 1400.0 * math.exp(-t * 7.0) + 300.0
        ph_ += TWO_PI * f / SR
        zip_ = (math.sin(ph_) + 0.3 * math.sin(3 * ph_)) * 0.45
        _, band = f_svf.process(rng.uniform(-1, 1), 2500.0 + 1500.0 * math.exp(-t * 10))
        env = math.exp(-t * 9.0) * min(1.0, t / 0.003)
        x[i] = (zip_ + band * 0.8) * env
    return x


def sfx_grapple_attach() -> array:
    n = int(0.45 * SR)
    x = buf(n)
    partials = ((620.0, 1.0, 10.0), (1711.0, 0.55, 14.0), (3348.0, 0.3, 20.0), (930.0, 0.4, 12.0))
    for i in range(n):
        t = i / SR
        s = 0.0
        for f, a, d in partials:
            s += a * math.sin(TWO_PI * f * t) * math.exp(-t * d)
        x[i] = s * min(1.0, t / 0.002)
    return x


def heli_sample(t: float, chop_phase: float, hum_phase: float, noise: float) -> float:
    chop = 0.5 + 0.5 * math.cos(chop_phase)
    chop = chop ** 6  # sharp "whup" pulses
    hum = math.sin(hum_phase) + 0.5 * math.sin(2 * hum_phase) + 0.25 * math.sin(3 * hum_phase)
    return noise * (0.25 + 1.4 * chop) + hum * 0.35 * (0.6 + 0.4 * chop)


def sfx_helicopter_loop(rng: random.Random) -> array:
    """1.5 s, seamless: chop 12 Hz (18 cycles), hum 48 Hz (72 cycles), noise filtered circularly."""
    L = int(1.5 * SR)  # 33075
    raw = [rng.uniform(-1, 1) for _ in range(L)]
    lp = OnePole(900.0)
    hp = OnePole(120.0)
    noise = buf(L)
    for lap in range(2):  # lap 0 = warm-up, lap 1 = periodic steady state
        for i in range(L):
            v = hp.hp(lp.lp(raw[i]))
            if lap == 1:
                noise[i] = v
    x = buf(L)
    for i in range(L):
        t = i / SR
        x[i] = heli_sample(t, TWO_PI * 12.0 * t, TWO_PI * 48.0 * t, noise[i] * 2.2)
    remove_dc(x)
    return x


def sfx_helicopter_start(rng: random.Random) -> array:
    n = int(1.3 * SR)
    x = buf(n)
    lp = OnePole(900.0)
    hp = OnePole(120.0)
    chop_ph = 0.0
    hum_ph = 0.0
    for i in range(n):
        t = i / SR
        k = min(1.0, t / 1.1)
        spin = k * k * (3 - 2 * k)  # smoothstep spin-up
        chop_ph += TWO_PI * (2.0 + 10.0 * spin) / SR
        hum_ph += TWO_PI * (20.0 + 28.0 * spin) / SR
        noise = hp.hp(lp.lp(rng.uniform(-1, 1))) * 2.2
        env = (0.25 + 0.75 * spin) * min(1.0, t / 0.02)
        x[i] = heli_sample(t, chop_ph, hum_ph, noise) * env
        # starter whine
        x[i] += math.sin(TWO_PI * (300.0 + 500.0 * spin) * t) * 0.12 * (1.0 - spin)
    # last 0.2 s fade so it blends into the loop that starts in parallel
    fade = int(0.2 * SR)
    for i in range(fade):
        x[n - fade + i] *= 1.0 - i / fade
    return x


def sfx_respawn() -> array:
    notes = [(0.00, 72, 0.7), (0.07, 76, 0.7), (0.14, 79, 0.7), (0.21, 84, 0.8), (0.28, 88, 0.6),
             (0.35, 91, 0.5)]
    x = tone_seq(notes, 0.95, "bell", 5.0)
    # shimmering high sine sweep
    for i in range(len(x)):
        t = i / SR
        x[i] += 0.12 * math.sin(TWO_PI * (1500.0 + 1500.0 * t) * t) * math.sin(math.pi * min(1.0, t / 0.9))
    return x


def sfx_checkpoint() -> array:
    notes = [(0.00, 72, 0.7), (0.10, 76, 0.7), (0.20, 79, 0.75), (0.32, 84, 1.0), (0.32, 79, 0.4),
             (0.32, 76, 0.3)]
    return tone_seq(notes, 1.05, "square", 4.5)


def sfx_item_fail() -> array:
    notes = [(0.0, 52, 0.8), (0.13, 47, 0.9)]
    x = tone_seq(notes, 0.38, "square", 11.0)
    lp = OnePole(1500.0)
    for i in range(len(x)):
        x[i] = lp.lp(x[i])
    return x


def sfx_ui_click() -> array:
    n = int(0.06 * SR)
    x = buf(n)
    for i in range(n):
        t = i / SR
        x[i] = (math.sin(TWO_PI * 1250.0 * t) + 0.4 * math.sin(TWO_PI * 2500.0 * t)) * math.exp(-t * 90.0)
    return x


def sfx_ui_hover() -> array:
    n = int(0.045 * SR)
    x = buf(n)
    for i in range(n):
        t = i / SR
        x[i] = math.sin(TWO_PI * 880.0 * t) * math.exp(-t * 110.0) * min(1.0, t / 0.003)
    return x


LOOPS = {"music_main", "helicopter_loop"}


def generate() -> list[str]:
    os.makedirs(OUT_DIR, exist_ok=True)
    written: list[str] = []
    rng = random.Random(4242)
    sfx = [
        ("jump", lambda: sfx_jump()),
        ("land", lambda: sfx_land(rng)),
        ("swing", lambda: sfx_swing(rng)),
        ("crate_break", lambda: sfx_crate_break(rng)),
        ("pickup", lambda: sfx_pickup()),
        ("bridge_place", lambda: sfx_bridge_place(rng)),
        ("trampoline_place", lambda: sfx_trampoline_place(rng)),
        ("trampoline_bounce", lambda: sfx_trampoline_bounce()),
        ("grapple_shoot", lambda: sfx_grapple_shoot(rng)),
        ("grapple_attach", lambda: sfx_grapple_attach()),
        ("helicopter_start", lambda: sfx_helicopter_start(rng)),
        ("helicopter_loop", lambda: sfx_helicopter_loop(rng)),
        ("respawn", lambda: sfx_respawn()),
        ("checkpoint", lambda: sfx_checkpoint()),
        ("item_fail", lambda: sfx_item_fail()),
        ("ui_click", lambda: sfx_ui_click()),
        ("ui_hover", lambda: sfx_ui_hover()),
    ]
    for name, fn in sfx:
        x = fn()
        if name not in LOOPS:
            remove_dc(x)
            edge_fades(x)
        normalize(x)
        written.append(write_wav(name, x))
        print(f"  {name:18s} {len(x) / SR:6.3f} s")
    x = gen_music()
    written.append(write_wav("music_main", x))
    print(f"  {'music_main':18s} {len(x) / SR:6.3f} s")
    return written


def main() -> int:
    print(f"[gen_audio] writing to {OUT_DIR}")
    generate()
    if "--verify" in sys.argv:
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import verify_audio  # noqa: E402
        return verify_audio.main([])
    return 0


if __name__ == "__main__":
    sys.exit(main())
