"""The troll MIDI, every note kept, snapped to a 90 BPM 1/16 grid, with a pounding deep bass and heavy drums."""
import importlib.util, math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
from readmidi import read, notes
spec = importlib.util.spec_from_file_location('assets', Path(__file__).resolve().parents[2] / 'tools' / 'build-assets.py')
a = importlib.util.module_from_spec(spec); spec.loader.exec_module(a)

BPM, PHASE = 90.0, 0.0722
BEAT = 60 / BPM
fmt, division, tracks = read(sys.argv[1])
sec = 0.5 / division
snap = lambda t: max(0.0, round((t - PHASE) / BEAT * 4) / 4)

lead, bass = [], []
for name, events in tracks:
    for start, dur, pitch, vel in notes(events):
        b0, b1 = snap(start * sec), snap((start + dur) * sec)
        (bass if 'bass' in name.lower() else lead).append((b0, max(0.25, b1 - b0), pitch))
end = max(b + d for b, d, _ in lead + bass)
bars = math.ceil(end / 4)
song = a.Song(bpm=BPM, beats=bars * 4)
total = bars * 4

# Lead: quiet, in the background.
for b, d, p in lead:
    song.note(b, p, d, a.pulse(0.25), 0.065, gate=0.9)

# Bass: an octave down, 8ths pounded on each note until the next one (gaps filled).
fat = lambda ph: 0.55 * (1.0 if ph % 1.0 < 0.5 else -1.0) + 0.45 * a.triangle(ph)
sub = a.sine
bass.sort()
starts = [b for b, _, _ in bass]
for i, (b, d, p) in enumerate(bass):
    until = starts[i + 1] if i + 1 < len(bass) else total
    t = b
    while t < until - 1e-6:
        length = min(0.5, until - t)
        song.note(t, p - 12, length, fat, 0.2, gate=0.8, decay=3)
        song.note(t, p - 24, length, sub, 0.22, gate=0.85)
        t += length
# Before the first bass note, hold the first bass pitch too.
if bass and starts[0] > 0:
    t, p = 0.0, bass[0][2]
    while t < starts[0] - 1e-6:
        song.note(t, p - 12, 0.5, fat, 0.2, gate=0.8, decay=3)
        song.note(t, p - 24, 0.5, sub, 0.22, gate=0.85)
        t += 0.5

# Drums: a deep, booming kick and a big snare.
kick = a.tone(lambda t: 42 + 90 * math.exp(-t * 28), 0.24, a.sine, 0.75, release=0.08)
click = a.noise(0.012, 0.25, decay=200, seed=3, lowpass=0.2)
kick = a.mix(kick, click)
snare = a.mix(a.noise(0.2, 0.45, decay=16, seed=31, lowpass=0.3), a.tone(lambda t: 190 - 300 * t, 0.08, a.sine, 0.3, release=0.04))
hat = a.noise(0.03, 0.08, decay=120, seed=37, lowpass=0.05)
crash = a.noise(1.0, 0.3, decay=4.5, seed=41, lowpass=0.08)
PATTERNS = ["k.h.s.h.k.hks.h.", "k.h.s.h.k.h.s.hk"]
for bar in range(bars):
    pattern = PATTERNS[bar % 2]
    for step, hit in enumerate(pattern):
        at = bar * 4 + step / 4
        song.place(at, {'k': kick, 's': snare, 'h': hat}.get(hit, []))
    if bar % 4 == 0:
        song.place(bar * 4, crash)

# Glue: a soft clip so the loud parts punch instead of crackling.
peak = max(abs(x) for x in song.samples)
drive = 1.6
out = [math.tanh(drive * x / peak) / math.tanh(drive) * 0.95 for x in song.samples]
a.write_wav(Path(sys.argv[2]), out)
print('bars', bars, 'seconds', round(len(out) / a.RATE, 1), 'raw peak', round(peak, 3))
