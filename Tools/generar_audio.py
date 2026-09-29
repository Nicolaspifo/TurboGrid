#!/usr/bin/env python3
"""
Generador de audio procedural de Turbo Grid.

Crea todos los efectos de sonido y la musica del juego por sintesis (sin archivos
externos, sin derechos de autor). Uso, desde la carpeta TurboGrid:

    python3 Tools/generar_audio.py

Requiere numpy. Escribe WAV mono 16-bit en Assets/Resources/Audio/SFX y /Musica.
Si quieres reemplazar un sonido por uno grabado, pon un archivo con el MISMO nombre
(puede ser .wav, .ogg o .mp3) y borra el .wav generado.
"""
import os
import wave

import numpy as np

SR = 44100
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Audio")
SFX_DIR = os.path.join(ROOT, "SFX")
MUSIC_DIR = os.path.join(ROOT, "Musica")
rng = np.random.default_rng(7)


# ----------------------------------------------------------------------------
# Utilidades
# ----------------------------------------------------------------------------

def t_axis(seconds):
    return np.arange(int(seconds * SR)) / SR


def save(path, signal, peak=0.89):
    signal = np.asarray(signal, dtype=np.float64)
    m = np.max(np.abs(signal)) if signal.size else 0
    if m > 0:
        signal = signal / m * peak
    # micro fundido para evitar clics en los bordes de los efectos
    data = np.clip(signal, -1, 1)
    pcm = (data * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"  {os.path.relpath(path, ROOT)}  ({len(data) / SR:.2f} s)")


def fade_edges(x, fade_in=0.002, fade_out=0.01):
    x = x.copy()
    n_in = max(1, int(fade_in * SR))
    n_out = max(1, int(fade_out * SR))
    x[:n_in] *= np.linspace(0, 1, n_in)
    x[-n_out:] *= np.linspace(1, 0, n_out)
    return x


def env_exp(n, decay):
    """Envolvente exponencial (decay en segundos hasta ~-60 dB)."""
    t = np.arange(n) / SR
    return np.exp(-t * 6.9 / max(decay, 1e-4))


def adsr(n, a=0.005, d=0.05, s=0.7, r=0.05):
    env = np.ones(n) * s
    na, nd, nr = int(a * SR), int(d * SR), int(r * SR)
    na = min(na, n)
    env[:na] = np.linspace(0, 1, na) if na else env[:na]
    end_d = min(na + nd, n)
    if end_d > na:
        env[na:end_d] = np.linspace(1, s, end_d - na)
    if nr > 0 and nr < n:
        env[-nr:] *= np.linspace(1, 0, nr)
    return env


def phase_from_freq(freq):
    """Fase integrada para frecuencias que cambian en el tiempo (glides)."""
    return 2 * np.pi * np.cumsum(freq) / SR


