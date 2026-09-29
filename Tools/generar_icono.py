#!/usr/bin/env python3
"""
Genera el icono de la app a partir del logo del diseñador (PANTALLA4/Logo.png).

    python3 Tools/generar_icono.py

Crea en Assets/Recursos/Icono/:
  - icono_app.png     1024x1024 completo, sin transparencia (iOS y Android clasico)
  - icono_fondo.png   1024x1024 fondo (capa trasera del icono adaptativo de Android)
  - icono_frente.png  1024x1024 logo con transparencia, dentro de la zona segura (capa delantera)
Requiere numpy y macOS (usa 'sips' para leer y escalar el logo).
"""
import os
import subprocess
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from quitar_fondo_blanco import load, save  # noqa: E402

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
LOGO = os.path.join(ROOT, "Assets/Recursos/Pantallas/PANTALLA4/Logo.png")
OUT = os.path.join(ROOT, "Assets/Recursos/Icono")
SIZE = 1024


def resized_logo(width):
    tmp = tempfile.mktemp(suffix=".png")
    subprocess.run(["sips", "--resampleWidth", str(width), LOGO, "--out", tmp], capture_output=True, check=True)
    img = load(tmp).astype(np.float64) / 255.0
    os.remove(tmp)
    return img


def background():
    y, x = np.mgrid[0:SIZE, 0:SIZE] / (SIZE - 1)
    top = np.array([0.09, 0.26, 0.58])
    bottom = np.array([0.03, 0.10, 0.27])
    img = top * (1 - y[..., None]) + bottom * y[..., None]

    # Brillo radial detras del logo
    d = np.sqrt((x - 0.5) ** 2 + (y - 0.46) ** 2)
    glow = np.clip(1 - d / 0.55, 0, 1) ** 2
    img += glow[..., None] * np.array([0.10, 0.22, 0.42])

    # Lineas diagonales de velocidad
    stripes = np.sin((x * 0.8 + y) * 22.0)
    img += (np.clip(stripes, 0.92, 1.0) - 0.92)[..., None] / 0.08 * 0.05

    # Banderas a cuadros en dos esquinas (como las pantallas del diseñador)
    cell = 64
    checker = ((np.floor(x * SIZE / cell) + np.floor(y * SIZE / cell)) % 2).astype(float)
    corner_tl = np.clip(1 - (x + y) / 0.42, 0, 1)
    corner_br = np.clip(((x + y) - 1.58) / 0.42, 0, 1)
    fade = np.maximum(corner_tl, corner_br) * 0.22
    img = img * (1 - fade[..., None]) + (checker * 0.35 + 0.08)[..., None] * fade[..., None] * np.array([0.45, 0.65, 1.0]) * 2.2

    # Franja naranja (el color del logo)
    band = np.exp(-(((x - y) * 0.7 + 0.62) ** 2) / 0.0008)
    img += band[..., None] * np.array([0.55, 0.25, 0.0]) * 0.35

    return np.clip(img, 0, 1)


def paste(canvas_rgb, canvas_alpha, logo, cx, cy, shadow=True):
    h, w, _ = logo.shape
    x0, y0 = int(cx - w / 2), int(cy - h / 2)
    a = logo[:, :, 3:4]
    if shadow:
        # sombra suave abajo-derecha
        sh = np.zeros_like(a)
        k = 14
        sh[k:, k:] = a[:-k, :-k]
        for _ in range(3):  # desenfoque barato
            sh = (sh + np.roll(sh, 3, 0) + np.roll(sh, -3, 0) + np.roll(sh, 3, 1) + np.roll(sh, -3, 1)) / 5
        region = canvas_rgb[y0:y0 + h, x0:x0 + w]
        region[:] = region * (1 - sh * 0.45)
        if canvas_alpha is not None:
            ra = canvas_alpha[y0:y0 + h, x0:x0 + w]
            ra[:] = np.maximum(ra, sh[..., 0] * 0.45)
    region = canvas_rgb[y0:y0 + h, x0:x0 + w]
    region[:] = region * (1 - a) + logo[:, :, :3] * a
    if canvas_alpha is not None:
        ra = canvas_alpha[y0:y0 + h, x0:x0 + w]
        ra[:] = a[..., 0] + ra * (1 - a[..., 0])


def to_rgba8(rgb, alpha=None):
    if alpha is None:
        alpha = np.ones(rgb.shape[:2])
    out = np.concatenate([rgb, alpha[..., None]], axis=2)
    return (np.clip(out, 0, 1) * 255).round().astype(np.uint8)


def main():
    os.makedirs(OUT, exist_ok=True)
    bg = background()

    # Icono completo: logo grande (92% del ancho)
    full = bg.copy()
    paste(full, None, resized_logo(int(SIZE * 0.92)), SIZE / 2, SIZE * 0.5)
    save(os.path.join(OUT, "icono_app.png"), to_rgba8(full))

    # Capas del icono adaptativo de Android: el logo cabe en el circulo seguro (66%)
    save(os.path.join(OUT, "icono_fondo.png"), to_rgba8(bg))
    fg_rgb = np.zeros((SIZE, SIZE, 3))
    fg_a = np.zeros((SIZE, SIZE))
    paste(fg_rgb, fg_a, resized_logo(int(SIZE * 0.56)), SIZE / 2, SIZE / 2)
    save(os.path.join(OUT, "icono_frente.png"), to_rgba8(fg_rgb, fg_a))
    print("Iconos generados en", os.path.relpath(OUT, ROOT))


if __name__ == "__main__":
    main()
