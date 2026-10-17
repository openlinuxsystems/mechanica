#!/usr/bin/env python3
"""Generates vanilla's audio and sound event files (chunk 16).

Every sound event in CATALOG_IDS.md gets a clip: the player's footsteps, landings, water, hurt and heartbeat
and every material's place, impact and break sounds are CC0 recordings from tools/sounds/recordings/ (see
CREDITS.md there), trimmed, layered and levelled; the rest are original synthesis. UI sounds are 16-bit mono
WAV (tiny, and instant to start), everything else Ogg Vorbis. Nothing is downloaded. The output is
deterministic for a given numpy and ffmpeg, so rerunning only changes what the recipes change.

Writes mods/vanilla/sounds/<group>.json (canonical DataJson form) and
mods/vanilla/sounds/<group>/<id>[_<n>].wav|.ogg. Replace any clip with a real recording by keeping its
path, or point the event at a new file with the Sound Event Binder in the Part Editor.

Needs Python 3 with numpy, and ffmpeg built with libvorbis.

    tools/sounds/make_placeholders.py            # regenerate everything
    tools/sounds/make_placeholders.py --check    # list the events without writing
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import wave
import zlib

import numpy as np

SR = 22050
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SOUNDS = os.path.join(ROOT, "mods", "vanilla", "sounds")

# ---- Synthesis helpers -------------------------------------------------------------------------


def n_of(dur):
    return max(1, int(round(dur * SR)))


def t_of(dur):
    return np.arange(n_of(dur)) / SR


def noise(rng, dur):
    return rng.uniform(-1.0, 1.0, n_of(dur))


def band(x, lo, hi):
    """Band-pass by FFT with soft 20 % edges."""
    spectrum = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1.0 / SR)
    lo_edge = np.clip((f - lo * 0.8) / max(lo * 0.2, 1.0), 0, 1) if lo > 0 else np.ones_like(f)
    hi_edge = np.clip((hi * 1.2 - f) / max(hi * 0.2, 1.0), 0, 1)
    return np.fft.irfft(spectrum * lo_edge * hi_edge, len(x))


def decay(dur, tau, attack=0.002):
    t = t_of(dur)
    env = np.exp(-t / tau)
    if attack > 0:
        env *= np.minimum(1.0, t / attack)
    return env


def tone(freq, dur, phase=0.0):
    return np.sin(2 * np.pi * freq * t_of(dur) + phase)


def glide(f0, f1, dur):
    """Sine sweeping exponentially from f0 to f1."""
    t = t_of(dur)
    f = f0 * (f1 / f0) ** (t / dur)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def saw(freq, dur):
    t = t_of(dur)
    return 2.0 * ((t * freq) % 1.0) - 1.0


def square(freq, dur):
    return np.sign(tone(freq, dur)) * 0.7


def fit(x, n):
    return x[:n] if len(x) >= n else np.concatenate([x, np.zeros(n - len(x))])


def mix(dur, *parts):
    """Adds (signal, start_seconds) pairs into one buffer of length dur."""
    out = np.zeros(n_of(dur))
    for sig, at in parts:
        i = n_of(at) if at > 0 else 0
        j = min(len(out), i + len(sig))
        if j > i:
            out[i:j] += sig[: j - i]
    return out


def hit(rng, dur, lo, hi, tau, partials=(), ptau=0.1, scale=1.0, amount=1.0):
    """A struck surface: a burst of band-limited noise plus decaying partials."""
    x = band(noise(rng, dur), lo * scale, hi * scale) * decay(dur, tau) * amount
    for freq, gain in partials:
        x = x + tone(freq * scale, dur, rng.uniform(0, 6.28)) * decay(dur, ptau) * gain
    return x


def edges(x, ms=4):
    k = min(len(x) // 2, n_of(ms / 1000.0))
    if k > 0:
        ramp = np.linspace(0, 1, k)
        x = x.copy()
        x[:k] *= ramp
        x[-k:] *= ramp[::-1]
    return x


def peak(x, level=0.8):
    m = float(np.max(np.abs(x)))
    return x * (level / m) if m > 0 else x


def q(freq, length):
    """Rounds a frequency to whole cycles per loop, so the loop has no seam."""
    return max(1, round(freq * length)) / length


def looped(make, length, fade=0.15):
    """Builds a seamless loop: renders length+fade and crossfades the tail into the head."""
    n, f = n_of(length), n_of(fade)
    x = fit(make(length + fade + 0.01), n + f)
    out = x[:n].copy()
    ramp = np.linspace(0, 1, f)
    out[:f] = x[:f] * ramp + x[n : n + f] * (1 - ramp)
    return out


# ---- Recordings --------------------------------------------------------------------------------
# Footsteps, landings, water and the material hits are CC0 recordings (tools/sounds/recordings/CREDITS.md),
# trimmed, layered and levelled here. They are written at REC_SR so their highs survive.

REC_SR = 44100
RECORDINGS = os.path.join(ROOT, "tools", "sounds", "recordings")
_decoded = {}


def load(name, pitch=1.0, lowpass=None):
    """tools/sounds/recordings/<name>.ogg as mono floats at REC_SR; pitch 0.8 plays it slower and lower."""
    key = (name, pitch, lowpass)
    if key not in _decoded:
        filters = [f"aresample={REC_SR}"]
        if pitch != 1.0:
            filters += [f"asetrate={round(REC_SR * pitch)}", f"aresample={REC_SR}"]
        if lowpass:
            filters.append(f"lowpass=f={lowpass}")
        raw = subprocess.run(["ffmpeg", "-v", "error", "-i", os.path.join(RECORDINGS, name + ".ogg"), "-af", ",".join(filters),
                              "-ac", "1", "-f", "f32le", "-"], check=True, capture_output=True).stdout
        _decoded[key] = np.frombuffer(raw, "<f4").astype(np.float64)
    return _decoded[key]


def take(name, at=0.0, gain=1.0, **filters):
    """One layer for rec(): a recording, when it starts in seconds and how loud it is against the others."""
    return load(name, **filters) * gain, at


def rec(*layers, level=0.8, max_dur=None, tail=0.06):
    """Layers recordings (names or take()s), cuts the silence around them, fades the ends and peaks at level."""
    layers = [take(l) if isinstance(l, str) else l for l in layers]
    out = np.zeros(max(round(at * REC_SR) + len(x) for x, at in layers))
    for x, at in layers:
        i = round(at * REC_SR)
        out[i : i + len(x)] += x
    loud = np.flatnonzero(np.abs(out) > np.max(np.abs(out)) * 0.01)
    start = max(0, loud[0] - round(0.002 * REC_SR))
    stop = min(len(out), loud[-1] + round(tail * REC_SR))
    if max_dur:
        stop = min(stop, start + round(max_dur * REC_SR))
    out = out[start:stop].copy()
    fade = min(len(out) // 3, round(tail * REC_SR))
    out[len(out) - fade :] *= np.linspace(1, 0, fade) ** 2
    attack = round(0.002 * REC_SR)
    out[:attack] *= np.linspace(0, 1, attack)
    return peak(out, level)


# ---- Recipes -----------------------------------------------------------------------------------

MATERIALS = {
    "wood": dict(lo=150, hi=2200, tau=0.045, partials=[(190, 0.6), (410, 0.25)], ptau=0.07),
    "metal": dict(lo=800, hi=7000, tau=0.02, partials=[(420, 0.5), (1159, 0.35), (2268, 0.25), (3750, 0.15)], ptau=0.35),
    "concrete": dict(lo=60, hi=1200, tau=0.07, partials=[(95, 0.5)], ptau=0.06),
    "glass": dict(lo=2500, hi=9000, tau=0.015, partials=[(1870, 0.4), (4130, 0.3), (6290, 0.2)], ptau=0.25),
    "scrap": dict(lo=300, hi=5000, tau=0.035, partials=[(330, 0.4), (870, 0.3), (1720, 0.2)], ptau=0.12),
    "plastic": dict(lo=700, hi=4500, tau=0.025, partials=[(620, 0.5), (1350, 0.2)], ptau=0.05),
    "tile": dict(lo=1500, hi=8000, tau=0.02, partials=[(940, 0.4), (2300, 0.25)], ptau=0.06),
    "cardboard": dict(lo=200, hi=2500, tau=0.05, partials=[(140, 0.3)], ptau=0.04),
    "fabric": dict(lo=150, hi=1500, tau=0.06, partials=[], ptau=0.05),
}


def mat_place(mat):
    m = MATERIALS[mat]
    return lambda rng: edges(peak(hit(rng, 0.4, m["lo"], m["hi"], m["tau"], m["partials"], m["ptau"], rng.uniform(0.92, 1.08)), 0.75))


def mat_impact(mat):
    m = MATERIALS[mat]

    def make(rng):
        s = rng.uniform(0.75, 0.9)
        x = hit(rng, 0.5, m["lo"] * 0.7, m["hi"], m["tau"] * 1.4, m["partials"], m["ptau"] * 1.2, s, 1.4)
        x = x + band(noise(rng, 0.5), 40, 250) * decay(0.5, 0.05) * 0.6
        return edges(peak(x, 0.85))

    return make


def mat_break(mat):
    m = MATERIALS[mat]

    def make(rng):
        dur = 0.9
        parts = []
        count = 14 if mat == "glass" else 8
        for i in range(count):
            at = rng.uniform(0, 0.45) if i else 0.0
            amount = 1.0 if i == 0 else rng.uniform(0.25, 0.7)
            parts.append((hit(rng, 0.4, m["lo"], m["hi"], m["tau"], m["partials"], m["ptau"] * 0.6, rng.uniform(0.8, 1.3), amount), at))
        crumble = band(noise(rng, dur), m["lo"] * 0.5, m["hi"] * 0.7) * decay(dur, 0.25) * 0.35
        return edges(peak(mix(dur, *parts) + crumble, 0.85))

    return make


def thud(rng, dur=0.3, lo=40, hi=600, tau=0.06, amount=1.0):
    return band(noise(rng, dur), lo, hi) * decay(dur, tau) * amount + tone(70, dur) * decay(dur, tau * 1.2) * 0.6 * amount


def click(rng, freq=2500, dur=0.06):
    return edges(peak(band(noise(rng, dur), freq * 0.5, freq * 2) * decay(dur, 0.006) + tone(freq, dur) * decay(dur, 0.01) * 0.4, 0.7))


def beeps(freqs, step=0.09, dur=None, wave_fn=tone, level=0.6):
    dur = dur or step * len(freqs) + 0.1
    parts = [(wave_fn(f, step) * decay(step, step / 2.5, 0.004), i * step) for i, f in enumerate(freqs)]
    return edges(peak(mix(dur, *parts), level))


def sweep_shot(f0, f1, dur, rng, grit=0.0, level=0.7):
    x = glide(f0, f1, dur) * np.sin(np.pi * np.linspace(0, 1, n_of(dur))) ** 0.5
    if grit:
        x = x + band(noise(rng, dur), f0, f1 * 4) * grit * np.linspace(1, 0.3, n_of(dur))
    return edges(peak(x, level))


def hiss(rng, dur, lo, hi, attack=0.03, release=0.1, level=0.6):
    n = n_of(dur)
    env = np.minimum(1, np.arange(n) / n_of(attack)) * np.minimum(1, (n - np.arange(n)) / n_of(release))
    return edges(peak(band(noise(rng, dur), lo, hi) * env, level))


def whoosh(rng, dur, lo=200, hi=3000, level=0.6):
    env = np.sin(np.pi * np.linspace(0, 1, n_of(dur))) ** 2
    a = band(noise(rng, dur), lo, hi * 0.5)
    b = band(noise(rng, dur), lo * 2, hi)
    ramp = np.linspace(0, 1, n_of(dur))
    return edges(peak((a * (1 - ramp) + b * ramp) * env, level))


def sledge_whoosh(rng, dur=0.66, peak_at=0.56, level=0.65):
    """A long, low whoosh that builds through the wind-up and peaks as the head comes down, cut at the hit."""
    t = np.linspace(0, dur, n_of(dur))
    env = np.exp(-((t - peak_at) / 0.11) ** 2) + 0.12 * np.exp(-((t - 0.22) / 0.1) ** 2)
    env *= np.clip((dur - t) / 0.03, 0, 1)
    a = band(noise(rng, dur), 90, 700)
    b = band(noise(rng, dur), 300, 2200)
    sweep = np.clip((t - 0.3) / (peak_at - 0.3), 0, 1)
    return edges(peak((a * (1 - 0.6 * sweep) + b * sweep * 0.8) * env, level))


def crackle(rng, dur, rate=40, level=0.6):
    x = band(noise(rng, dur), 300, 3000) * 0.08
    for _ in range(int(rate * dur)):
        at = rng.uniform(0, dur - 0.03)
        x = x + mix(dur, (band(noise(rng, 0.03), 800, 6000) * decay(0.03, 0.004) * rng.uniform(0.3, 1.0), at))
    return peak(x, level)


def boom(rng, dur=1.6, level=0.9):
    x = band(noise(rng, dur), 30, 3000) * decay(dur, 0.25, 0.003) + glide(90, 30, dur) * decay(dur, 0.4) * 0.8
    return edges(peak(x, level))


def voice(base, dur, shape, rng, buzz=0.0, level=0.6):
    """A small machine voice: a pitched glide with formant-ish harmonics."""
    f0, f1 = base * shape[0], base * shape[1]
    x = glide(f0, f1, dur) + 0.4 * glide(f0 * 2, f1 * 2, dur) + 0.2 * glide(f0 * 3.01, f1 * 3.01, dur)
    if buzz:
        x = x + buzz * np.sign(glide(f0, f1, dur))
    env = np.sin(np.pi * np.linspace(0, 1, n_of(dur))) ** 0.6
    return edges(peak(x * env, level))


# Loops: each recipe takes (rng, length) and returns a function of duration for looped().

def engine_loop(base, rough):
    def make(rng, length):
        f = q(base, length)

        def render(d):
            x = sum(saw(f * h, d) * (0.6 / h) for h in (1, 2, 3))
            pulse = 0.6 + 0.4 * np.sign(tone(f / 2, d))
            return x * pulse + band(noise(rng, d), 80, 1500) * rough
        return looped(render, length)
    return make


def tonal_loop(freqs, wobble=0.0, wobble_rate=4.0, noise_amount=0.0, lo=200, hi=4000):
    def make(rng, length):
        def render(d):
            rate = q(wobble_rate, length)
            mod = 1 + wobble * tone(rate, d)
            x = sum(tone(q(f, length), d) * g for f, g in freqs) * mod
            if noise_amount:
                x = x + band(noise(rng, d), lo, hi) * noise_amount
            return x
        return looped(render, length)
    return make


def noise_loop(lo, hi, am_rate=0.0, am_depth=0.0, tonal=()):
    def make(rng, length):
        def render(d):
            x = band(noise(rng, d), lo, hi)
            if am_rate:
                x = x * (1 - am_depth + am_depth * (0.5 + 0.5 * tone(q(am_rate, length), d)))
            for f, g in tonal:
                x = x + tone(q(f, length), d) * g
            return x
        return looped(render, length)
    return make


def pattern_loop(hits_fn, period):
    """A loop made of events repeating every `period` seconds plus a hum."""
    def make(rng, length):
        def render(d):
            parts = []
            k = 0
            while k * period < d:
                parts.extend((sig, k * period + off) for sig, off in hits_fn(rng))
                k += 1
            return mix(d, *parts)
        return looped(render, length, fade=0.05)
    return make


def chirps_loop(lo, hi, count):
    def make(rng, length):
        def render(d):
            parts = [(voice(rng.uniform(lo, hi), rng.uniform(0.06, 0.15), (1.0, rng.uniform(1.1, 1.6)), rng, level=rng.uniform(0.2, 0.6)),
                      rng.uniform(0, d - 0.2)) for _ in range(int(count * d))]
            return mix(d, *parts) + band(noise(rng, d), 2000, 6000) * 0.02
        return looped(render, length)
    return make


NOTES = {"c": 0, "d": 2, "e": 4, "f": 5, "g": 7, "a": 9, "b": 11}


def note_freq(name, octave):
    return 440.0 * 2 ** ((NOTES[name] - 9) / 12 + (octave - 4))


def music_loop(chords, bpm, bright):
    """Pads plus a plucked arpeggio over a chord progression, one bar per chord."""
    def make(rng, length):
        beat = 60.0 / bpm

        def render(d):
            parts = []
            bar = beat * 4
            for i in range(int(np.ceil(d / bar))):
                root, third, fifth = chords[i % len(chords)]
                pad = sum(tone(f, bar) * 0.25 for f in (root, third, fifth, root / 2))
                env = np.minimum(1, np.arange(n_of(bar)) / n_of(0.3)) * np.minimum(1, (n_of(bar) - np.arange(n_of(bar))) / n_of(0.3))
                parts.append((pad * env, i * bar))
                for k in range(8):
                    f = (root, third, fifth, third * 2)[k % 4] * (2 if bright else 1)
                    pluck = (tone(f, beat) + 0.3 * tone(f * 2, beat)) * decay(beat, beat / 4, 0.003) * 0.35
                    parts.append((pluck, i * bar + k * beat / 2))
            return mix(d, *parts)
        return looped(render, length, fade=0.2)
    return make


def chord(names, octave=3):
    return [note_freq(n, octave + (1 if j and NOTES[n] < NOTES[names[0]] else 0)) for j, n in enumerate(names)]


# ---- The catalog -------------------------------------------------------------------------------

EVENTS = []  # (group, id, recipe, options, variants, is_loop, loop_length)


def ev(group, sid, recipe, variants=1, loop=None, **opts):
    EVENTS.append((group, sid, recipe, opts, variants, loop is not None, loop))


def loudness(x, rate):
    """The loudest 400 ms of x as RMS in dB (the whole clip when it is shorter)."""
    w = min(len(x), round(0.4 * rate))
    power = np.convolve(x * x, np.ones(w) / w, mode="valid")
    return 10 * np.log10(max(float(np.max(power)), 1e-12))


def level_to(x, rate, target_db, ceiling=0.9, squash_db=8.0):
    """Scales x so its loudest 400 ms sits at target_db. A clip whose peaks would pass the ceiling first is
    soft-limited (up to squash_db over it) so a sharp transient does not hold its body down; past that it stays quieter."""
    gain = 10 ** ((target_db - loudness(x, rate)) / 20)
    top = float(np.max(np.abs(x)))
    if top * gain <= ceiling:
        return x * gain
    gain = min(gain, ceiling / top * 10 ** (squash_db / 20))
    return ceiling * np.tanh(x * gain / ceiling)


def rev(group, sid, makers, **opts):
    """A recorded event: one maker per variant, each returning rec(...). loudness= (dB) levels every variant alike."""
    ev(group, sid, [lambda rng, make=make: make() for make in makers], variants=len(makers), rate=REC_SR, **opts)


K = "kenney/"
W = "rubberduck/"


def series(name, numbers, **kw):
    """Makers for kenney/<name>_000-style files, one per number."""
    return [lambda n=n: rec(take(f"{K}{name}_{n:03d}", **{k: v for k, v in kw.items() if k in ("pitch", "lowpass")}), level=kw.get("level", 0.8))
            for n in numbers]


# Per material: place (3 variants), impact (3) and break (2) from recordings.
MAT_PLACE = 0.75
MAT_HIT = 0.85
MATERIAL_RECORDINGS = {
    "wood": (series("impactWood_light", range(3), level=MAT_PLACE), series("impactWood_medium", range(3), level=MAT_HIT),
             [lambda: rec(f"{K}impactWood_heavy_000", take(f"{K}impactPlank_medium_000", 0.06, 0.8), take(f"{K}impactWood_light_003", 0.15, 0.6), level=MAT_HIT),
              lambda: rec(f"{K}impactWood_heavy_001", take(f"{K}impactPlank_medium_001", 0.05, 0.8), take(f"{K}impactWood_light_004", 0.18, 0.6), level=MAT_HIT)]),
    "metal": (series("impactMetal_light", range(3), level=MAT_PLACE), series("impactMetal_medium", range(3), level=MAT_HIT),
              [lambda: rec(f"{K}impactMetal_heavy_000", take(f"{K}impactPlate_heavy_000", 0.05, 0.8), take(f"{K}impactMetal_light_003", 0.2, 0.5), level=MAT_HIT),
               lambda: rec(f"{K}impactMetal_heavy_001", take(f"{K}impactPlate_heavy_001", 0.04, 0.8), take(f"{K}impactMetal_light_004", 0.22, 0.5), level=MAT_HIT)]),
    "concrete": (series("impactMining", range(3), pitch=0.8, lowpass=2500, level=MAT_PLACE), series("impactMining", (2, 3, 4), pitch=0.9, level=MAT_HIT),
                 [lambda: rec(take(f"{K}impactSoft_heavy_000", lowpass=600), take(f"{K}impactMining_003", 0, 0.9, pitch=0.8),
                              take(f"{K}impactMining_004", 0.07, 0.7, pitch=0.85), level=MAT_HIT),
                  lambda: rec(take(f"{K}impactSoft_heavy_001", lowpass=600), take(f"{K}impactMining_001", 0, 0.9, pitch=0.8),
                              take(f"{K}impactMining_000", 0.09, 0.7, pitch=0.85), level=MAT_HIT)]),
    "glass": (series("impactGlass_light", range(3), level=MAT_PLACE), series("impactGlass_medium", range(3), level=MAT_HIT),
              [lambda: rec(f"{K}impactGlass_heavy_000", take(f"{K}impactGlass_heavy_001", 0.08, 0.8), take(f"{K}impactGlass_light_003", 0.2, 0.6), level=MAT_HIT),
               lambda: rec(f"{K}impactGlass_heavy_002", take(f"{K}impactGlass_heavy_003", 0.06, 0.8), take(f"{K}impactGlass_light_004", 0.17, 0.6), level=MAT_HIT)]),
    "scrap": (series("impactTin_medium", range(3), level=MAT_PLACE), series("impactPlate_medium", range(3), level=MAT_HIT),
              [lambda: rec(f"{K}impactMetal_heavy_002", take(f"{K}impactTin_medium_003", 0.06, 0.8), take(f"{K}impactPlate_light_003", 0.15, 0.6), level=MAT_HIT),
               lambda: rec(f"{K}impactMetal_heavy_003", take(f"{K}impactTin_medium_004", 0.05, 0.8), take(f"{K}impactPlate_light_004", 0.19, 0.6), level=MAT_HIT)]),
    "plastic": (series("impactGeneric_light", range(3), level=MAT_PLACE), series("impactGeneric_light", (3, 4, 0), pitch=0.85, level=MAT_HIT),
                [lambda: rec(f"{K}impactGeneric_light_001", take(f"{K}impactGeneric_light_002", 0.07, 0.8, pitch=1.1), take(f"{K}impactGeneric_light_003", 0.15, 0.6, pitch=1.2), level=MAT_HIT),
                 lambda: rec(f"{K}impactGeneric_light_004", take(f"{K}impactGeneric_light_000", 0.06, 0.8, pitch=1.1), take(f"{K}impactGeneric_light_001", 0.16, 0.6, pitch=1.2), level=MAT_HIT)]),
    "tile": (series("impactGlass_light", (3, 4, 0), pitch=0.7, level=MAT_PLACE), series("impactGlass_medium", (3, 4, 2), pitch=0.75, level=MAT_HIT),
             [lambda: rec(take(f"{K}impactGlass_heavy_004", pitch=0.8), take(f"{K}impactMining_000", 0.03, 0.7), take(f"{K}impactGlass_light_001", 0.15, 0.6, pitch=0.8), level=MAT_HIT),
              lambda: rec(take(f"{K}impactGlass_heavy_001", pitch=0.8), take(f"{K}impactMining_002", 0.04, 0.7), take(f"{K}impactGlass_light_002", 0.18, 0.6, pitch=0.8), level=MAT_HIT)]),
    "cardboard": ([lambda n=n: rec(f"{K}bookPlace{n}", level=MAT_PLACE) for n in (1, 2, 3)],
                  [lambda: rec(f"{K}bookClose", level=MAT_HIT), lambda: rec(f"{K}dropLeather", level=MAT_HIT), lambda: rec(take(f"{K}bookPlace2", pitch=0.8), level=MAT_HIT)],
                  [lambda: rec(f"{K}bookFlip1", take(f"{K}bookClose", 0.05), take(f"{K}bookFlip2", 0.2, 0.7), level=MAT_HIT),
                   lambda: rec(f"{K}bookFlip3", take(f"{K}dropLeather", 0.04), level=MAT_HIT)]),
    "fabric": (series("impactSoft_medium", range(3), level=MAT_PLACE), series("impactSoft_heavy", range(3), level=MAT_HIT),
               [lambda: rec(f"{K}cloth1", take(f"{K}cloth3", 0.1, 0.8), level=MAT_HIT), lambda: rec(f"{K}cloth2", take(f"{K}cloth4", 0.08, 0.8), level=MAT_HIT)]),
}


def heartbeat(length):
    """Two low thumps a beat, every second, as a seamless loop."""
    out = np.zeros(round(length * REC_SR))
    lub, dub = load(f"{K}impactSoft_heavy_003", lowpass=180), load(f"{K}impactSoft_heavy_004", lowpass=180) * 0.7
    for beat in range(round(length)):
        for x, at in ((lub, beat), (dub, beat + 0.25)):
            np.add.at(out, (round(at * REC_SR) + np.arange(len(x))) % len(out), x)  # a tail past the end wraps to the start
    return out


QUIET = dict(max_distance=20.0)
TOOL = dict(max_distance=30.0)
MAT_OPTS = dict(pitch_variation=0.06, volume_variation_db=1.5, max_distance=30.0, cooldown=0.02)

for mat in MATERIALS:
    place, impact, smash = MATERIAL_RECORDINGS[mat]
    rev("materials", f"place_{mat}", place, loudness=-21.0, **MAT_OPTS)
    rev("materials", f"break_{mat}", smash, loudness=-18.0, **{**MAT_OPTS, "max_distance": 45.0})
    rev("materials", f"impact_{mat}", impact, loudness=-19.0, **{**MAT_OPTS, "max_instances": 6})
ev("materials", "burn_wood", lambda rng, length: looped(lambda d: crackle(rng, d), length), loop=3.0, max_distance=25.0,
   parameters=[dict(name="intensity", pitch=[0.9, 1.1], volume_db=[-12.0, 0.0])])
ev("materials", "vehicle_crash", lambda rng: edges(peak(mix(0.9, (mat_break("metal")(rng), 0), (mat_impact("scrap")(rng), 0.05), (boom(rng, 0.9), 0)), 0.9)),
   pitch_variation=0.08, max_distance=80.0)
rev("materials", "impact_soft", series("impactSoft_medium", (3, 4), level=0.6), loudness=-20.0, pitch_variation=0.08, **QUIET)

# Building and tools
ev("building", "part_attach", lambda rng: edges(peak(mix(0.3, (click(rng, 1800), 0), (thud(rng, 0.25, 80, 1200, 0.03, 0.8), 0.02)), 0.7)), pitch_variation=0.05, **TOOL)
ev("building", "part_detach", lambda rng: edges(peak(mix(0.3, (glide(900, 300, 0.12) * decay(0.12, 0.04), 0), (click(rng, 1200), 0.05)), 0.7)), pitch_variation=0.05, **TOOL)
ev("building", "remove_block", lambda rng: edges(peak(mix(0.35, (glide(600, 180, 0.15) * decay(0.15, 0.05), 0), (thud(rng, 0.3, 60, 900, 0.04), 0.03)), 0.75)), pitch_variation=0.06, **TOOL)
ev("building", "hammer_swing", lambda rng: whoosh(rng, 0.22, 300, 4000, 0.5), variants=2, pitch_variation=0.1, volume_variation_db=2.0, max_distance=15.0)
ev("building", "lift_place", lambda rng: edges(peak(mix(0.5, (thud(rng, 0.4, 50, 900, 0.08), 0), (click(rng, 900, 0.1), 0.05)), 0.8)), **TOOL)
ev("building", "lift_raise", lambda rng: edges(peak(glide(120, 220, 0.7) * 0.5 + band(noise(rng, 0.7), 300, 2500) * 0.25 * np.sin(np.pi * np.linspace(0, 1, n_of(0.7))), 0.7)), **TOOL)
ev("building", "lift_lower", lambda rng: edges(peak(glide(220, 110, 0.7) * 0.5 + band(noise(rng, 0.7), 300, 2500) * 0.25 * np.sin(np.pi * np.linspace(0, 1, n_of(0.7))), 0.7)), **TOOL)
ev("building", "connect_success", lambda rng: beeps([880, 1320]), **QUIET)
ev("building", "connect_fail", lambda rng: beeps([440, 330], wave_fn=square, level=0.5), **QUIET)
ev("building", "disconnect", lambda rng: beeps([990, 660], step=0.06), **QUIET)
ev("building", "weld_start", lambda rng: edges(crackle(rng, 0.4, 90, 0.6)), pitch_variation=0.05, **TOOL)
ev("building", "weld_complete", lambda rng: edges(peak(mix(0.7, (crackle(rng, 0.3, 60, 0.5), 0), (hiss(rng, 0.4, 2000, 8000, 0.01, 0.3, 0.3), 0.1), (tone(1760, 0.5) * decay(0.5, 0.15) * 0.4, 0.25)), 0.75)), **TOOL)
ev("building", "paint_spray", lambda rng: hiss(rng, 0.45, 2500, 9000, 0.02, 0.12, 0.55), variants=2, pitch_variation=0.05, **QUIET)
ev("building", "paint_erase", lambda rng: hiss(rng, 0.35, 600, 3000, 0.04, 0.12, 0.5), pitch_variation=0.05, **QUIET)
# The Sledgehammer: a heavy whoosh that swells as the head comes over (the hit lands 0.6 s after the click,
# sledge.strike_seconds), then a deep steel thump on what it hits, or a thump and a crunch when it breaks.
ev("building", "sledgehammer_swing", lambda rng: sledge_whoosh(rng), variants=2, pitch_variation=0.06, volume_variation_db=1.5, max_distance=15.0)
rev("building", "smash_hit", [
    lambda: rec(take(f"{K}impactPlate_heavy_000", pitch=0.78), take(f"{K}impactPunch_heavy_000", 0.0, 0.9),
                take(f"{K}impactMetal_heavy_000", 0.005, 0.45, pitch=0.7), level=0.9),
    lambda: rec(take(f"{K}impactPlate_heavy_001", pitch=0.75), take(f"{K}impactPunch_medium_001", 0.0, 0.9, pitch=0.85),
                take(f"{K}impactMetal_heavy_002", 0.005, 0.45, pitch=0.72), level=0.9),
], loudness=-15.0, pitch_variation=0.07, volume_variation_db=1.5, max_distance=40.0)
rev("building", "smash_break", [
    lambda: rec(take(f"{K}impactPlate_heavy_000", pitch=0.72), take(f"{K}impactPunch_heavy_000", 0.0, 0.9),
                take(f"{K}impactMining_002", 0.03, 0.8, pitch=0.8), take(f"{K}impactMining_004", 0.12, 0.6, pitch=0.9),
                take(f"{K}impactMetal_light_003", 0.2, 0.3, pitch=0.8), level=0.9),
], loudness=-14.0, pitch_variation=0.07, max_distance=45.0)

# Engines and power
ENGINE = dict(max_distance=60.0)
ev("engines", "engine_start", lambda rng: edges(peak(mix(1.3, *[(thud(rng, 0.12, 60, 900, 0.03, 0.8), i * 0.12) for i in range(6)], (engine_loop(55, 0.2)(rng, 0.6)[:n_of(0.6)], 0.7)), 0.8)), **ENGINE)
ev("engines", "engine_idle", engine_loop(38, 0.15), loop=2.0, volume_db=-3.0, **ENGINE,
   parameters=[dict(name="load", pitch=[1.0, 1.3], volume_db=[-2.0, 0.0])])
ev("engines", "engine_rev", engine_loop(70, 0.25), loop=2.0, **ENGINE,
   parameters=[dict(name="rpm", pitch=[0.8, 2.0], volume_db=[-6.0, 0.0])])
ev("engines", "engine_sputter", lambda rng: edges(peak(mix(1.0, *[(thud(rng, 0.1, 60, 1200, 0.025, rng.uniform(0.4, 1.0)), rng.uniform(0, 0.85)) for _ in range(7)]), 0.8)), pitch_variation=0.06, **ENGINE)
ev("engines", "electric_start", lambda rng: sweep_shot(80, 440, 0.8, rng, 0.05), **ENGINE)
ev("engines", "electric_whirr", tonal_loop([(440, 0.6), (880, 0.25), (1320, 0.1)], 0.05, 6.0, 0.08, 1000, 6000), loop=2.0, volume_db=-4.0, max_distance=40.0,
   parameters=[dict(name="speed", pitch=[0.5, 2.0], volume_db=[-12.0, 0.0])])
ev("engines", "battery_insert", lambda rng: edges(peak(mix(0.6, (click(rng, 1500), 0), (glide(200, 600, 0.4) * decay(0.4, 0.3) * 0.4, 0.08)), 0.7)), **QUIET)
ev("engines", "battery_drain", lambda rng: sweep_shot(500, 120, 1.0, rng, 0.0, 0.5), **QUIET)
ev("engines", "fuel_pour", lambda rng: edges(peak(band(noise(rng, 1.0), 200, 1500) * (0.6 + 0.4 * tone(7, 1.0)) * np.sin(np.pi * np.linspace(0, 1, n_of(1.0))), 0.6)), **QUIET)

# Joints and motion
ev("joints", "bearing_start", lambda rng: sweep_shot(150, 450, 0.5, rng, 0.05, 0.6), **TOOL)
ev("joints", "bearing_spin_loop", tonal_loop([(300, 0.5), (600, 0.2), (912, 0.1)], 0.15, 12.0, 0.05, 2000, 6000), loop=1.5, volume_db=-6.0, **TOOL,
   parameters=[dict(name="speed", pitch=[0.6, 2.2], volume_db=[-14.0, 0.0])])
ev("joints", "bearing_stop", lambda rng: sweep_shot(450, 150, 0.5, rng, 0.05, 0.6), **TOOL)
PISTON = dict(max_distance=30.0, pitch_variation=0.04)
ev("joints", "piston_extend", lambda rng: edges(peak(glide(180, 320, 0.6) * 0.5 + hiss(rng, 0.6, 1500, 6000, 0.05, 0.2, 0.3), 0.7)), **PISTON)
ev("joints", "piston_retract", lambda rng: edges(peak(glide(320, 180, 0.6) * 0.5 + hiss(rng, 0.6, 1500, 6000, 0.05, 0.2, 0.3), 0.7)), **PISTON)
ev("joints", "piston_stall", lambda rng: edges(peak(square(95, 0.5) * decay(0.5, 0.3) + click(rng, 600, 0.5) * 0.5, 0.6)), **PISTON)
ev("joints", "suspension_compress", lambda rng: edges(peak(glide(260, 140, 0.25) * decay(0.25, 0.1) + thud(rng, 0.25, 50, 500, 0.04, 0.5), 0.6)), variants=2, pitch_variation=0.08, **QUIET)
ev("joints", "suspension_extend", lambda rng: edges(peak(glide(140, 260, 0.25) * decay(0.25, 0.1), 0.5)), variants=2, pitch_variation=0.08, **QUIET)

# Flight
FLY = dict(max_distance=120.0)
ev("flight", "thruster_ignite", lambda rng: edges(peak(mix(0.8, (boom(rng, 0.5, 0.8), 0), (hiss(rng, 0.7, 150, 4000, 0.1, 0.2, 0.6), 0.05)), 0.85)), **FLY)
ev("flight", "thruster_loop", noise_loop(80, 4000, 25.0, 0.15, [(q(60, 2.0), 0.15)]), loop=2.0, **FLY,
   parameters=[dict(name="thrust", pitch=[0.8, 1.25], volume_db=[-12.0, 0.0])])
ev("flight", "thruster_cut", lambda rng: edges(peak(band(noise(rng, 0.6), 80, 4000) * decay(0.6, 0.12), 0.7)), **FLY)
ev("flight", "rotor_spin_up", lambda rng: edges(peak(band(noise(rng, 1.5), 100, 1500) * (0.5 + 0.5 * glide(4, 14, 1.5)) * np.linspace(0.2, 1, n_of(1.5)), 0.7)), max_distance=80.0)
ev("flight", "rotor_loop", noise_loop(100, 1500, 14.0, 0.8), loop=2.0, max_distance=80.0,
   parameters=[dict(name="speed", pitch=[0.5, 1.6], volume_db=[-14.0, 0.0])])
ev("flight", "rotor_spin_down", lambda rng: edges(peak(band(noise(rng, 1.5), 100, 1500) * (0.5 + 0.5 * glide(14, 3, 1.5)) * np.linspace(1, 0.1, n_of(1.5)), 0.7)), max_distance=80.0)
ev("flight", "hover_ignite", lambda rng: sweep_shot(55, 110, 0.6, rng, 0.1, 0.7), max_distance=60.0)
ev("flight", "hover_hum", tonal_loop([(110, 0.6), (220, 0.3), (331, 0.12)], 0.08, 3.0, 0.05, 200, 1200), loop=2.0, max_distance=60.0,
   parameters=[dict(name="thrust", pitch=[0.85, 1.2], volume_db=[-10.0, 0.0])])
ev("flight", "hover_cut", lambda rng: sweep_shot(110, 45, 0.6, rng, 0.05, 0.6), max_distance=60.0)
ev("flight", "air_whoosh", noise_loop(150, 3000, 0.7, 0.3), loop=3.0, max_distance=30.0,
   parameters=[dict(name="airspeed", pitch=[0.7, 1.8], volume_db=[-30.0, 0.0])])
ev("flight", "wing_stall", lambda rng: edges(peak(band(noise(rng, 0.8), 80, 900) * (0.5 + 0.5 * np.abs(tone(9, 0.8))) * decay(0.8, 0.5), 0.7)), max_distance=40.0)
ev("flight", "surface_move", lambda rng: edges(peak(glide(700, 900, 0.25) * 0.4 + band(noise(rng, 0.25), 2000, 5000) * 0.1, 0.4)), pitch_variation=0.05, **QUIET)
ev("flight", "surface_lock", lambda rng: click(rng, 2200, 0.08), **QUIET)

# Logic and seats
ev("logic", "seat_enter", lambda rng: edges(peak(thud(rng, 0.3, 60, 900, 0.05, 0.7) + click(rng, 1400, 0.3) * 0.5, 0.6)), **QUIET)
ev("logic", "seat_exit", lambda rng: edges(peak(whoosh(rng, 0.25, 200, 2000) + click(rng, 1100, 0.25) * 0.4, 0.5)), **QUIET)
ev("logic", "seat_click", lambda rng: click(rng, 1600), **QUIET)
ev("logic", "controller_start", lambda rng: beeps([523, 659, 784], 0.07), **QUIET)
ev("logic", "controller_tick", lambda rng: click(rng, 3000, 0.04), max_instances=4, **QUIET)
ev("logic", "controller_stop", lambda rng: beeps([784, 659, 523], 0.07), **QUIET)
ev("logic", "logic_click", lambda rng: click(rng, 2800, 0.05), max_instances=4, **QUIET)
ev("logic", "logic_activate", lambda rng: beeps([1200, 1600], 0.05, level=0.45), max_instances=4, **QUIET)
ev("logic", "timer_tick", lambda rng: click(rng, 3500, 0.04), max_instances=4, **QUIET)
ev("logic", "timer_complete", lambda rng: beeps([1046, 1046, 1568], 0.08), **QUIET)
ev("logic", "sensor_detect", lambda rng: beeps([1400, 1900], 0.06, level=0.5), **QUIET)
ev("logic", "sensor_lose", lambda rng: beeps([1900, 1400], 0.06, level=0.5), **QUIET)
ev("logic", "sensor_ping", lambda rng: edges(peak(tone(2400, 0.3) * decay(0.3, 0.06), 0.4)), max_instances=4, **QUIET)
ev("logic", "switch_toggle", lambda rng: click(rng, 1900, 0.07), **QUIET)
ev("logic", "button_press", lambda rng: click(rng, 1300, 0.07), **QUIET)
ev("logic", "horn_honk", tonal_loop([(400, 0.5), (500, 0.4), (800, 0.15), (1000, 0.1)], 0.0), loop=1.0, max_distance=80.0)

# Tools on parts
ev("part_tools", "saw_spin", tonal_loop([(1200, 0.3), (2400, 0.15)], 0.1, 30.0, 0.25, 2000, 8000), loop=1.5, max_distance=40.0,
   parameters=[dict(name="load", pitch=[1.0, 0.8], volume_db=[0.0, 3.0])])
ev("part_tools", "saw_cut", lambda rng: edges(peak(band(noise(rng, 0.5), 1500, 8000) * (0.6 + 0.4 * tone(40, 0.5)) * decay(0.5, 0.25), 0.7)), variants=2, pitch_variation=0.06, max_distance=40.0)
ev("part_tools", "drill_spin", tonal_loop([(600, 0.4), (1200, 0.15)], 0.3, 30.0, 0.15, 500, 4000), loop=1.5, max_distance=40.0,
   parameters=[dict(name="load", pitch=[1.0, 0.8], volume_db=[0.0, 3.0])])
ev("part_tools", "drill_hit", lambda rng: edges(peak(mat_impact("concrete")(rng) + band(noise(rng, 0.5), 2000, 6000) * decay(0.5, 0.1) * 0.5, 0.8)), variants=2, pitch_variation=0.06, max_distance=40.0)
ev("part_tools", "drill_break", lambda rng: mat_break("concrete")(rng), pitch_variation=0.06, max_distance=45.0)
ev("part_tools", "water_cannon_fire", lambda rng: edges(peak(mix(0.8, (thud(rng, 0.3, 40, 600, 0.05), 0), (hiss(rng, 0.7, 300, 3000, 0.02, 0.4, 0.6), 0.02)), 0.8)), max_distance=40.0)

# Containers and stations
ev("stations", "chest_place", lambda rng: mat_place("wood")(rng), **TOOL)
ev("stations", "chest_open", lambda rng: edges(peak(glide(300, 500, 0.4) * 0.2 * np.sin(np.pi * np.linspace(0, 1, n_of(0.4))) + click(rng, 900, 0.4) * 0.6, 0.6)), **QUIET)
ev("stations", "chest_close", lambda rng: edges(peak(thud(rng, 0.3, 60, 1200, 0.04), 0.6)), **QUIET)
ev("stations", "container_open", lambda rng: edges(peak(click(rng, 1200, 0.3) + hiss(rng, 0.3, 1000, 5000, 0.01, 0.2, 0.2), 0.6)), **QUIET)
ev("stations", "fabricator_start", lambda rng: beeps([392, 523, 784], 0.1, wave_fn=square, level=0.5), **TOOL)
ev("stations", "fabricator_work", pattern_loop(lambda rng: [(thud(rng, 0.2, 100, 3000, 0.03, 0.6), 0.0), (click(rng, 1800, 0.1), 0.25),
                                                            (tone(q(180, 1.0), 0.5) * 0.15, 0.5)], 0.5), loop=2.0, volume_db=-3.0, max_distance=30.0)
ev("stations", "fabricator_complete", lambda rng: beeps([784, 988, 1175, 1568], 0.09), **TOOL)
ev("stations", "refiner_start", lambda rng: beeps([262, 330, 392], 0.12, wave_fn=square, level=0.5), **TOOL)
ev("stations", "refiner_work", pattern_loop(lambda rng: [(band(noise(rng, 0.6), 100, 900) * decay(0.6, 0.3) * 0.5, 0.0), (thud(rng, 0.3, 50, 600, 0.06, 0.8), 0.4)], 1.0),
   loop=2.0, volume_db=-3.0, max_distance=30.0)
ev("stations", "refiner_complete", lambda rng: beeps([523, 659, 784, 1046], 0.1), **TOOL)
ev("stations", "bed_use", lambda rng: edges(peak(whoosh(rng, 0.4, 100, 1200) + tone(660, 0.4) * decay(0.4, 0.2) * 0.2, 0.5)), **QUIET)
ev("stations", "bed_set", lambda rng: beeps([523, 784, 1046], 0.12), **QUIET)
ev("stations", "radio_on", lambda rng: edges(peak(click(rng, 1200, 0.4) * 0.6 + band(noise(rng, 0.4), 500, 6000) * decay(0.4, 0.15) * 0.4, 0.6)), **QUIET)
ev("stations", "radio_tune", lambda rng: edges(peak(band(noise(rng, 0.5), 500, 6000) * 0.4 + glide(800, 1600, 0.5) * 0.3, 0.5)), **QUIET)
ev("stations", "radio_off", lambda rng: edges(peak(click(rng, 900, 0.2), 0.6)), **QUIET)
ev("stations", "light_on", lambda rng: edges(peak(click(rng, 2000, 0.3) + tone(120, 0.3) * decay(0.3, 0.2) * 0.2, 0.5)), **QUIET)
ev("stations", "light_off", lambda rng: click(rng, 1500, 0.1), **QUIET)
ev("stations", "beacon_activate", lambda rng: beeps([660, 880, 1320], 0.15), max_distance=60.0)
ev("stations", "beacon_ping", lambda rng: edges(peak(mix(1.2, (tone(1320, 0.5) * decay(0.5, 0.15), 0), (tone(1320, 0.5) * decay(0.5, 0.15) * 0.4, 0.3), (tone(1320, 0.5) * decay(0.5, 0.15) * 0.15, 0.6)), 0.5)), max_distance=100.0)

# Items
for mat, base in (("metal", "metal"), ("wood", "wood"), ("component", "plastic")):
    ev("items", f"pickup_{mat}", (lambda b: lambda rng: edges(peak(mix(0.35, (mat_place(b)(rng) * 0.6, 0), (glide(500, 900, 0.1) * decay(0.1, 0.05) * 0.3, 0.02)), 0.6)))(base), pitch_variation=0.06, **QUIET)
    ev("items", f"drop_{mat}", (lambda b: lambda rng: mat_impact(b)(rng) * 0.8)(base), pitch_variation=0.06, **QUIET)
ev("items", "pickup_fuel", lambda rng: edges(peak(mix(0.4, (mat_place("metal")(rng) * 0.5, 0), (band(noise(rng, 0.3), 200, 1200) * decay(0.3, 0.1) * 0.4, 0.05)), 0.6)), **QUIET)
ev("items", "pickup_battery", lambda rng: edges(peak(mix(0.35, (click(rng, 1500, 0.2), 0), (tone(880, 0.2) * decay(0.2, 0.08) * 0.3, 0.05)), 0.6)), **QUIET)
ev("items", "pickup_produce", lambda rng: edges(peak(thud(rng, 0.25, 100, 1500, 0.03, 0.6) + fit(glide(400, 700, 0.12) * decay(0.12, 0.05), n_of(0.25)) * 0.3, 0.5)),
   pitch_variation=0.08, **QUIET)
ev("items", "seed_place", lambda rng: edges(peak(band(noise(rng, 0.3), 200, 2500) * decay(0.3, 0.06), 0.5)), pitch_variation=0.1, **QUIET)
ev("items", "seed_pickup", lambda rng: edges(peak(band(noise(rng, 0.25), 400, 3500) * decay(0.25, 0.04), 0.5)), pitch_variation=0.1, **QUIET)
ev("items", "eat_crunch", lambda rng: edges(peak(mix(0.5, *[(band(noise(rng, 0.08), 1000, 7000) * decay(0.08, 0.02), i * 0.12) for i in range(4)]), 0.6)), variants=2, pitch_variation=0.1, **QUIET)
ev("items", "eat_soft", lambda rng: edges(peak(mix(0.5, *[(band(noise(rng, 0.12), 200, 1500) * decay(0.12, 0.04), i * 0.15) for i in range(3)]), 0.5)), pitch_variation=0.1, **QUIET)
ev("items", "blaster_fire", lambda rng: edges(peak(mix(0.4, (thud(rng, 0.3, 60, 2000, 0.03, 1.0), 0), (glide(600, 200, 0.12) * decay(0.12, 0.04), 0)), 0.85)), variants=2, pitch_variation=0.06, max_distance=50.0)
ev("items", "blaster_reload", lambda rng: edges(peak(mix(0.5, (click(rng, 1200, 0.1), 0), (click(rng, 900, 0.1), 0.25)), 0.6)), **QUIET)
ev("items", "scattergun_fire", lambda rng: edges(peak(mix(0.6, (boom(rng, 0.5, 0.9), 0), (band(noise(rng, 0.3), 1000, 6000) * decay(0.3, 0.05), 0)), 0.9)), pitch_variation=0.05, max_distance=60.0)
ev("items", "scattergun_pump", lambda rng: edges(peak(mix(0.5, (click(rng, 700, 0.15), 0), (click(rng, 1100, 0.15), 0.2)), 0.7)), **QUIET)
ev("items", "launcher_fire", lambda rng: edges(peak(mix(0.9, (boom(rng, 0.6, 0.9), 0), (whoosh(rng, 0.6, 300, 4000), 0.1)), 0.9)), max_distance=80.0)
ev("items", "popper_explode", lambda rng: boom(rng, 1.6), max_distance=150.0, rolloff="linear")

# Bots: each family has a voice; colour variants share their family's events.
FAMILIES = {"scuttler": 520, "rakehand": 300, "spooler": 700, "thresher": 90, "grazer": 160}
for fam, base in FAMILIES.items():
    big = fam == "thresher"
    reach = 80.0 if big else 40.0
    ev("bots", f"{fam}_idle", (lambda b: lambda rng: mix(0.6, (voice(b, 0.15, (1.0, 1.2), rng, level=0.4), 0), (voice(b * 1.1, 0.12, (1.1, 0.95), rng, level=0.3), 0.25)))(base),
       variants=2, pitch_variation=0.08, volume_variation_db=2.0, max_distance=reach)
    ev("bots", f"{fam}_alert", (lambda b: lambda rng: voice(b, 0.35, (0.8, 2.0), rng, 0.2, 0.7))(base), pitch_variation=0.05, max_distance=reach * 1.5)
    ev("bots", f"{fam}_attack", (lambda b: lambda rng: voice(b * 0.8, 0.4, (1.2, 0.7), rng, 0.8, 0.8))(base), pitch_variation=0.06, max_distance=reach)
    ev("bots", f"{fam}_death", (lambda b: lambda rng: edges(peak(mix(1.0, (voice(b, 0.7, (1.4, 0.3), rng, 0.3, 0.6), 0), (mat_break("scrap")(rng), 0.2)), 0.8)))(base),
       max_distance=reach)
ev("bots", "scuttler_charge", lambda rng: voice(520, 0.6, (0.6, 1.8), rng, 0.6, 0.7), max_distance=50.0)
ev("bots", "scuttler_stomp", lambda rng: edges(peak(thud(rng, 0.3, 40, 800, 0.05, 1.0), 0.8)), variants=2, pitch_variation=0.08, max_distance=40.0)
ev("bots", "scuttler_explode", lambda rng: boom(rng, 1.4), max_distance=120.0, rolloff="linear")
ev("bots", "rakehand_chase", engine_loop(55, 0.35), loop=1.5, max_distance=50.0)
ev("bots", "spooler_fire", lambda rng: edges(peak(mix(0.4, (glide(1400, 500, 0.15) * decay(0.15, 0.06), 0), (click(rng, 2000, 0.1), 0)), 0.75)), pitch_variation=0.06, max_distance=50.0)

# Player
STEP = dict(pitch_variation=0.1, volume_variation_db=2.0, max_distance=15.0)
rev("player", "player_hurt", [lambda: rec(f"{K}impactPunch_medium_000", take(f"{K}cloth1", 0.02, 0.5), level=0.7),
                              lambda: rec(f"{K}impactPunch_medium_001", take(f"{K}cloth2", 0.02, 0.5), level=0.7)], pitch_variation=0.06, spatial=False, loudness=-18.0)
ev("player", "player_heal", lambda rng: beeps([523, 659, 784, 1046], 0.07, level=0.4), spatial=False)
ev("player", "heartbeat_low", lambda rng, length: heartbeat(length), loop=2.0, rate=REC_SR, spatial=False,
   parameters=[dict(name="urgency", pitch=[0.9, 1.4], volume_db=[-6.0, 0.0])])
rev("player", "footstep_default", series("footstep_grass", range(4), level=0.5), **STEP, loudness=-25.0)
rev("player", "footstep_metal", [lambda n=n: rec(f"{K}footstep_concrete_{n:03d}", take(f"{K}impactPlate_light_{n:03d}", 0.0, 0.6), level=0.5, max_dur=0.4)
                                 for n in range(3)], **STEP, loudness=-25.0)
rev("player", "footstep_wood", series("footstep_wood", range(4), level=0.5), **STEP, loudness=-25.0)
rev("player", "footstep_water", [lambda n=n: rec(f"{W}splash_{n:02d}", level=0.45, max_dur=0.35) for n in (10, 9, 6)], **STEP, loudness=-25.0)
rev("player", "player_jump", [lambda: rec(f"{K}cloth3", take(f"{K}footstep_concrete_004", 0.0, 0.5), level=0.5)], pitch_variation=0.08, max_distance=15.0, loudness=-24.0)
rev("player", "player_land", [lambda: rec(f"{K}footstep_grass_004", take(f"{K}impactSoft_medium_000", 0.0, 0.8), level=0.6),
                              lambda: rec(f"{K}footstep_concrete_003", take(f"{K}impactSoft_medium_001", 0.0, 0.8), level=0.6)],
    pitch_variation=0.08, max_distance=20.0, loudness=-20.0)
rev("player", "player_land_hard", [lambda: rec(f"{K}impactSoft_heavy_000", take(f"{K}impactPunch_heavy_000", 0.0, 0.5), take(f"{K}cloth4", 0.03, 0.6), level=0.8)],
    pitch_variation=0.05, max_distance=30.0, loudness=-16.0)
rev("player", "swim_stroke", [lambda n=n: rec(f"{W}splash_{n:02d}", level=0.5, max_dur=0.5) for n in (14, 15)],
    pitch_variation=0.1, volume_variation_db=2.0, max_distance=20.0, loudness=-25.0)
rev("player", "water_enter", [lambda: rec(f"{W}splash_01", take(f"{W}splash_05", 0.04, 0.8), take(f"{W}splash_11", 0.1, 0.6), level=0.8)], pitch_variation=0.06, max_distance=30.0, loudness=-15.0)
rev("player", "water_exit", [lambda: rec(f"{W}splash_02", take(f"{W}splash_09", 0.3, 0.5), level=0.6)], pitch_variation=0.06, max_distance=20.0, loudness=-21.0)
rev("player", "tool_equip", [lambda: rec(f"{K}handleSmallLeather", take(f"{K}metalClick", 0.05, 0.7), level=0.45)],
    pitch_variation=0.06, max_distance=12.0, cooldown=0.05, loudness=-24.0)

# UI: not positional.
UI = dict(bus="ui")
ev("ui", "ui_click", lambda rng: click(rng, 2400, 0.05), **UI, cooldown=0.03)
ev("ui", "ui_hover", lambda rng: edges(peak(tone(1800, 0.03) * decay(0.03, 0.008), 0.25)), **UI, cooldown=0.05, volume_db=-6.0)
ev("ui", "ui_open", lambda rng: sweep_shot(400, 900, 0.15, rng, 0.0, 0.4), **UI)
ev("ui", "ui_close", lambda rng: sweep_shot(900, 400, 0.15, rng, 0.0, 0.4), **UI)
ev("ui", "ui_success", lambda rng: beeps([784, 1175], 0.08, level=0.45), **UI)
ev("ui", "ui_error", lambda rng: beeps([330, 247], 0.1, wave_fn=square, level=0.35), **UI, cooldown=0.1)
ev("ui", "inventory_open", lambda rng: edges(peak(whoosh(rng, 0.2, 400, 4000) + click(rng, 1500, 0.2) * 0.5, 0.45)), **UI)
ev("ui", "inventory_close", lambda rng: edges(peak(whoosh(rng, 0.18, 300, 3000) + click(rng, 1100, 0.18) * 0.5, 0.45)), **UI)
ev("ui", "inventory_full", lambda rng: beeps([440, 440], 0.1, wave_fn=square, level=0.35), **UI, cooldown=0.3)
ev("ui", "craft_start", lambda rng: beeps([523, 659], 0.07, level=0.4), **UI)
ev("ui", "craft_complete", lambda rng: beeps([659, 784, 1046], 0.08, level=0.45), **UI)

# World and ambience
ev("ambient", "raid_horn", lambda rng: edges(peak((saw(110, 2.5) * 0.6 + saw(138.6, 2.5) * 0.4) * np.minimum(1, np.arange(n_of(2.5)) / n_of(0.4))
                                                  * np.minimum(1, (n_of(2.5) - np.arange(n_of(2.5))) / n_of(0.6)), 0.8), 20),
   max_distance=400.0, min_distance=10.0, rolloff="linear")
AMB = dict(bus="ambient", spatial=False)
ev("ambient", "wind_light", noise_loop(100, 900, 0.25, 0.5), loop=6.0, volume_db=-8.0, **AMB)
ev("ambient", "wind_strong", noise_loop(80, 2500, 0.4, 0.6), loop=6.0, volume_db=-4.0, **AMB)
ev("ambient", "birds_day", chirps_loop(2500, 4500, 3), loop=6.0, volume_db=-10.0, **AMB)
ev("ambient", "night_insects", tonal_loop([(4200, 0.3), (4400, 0.2)], 0.9, 18.0, 0.02, 3000, 6000), loop=4.0, volume_db=-14.0, **AMB)
ev("ambient", "water_lap", noise_loop(100, 1200, 0.5, 0.7), loop=6.0, bus="ambient", max_distance=30.0, rolloff="linear", min_distance=3.0)
ev("ambient", "machinery_hum", tonal_loop([(60, 0.5), (120, 0.3), (180, 0.15), (240, 0.08)], 0.05, 1.0, 0.04, 200, 2000), loop=2.0,
   bus="ambient", max_distance=30.0, rolloff="linear", min_distance=3.0)

# Music and radio stations: original placeholder progressions.
MUSIC = dict(bus="music", volume_db=-6.0)
ev("music", "music_explore", music_loop([chord("ceg"), chord("aCe".lower()), chord("fac"), chord("gbd")], 90, False), loop=10.667, **MUSIC)
ev("music", "music_raid", music_loop([chord("ace", 2), chord("ace", 2), chord("fac", 2), chord("gbd", 2)], 140, True), loop=6.857, **MUSIC)
ev("music", "music_menu", music_loop([chord("fac"), chord("ceg"), chord("dfa"), chord("bdf")], 70, False), loop=13.714, **MUSIC)
STATIONS = [("gbd", "ceg", 100), ("dfa", "gbd", 120), ("ace", "dfa", 85), ("ceg", "fac", 110), ("egb", "ace", 95), ("bdf", "egb", 130)]
for i, (a, b, bpm) in enumerate(STATIONS, 1):
    bars = 2
    ev("music", f"radio_loop_{i:02d}", music_loop([chord(a), chord(b)], bpm, i % 2 == 0), loop=round(bars * 4 * 60.0 / bpm, 3),
       bus="music", spatial=True, min_distance=2.0, max_distance=25.0, volume_db=-4.0)

# ---- Output ------------------------------------------------------------------------------------

GROUP_ORDER = ["materials", "building", "engines", "joints", "flight", "logic", "part_tools", "stations", "items", "bots", "player", "ui", "ambient", "music"]
KEY_ORDER = ["id", "description", "clips", "bus", "loop", "volume_db", "pitch", "volume_variation_db", "pitch_variation", "spatial",
             "min_distance", "max_distance", "rolloff", "parameters", "max_instances", "cooldown"]
PARAM_ORDER = ["name", "min", "max", "default", "pitch", "volume_db"]


def write_wav(path, x, rate=SR):
    data = (np.clip(x, -1, 1) * 32767).astype("<i2").tobytes()
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(data)


def write_ogg(path, x, rate=SR):
    with tempfile.TemporaryDirectory() as tmp:
        src = os.path.join(tmp, "in.wav")
        write_wav(src, x, rate)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", src, "-c:a", "libvorbis", "-q:a", "3", "-map_metadata", "-1",
                        "-fflags", "+bitexact", "-flags:a", "+bitexact", path], check=True)


def decode(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-f", "f32le", "-"], check=True, capture_output=True).stdout
    return np.frombuffer(raw, "<f4").astype(np.float64)


def fade_out(x, rate, ms=15):
    """Fades a one-shot that is still sounding at its last sample, so it never ends on a click."""
    k = min(len(x) // 2, round(ms / 1000 * rate))
    if float(np.max(np.abs(x[-k:]))) <= 0.1:
        return x
    x = x.copy()
    x[-k:] *= np.linspace(1, 0, k)
    return x


def write_clip(group, name, x, rate, is_loop):
    """Writes Vorbis, checked by decoding it: a peak Vorbis pushed past full scale is lowered and the clip rewritten,
    and a loop whose seam Vorbis smeared (its end no longer meets its start) is kept as 16-bit WAV instead. Loops come
    back sample-exact; a one-shot gets silence on its end, since Vorbis can cut a few milliseconds off it."""
    rel = f"sounds/{group}/{name}.ogg"
    path = os.path.join(ROOT, "mods", "vanilla", rel)
    if not is_loop:
        x = np.concatenate([x, np.zeros(512)])  # the encoder can drop up to 256 samples off the end; let it drop silence
    write_ogg(path, x, rate)
    y = decode(path)
    if float(np.max(np.abs(y))) > 0.98:
        x = x * (0.95 / float(np.max(np.abs(y))))
        write_ogg(path, x, rate)
        y = decode(path)
    if is_loop and abs(y[-1] - y[0]) > abs(x[-1] - x[0]) + 0.05:
        os.remove(path)
        rel = f"sounds/{group}/{name}.wav"
        write_wav(os.path.join(ROOT, "mods", "vanilla", rel), x, rate)
    return rel


def num(v):
    """Formats a number the way System.Text.Json writes a float: shortest round-trip, no trailing .0."""
    f = np.float32(v)
    if float(f) == int(f):
        return str(int(f))
    return np.format_float_positional(f, unique=True, trim="-")


def to_json(value, indent=0):
    pad = "  " * indent
    inner = "  " * (indent + 1)
    if isinstance(value, dict):
        if not value:
            return "{}"
        items = [f'{inner}"{k}": {to_json(v, indent + 1)}' for k, v in value.items()]
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    if isinstance(value, list):
        if not value:
            return "[]"
        return "[\n" + ",\n".join(inner + to_json(v, indent + 1) for v in value) + "\n" + pad + "]"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return num(value)
    return json.dumps(value, ensure_ascii=False)


def ordered(d, order):
    return {k: d[k] for k in order if k in d}


def main():
    check = "--check" in sys.argv
    ids = [e[1] for e in EVENTS]
    dupes = sorted({i for i in ids if ids.count(i) > 1})
    if dupes:
        sys.exit(f"duplicate ids: {dupes}")
    if check:
        for group, sid, _, _, variants, is_loop, _ in EVENTS:
            print(f"{group}/{sid}{' (loop)' if is_loop else ''}{f' x{variants}' if variants > 1 else ''}")
        print(f"{len(EVENTS)} events")
        return
    if shutil.which("ffmpeg") is None:
        sys.exit("ffmpeg (with libvorbis) is needed to write the loops.")
    if os.path.isdir(SOUNDS):
        shutil.rmtree(SOUNDS)
    banks = {g: [] for g in GROUP_ORDER}
    for group, sid, recipe, opts, variants, is_loop, length in EVENTS:
        folder = os.path.join(SOUNDS, group)
        os.makedirs(folder, exist_ok=True)
        clips = []
        for v in range(variants):
            rng = np.random.default_rng(zlib.crc32(f"{sid}#{v}".encode()))
            name = sid if v == 0 else f"{sid}_{v + 1}"
            make = recipe[v] if isinstance(recipe, list) else recipe
            x = peak(make(rng, length), 0.7) if is_loop else make(rng)
            if not np.all(np.isfinite(x)) or float(np.max(np.abs(x))) < 0.05:
                sys.exit(f"{sid}: the recipe made silence or NaNs")
            rate = opts.get("rate", SR)
            if "loudness" in opts:
                x = level_to(x, rate, opts["loudness"])
            if not is_loop:
                x = fade_out(x, rate)
            if group == "ui":
                rel = f"sounds/{group}/{name}.wav"
                write_wav(os.path.join(ROOT, "mods", "vanilla", rel), x)
            else:
                rel = write_clip(group, name, x, rate, is_loop)
            clips.append(rel)
        entry = {"id": sid, "clips": clips}
        if opts.get("bus"):
            entry["bus"] = opts["bus"]
        if is_loop:
            entry["loop"] = True
        for key in ("volume_db", "volume_variation_db", "pitch_variation", "spatial", "min_distance", "max_distance", "rolloff",
                    "max_instances", "cooldown"):
            if key in opts:
                entry[key] = opts[key]
        if "parameters" in opts:
            entry["parameters"] = [ordered({"name": p["name"], **{k: v for k, v in p.items() if k != "name"}}, PARAM_ORDER) for p in opts["parameters"]]
        banks[group].append(ordered(entry, KEY_ORDER))
    for group in GROUP_ORDER:
        with open(os.path.join(SOUNDS, f"{group}.json"), "w", encoding="utf-8", newline="\n") as f:
            f.write(to_json({"events": banks[group]}) + "\n")
    print(f"wrote {len(EVENTS)} events in {len(GROUP_ORDER)} files to {os.path.relpath(SOUNDS, ROOT)}")


if __name__ == "__main__":
    main()