def osc(kind, freq, n=None, harmonics=12):
    """Osciladores con pocos armonicos (suenan limpios, sin aliasing)."""
    if np.isscalar(freq):
        freq = np.full(n, float(freq))
    ph = phase_from_freq(freq)
    if kind == "sine":
        return np.sin(ph)
    out = np.zeros_like(ph)
    nyq = SR / 2
    for k in range(1, harmonics + 1):
        if kind == "square" and k % 2 == 0:
            continue
        if kind == "tri" and k % 2 == 0:
            continue
        mask = (freq * k) < nyq
        if kind == "saw":
            amp = 1.0 / k
        elif kind == "square":
            amp = 1.0 / k
        else:  # tri
            amp = (1.0 / (k * k)) * (-1) ** ((k - 1) // 2)
        out += amp * np.sin(k * ph) * mask
    return out


def noise(n):
    return rng.uniform(-1, 1, n)


def lowpass(x, cutoff):
    """Filtro de un polo. cutoff puede ser un numero o un arreglo (barrido)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=np.float64), x.shape)
    a = 1 - np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def bandpass(x, low, high):
    return lowpass(highpass(x, low), high)


def note_freq(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def pad_to(x, seconds):
    n = int(seconds * SR)
    if len(x) >= n:
        return x[:n]
    return np.concatenate([x, np.zeros(n - len(x))])


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def delay(x, seconds, feedback=0.35, taps=3):
    out = x.copy()
    d = int(seconds * SR)
    gain = feedback
    for k in range(1, taps + 1):
        shifted = np.zeros(len(x) + d * k)
        shifted[d * k:] = x * gain
        out = mix(out, shifted)
        gain *= feedback
    return out


# ----------------------------------------------------------------------------
# Efectos de sonido
# ----------------------------------------------------------------------------

def sfx_gasolina():
    # "moneda": dos notas rapidas hacia arriba + brillo
    a = osc("square", note_freq(83), int(0.07 * SR), 7) * env_exp(int(0.07 * SR), 0.2)
    b_n = int(0.32 * SR)
    b = osc("square", note_freq(88), b_n, 7) * env_exp(b_n, 0.3)
    sparkle = osc("sine", note_freq(100), b_n) * env_exp(b_n, 0.12) * 0.3
    return fade_edges(np.concatenate([a, b + sparkle]) * 0.8)


def sfx_recoger_drs():
    # arpegio ascendente brillante (azul/turbo)
    notes = [72, 76, 79, 84, 88, 91]
    parts = []
    for i, m in enumerate(notes):
        n = int(0.055 * SR)
        tone = osc("tri", note_freq(m), n, 9) + 0.4 * osc("square", note_freq(m + 12), n, 5)
        parts.append(tone * env_exp(n, 0.2))
    x = np.concatenate(parts)
    tail_n = int(0.35 * SR)
    tail = osc("sine", note_freq(96), tail_n) * env_exp(tail_n, 0.3) * 0.5
    x = np.concatenate([x, tail])
    return fade_edges(delay(x, 0.09, 0.3, 2))


def sfx_activar_drs():
    # "whoosh" de turbo: ruido con filtro que sube + tono que acelera
    dur = 1.6
    n = int(dur * SR)
    t = t_axis(dur)
    sweep = 300 + 7000 * (t / dur) ** 1.6
    whoosh = lowpass(noise(n), sweep) * np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 0.8
    spool_f = 180 + 950 * (1 - np.exp(-t * 2.2))
    spool = np.tanh(2.5 * osc("saw", spool_f, n, 10)) * 0.35 * adsr(n, 0.05, 0.3, 0.8, 0.5)
    whistle = osc("sine", spool_f * 3.2, n) * 0.12 * adsr(n, 0.2, 0.3, 0.7, 0.6)
    return fade_edges(whoosh * 0.9 + spool + whistle, 0.01, 0.2)


def sfx_recoger_pinchazo():
    # "blip" malvado descendente (morado)
    n1 = int(0.13 * SR)
    f1 = np.linspace(note_freq(79), note_freq(74), n1)
    a = osc("square", f1, None, 7) * env_exp(n1, 0.25)
    n2 = int(0.3 * SR)
    f2 = np.linspace(note_freq(70), note_freq(63), n2)
    b = osc("square", f2, None, 7) * env_exp(n2, 0.35)
    wobble = 1 + 0.25 * np.sin(2 * np.pi * 18 * np.arange(n2) / SR)
    return fade_edges(np.concatenate([a, b * wobble]) * 0.8)


def sfx_lanzar_pinchazo():
    # "¡pum!" + silbido de aire
    n = int(0.9 * SR)
    t = np.arange(n) / SR
    pop = noise(n) * env_exp(n, 0.05)
    thump = osc("sine", 140 * np.exp(-t * 18) + 45, None) * env_exp(n, 0.18)
    hiss = bandpass(noise(n), 2500, 9000) * np.clip((t - 0.04) / 0.05, 0, 1) * env_exp(n, 0.8) * 0.6
    return fade_edges(pop * 0.8 + thump * 1.0 + hiss, 0.001, 0.08)


def sfx_pinchado():
    # llanta desinflandose: silbido que baja + "flap" de la llanta + tono triste
    dur = 1.5
    n = int(dur * SR)
    t = t_axis(dur)
    hiss_cut = 7000 * np.exp(-t * 1.4) + 600
    hiss = lowpass(highpass(noise(n), 900), hiss_cut) * env_exp(n, 1.6)
    flap = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * (14 - 6 * t / dur) * t))
    thud = lowpass(noise(n), 250) * flap * env_exp(n, 1.2) * 1.6
    sad_f = 420 * np.exp(-t * 1.2) + 90
    sad = osc("tri", sad_f, None, 7) * adsr(n, 0.02, 0.1, 0.6, 0.4) * 0.45
    return fade_edges(hiss * 0.8 + thud + sad, 0.003, 0.2)


def sfx_choque():
    n = int(0.45 * SR)
    t = np.arange(n) / SR
    thump = osc("sine", 110 * np.exp(-t * 12) + 38, None) * env_exp(n, 0.25)
    crack = lowpass(noise(n), 3000) * env_exp(n, 0.06)
    ring = (np.sin(2 * np.pi * 1310 * t) + 0.7 * np.sin(2 * np.pi * 2170 * t) + 0.4 * np.sin(2 * np.pi * 3390 * t))
    ring *= env_exp(n, 0.2) * 0.25
    return fade_edges(thump * 1.2 + crack * 0.8 + ring, 0.001, 0.05)


def sfx_frenazo():
    # chirrido de llantas: tonos agudos inestables + ruido filtrado
    dur = 0.9
    n = int(dur * SR)
    t = t_axis(dur)
    jitter = lowpass(noise(n), 30) * 900
    squeal = sum(osc("sine", f + jitter * (0.6 + 0.2 * i), None) * (0.6 ** i) for i, f in enumerate([2350, 2980, 3620]))
    rough = bandpass(noise(n), 1800, 5000) * 0.6
    env = adsr(n, 0.02, 0.1, 0.8, 0.35) * (0.8 + 0.2 * np.sin(2 * np.pi * 7 * t))
    return fade_edges((squeal * 0.5 + rough) * env, 0.01, 0.2)


def sfx_cuenta_beep():
    n = int(0.28 * SR)
    x = osc("square", note_freq(74), n, 5) * 0.6 + osc("sine", note_freq(74), n) * 0.5
    return fade_edges(x * adsr(n, 0.003, 0.05, 0.7, 0.08))


def sfx_cuenta_go():
    n = int(0.75 * SR)
    x = osc("square", note_freq(86), n, 5) * 0.55 + osc("sine", note_freq(86), n) * 0.5 + osc("sine", note_freq(98), n) * 0.2
    return fade_edges(x * adsr(n, 0.003, 0.1, 0.75, 0.3))


def sfx_boton():
    n = int(0.06 * SR)
    t = np.arange(n) / SR
    click = noise(n) * env_exp(n, 0.006) * 0.5
    tone = np.sin(2 * np.pi * 1750 * t) * env_exp(n, 0.04)
    return fade_edges(click + tone, 0.0005, 0.005)


def sfx_jugador_listo():
    parts = []
    for m, d in [(79, 0.1), (86, 0.45)]:
        n = int(d * SR)
        bell = osc("sine", note_freq(m), n) + 0.35 * osc("sine", note_freq(m) * 2.76, n) + 0.15 * osc("sine", note_freq(m) * 5.4, n)
        parts.append(bell * env_exp(n, d * 1.4))
    return fade_edges(np.concatenate(parts))


def sfx_jugador_entra():
    n = int(0.22 * SR)
    t = np.arange(n) / SR
    f = 280 + 900 * np.clip(t / 0.07, 0, 1)
    pop = osc("sine", f, None) * env_exp(n, 0.16)
    return fade_edges(pop + 0.3 * noise(n) * env_exp(n, 0.01))


def reverb(x, decay=1.6, wet=0.45, predelay=0.02, brightness=5000):
    """Reverberacion por convolucion con una respuesta de sala sintetica."""
    n_ir = int(decay * SR)
    t = np.arange(n_ir) / SR
    ir = noise(n_ir) * np.exp(-t * 6.9 / decay)
    ir = lowpass(ir, brightness)
    # reflexiones tempranas (paredes cercanas)
    for d, g in [(0.011, 0.5), (0.019, 0.4), (0.029, 0.33), (0.041, 0.25)]:
        k = int(d * SR)
        if k < n_ir:
            ir[k] += g
    pre = np.zeros(int(predelay * SR))
    ir = np.concatenate([pre, ir])
    ir /= np.sqrt(np.sum(ir ** 2))
    size = 1
    while size < len(x) + len(ir):
        size *= 2
    wet_sig = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[: len(x) + len(ir)]
    dry = np.concatenate([x, np.zeros(len(ir))])
    wet_sig *= np.max(np.abs(dry)) / (np.max(np.abs(wet_sig)) + 1e-9)
    return dry * (1 - wet * 0.5) + wet_sig * wet


def sfx_aplausos():
    # Publico aplaudiendo en un estadio: cientos de palmadas con timbre distinto,
    # un "uuuh" de fondo y reverberacion de estadio.
    dur = 3.6
    n = int(dur * SR)
    out = np.zeros(n)
    t = t_axis(dur)

    # Densidad: sube rapido, se mantiene y se apaga poco a poco.
    density = np.clip(t / 0.35, 0, 1) * np.clip((dur - 0.3 - t) / 1.6, 0, 1)
    starts = []
    while len(starts) < 1100:
        c = rng.uniform(0, dur - 0.05)
        if rng.uniform() < density[int(c * SR)]:
            starts.append(c)

    for c in starts:
        ln = int(rng.uniform(0.006, 0.016) * SR)
        burst = noise(ln) * env_exp(ln, rng.uniform(0.008, 0.02))
        # cada persona suena distinto (manos mas graves o mas agudas)
        burst = bandpass(burst, rng.uniform(500, 1300), rng.uniform(2200, 5200))
        s0 = int(c * SR)
        out[s0:s0 + ln] += burst * rng.uniform(0.35, 1.0)

    # "Uuuh" del publico: ruido grave con formantes que ondulan
    cheer = lowpass(noise(n), 900) - lowpass(noise(n), 250)
    swell = np.clip(t / 0.5, 0, 1) * np.clip((dur - t) / 2.0, 0, 1)
    wobble = 0.75 + 0.25 * np.sin(2 * np.pi * 0.9 * t) * np.sin(2 * np.pi * 0.37 * t + 1)
    out += cheer * swell * wobble * 0.9

    out = reverb(out, decay=1.8, wet=0.55, predelay=0.025, brightness=4500)
    # recortar el silencio del final de la cola de reverberacion
    loud = np.nonzero(np.abs(out) > np.max(np.abs(out)) * 0.004)[0]
    if loud.size:
        out = out[: loud[-1] + int(0.05 * SR)]
    return fade_edges(out, 0.01, 0.4)


# ----------------------------------------------------------------------------
# Musica (loops perfectos: la cola de las notas se "envuelve" al inicio)
# ----------------------------------------------------------------------------

class Track:
    def __init__(self, bpm, bars, beats_per_bar=4):
        self.bpm = bpm
        self.beat = 60.0 / bpm
        self.length = bars * beats_per_bar * self.beat
        self.n = int(round(self.length * SR))
        self.buf = np.zeros(self.n + int(3 * SR))

    def add(self, start_beats, signal, gain=1.0):
        s = int(round(start_beats * self.beat * SR))
        self.buf[s:s + len(signal)] += signal * gain

    def render(self):
        loop = self.buf[: self.n].copy()
        tail = self.buf[self.n:]
        # envolver la cola al inicio -> el loop no tiene corte
        k = min(len(tail), self.n)
        loop[:k] += tail[:k]
        return loop


def kick(dur=0.22):
    n = int(dur * SR)
    t = np.arange(n) / SR
    body = osc("sine", 160 * np.exp(-t * 28) + 48, None) * env_exp(n, dur)
    click = noise(n) * env_exp(n, 0.004) * 0.3
    return body + click


def snare(dur=0.18):
    n = int(dur * SR)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * 190 * t) * env_exp(n, 0.07)
    rattle = highpass(noise(n), 1200) * env_exp(n, dur)
    return tone * 0.6 + rattle * 0.8


_HAT_CACHE = {}


def hat(dur=0.045, open_=False):
    key = (dur, open_)
    if key not in _HAT_CACHE:
        d = 0.2 if open_ else dur
        n = int(d * SR)
        _HAT_CACHE[key] = highpass(noise(n), 6000) * env_exp(n, d)
    return _HAT_CACHE[key]


def synth_note(kind, midi, seconds, attack=0.005, release=0.06, harmonics=10, vibrato=0.0):
    n = int(seconds * SR)
    f = note_freq(midi)
    if vibrato:
        t = np.arange(n) / SR
        f = f * (1 + vibrato * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / 0.15, 0, 1))
    x = osc(kind, f, n, harmonics)
    return x * adsr(n, attack, 0.08, 0.7, release)


# Progresion en La menor: Am - F - C - G (raiz, tercera, quinta)
CHORDS = [(57, 60, 64), (53, 57, 60), (48, 52, 55), (55, 59, 62)]


def music_race():
    bars = 16
    tr = Track(bpm=150, bars=bars)
    k, s = kick(), snare()
    for bar in range(bars):
        chord = CHORDS[bar % 4]
        root = chord[0] - 12
        b0 = bar * 4
        # bateria
        for beat in range(4):
            tr.add(b0 + beat, k, 0.9)
            tr.add(b0 + beat + 0.5, hat(), 0.25)
            tr.add(b0 + beat, hat(), 0.12)
        tr.add(b0 + 1, s, 0.55)
        tr.add(b0 + 3, s, 0.55)
        if bar % 4 == 3:  # redoble al final de cada frase
            for i in range(4):
                tr.add(b0 + 3.5 + i * 0.125, s, 0.25 + i * 0.08)
        # bajo en corcheas (raiz / octava)
        for e in range(8):
            m = root if e % 2 == 0 else root + 12
            tr.add(b0 + e * 0.5, synth_note("saw", m, 0.22, 0.003, 0.04, 8), 0.32)
        # arpegio en semicorcheas
        pattern = [0, 1, 2, 1, 2, 0, 1, 2]
        for i in range(16):
            m = chord[pattern[i % 8]] + 12
            tr.add(b0 + i * 0.25, synth_note("square", m, 0.11, 0.002, 0.03, 6), 0.1)
    # melodia en la segunda mitad
    melody = [
        (76, 1), (74, 0.5), (72, 0.5), (74, 1), (76, 1),
        (77, 1.5), (76, 0.5), (74, 2),
        (72, 1), (74, 0.5), (76, 0.5), (79, 1.5), (76, 0.5),
        (74, 1), (72, 1), (71, 2),
        (76, 1), (79, 0.5), (81, 0.5), (79, 1), (76, 1),
        (77, 1), (76, 1), (74, 2),
        (72, 0.5), (74, 0.5), (76, 1), (79, 1), (84, 1),
        (83, 1), (81, 1), (79, 2),
    ]
    beat = 32
    for m, d in melody:
        tr.add(beat, synth_note("square", m, d * tr.beat * 0.95, 0.01, 0.08, 7, vibrato=0.006), 0.22)
        beat += d
    return tr.render()


def music_menu():
    bars = 16
    tr = Track(bpm=108, bars=bars)
    k = kick(0.3)
    for bar in range(bars):
        chord = CHORDS[bar % 4]
        b0 = bar * 4
        tr.add(b0, k, 0.7)
        tr.add(b0 + 2, k, 0.55)
        tr.add(b0 + 1, snare(0.2), 0.3)
        tr.add(b0 + 3, snare(0.2), 0.3)
        for beat in range(4):
            tr.add(b0 + beat + 0.5, hat(), 0.18)
        # colchon de acordes (entrada suave)
        for m in chord:
            tr.add(b0, synth_note("tri", m, 4 * tr.beat, 0.25, 0.4, 9), 0.12)
        # bajo en negras
        for beat in range(4):
            tr.add(b0 + beat, synth_note("tri", chord[0] - 12, 0.9 * tr.beat, 0.01, 0.1, 9), 0.35)
        # arpegio suave en corcheas (solo en la segunda mitad)
        if bar >= 8:
            for e in range(8):
                m = chord[e % 3] + 24
                tr.add(b0 + e * 0.5, synth_note("sine", m, 0.2, 0.005, 0.08), 0.1)
    return tr.render()


def music_victory():
    beat = 60.0 / 130
    tr = Track(bpm=130, bars=3)
    fanfare = [(72, 0, 1 / 3), (76, 1 / 3, 1 / 3), (79, 2 / 3, 1 / 3), (84, 1, 1.5), (79, 2.5, 0.5), (84, 3, 3)]
    for m, start, d in fanfare:
        tone = synth_note("square", m, d * beat, 0.005, 0.15, 7, vibrato=0.004) * 0.6
        tone += synth_note("saw", m - 12, d * beat, 0.005, 0.15, 6) * 0.3
        tr.add(start, tone, 0.5)
    for m in (60, 64, 67, 72):
        tr.add(3, synth_note("tri", m, 3 * beat, 0.02, 0.8, 9), 0.2)
    for i in range(8):
        tr.add(2 + i * 0.125, snare(0.12), 0.15 + i * 0.05)
    tr.add(3, kick(0.4), 1.0)
    tr.add(3, hat(open_=True), 0.4)
    out = tr.buf[: int(8 * beat * SR)]
    return fade_edges(out, 0.001, 0.8)


# ----------------------------------------------------------------------------

def main():
    os.makedirs(SFX_DIR, exist_ok=True)
    os.makedirs(MUSIC_DIR, exist_ok=True)

    print("Efectos:")
    effects = {
        "gasolina": sfx_gasolina,
        "recoger_drs": sfx_recoger_drs,
        "activar_drs": sfx_activar_drs,
        "recoger_pinchazo": sfx_recoger_pinchazo,
        "lanzar_pinchazo": sfx_lanzar_pinchazo,
        "pinchado": sfx_pinchado,
        "choque": sfx_choque,
        "frenazo": sfx_frenazo,
        "cuenta_beep": sfx_cuenta_beep,
        "cuenta_go": sfx_cuenta_go,
        "boton": sfx_boton,
        "jugador_listo": sfx_jugador_listo,
        "jugador_entra": sfx_jugador_entra,
        "aplausos": sfx_aplausos,
    }
    for name, fn in effects.items():
        save(os.path.join(SFX_DIR, name + ".wav"), fn())

    print("Musica:")
    save(os.path.join(MUSIC_DIR, "musica_carrera.wav"), music_race(), peak=0.8)
    save(os.path.join(MUSIC_DIR, "musica_menu.wav"), music_menu(), peak=0.75)
    save(os.path.join(MUSIC_DIR, "musica_victoria.wav"), music_victory(), peak=0.85)


if __name__ == "__main__":
    main()
