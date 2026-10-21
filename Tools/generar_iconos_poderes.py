"""Genera los iconos de los power-ups nuevos (Escudo, Iman, Puntos x2) con el
mismo estilo que Boton_velocidad.png: circulo con anillos oscuros, relleno de
color y simbolo blanco con sombra. Uso: /usr/bin/python3 Tools/generar_iconos_poderes.py"""
import os, struct, zlib
import numpy as np

W, H, SS = 710, 718, 3
CX, CY = 355, 370
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Recursos", "Pantallas", "PANTALLA JUGABILIDAD")

yy, xx = np.mgrid[0:H * SS, 0:W * SS].astype(np.float32) / SS


def down(mask):
    return mask.reshape(H, SS, W, SS).mean(axis=(1, 3))


def circle(r, cx=CX, cy=CY):
    return (xx - cx) ** 2 + (yy - cy) ** 2 <= r * r


def polygon(pts):
    """Mascara de poligono (regla par-impar)."""
    inside = np.zeros_like(xx, dtype=bool)
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]; x2, y2 = pts[(i + 1) % n]
        cond = (y1 > yy) != (y2 > yy)
        with np.errstate(divide="ignore", invalid="ignore"):
            xint = (x2 - x1) * (yy - y1) / (y2 - y1 + 1e-9) + x1
        inside ^= cond & (xx < xint)
    return inside


def blur(a, r):
    k = np.ones(r) / r
    a = np.apply_along_axis(lambda m: np.convolve(m, k, "same"), 0, a)
    return np.apply_along_axis(lambda m: np.convolve(m, k, "same"), 1, a)


def shift(a, dx, dy):
    return np.roll(np.roll(a, dy, 0), dx, 1)


def save_png(path, rgba):
    h, w, _ = rgba.shape
    raw = b"".join(b"\x00" + rgba[y].tobytes() for y in range(h))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


def hexc(h):
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], np.float32) / 255


def compose(symbol_mask, dark, mid, fill, name):
    disc = down(circle(316))
    ring_mid = down(circle(300))
    ring_dark = down(circle(272))
    inner = down(circle(262))
    sym = down(symbol_mask)

    color = np.zeros((H, W, 3), np.float32)
    alpha = np.zeros((H, W), np.float32)

    def over(c, a):
        nonlocal color, alpha
        a = a[..., None]
        color = c * a + color * (1 - a)
        alpha[:] = a[..., 0] + alpha * (1 - a[..., 0])

    shadow = blur(shift(disc, 10, 12), 15) * 0.55
    over(np.zeros(3, np.float32), shadow)
    over(hexc(dark), disc)
    over(hexc(mid), ring_mid)
    over(hexc(dark) * 0.7, ring_dark)
    over(hexc(fill), inner)
    over(hexc(dark) * 0.6, blur(shift(sym, 5, 7), 7) * 0.7)
    over(np.array([0.96, 0.98, 1.0], np.float32), sym)

    rgba = np.dstack([np.clip(color, 0, 1) * 255, np.clip(alpha, 0, 1) * 255]).astype(np.uint8)
    save_png(os.path.join(OUT, name), rgba)
    print("  ", name)


def shield():
    # escudo clasico: hombros redondeados arriba y punta abajo
    top, bottom, half = CY - 150, CY + 165, 125
    pts = [(CX - half, top + 30), (CX - 40, top), (CX, top - 12), (CX + 40, top), (CX + half, top + 30)]
    for t in np.linspace(0, 1, 24):
        pts.append((CX + half * (1 - t ** 1.6), top + 30 + (bottom - top - 30) * t))
    for t in np.linspace(1, 0, 24):
        pts.append((CX - half * (1 - t ** 1.6), top + 30 + (bottom - top - 30) * t))
    outer = polygon(pts)
    # franja interior para darle detalle (hueco en V)
    inner_pts = [(CX + (x - CX) * 0.62, CY + 5 + (y - CY) * 0.62) for x, y in pts]
    inner = polygon(inner_pts)
    left_half = xx < CX
    return outer & ~(inner & left_half)


def magnet():
    # iman en U: arco abajo + dos brazos hacia arriba, puntas separadas
    r_out, r_in = 140, 72
    cy = CY + 20
    d2 = (xx - CX) ** 2 + (yy - cy) ** 2
    arc = (d2 <= r_out ** 2) & (d2 >= r_in ** 2) & (yy >= cy)
    arms = ((np.abs(xx - CX) <= r_out) & (np.abs(xx - CX) >= r_in) & (yy >= cy - 150) & (yy < cy))
    u = arc | arms
    tips = arms & (yy < cy - 95)
    return u & ~((yy >= cy - 95) & (yy < cy - 80) & arms)  # ranura que separa las puntas


def star():
    pts = []
    for i in range(10):
        ang = -np.pi / 2 + i * np.pi / 5
        r = 165 if i % 2 == 0 else 70
        pts.append((CX + r * np.cos(ang), CY + 12 + r * np.sin(ang)))
    return polygon(pts)


if __name__ == "__main__":
    print("Iconos:")
    compose(shield(), "#0E4A3A", "#1FA57E", "#2BBF92", "Boton_escudo.png")
    compose(magnet(), "#5A1414", "#C23333", "#DB4747", "Boton_iman.png")
    compose(star(), "#6B4508", "#E0990F", "#F2B42A", "Boton_doble.png")
