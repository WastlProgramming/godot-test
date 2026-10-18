#!/usr/bin/env python3
"""Programmatic checks for art/audio (a listen test is not possible in CI).

Usage: python3 tools/verify_audio.py [--determinism]
Checks per file: WAV header (mono, 16 bit, 22 050 Hz), duration limits, peak level (normalised to about
-3 dBFS, never clipping), DC offset, click-free one-shot edges, seamless loop boundaries; plus the total
size of art/audio (<= 6 MB) and, with --determinism, that a second generator run gives identical bytes.
Exit code 0 = all checks passed.
"""
from __future__ import annotations

import hashlib
import math
import os
import subprocess
import sys
import tempfile
import wave
from array import array

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AUDIO_DIR = os.path.join(ROOT, "art", "audio")
SR = 22050

SFX = ["jump", "land", "swing", "crate_break", "pickup", "bridge_place", "trampoline_place",
       "trampoline_bounce", "grapple_shoot", "grapple_attach", "helicopter_start", "helicopter_loop",
       "respawn", "checkpoint", "item_fail", "ui_click", "ui_hover"]
LOOPS = {"music_main", "helicopter_loop"}


def db(v: float) -> float:
    return 20 * math.log10(v) if v > 0 else -999.0


def load(path: str) -> tuple[wave._wave_params, array]:
    with wave.open(path, "rb") as w:
        p = w.getparams()
        data = array("h")
        data.frombytes(w.readframes(p.nframes))
    if sys.byteorder != "little":
        data.byteswap()
    return p, data


def check_file(name: str, fails: list[str]) -> str:
    path = os.path.join(AUDIO_DIR, name + ".wav")
    if not os.path.exists(path):
        fails.append(f"{name}: missing")
        return f"{name}: MISSING"
    p, d = load(path)
    if p.nchannels != 1 or p.sampwidth != 2 or p.framerate != SR or p.comptype != "NONE":
        fails.append(f"{name}: bad header {p}")
    n = len(d)
    dur = n / SR
    x = [v / 32768.0 for v in d]
    peak = max(abs(v) for v in x)
    peak_db = db(peak)
    clipped = sum(1 for v in d if v >= 32767 or v <= -32768)
    dc = sum(x) / n
    rms_db = db(math.sqrt(sum(v * v for v in x) / n))
    info = f"{name:18s} {dur:7.3f}s peak {peak_db:6.2f} dBFS rms {rms_db:6.1f} dBFS dc {dc:+.5f}"

    if clipped:
        fails.append(f"{name}: {clipped} clipped samples")
    if not (-3.6 <= peak_db <= -2.4):
        fails.append(f"{name}: peak {peak_db:.2f} dBFS not ~-3 dBFS")
    if name == "music_main" and peak_db > -1.0:
        fails.append(f"{name}: peak above -1 dBFS")
    if abs(dc) > 0.003:
        fails.append(f"{name}: DC offset {dc:.4f}")

    diffs = [abs(x[i + 1] - x[i]) for i in range(n - 1)]
    diffs_sorted = sorted(diffs)
    p999 = diffs_sorted[int(0.999 * (len(diffs_sorted) - 1))]
    if name == "music_main":
        if not (60.0 <= dur <= 90.0):
            fails.append(f"{name}: duration {dur:.2f}s not in 60..90 s")
        bar = SR * 60 // 105 * 4
        if n % bar != 0:
            fails.append(f"{name}: length {n} is not a whole number of bars ({bar})")
        # no silent stretches: every bar must carry signal
        quiet = [b for b in range(n // bar)
                 if math.sqrt(sum(v * v for v in x[b * bar:(b + 1) * bar]) / bar) < 0.02]
        if quiet:
            fails.append(f"{name}: quiet bars {quiet}")
    elif name == "helicopter_loop":
        if not (1.0 <= dur <= 2.0):
            fails.append(f"{name}: loop duration {dur:.2f}s not in 1..2 s")
    else:
        if dur > 2.0:
            fails.append(f"{name}: one-shot longer than 2 s ({dur:.2f})")
        if abs(x[0]) > 0.002 or abs(x[-1]) > 0.002:
            fails.append(f"{name}: edges not at zero ({x[0]:.4f} / {x[-1]:.4f}) -> click")

    if name in LOOPS:
        jump = abs(x[0] - x[-1])
        # the wrap step must look like any ordinary step inside the file
        if jump > max(p999, 1e-3):
            fails.append(f"{name}: loop seam step {jump:.4f} > 99.9th pct step {p999:.4f}")
        # slope continuity (second difference) across the seam
        d2_seam = abs((x[0] - x[-1]) - (x[-1] - x[-2]))
        d2 = sorted(abs(x[i + 2] - 2 * x[i + 1] + x[i]) for i in range(0, n - 2, 7))
        d2_p999 = d2[int(0.999 * (len(d2) - 1))]
        if d2_seam > max(d2_p999, 1e-3):
            fails.append(f"{name}: loop seam curvature {d2_seam:.4f} > {d2_p999:.4f}")
        info += f" seam {jump:.4f} (p99.9 {p999:.4f})"
    return info


def determinism(fails: list[str]) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        env = dict(os.environ, JUMPRUN_AUDIO_OUT=tmp)
        subprocess.run([sys.executable, os.path.join(ROOT, "tools", "gen_audio.py")], env=env, check=True,
                       stdout=subprocess.DEVNULL)
        for f in sorted(os.listdir(tmp)):
            a = hashlib.sha256(open(os.path.join(tmp, f), "rb").read()).hexdigest()
            bp = os.path.join(AUDIO_DIR, f)
            b = hashlib.sha256(open(bp, "rb").read()).hexdigest() if os.path.exists(bp) else "-"
            if a != b:
                fails.append(f"determinism: {f} differs between runs")
    print("  determinism: second run compared byte-for-byte")


def main(argv: list[str]) -> int:
    fails: list[str] = []
    for name in SFX + ["music_main"]:
        print("  " + check_file(name, fails))
    total = sum(os.path.getsize(os.path.join(AUDIO_DIR, f)) for f in os.listdir(AUDIO_DIR)
                if f.endswith(".wav"))
    print(f"  total art/audio/*.wav: {total / 1e6:.2f} MB")
    if total > 6_000_000:
        fails.append(f"total size {total} > 6 MB")
    if "--determinism" in argv:
        determinism(fails)
    if fails:
        for f in fails:
            print("FAIL " + f)
        print(f"AUDIO_VERIFY_FAIL ({len(fails)})")
        return 1
    print("AUDIO_VERIFY_PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
