#!/usr/bin/env python3
"""
Quita el fondo blanco de un PNG (lo vuelve transparente) sin tocar el blanco que
esta DENTRO de los dibujos: solo se borra el blanco conectado con el borde de la imagen.
Los bordes quedan suaves (sin halo blanco). Uso:

    python3 Tools/quitar_fondo_blanco.py ruta/imagen.png [ruta/otra.png ...]

Requiere numpy y macOS (usa 'sips' para leer la imagen).
"""
import os, struct, subprocess, sys, tempfile, zlib
import numpy as np


def load(path):
    tmp = tempfile.mktemp(suffix=".bmp")
    subprocess.run(["sips", "-s", "format", "bmp", path, "--out", tmp], capture_output=True, check=True)
    d = open(tmp, "rb").read()
    os.remove(tmp)
    off = struct.unpack("<I", d[10:14])[0]
    hs, w, h, _, bpp, _ = struct.unpack("<IiiHHI", d[14:34])
    if bpp != 32:
        raise ValueError(f"{path}: se esperaba RGBA de 32 bits (tiene {bpp})")
    masks = struct.unpack("<IIII", d[54:70]) if hs >= 56 else (0xFF0000, 0xFF00, 0xFF, 0xFF000000)
    px = np.frombuffer(d[off:off + w * abs(h) * 4], np.uint32).reshape(abs(h), w)

    def ch(m):
        if m == 0:
            return np.full(px.shape, 255, np.uint8)
        s = (m & -m).bit_length() - 1
        return ((px & m) >> s).astype(np.uint8)

    img = np.stack([ch(masks[0]), ch(masks[1]), ch(masks[2]), ch(masks[3])], 2)
    return img[::-1] if h > 0 else img


def save(path, img):
    h, w, _ = img.shape
    raw = b"".join(b"\x00" + img[y].tobytes() for y in range(h))

    def chunk(t, data):
        return struct.pack(">I", len(data)) + t + data + struct.pack(">I", zlib.crc32(t + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def dilate(mask):
    out = mask.copy()
    out[1:] |= mask[:-1]
    out[:-1] |= mask[1:]
    out[:, 1:] |= mask[:, :-1]
    out[:, :-1] |= mask[:, 1:]
    return out


def remove_white_background(img, threshold=228, max_tint=28):
    rgb = img[:, :, :3].astype(np.int32)
    whiteness = rgb.min(axis=2)
    tint = rgb.max(axis=2) - rgb.min(axis=2)
    candidate = (whiteness >= threshold) & (tint <= max_tint)

    # Relleno desde los bordes (solo el blanco "de afuera")
    bg = np.zeros_like(candidate)
    bg[0, :] = candidate[0, :]
    bg[-1, :] = candidate[-1, :]
    bg[:, 0] = candidate[:, 0]
    bg[:, -1] = candidate[:, -1]
    while True:
        grown = dilate(bg) & candidate
        if grown.sum() == bg.sum():
            break
        bg = grown

    alpha = img[:, :, 3].astype(np.float64) / 255.0
    alpha[bg] = 0.0

    # Borde suave: los pixeles vecinos al fondo que son claros se vuelven semi-transparentes
    edge = dilate(dilate(bg)) & ~bg
    light = np.clip((whiteness - 150) / (255 - 150), 0, 1)
    edge_alpha = 1.0 - light
    alpha[edge] = np.minimum(alpha[edge], edge_alpha[edge])

    # Quitar el "blanco" mezclado del color del borde (des-premultiplicar sobre blanco)
    out = img.copy().astype(np.float64)
    a = np.clip(alpha, 1e-3, 1)[..., None]
    fixed = (out[:, :, :3] - 255.0 * (1 - a)) / a
    out[:, :, :3] = np.where(edge[..., None], np.clip(fixed, 0, 255), out[:, :, :3])
    out[:, :, 3] = alpha * 255.0
    return out.round().astype(np.uint8), int(bg.sum())


if __name__ == "__main__":
    for p in sys.argv[1:]:
        image = load(p)
        result, removed = remove_white_background(image)
        save(p, result)
        print(f"{p}: {removed} pixeles de fondo quitados ({removed / (image.shape[0] * image.shape[1]):.0%})")
