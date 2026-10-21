"""RuneSkin sprite renderer: height-field relief -> lit RGBA sprites (deterministic, no GPU).

Every element is modelled as a height field (bevels, carved rune grooves, leather wraps) in final sprite pixels,
rendered supersampled (SS x) with one light rig (key light from the top-left, ambient, cavity AO, cast shadows),
then box-downsampled. Surface albedo detail comes from the AI textures in src/ai (stone/leather), procedural
periodic noise as fallback. Sprites are authored at 2x reference pixels (PPU 200 in Unity).

Usage: python -I art/src/render_sprites.py            -> art/sprites/*.png + sprites.json + contact sheets
"""
import json
import math
import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi

ART = pathlib.Path(__file__).resolve().parents[1]
SRC, OUT = ART / "src", ART / "sprites"
SS = 4

PALETTE = {
    "stone": "#3b4147", "stone_block": "#464d54", "leather": "#7a4a2a", "leather_edge": "#3a2416",
    "thread": "#c9ad7d", "thong": "#6b4229", "rune_glow": "#52e0f0", "glass_top": "#1e272d",
    "glass_bottom": "#0b0f12", "glass_highlight": "#a8e6f0", "groove_dark": "#050708", "groove_light": "#8fb8c4",
    "text_header": "#e9c46a", "text_body": "#d9d4c7", "text_disabled": "#7b7f80",
    # M1 HUD tints for the white masks (theme tokens own the final values)
    "pool_hp": "#c3262e", "pool_stamina": "#e3b23c", "pool_eitr": "#7a5cf5", "pool_es": "#70d8e6",
    "pool_adrenaline": "#ff8a3d", "bar_lag": "#f1e3c6", "bar_growth": "#e9c46a", "bar_end_line": "#70d8e6",
    "dot_bleed": "#3c060a", "dot_fire": "#ffb347", "dot_poison": "#8be04e", "reserved_edge": "#c8f4fa",
    "glyph": "#52e0f0", "select_ring": "#52e0f0", "food_timer": "#52e0f0", "ice": "#d6f2ff", "water": "#a9cfe0",
}
LIGHT = np.array([-0.52, -0.6, 0.6])
LIGHT /= np.linalg.norm(LIGHT)


def rgb(h):
    return np.array([int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)])


def sstep(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def grid(w, h):
    y, x = np.mgrid[0:h * SS, 0:w * SS]
    return (x + 0.5) / SS, (y + 0.5) / SS


def sd_box(x, y, cx, cy, hw, hh, r=0.0, chamfer=0.0):
    qx, qy = np.abs(x - cx) - (hw - r), np.abs(y - cy) - (hh - r)
    d = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r
    if chamfer:
        d = np.maximum(d, (np.abs(x - cx) + np.abs(y - cy) - (hw + hh - chamfer)) / math.sqrt(2))
    return d


def seg_dist(x, y, ax, ay, bx, by):
    px, py, dx, dy = x - ax, y - ay, bx - ax, by - ay
    t = np.clip((px * dx + py * dy) / (dx * dx + dy * dy), 0, 1)
    return np.hypot(px - t * dx, py - t * dy)


def groove(d, w):
    """Rounded V groove profile, 1 at the centre line, 0 at distance w."""
    return np.clip(1 - (d / w) ** 2, 0, 1)


# Elder Futhark runes as strokes in a unit box (x right, y down)
RUNES = {
    "algiz": [((.5, 0), (.5, 1)), ((.5, .45), (.1, 0)), ((.5, .45), (.9, 0))],
    "ansuz": [((.25, 0), (.25, 1)), ((.25, 0), (.75, .3)), ((.25, .3), (.75, .6))],
    "tiwaz": [((.5, 0), (.5, 1)), ((.5, 0), (.12, .38)), ((.5, 0), (.88, .38))],
    "othala": [((.5, 0), (.12, .4)), ((.5, 0), (.88, .4)), ((.12, .4), (.7, 1)), ((.88, .4), (.3, 1))],
    "dagaz": [((.1, 0), (.1, 1)), ((.9, 0), (.9, 1)), ((.1, 0), (.9, 1)), ((.1, 1), (.9, 0))],
    "ingwaz": [((.5, 0), (.12, .5)), ((.12, .5), (.5, 1)), ((.5, 1), (.88, .5)), ((.88, .5), (.5, 0))],
}


def rune_dist(x, y, name, cx, cy, size):
    d = np.full(x.shape, 1e9)
    for (ax, ay), (bx, by) in RUNES[name]:
        d = np.minimum(d, seg_dist(x, y, cx + (ax - .5) * size, cy + (ay - .5) * size,
                                   cx + (bx - .5) * size, cy + (by - .5) * size))
    return d


def diamond_dist(x, y, cx, cy, r):
    """Distance to the outline of a diamond (rotated square) of 'radius' r."""
    return np.abs((np.abs(x - cx) + np.abs(y - cy) - r) / math.sqrt(2))


# ---------------------------------------------------------------- surface detail
_rng = np.random.default_rng(1971)


def pnoise(period, sigmas, seed):
    """Periodic (tileable) noise of size period x period (SS px), std 1."""
    rng = np.random.default_rng(seed)
    n = np.zeros((period, period))
    for s, a in sigmas:
        n += a * ndi.gaussian_filter(rng.standard_normal((period, period)), s * SS, mode="wrap")
    return (n - n.mean()) / (n.std() + 1e-9)


def ai_detail(name, period):
    """AI albedo texture as periodic luminance detail (std 1), or None when the texture is missing."""
    p = SRC / "ai" / name
    if not p.exists():
        return None
    im = Image.open(p).convert("L").resize((period, period), Image.LANCZOS)
    a = np.asarray(im, dtype=np.float64)
    a = a - ndi.gaussian_filter(a, period / 6, mode="wrap")  # drop baked low-frequency shading
    return (a - a.mean()) / (a.std() + 1e-9)


def tiled(n, shape, ox=0, oy=0):
    p = n.shape[0]
    iy = (np.arange(shape[0]) + oy) % p
    ix = (np.arange(shape[1]) + ox) % p
    return n[np.ix_(iy, ix)]


def detail(kind, period, shape, seed, ox=0, oy=0):
    n = pnoise(period, [(0.5, .5), (1.5, .6), (5, .5)], seed)
    ai = ai_detail(f"{kind}_albedo.png", period)
    if ai is not None:
        n = 0.45 * n + 0.75 * ai
        n = (n - n.mean()) / n.std()
    return tiled(n, shape, ox, oy)


# ---------------------------------------------------------------- lighting
def shade(h, albedo, spec_k=0.18, spec_p=20.0, shadow_steps=24):
    """Lit colour of a height field (heights in final px). Returns rgb array (H,W,3) and shadow factor."""
    gy, gx = np.gradient(h)
    gx, gy = gx * SS, gy * SS
    n = np.dstack([-gx, -gy, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    diff = np.clip(n @ LIGHT, 0, 1)
    half = LIGHT + np.array([0, 0, 1.0])
    half /= np.linalg.norm(half)
    spec = np.clip(n @ half, 0, 1) ** spec_p * spec_k
    amb = 0.30 + 0.22 * n[..., 2]
    cav = h - ndi.gaussian_filter(h, 2.5 * SS)
    ao = np.clip(1 + cav * 0.10, 0.55, 1.12)
    # cast shadow: march towards the light (up-left) in SS steps
    rise = LIGHT[2] / math.hypot(LIGHT[0], LIGHT[1]) / SS  # height gained per SS px travelled
    dirx, diry = LIGHT[0] / math.hypot(LIGHT[0], LIGHT[1]), LIGHT[1] / math.hypot(LIGHT[0], LIGHT[1])
    pad = shadow_steps * SS
    hp = np.pad(h, pad, mode="edge")
    sh = np.zeros_like(h)
    H, W = h.shape
    for i in range(1, shadow_steps * SS, max(1, SS // 2)):
        ox, oy = int(round(dirx * i)), int(round(diry * i))
        hs = hp[pad + oy:pad + oy + H, pad + ox:pad + ox + W]
        sh = np.maximum(sh, np.clip((hs - (h + i * rise)) / 0.8, 0, 1))
    lit = amb * ao + diff * 0.95 * (1 - 0.8 * sh)
    col = albedo * lit[..., None] + spec[..., None] * (1 - 0.8 * sh[..., None])
    return col, sh


def over(top_rgb, top_a, bot_rgb, bot_a):
    a = top_a + bot_a * (1 - top_a)
    c = (top_rgb * top_a[..., None] + bot_rgb * (bot_a * (1 - top_a))[..., None]) / np.maximum(a, 1e-6)[..., None]
    return c, a


def drop_shadow(mask, dx=1.6, dy=2.4, blur=3.0, strength=0.6):
    m = ndi.shift(mask, (dy * SS, dx * SS), order=1, mode="constant")
    return np.clip(ndi.gaussian_filter(m, blur * SS) * strength, 0, 1)


def finish(rgbc, a):
    """Downsample SS x SS (premultiplied box filter) -> straight-alpha RGBA uint8 image."""
    H, W = a.shape
    pm = np.dstack([rgbc * a[..., None], a])
    pm = pm.reshape(H // SS, SS, W // SS, SS, 4).mean(axis=(1, 3))
    alpha = pm[..., 3:4]
    col = np.where(alpha > 1e-4, pm[..., :3] / np.maximum(alpha, 1e-4), 0)
    out = np.dstack([np.clip(col, 0, 1), np.clip(alpha, 0, 1)])
    return Image.fromarray((out * 255 + 0.5).astype(np.uint8), "RGBA")


def glow_layer(core, color, k_core=0.95, k_mid=0.8, k_halo=0.35):
    a = np.clip(core * k_core + ndi.gaussian_filter(core, 1.6 * SS) * k_mid * 1.6
                + ndi.gaussian_filter(core, 5 * SS) * k_halo * 2.5, 0, 1)
    rgbc = np.broadcast_to(color, a.shape + (3,)).copy()
    hot = np.clip(core * 0.7, 0, 1)[..., None]  # white-hot core
    return rgbc * (1 - hot) + hot, a


def glass_color(y, y0, y1):
    t = np.clip((y - y0) / max(y1 - y0, 1e-6), 0, 1)[..., None]
    return rgb(PALETTE["glass_top"]) * (1 - t) + rgb(PALETTE["glass_bottom"]) * t


# ---------------------------------------------------------------- panel frame + corner caps + glow
FR = dict(size=288, margin=8, rail=30, block=64, border=96, braid=24, leather=35)
CORNER_RUNES = {"tl": "algiz", "tr": "ansuz", "bl": "tiwaz", "br": "othala"}


def corner_local(x, y, size, m, key):
    u = x - m if key[1] == "l" else size - m - x
    v = y - m if key[0] == "t" else size - m - y
    return u, v


def render_frame():
    S, M, W, B, K = FR["size"], FR["margin"], FR["rail"], FR["border"], FR["block"]
    x, y = grid(S, S)
    c = S / 2
    dout = -sd_box(x, y, c, c, c - M, c - M, r=2.0)
    din = sd_box(x, y, c, c, c - M - W, c - M - W, r=1.0)
    rail = (dout > 0) & (din > 0)
    # rail profile: outer/inner bevel, two incised lines, central channel
    h = 6.0 * np.minimum(sstep(dout / 3.5), sstep(din / 3.0))
    for pos in (6.5, W - 6.5):
        h -= 0.9 * groove(np.abs(dout - pos), 0.9)
    # carved braid band: two crossing zigzags (diamond chain) along each rail, period divides the tile period
    side = np.argmin(np.stack([y - M, S - M - y, x - M, S - M - x]), axis=0)
    along = np.where(side < 2, x, y)
    ph = np.mod(along / FR["braid"], 1.0)
    tri_w = np.abs(ph * 2 - 1) * 2 - 1  # triangle wave in [-1, 1]
    amp = W / 2 - 9.0
    band = np.abs(dout - W / 2) < amp + 2.5
    bd = np.minimum(np.abs(dout - W / 2 - amp * tri_w), np.abs(dout - W / 2 + amp * tri_w)) * 0.72
    h -= 1.7 * groove(bd, 1.25) * band
    period = (S - 2 * B) * SS  # tile period = centre segment, so Tiled draw mode stays seamless
    nz = detail("stone", period, h.shape, 11, ox=-B * SS, oy=-B * SS)
    h += 0.22 * nz * rail
    mask = rail.astype(float)
    alb = np.broadcast_to(rgb(PALETTE["stone"]), h.shape + (3,)).copy()
    glow_core = np.zeros_like(h)
    leather_h = np.zeros_like(h)
    leather_m = np.zeros_like(h)
    leather_alb = np.zeros(h.shape + (3,))
    for key, rune in CORNER_RUNES.items():
        u, v = corner_local(x, y, S, M, key)
        db = -sd_box(u, v, K / 2, K / 2, K / 2, K / 2, r=2.0)
        blk = db > 0
        hb = 8.5 * sstep(db / 3.0) + 0.3 * nz
        hb -= 0.7 * groove(np.abs(db - 5.0), 0.8)
        off = M + K / 2 + 6  # runes keep their orientation in every corner (no mirroring)
        rd = rune_dist(x, y, rune, off if key[1] == "l" else S - off, off if key[0] == "t" else S - off, 22)
        hb -= 2.8 * groove(rd, 1.7)
        h = np.where(blk, hb, h)
        mask = np.maximum(mask, blk)
        alb = np.where(blk[..., None], rgb(PALETTE["stone_block"]), alb)
        glow_core = np.where(blk, groove(rd, 1.1), glow_core)
        # leather corner wrap: triangle over the outer tip, following the block's bevel
        tri = (u + v) / math.sqrt(2)
        LW = FR["leather"]
        lm = blk & (tri < LW)
        e = np.minimum(np.minimum(u, v), LW - tri)  # distance to the nearest edge of the leather piece
        lh = 8.5 * sstep(db / 3.0) + 1.6 * sstep((LW - tri) / 1.6) + 1.1 * sstep(e / 7.0) + 0.2
        dash = np.mod((u - v) / math.sqrt(2), 4.4)
        st = groove(np.abs(tri - (LW - 4.5)), 0.75) * (dash > 0.6) * (dash < 3.2)
        hole = groove(np.abs(tri - (LW - 4.5)), 0.8) * (np.minimum(np.abs(dash - 0.4), np.abs(dash - 3.4)) < 0.5)
        lh += 0.35 * st - 0.5 * hole
        leather_h = np.where(lm, lh, leather_h)
        leather_m = np.maximum(leather_m, lm)
        la = np.broadcast_to(rgb(PALETTE["leather"]), h.shape + (3,)) * (0.62 + 0.38 * sstep(e / 3.0))[..., None]
        la = la * (1 - st[..., None]) + rgb(PALETTE["thread"]) * st[..., None]
        leather_alb = np.where(lm[..., None], la, leather_alb)
        # leather thongs bound round both rails next to the block
        for a_, b_ in ((u, v), (v, u)):
            on_rail = (b_ > 0.2) & (b_ < W - 0.2) & (a_ > K) & (a_ < K + 19)
            for i in range(4):
                ac = K + 3.2 + i * 3.9
                d = np.abs(a_ - 0.10 * (b_ - W / 2) - ac)
                tm = on_rail & (d < 1.5)
                th = 6.2 + 1.5 * np.sqrt(np.clip(1 - (d / 1.5) ** 2, 0, 1)) - 0.6 * sstep(1 - np.minimum(b_, W - b_) / 1.5)
                leather_h = np.where(tm, th, leather_h)
                leather_m = np.maximum(leather_m, tm)
                tc = rgb(PALETTE["thong"]) * (0.88 + 0.06 * i) * (0.7 + 0.3 * sstep((1.5 - d) / 0.6))[..., None]
                leather_alb = np.where(tm[..., None], tc, leather_alb)
    lnz = detail("leather", period, h.shape, 23, ox=-B * SS, oy=-B * SS)
    leather_alb = leather_alb * (1 + 0.10 * lnz)[..., None]
    alb = alb * (1 + 0.09 * nz)[..., None]
    # base frame (no leather)
    col0, _ = shade(h, alb)
    sh0 = drop_shadow(mask)
    frame_rgb, frame_a = over(col0, mask, np.zeros_like(col0), sh0 * (1 - mask))
    # with leather: same scene, leather on top
    hl = np.where(leather_m > 0, leather_h, h)
    albl = np.where(leather_m[..., None] > 0, leather_alb, alb)
    col1, _ = shade(hl, albl, spec_k=0.12, spec_p=14)
    lum0 = col0.mean(axis=2) + 1e-4
    lum1 = col1.mean(axis=2)
    dark = np.clip(1 - lum1 / lum0, 0, 1) * mask * (1 - leather_m)  # shadow/AO the leather casts on stone
    lsh = drop_shadow(leather_m, 1.0, 1.5, 1.2, 0.5) * (1 - mask)  # leather shadow outside the stone
    cap_rgb = np.where(leather_m[..., None] > 0, col1, 0)
    cap_a = np.clip(leather_m + np.maximum(dark, lsh) * (1 - leather_m), 0, 1)
    glow_rgb, glow_a = glow_layer(glow_core * mask, np.ones(3))
    return dict(frame=finish(frame_rgb, frame_a), caps=finish(cap_rgb, cap_a), glow=finish(glow_rgb, glow_a))


# ---------------------------------------------------------------- glass fill
def render_glass(w=64, h_=192):
    x, y = grid(w, h_)
    col = glass_color(y, 0, h_)
    a = np.full(x.shape, 0.93)
    edge = np.minimum(np.minimum(x, w - x), h_ - y)
    col *= (0.85 + 0.15 * sstep(edge / 10))[..., None]
    hl = np.exp(-((y - 1.6) / 0.7) ** 2) * 0.22 + np.exp(-np.maximum(y - 1.6, 0) / 9) * 0.05
    hlc = rgb(PALETTE["glass_highlight"])
    col = col * (1 - hl[..., None]) + hlc * hl[..., None]
    return finish(col, a)


# ---------------------------------------------------------------- buttons
BT = dict(w=256, h=96, margin=8, cap=40, chamfer=12)


def render_button(state):
    Wd, Ht, M, C = BT["w"], BT["h"], BT["margin"], BT["cap"]
    x, y = grid(Wd, Ht)
    cx, cy = Wd / 2, Ht / 2
    dbody = -sd_box(x, y, cx, cy, cx - M, cy - M, chamfer=BT["chamfer"])
    body = dbody > 0
    # glass well: centre between the stone caps, inset by the rim
    dwell = -sd_box(x, y, cx, cy, cx - M - C, cy - M - 7, r=2.0)
    well = dwell > 0
    depth = {"pressed": 2.6, "hover": 1.6}.get(state, 1.8)
    h = 6.0 * sstep(dbody / 3.0)
    h -= 0.6 * groove(np.abs(dbody - 3.6), 0.6)
    h = np.where(well, 6.0 - depth * sstep(dwell / 1.6), h)
    # carved diamond knots in the stone caps
    rd = np.full(x.shape, 1e9)
    for kx in (M + C / 2 + 1, Wd - M - C / 2 - 1):
        rd = np.minimum(rd, diamond_dist(x, y, kx, cy, 10.5))
        rd = np.minimum(rd, diamond_dist(x, y, kx, cy, 4.5))
        rd = np.minimum(rd, seg_dist(x, y, kx, cy - 18, kx, cy - 13.5))
        rd = np.minimum(rd, seg_dist(x, y, kx, cy + 13.5, kx, cy + 18))
    hk = 2.2 * groove(rd, 1.5) * body * (~well)
    h -= hk
    nz = detail("stone", 64 * SS, h.shape, 31)
    h += 0.18 * nz * (body & ~well)
    alb = np.broadcast_to(rgb(PALETTE["stone_block"]), h.shape + (3,)) * (1 + 0.08 * nz * (body & ~well))[..., None]
    glass = glass_color(y, M + 7, Ht - M - 7)
    if state == "hover":
        glass = glass * 1.45 + rgb(PALETTE["rune_glow"]) * 0.035
    if state == "pressed":
        glass = glass * 0.75
    alb = np.where(well[..., None], glass, alb)
    col, _ = shade(h, alb)
    if state == "pressed":
        col = np.where(well[..., None], col, col * 0.86)
    # glass top highlight (inner)
    hl = np.exp(-((dwell - 2.2) / 0.6) ** 2) * (y < cy) * well * (0.10 if state != "pressed" else 0.03)
    col = col * (1 - hl[..., None]) + rgb(PALETTE["glass_highlight"]) * hl[..., None]
    mask = body.astype(float)
    sh = drop_shadow(mask, 1.2, 1.8, 2.2, 0.55)
    out_rgb, out_a = over(col, mask, np.zeros_like(col), sh * (1 - mask))
    glow_core = np.zeros_like(h)
    if state in ("hover", "selected"):
        glow_core = np.maximum(glow_core, groove(rd, 1.0) * body * (~well))
    if state == "hover":
        glow_core = np.maximum(glow_core, 0.45 * groove(np.abs(dwell - 0.6), 0.7) * well)
    if state == "selected":
        glow_core = np.maximum(glow_core, 0.9 * groove(np.abs(dbody + 1.6), 1.1))
        glow_core = np.maximum(glow_core, 0.35 * groove(np.abs(dwell - 0.6), 0.7) * well)
    if glow_core.any():
        g_rgb, g_a = glow_layer(glow_core, rgb(PALETTE["rune_glow"]), 0.9, 0.55, 0.25)
        out_rgb, out_a = over(g_rgb, g_a, out_rgb, out_a)
    img = finish(out_rgb, out_a)
    if state == "disabled":
        a = np.asarray(img, dtype=np.float64) / 255
        lum = a[..., :3] @ np.array([0.3, 0.59, 0.11])
        a[..., :3] = (a[..., :3] * 0.15 + lum[..., None] * 0.85) * 0.62
        a[..., 3] *= 0.92
        img = Image.fromarray((a * 255 + 0.5).astype(np.uint8), "RGBA")
    return img


# ---------------------------------------------------------------- tooltip frame
def render_tooltip(S=128, M=6, R=7):
    x, y = grid(S, S)
    c = S / 2
    dout = -sd_box(x, y, c, c, c - M, c - M, r=1.5)
    din = sd_box(x, y, c, c, c - M - R, c - M - R, r=0.5)
    rail = (dout > 0) & (din > 0)
    inside = din <= 0
    h = 3.6 * np.minimum(sstep(dout / 2.2), sstep(din / 1.8))
    h -= 1.2 * groove(np.abs(dout - R / 2), 1.2)
    mask = rail.astype(float)
    alb = np.broadcast_to(rgb(PALETTE["stone"]), h.shape + (3,)).copy()
    nz = detail("stone", (S - 64) * SS, h.shape, 41, ox=-32 * SS, oy=-32 * SS)
    h += 0.15 * nz * rail
    alb *= (1 + 0.08 * nz)[..., None]
    h = np.where(inside, 0.0, h)
    alb = np.where(inside[..., None], glass_color(y, M + R, S - M - R) * 1.1, alb)
    for kx in (M + R / 2, S - M - R / 2):
        for ky in (M + R / 2, S - M - R / 2):
            dd = (np.abs(x - kx) + np.abs(y - ky)) / math.sqrt(2)
            stud = dd < 6.4
            hs = 5.2 * sstep((6.4 - dd) / 2.0) + 3.0
            hs -= 1.4 * groove(np.abs(dd - 3.0), 0.7)
            h = np.where(stud, np.maximum(h, hs), h)
            mask = np.maximum(mask, stud)
            alb = np.where(stud[..., None], rgb(PALETTE["stone_block"]), alb)
    glass_px = inside & (mask == 0)
    col, _ = shade(h, alb)
    inner_shadow = ndi.gaussian_filter((~inside).astype(float), 1.5 * SS)
    col = np.where(glass_px[..., None], col * (1 - 0.45 * inner_shadow)[..., None], col)
    a = np.where(glass_px, 0.95, mask)
    sh = drop_shadow((dout > 0).astype(float), 1.4, 2.0, 2.4, 0.55)
    out_rgb, out_a = over(col, a, np.zeros_like(col), sh * (1 - a))
    return finish(out_rgb, out_a)


# ---------------------------------------------------------------- divider
def render_divider(Wd=256, Ht=24, E=40):
    x, y = grid(Wd, Ht)
    cy = Ht / 2
    end = np.minimum(x, Wd - x)
    taper = sstep((end - 16) / (E - 14))  # line width tapers to the ends
    d = np.abs(y - cy)
    dark = np.exp(-(d / (0.9 * np.maximum(taper, 0.05))) ** 2) * (end > 16) * 0.9
    light = np.exp(-((y - cy - 1.7) / 0.55) ** 2) * (end > 18) * taper * 0.28
    col = np.zeros(x.shape + (3,))
    col_dark, col_light = rgb(PALETTE["groove_dark"]), rgb(PALETTE["groove_light"])
    a = np.clip(dark + light, 0, 1)
    col = (col_dark * dark[..., None] + col_light * light[..., None]) / np.maximum(a, 1e-6)[..., None]
    # stone studs (diamond) at both ends
    h = np.zeros_like(x)
    smask = np.zeros_like(x)
    for kx in (12.0, Wd - 12.0):
        dd = (np.abs(x - kx) + np.abs(y - cy)) / math.sqrt(2)
        st = dd < 5.6
        hs = 4.5 * sstep((5.6 - dd) / 1.8) - 1.2 * groove(np.abs(dd - 2.4), 0.6)
        h = np.where(st, hs, h)
        smask = np.maximum(smask, st)
    scol, _ = shade(h, np.broadcast_to(rgb(PALETTE["stone_block"]), h.shape + (3,)).copy())
    col, a = over(scol, smask, col, a)
    sh = drop_shadow(smask, 0.8, 1.2, 1.2, 0.5)
    col, a = over(col, a, np.zeros_like(col), sh * (1 - a))
    return finish(col, a)


# ---------------------------------------------------------------- header plaque
PL = dict(w=320, h=72, margin=6, point=16, border_x=76, border_y=24)


def render_plaque():
    Wd, Ht, M, P = PL["w"], PL["h"], PL["margin"], PL["point"]
    x, y = grid(Wd, Ht)
    cx, cy = Wd / 2, Ht / 2
    hw, hh = cx - M, cy - M
    k = P / hh
    dpl = ((np.abs(x - cx) - hw) + np.abs(y - cy) * k) / math.sqrt(1 + k * k)
    d = np.maximum(np.abs(y - cy) - hh, dpl)
    dbody = -d
    body = dbody > 0
    h = 6.0 * sstep(dbody / 3.0)
    # recessed text field
    fhw = hw - 52
    dfield = -sd_box(x, y, cx, cy, fhw, hh - 8, r=2.0)
    field = dfield > 0
    h -= 1.8 * sstep(dfield / 1.5)
    h -= 0.7 * groove(np.abs(dfield + 2.6), 0.7)
    # carved knots at both ends: two nested diamonds + crossing strokes
    rd = np.full(x.shape, 1e9)
    for kx in (M + 34.0, Wd - M - 34.0):
        rd = np.minimum(rd, diamond_dist(x, y, kx, cy, 15.0))
        rd = np.minimum(rd, diamond_dist(x, y, kx, cy, 7.5))
        rd = np.minimum(rd, seg_dist(x, y, kx - 10.6, cy - 10.6 + 0, kx + 10.6, cy + 10.6) + 0 * x)
        rd = np.minimum(rd, seg_dist(x, y, kx - 10.6, cy + 10.6, kx + 10.6, cy - 10.6))
    knot = groove(rd, 1.5) * body * (~field)
    h -= 2.0 * knot
    period = (Wd - 2 * PL["border_x"]) * SS
    nz = detail("stone", period, h.shape, 51, ox=-PL["border_x"] * SS, oy=-PL["border_y"] * SS)
    h += 0.3 * nz * body
    alb = np.broadcast_to(rgb(PALETTE["stone_block"]), h.shape + (3,)) * (1 + 0.09 * nz)[..., None]
    alb = np.where(field[..., None], alb * 0.62, alb)
    col, _ = shade(h, alb)
    mask = body.astype(float)
    sh = drop_shadow(mask, 1.4, 2.0, 2.4, 0.55)
    out_rgb, out_a = over(col, mask, np.zeros_like(col), sh * (1 - mask))
    g_rgb, g_a = glow_layer(groove(rd, 0.9) * body * (~field), rgb(PALETTE["rune_glow"]), 0.55, 0.3, 0.12)
    out_rgb, out_a = over(g_rgb, g_a, out_rgb, out_a)
    return finish(out_rgb, out_a)


# ---------------------------------------------------------------- menu-entry knot
def render_knot(Wd=64, Ht=32):
    """Small carved stone knot beside menu entries (vanilla BraidKnotMedium slot): a raised diamond with a nested
    carved diamond and rune glow, flanked by two small studs; symmetric, so left and right entries share it."""
    x, y = grid(Wd, Ht)
    cx, cy = Wd / 2, Ht / 2
    dd = (np.abs(x - cx) + np.abs(y - cy)) / math.sqrt(2)
    body = dd < 10.5
    h = 5.5 * sstep((10.5 - dd) / 2.2)
    inner = diamond_dist(x, y, cx, cy, 5.2)
    h -= 1.6 * groove(inner, 0.9) * body
    smask = body.astype(float)
    for kx in (cx - 19.0, cx + 19.0):
        ds = (np.abs(x - kx) + np.abs(y - cy)) / math.sqrt(2)
        st = ds < 4.6
        h = np.where(st, 3.6 * sstep((4.6 - ds) / 1.5), h)
        smask = np.maximum(smask, st)
    nz = detail("stone", Wd * SS, h.shape, 61)
    h += 0.12 * nz * smask
    alb = np.broadcast_to(rgb(PALETTE["stone_block"]), h.shape + (3,)) * (1 + 0.08 * nz)[..., None]
    col, _ = shade(h, alb)
    sh = drop_shadow(smask, 0.8, 1.2, 1.2, 0.5)
    out_rgb, out_a = over(col, smask, np.zeros_like(col), sh * (1 - smask))
    g_rgb, g_a = glow_layer(groove(inner, 0.6) * body, rgb(PALETTE["rune_glow"]), 0.6, 0.35, 0.15)
    out_rgb, out_a = over(g_rgb, g_a, out_rgb, out_a)
    return finish(out_rgb, out_a)


# ================================================================ M1 HUD
def compose(h, mask, alb, glow=None, gcol=None, sh=(0.8, 1.2, 1.4, 0.5), spec_k=0.18, spec_p=20.0,
            extra_a=None, gk=(0.9, 0.55, 0.25)):
    """Lit relief over its own drop shadow (+ optional black alpha layer, e.g. an inner shadow) + baked glow."""
    col, _ = shade(h, alb, spec_k, spec_p)
    under = drop_shadow(mask, *sh) * (1 - mask)
    if extra_a is not None:
        under = np.maximum(under, extra_a * (1 - mask))
    out_rgb, out_a = over(col, mask, np.zeros_like(col), under)
    if glow is not None and glow.any():
        g_rgb, g_a = glow_layer(glow, gcol if gcol is not None else rgb(PALETTE["rune_glow"]), *gk)
        out_rgb, out_a = over(g_rgb, g_a, out_rgb, out_a)
    return finish(out_rgb, out_a)


def stone_alb(shape, nz, key="stone_block", k=0.09):
    return np.broadcast_to(rgb(PALETTE[key]), shape + (3,)) * (1 + k * nz)[..., None]


def white_mask(core, soft=0.5, halo=0.3, lum=None):
    """White (tintable) mask: alpha = core + soft glow; lum = optional grayscale shading (Image.color multiplies)."""
    a = np.clip(core + ndi.gaussian_filter(core, 1.2 * SS) * soft + ndi.gaussian_filter(core, 3.5 * SS) * halo, 0, 1)
    c = np.ones(core.shape + (3,)) if lum is None else np.repeat(np.clip(lum, 0, 1)[..., None], 3, axis=2)
    return finish(c, a)


def inner_shadow(body, dx=1.2, dy=1.8, blur=2.0, k=0.75):
    """Shadow the lips of a frame cast into its window (light top-left)."""
    m = ndi.shift(body.astype(float), (dy * SS, dx * SS), order=1, mode="nearest")
    return np.clip(ndi.gaussian_filter(m, blur * SS) * k, 0, 1)


def stroke(d, w, aa=0.6):
    return sstep((w - d) / aa)


def poly_dist(x, y, pts, closed=True):
    d = np.full(x.shape, 1e9)
    n = len(pts) if closed else len(pts) - 1
    for i in range(n):
        (ax, ay), (bx, by) = pts[i], pts[(i + 1) % len(pts)]
        d = np.minimum(d, seg_dist(x, y, ax, ay, bx, by))
    return d


def leather_band(x, y, x0, x1, y0, y1, lnz):
    """Leather strap wrapped round the bar (cylindrical across x), two stitch rows; returns mask, height, albedo."""
    m = (x >= x0) & (x <= x1) & (y >= y0) & (y <= y1)
    u = np.clip((x - x0) / (x1 - x0) * 2 - 1, -1, 1)
    hc = 7.2 + 1.6 * np.sqrt(np.clip(1 - u * u, 0, 1))
    ev = np.minimum(y - y0, y1 - y)
    hc = hc - 1.2 * (1 - sstep(ev / 2.0))
    dash = np.mod(y, 4.2)
    st = (np.minimum(np.abs(x - x0 - 2.6), np.abs(x1 - x - 2.6)) < 0.55) & (dash > 0.8) & (dash < 3.0)
    hc = hc + 0.25 * st
    la = np.broadcast_to(rgb(PALETTE["leather"]), x.shape + (3,)) * (0.62 + 0.38 * np.sqrt(np.clip(1 - u * u, 0, 1)))[..., None]
    la = la * (1 + 0.10 * lnz)[..., None]
    la = np.where(st[..., None], rgb(PALETTE["thread"]) * 0.8, la)
    return m, hc, la


# ---------------------------------------------------------------- resource bar
BAR = dict(w=400, h=80, m=4, cap=52, band=20, rail=14, bx=80, by=24)
BAR_WIN = BAR["m"] + BAR["cap"] + BAR["band"], BAR["m"] + BAR["rail"]  # window inset (x, y) = (72, 18)


def render_bar_frame():
    Wd, Ht, M, C, BD = BAR["w"], BAR["h"], BAR["m"], BAR["cap"], BAR["band"]
    wx, wy = BAR_WIN
    x, y = grid(Wd, Ht)
    cx, cy = Wd / 2, Ht / 2
    dbody = -sd_box(x, y, cx, cy, cx - M, cy - M, chamfer=8)
    dwin = sd_box(x, y, cx, cy, cx - wx, cy - wy, r=2.0)
    body = (dbody > 0) & (dwin > 0)
    period = (Wd - 2 * BAR["bx"]) * SS
    nz = detail("stone", period, x.shape, 71, ox=-BAR["bx"] * SS, oy=-BAR["bx"] * SS)
    lnz = detail("leather", period, x.shape, 72)
    h = 5.0 * np.minimum(sstep(dbody / 2.5), sstep(dwin / 2.2))
    h -= 0.7 * groove(np.abs(dbody - 4.2), 0.6) * (dwin > 2)
    h += 0.22 * nz
    alb = stone_alb(x.shape, nz, "stone")
    glow = np.zeros_like(x)
    for side in (0, 1):
        bxc = M + C / 2 if side == 0 else Wd - M - C / 2
        dblk = -sd_box(x, y, bxc, cy, C / 2, cy - M, r=2.0)
        blk = (dblk > 0) & (dbody > 0)
        hb = 7.6 * np.minimum(sstep(dblk / 2.8), sstep(dbody / 2.5)) + 0.3 * nz
        hb -= 0.7 * groove(np.abs(dblk - 4.6), 0.7)
        if side == 0:  # recessed round socket for the pool glyph (code places glyph_* on it)
            r = np.hypot(x - bxc, y - cy)
            dso = 16.5 - r
            hb = np.where(dso > 0, hb - 2.6 * sstep(dso / 1.6), hb)
            hb -= 0.6 * groove(np.abs(r - 19.0), 0.6)
            sock = dso > 0
        else:  # carved knot: two nested diamonds + ties
            rd = np.minimum(diamond_dist(x, y, bxc, cy, 15.5), diamond_dist(x, y, bxc, cy, 7.5))
            rd = np.minimum(rd, seg_dist(x, y, bxc, cy - 27, bxc, cy - 20))
            rd = np.minimum(rd, seg_dist(x, y, bxc, cy + 20, bxc, cy + 27))
            hb -= 2.0 * groove(rd, 1.5)
            glow = np.where(blk, 0.75 * groove(rd, 1.0), glow)
        h = np.where(blk, hb, h)
        body = body | blk
        alb = np.where(blk[..., None], stone_alb(x.shape, nz), alb)
        if side == 0:
            alb = np.where((sock & blk)[..., None], glass_color(y, cy - 16, cy + 16) * 1.3, alb)
        lx0 = M + C if side == 0 else Wd - M - C - BD
        lm, lh, la = leather_band(x, y, lx0, lx0 + BD, M - 1.0, Ht - M + 1.0, lnz)
        h = np.where(lm, lh, h)
        body = body | lm
        alb = np.where(lm[..., None], la, alb)
    win = (dwin <= 0)
    ish = inner_shadow(body | ~win) * win
    return compose(h, body.astype(float), alb, glow, extra_a=ish, gk=(0.55, 0.3, 0.12), spec_k=0.15)


def render_trough(Wd=96, Ht=44):
    x, y = grid(Wd, Ht)
    col = glass_color(y, 0, Ht) * 0.85
    e = np.minimum(np.minimum(x, Wd - x), np.minimum(y, Ht - y))
    col *= (0.7 + 0.3 * sstep(e / 6))[..., None]
    hl = np.exp(-((y - (Ht - 2.0)) / 0.9) ** 2) * 0.10  # faint glass lip at the bottom
    col = col * (1 - hl[..., None]) + rgb(PALETTE["glass_highlight"]) * hl[..., None]
    return finish(col, np.full(x.shape, 0.96))


def fill_lum(x, y, Ht, top=0.0):
    lum = 0.80 + 0.20 * np.exp(-((y - top - 6.0) / 3.5) ** 2) - 0.28 * sstep((y - Ht * 0.5) / (Ht * 0.5))
    lum -= 0.10 * (1 - sstep((y - top) / 2.0))
    return lum


def render_bar_fill(Wd=160, Ht=44, B=8):
    x, y = grid(Wd, Ht)
    n = pnoise((Wd - 2 * B) * SS, [(0.8, .15), (3, .5), (9, .7)], 81)
    nz = tiled(n, x.shape, ox=-B * SS)
    lum = fill_lum(x, y, Ht) + 0.04 * nz
    lum += 0.18 * np.exp(-((Wd - x - 1.2) / 0.9) ** 2)  # bright leading edge at the fill front
    return white_mask(np.ones_like(x), 0, 0, lum)


def render_bar_lag(Wd=64, Ht=44):
    x, y = grid(Wd, Ht)
    lum = 0.92 - 0.12 * sstep(y / Ht)
    a = 0.8 * sstep((Wd - x) / 4.0)
    return finish(np.repeat(lum[..., None], 3, axis=2), a)


def render_bar_growth(Wd=128, Ht=44):
    x, y = grid(Wd, Ht)
    u = np.mod(x + 0.55 * y, 32.0)
    streak = np.exp(-((np.minimum(u, 32 - u)) / 2.2) ** 2)
    a = np.clip(0.07 + 0.22 * streak * (0.6 + 0.4 * np.sin(y / Ht * math.pi)), 0, 1)
    return finish(np.ones(x.shape + (3,)), a)


def render_end_line(Wd=8, Ht=56):
    x, y = grid(Wd, Ht)
    core = stroke(np.abs(x - Wd / 2), 0.8) * sstep(np.minimum(y, Ht - y) / 6.0)
    return white_mask(core, 0.7, 0.4)


# ---------------------------------------------------------------- thin rails (ES, adrenaline)
RAIL = dict(w=400, h=28, m=2, cap=28, wx=30, wy=9, bx=32, by=8)


def render_rail_frame(kind):
    Wd, Ht, M, C = RAIL["w"], RAIL["h"], RAIL["m"], RAIL["cap"]
    x, y = grid(Wd, Ht)
    cx, cy = Wd / 2, Ht / 2
    dbody = -sd_box(x, y, cx, cy, cx - M, cy - M, chamfer=5)
    dwin = sd_box(x, y, cx, cy, cx - RAIL["wx"], cy - RAIL["wy"], r=1.0)
    body = (dbody > 0) & (dwin > 0)
    period = (Wd - 2 * RAIL["bx"]) * SS
    nz = detail("stone", period, x.shape, 91, ox=-RAIL["bx"] * SS, oy=-RAIL["bx"] * SS)
    h = 3.6 * np.minimum(sstep(dbody / 1.8), sstep(dwin / 1.6)) + 0.15 * nz
    h -= 0.5 * groove(np.abs(dbody - 2.6), 0.5) * (dwin > 1.5)
    alb = stone_alb(x.shape, nz, "stone")
    glow = np.zeros_like(x)
    gem = np.zeros(x.shape, bool)
    lnz = detail("leather", period, x.shape, 92)
    for side in (0, 1):
        bxc = M + C / 2 if side == 0 else Wd - M - C / 2
        dblk = -sd_box(x, y, bxc, cy, C / 2, cy - M, chamfer=5)
        blk = (dblk > 0) & (dbody > 0)
        hb = 5.0 * sstep(dblk / 1.8) + 0.2 * nz
        if kind == "es":  # cyan glass gem stud
            dd = (np.abs(x - bxc) + np.abs(y - cy)) / math.sqrt(2)
            g = dd < 5.2
            hb = np.where(g, 4.0 + 2.2 * sstep((5.2 - dd) / 2.4), hb - 0.6 * groove(np.abs(dd - 7.0), 0.6))
            gem |= g & blk
            glow = np.where(blk, 0.6 * sstep((4.5 - dd) / 2.0), glow)
            h = np.where(blk, hb, h)
            alb = np.where(blk[..., None], stone_alb(x.shape, nz), alb)
        else:  # adrenaline: leather-wrapped cap with a carved ember chevron
            dirx = 1 if side == 0 else -1
            ax = bxc - dirx * 3.5
            rd = np.minimum(seg_dist(x, y, ax, cy - 6, ax + dirx * 6, cy), seg_dist(x, y, ax + dirx * 6, cy, ax, cy + 6))
            hb -= 1.4 * groove(rd, 1.1)
            glow = np.where(blk, groove(rd, 0.8), glow)
            h = np.where(blk, hb, h)
            alb = np.where(blk[..., None], stone_alb(x.shape, nz), alb)
            lx0 = M + C if side == 0 else Wd - M - C - 7
            lm, lh, la = leather_band(x, y, lx0, lx0 + 7, M - 0.5, Ht - M + 0.5, lnz)
            h = np.where(lm, lh - 3.0, h)
            blk = blk | lm
            alb = np.where(lm[..., None], la, alb)
        body = body | blk
    if kind == "es":
        alb = np.where(gem[..., None], rgb(PALETTE["pool_es"]) * 0.75, alb)
    win = dwin <= 0
    ish = inner_shadow(body | ~win, 0.8, 1.2, 1.2, 0.7) * win
    gcol = rgb(PALETTE["pool_es"] if kind == "es" else PALETTE["pool_adrenaline"])
    return compose(h, body.astype(float), alb, glow, gcol, sh=(0.6, 0.9, 1.0, 0.5), extra_a=ish,
                   gk=(0.8, 0.45, 0.15))


def render_rail_fill(Wd=64, Ht=10):
    x, y = grid(Wd, Ht)
    lum = 0.86 + 0.14 * np.exp(-((y - 2.3) / 1.1) ** 2) - 0.25 * sstep((y - 5) / 5)
    lum += 0.15 * np.exp(-((Wd - x - 0.8) / 0.7) ** 2)
    return white_mask(np.ones_like(x), 0, 0, lum)


def render_rail_trough(Wd=64, Ht=10):
    x, y = grid(Wd, Ht)
    col = glass_color(y, 0, Ht) * 0.8
    col *= (0.65 + 0.35 * sstep(np.minimum(x, Wd - x) / 3))[..., None]
    return finish(col, np.full(x.shape, 0.96))


# ---------------------------------------------------------------- DoT stripe masks (tile horizontally)
def render_dot(kind, Wd=32, Ht=44):
    x, y = grid(Wd, Ht)
    if kind == "bleed":  # thick forward slashes '/'
        u = np.mod(x + y, 16.0)
        core = stroke(np.minimum(u, 16 - u) / math.sqrt(2), 2.6, 0.8)
    elif kind == "fire":  # upward flame chevrons '^', thin
        v = y - 6.0 * np.abs(np.mod(x, 16.0) - 8.0) / 8.0
        u = np.mod(v, 11.0)
        core = stroke(np.minimum(u, 11 - u) / math.sqrt(1 + (6 / 8) ** 2), 1.3, 0.6)
    else:  # poison: staggered round drops
        d = np.full(x.shape, 1e9)
        for ox, oy in ((0, 0), (8, 8)):
            px = np.mod(x - ox, 16.0) - 8.0
            py = np.mod(y - oy - 3.0, 16.0) - 8.0
            d = np.minimum(d, np.hypot(px, py))
        core = stroke(d, 3.4, 0.7)
    return finish(np.ones(x.shape + (3,)), 0.92 * core)


# ---------------------------------------------------------------- reserved segment + glyphs
def render_reserved(Wd=64, Ht=44):
    x, y = grid(Wd, Ht)
    u = np.mod(x - y, 16.0)
    gv = groove(np.minimum(u, 16 - u) / math.sqrt(2), 1.9)
    nz = detail("stone", Wd * SS, x.shape, 101)
    h = 3.0 - 1.5 * gv + 0.25 * nz
    h -= 1.5 * (1 - sstep(np.minimum(y, Ht - y) / 3.0))
    alb = np.broadcast_to(rgb("#2a2e33"), x.shape + (3,)) * (1 + 0.10 * nz)[..., None]
    col, _ = shade(h, alb)
    col *= (1 - 0.35 * gv)[..., None]
    return finish(col * 0.9, np.ones_like(x))


def render_reserved_edge(Wd=8, Ht=44):
    x, y = grid(Wd, Ht)
    return white_mask(stroke(np.abs(x - 2.5), 0.7) * 1.0, 0.6, 0.25)


def render_glyph(kind):
    S = 48 if kind == "seal" else 40
    x, y = grid(S, S)
    c = S / 2
    if kind == "lock":
        r = np.hypot(x - c, y - 17)
        shackle = np.where(y < 17, np.abs(r - 7.5), 1e9)
        shackle = np.minimum(shackle, np.minimum(seg_dist(x, y, c - 7.5, 17, c - 7.5, 20), seg_dist(x, y, c + 7.5, 17, c + 7.5, 20)))
        d = np.minimum(shackle, np.abs(sd_box(x, y, c, 27.5, 11, 8, r=2.0)))
        d = np.minimum(d, rune_dist(x, y, "tiwaz", c, 27.5, 9))
        core = stroke(d, 1.3)
    elif kind == "es":
        d = poly_dist(x, y, [(9, 7), (31, 7), (31, 19), (20, 34), (9, 19)])
        d = np.minimum(d, rune_dist(x, y, "algiz", c, 19, 13))
        core = stroke(d, 1.3)
    else:
        r = np.hypot(x - c, y - c)
        d = np.minimum(np.abs(r - 19.5), np.abs(r - 15.5) + 0.4)
        d = np.minimum(d, rune_dist(x, y, "ingwaz", c, c, 17))
        d = np.minimum(d, diamond_dist(x, y, c, c, 3.0) + 0.3)
        for k in range(8):
            a = k * math.pi / 4 + math.pi / 8
            d = np.minimum(d, np.hypot(x - c - 17.5 * math.cos(a), y - c - 17.5 * math.sin(a)) - 0.6)
        core = stroke(d, 1.3)
    return white_mask(core, 0.55, 0.3)


# ---------------------------------------------------------------- conversion plaque (sealed, intact)
SEAL = dict(w=200, h=80, m=4, point=14, bl=80, br=40, by=20)


def render_seal_plaque():
    Wd, Ht, M, P = SEAL["w"], SEAL["h"], SEAL["m"], SEAL["point"]
    x, y = grid(Wd, Ht)
    cx, cy = Wd / 2, Ht / 2
    hw, hh = cx - M, cy - M
    k = P / hh
    dpl = ((np.abs(x - cx) - hw) + np.abs(y - cy) * k) / math.sqrt(1 + k * k)
    dbody = -np.maximum(np.abs(y - cy) - hh, dpl)
    body = dbody > 0
    period = (Wd - SEAL["bl"] - SEAL["br"]) * SS
    nz = detail("stone", period, x.shape, 111, ox=-SEAL["bl"] * SS, oy=-SEAL["bl"] * SS)
    h = 6.0 * sstep(dbody / 3.0) + 0.28 * nz
    h -= 0.6 * groove(np.abs(dbody - 3.8), 0.6)
    sx = M + P + 22.0
    r = np.hypot(x - sx, y - cy)
    dso = 20.0 - r
    h = np.where(dso > 0, h - 2.4 * sstep(dso / 1.6), h)
    h -= 0.7 * groove(np.abs(r - 23.0), 0.7)
    dfield = -sd_box(x, y, (72 + Wd - 34) / 2, cy, (Wd - 34 - 72) / 2, hh - 10, r=2.0)
    field = dfield > 0
    h -= 1.6 * sstep(dfield / 1.5)
    h -= 0.6 * groove(np.abs(dfield + 2.4), 0.6)
    kx = Wd - M - P - 2.0
    rd = diamond_dist(x, y, kx, cy, 6.5)
    h -= 1.4 * groove(rd, 1.1) * ~field
    alb = stone_alb(x.shape, nz)
    alb = np.where(field[..., None], alb * 0.6, alb)
    alb = np.where((dso > 0)[..., None], glass_color(y, cy - 20, cy + 20) * 1.3, alb)
    glow = groove(rd, 0.8) * body * ~field + 0.45 * groove(np.abs(r - 23.0), 0.5) * body
    return compose(h, body.astype(float), alb, glow, sh=(1.2, 1.8, 2.0, 0.55), gk=(0.6, 0.35, 0.12))


# ---------------------------------------------------------------- frame overlays (frost, wet)
def render_frost(Wd=112, Ht=96):
    x, y = grid(Wd, Ht)
    h = np.zeros_like(x)
    m = np.zeros(x.shape, bool)
    shards = [(5, 5, 22, 60, 7.5), (7, 5, 46, 50, 8.5), (5, 8, 70, 56, 7.0), (14, 4, 5, 40, 5.0), (4, 14, 86, 40, 5.0),
              (24, 6, 32, 28, 4.2), (6, 24, 60, 28, 4.2), (36, 4, 12, 22, 3.4), (4, 36, 80, 22, 3.4), (50, 4, 2, 16, 2.6)]
    for bx, by, ang, ln, w in shards:
        a = math.radians(ang)
        ux, uy = math.cos(a), math.sin(a)
        along = (x - bx) * ux + (y - by) * uy
        across = -(x - bx) * uy + (y - by) * ux
        t = np.clip(along / ln, 0, 1)
        wid = w * np.where(t < 0.25, 0.6 + 1.6 * t, 1.0 - (t - 0.25) / 0.75)
        inside = (along > -1) & (along < ln) & (np.abs(across) < wid)
        hs = 2.0 + 4.0 * (1 - np.abs(across) / np.maximum(wid, 1e-3)) * (1 - 0.4 * t) + 1.5 * (1 - t)
        h = np.where(inside & (hs > h), hs, h)
        m |= inside
    # rime: frost grains along the top and left edge, fading away from the corner
    n = pnoise(128 * SS, [(0.4, .7), (1.2, .5)], 121)
    gr = tiled(n, x.shape)
    edge = np.minimum(y - 2, x - 2)
    fall = np.exp(-np.maximum(x, y) / 55.0)
    rime = (gr > 0.6 - 1.2 * sstep((8 - edge) / 6)) & (edge < 9) & (edge > 0) & (fall > 0.2)
    h = np.where(rime & ~m, 1.2 + 0.4 * gr, h)
    mm = m | rime
    alb = np.broadcast_to(rgb(PALETTE["ice"]), x.shape + (3,)) * (0.8 + 0.08 * gr)[..., None]
    col, _ = shade(h, alb, spec_k=0.55, spec_p=34)
    a = np.where(m, 0.88, np.where(rime, 0.7 * fall + 0.25, 0.0))
    sh = drop_shadow(mm.astype(float), 0.6, 0.9, 1.0, 0.35) * (1 - a)
    out_rgb, out_a = over(col, a, np.zeros_like(col), sh)
    g_rgb, g_a = glow_layer(m * 0.0 + (m & (h > 5.2)) * 0.6, rgb(PALETTE["ice"]), 0.3, 0.25, 0.15)
    out_rgb, out_a = over(g_rgb, g_a, out_rgb, out_a)
    return finish(out_rgb, out_a)


def render_wet(Wd=128, Ht=32):
    x, y = grid(Wd, Ht)
    h = np.zeros_like(x)
    m = np.zeros(x.shape, bool)
    for dx, ln, rb in ((14, 12, 3.8), (47, 22, 4.6), (79, 9, 3.2), (104, 17, 4.2)):
        neck = seg_dist(x, y, dx, 0, dx, ln)
        wneck = 0.6 + (rb - 0.6) * sstep(y / max(ln, 1)) ** 2
        d = np.minimum(neck - wneck, np.hypot(x - dx, y - ln) - rb)
        inside = d < 0
        hd = 3.0 * np.sqrt(np.clip(-d / rb, 0, 1))
        h = np.where(inside, np.maximum(h, hd), h)
        m |= inside
    film = y < 3.0
    h = np.where(film & ~m, 0.8, h)
    alb = np.broadcast_to(rgb(PALETTE["water"]), x.shape + (3,)) * 0.75
    col, _ = shade(h, alb, spec_k=1.2, spec_p=28)
    a = np.where(m, 0.72, np.where(film, 0.4 * (1 - y / 3.0), 0.0))
    return finish(col, a)


# ---------------------------------------------------------------- sockets / cells / plates / ring
def socket_round(x, y, c, R, rim, nz):
    r = np.hypot(x - c[0], y - c[1])
    dout, din = R - r, r - (R - rim)
    ring = (dout > 0) & (din > 0)
    h = 5.0 * np.minimum(sstep(dout / 2.2), sstep(din / 1.8)) + 0.2 * nz
    h -= 0.7 * groove(np.abs(dout - 3.2), 0.6)
    for k in range(4):  # rune notches at N/E/S/W
        a = k * math.pi / 2 - math.pi / 2
        px, py = c[0] + (R - rim / 2) * math.cos(a), c[1] + (R - rim / 2) * math.sin(a)
        h -= 1.2 * groove((np.abs(x - px) + np.abs(y - py)) / math.sqrt(2), 1.3) * ring
    return ring, h, din <= 0, r


def render_socket_status(more=False, S=80):
    x, y = grid(S, S)
    nz = detail("stone", S * SS, x.shape, 131)
    c = (37.0, 37.0) if more else (S / 2, S / 2)
    R = 33.0 if more else 36.0
    h = np.zeros_like(x)
    mask = np.zeros(x.shape, bool)
    alb = stone_alb(x.shape, nz)
    if more:  # two stacked sockets peeking out behind (overflow stack)
        for off, dk in ((6.0, 0.62), (3.0, 0.8)):
            rb, hb, _, _ = socket_round(x, y, (c[0] + off, c[1] + off), R, 7, nz)
            dsk = np.hypot(x - c[0] - off, y - c[1] - off) < R
            h = np.where(dsk, hb * (dsk & rb) + 3.0 * (dsk & ~rb), h)
            mask |= dsk
            alb = np.where(dsk[..., None], stone_alb(x.shape, nz) * dk, alb)
    ring, hr, glass, r = socket_round(x, y, c, R, 7, nz)
    disk = r < R
    h = np.where(disk, np.where(ring, hr, 0.0), h)
    mask |= disk
    alb = np.where(disk[..., None], stone_alb(x.shape, nz), alb)
    alb = np.where(glass[..., None], glass_color(y, c[1] - R, c[1] + R) * 1.15, alb)
    ish = inner_shadow(~glass, 1.0, 1.4, 1.6, 0.7)
    col_glass_dark = glass & disk
    img_mask = mask.astype(float)
    col, _ = shade(h, alb)
    col = np.where(col_glass_dark[..., None], col * (1 - ish)[..., None], col)
    out_rgb, out_a = over(col, img_mask, np.zeros_like(col), drop_shadow(img_mask, 1.0, 1.5, 1.8, 0.55) * (1 - img_mask))
    return finish(out_rgb, out_a)


def plate(Wd, Ht, M, chamfer, inset, seed, bx, field_dark=0.6):
    x, y = grid(Wd, Ht)
    cx, cy = Wd / 2, Ht / 2
    dbody = -sd_box(x, y, cx, cy, cx - M, cy - M, chamfer=chamfer)
    body = dbody > 0
    nz = detail("stone", max(Wd - 2 * bx, 8) * SS, x.shape, seed, ox=-bx * SS, oy=-bx * SS)
    h = 4.5 * sstep(dbody / 2.0) + 0.15 * nz
    dfield = -sd_box(x, y, cx, cy, cx - M - inset, cy - M - inset, r=1.5)
    field = dfield > 0
    h -= 1.2 * sstep(dfield / 1.2)
    alb = stone_alb(x.shape, nz)
    alb = np.where(field[..., None], alb * field_dark, alb)
    return compose(h, body.astype(float), alb, sh=(0.8, 1.2, 1.2, 0.5))


def render_socket_food(gem=False, S=112):
    x, y = grid(S, S)
    c = S / 2
    L1 = np.abs(x - c) + np.abs(y - c)
    dout, din = (52.0 - L1) / math.sqrt(2), (L1 - 38.0) / math.sqrt(2)
    rim = (dout > 0) & (din > 0)
    nz = detail("stone", S * SS, x.shape, 141 + gem)
    h = 5.5 * np.minimum(sstep(dout / 2.2), sstep(din / 1.8)) + 0.22 * nz
    h -= 0.7 * groove(np.abs(dout - 3.4), 0.6) * rim
    if gem:
        h -= 0.6 * groove(np.abs(din - 2.6), 0.5) * rim
    glass = din <= 0
    alb = stone_alb(x.shape, nz, "stone_block" if not gem else "stone_block", 0.09)
    if gem:
        alb = alb * 1.12
    alb = np.where(glass[..., None], glass_color(y, c - 38, c + 38) * 1.15, alb)
    glow = np.zeros_like(x)
    gems = np.zeros(x.shape, bool)
    pts = [(c, 11.0), (S - 11.0, c), (c, S - 11.0), (11.0, c)]
    for i, (px, py) in enumerate(pts):
        dd = (np.abs(x - px) + np.abs(y - py)) / math.sqrt(2)
        if gem:
            g = dd < 4.6
            h = np.where(g, 4.0 + 2.6 * sstep((4.6 - dd) / 2.0), h)
            gems |= g
            glow = np.maximum(glow, 0.8 * sstep((4.0 - dd) / 1.8))
        elif i == 0:
            h -= 1.3 * groove(np.abs(dd - 3.0), 0.8)
            glow = np.maximum(glow, 0.6 * groove(np.abs(dd - 3.0), 0.6))
    alb = np.where(gems[..., None], rgb(PALETTE["rune_glow"]) * 0.7, alb)
    if gem:
        glow = np.maximum(glow, 0.55 * groove(np.abs(dout + 0.8), 0.9))
    mask = (dout > 0).astype(float)
    col, _ = shade(h, alb)
    ish = inner_shadow(~glass, 1.0, 1.4, 1.8, 0.7)
    col = np.where(glass[..., None], col * (1 - ish)[..., None], col)
    out_rgb, out_a = over(col, mask, np.zeros_like(col), drop_shadow(mask, 1.0, 1.5, 1.8, 0.55) * (1 - mask))
    if glow.any():
        g_rgb, g_a = glow_layer(glow, rgb(PALETTE["rune_glow"]), 0.8, 0.45, 0.2)
        out_rgb, out_a = over(g_rgb, g_a, out_rgb, out_a)
    return finish(out_rgb, out_a)


def render_food_ring(S=112):
    x, y = grid(S, S)
    c = S / 2
    L1 = np.abs(x - c) + np.abs(y - c)
    return white_mask(stroke(np.abs(L1 - 42.0) / math.sqrt(2), 1.4), 0.4, 0.15)


def render_square_socket(S, M, rim, chamfer, rivets, seed):
    x, y = grid(S, S)
    c = S / 2
    hw = c - M
    dout = -sd_box(x, y, c, c, hw, hw, r=3.0 if not chamfer else 0.0, chamfer=chamfer)
    din = sd_box(x, y, c, c, hw - rim, hw - rim, r=2.0)
    rimm = (dout > 0) & (din > 0)
    nz = detail("stone", S * SS, x.shape, seed)
    h = 5.5 * np.minimum(sstep(dout / 2.2), sstep(din / 1.8)) + 0.2 * nz
    h -= 0.6 * groove(np.abs(dout - 3.4), 0.55) * rimm
    glass = din <= 0
    if rivets:
        for px in (M + rim / 2, S - M - rim / 2):
            for py in (M + rim / 2, S - M - rim / 2):
                dd = (np.abs(x - px) + np.abs(y - py)) / math.sqrt(2)
                st = dd < 4.2
                h = np.where(st, 5.5 + 2.0 * sstep((4.2 - dd) / 1.6), h)
    alb = stone_alb(x.shape, nz)
    alb = np.where(glass[..., None], glass_color(y, c - hw, c + hw) * 1.15, alb)
    mask = (dout > 0).astype(float)
    col, _ = shade(h, alb)
    ish = inner_shadow(~glass, 1.0, 1.4, 1.8, 0.7)
    col = np.where(glass[..., None], col * (1 - ish)[..., None], col)
    out_rgb, out_a = over(col, mask, np.zeros_like(col), drop_shadow(mask, 1.0, 1.5, 1.8, 0.55) * (1 - mask))
    return finish(out_rgb, out_a)


def render_ring_select(S=120):
    x, y = grid(S, S)
    c = S / 2
    d = np.abs(sd_box(x, y, c, c, c - 7, c - 7, r=9.0))
    core = stroke(d, 1.1)
    for px in (7.0, S - 7.0):  # corner ticks
        for py in (7.0, S - 7.0):
            core = np.maximum(core, stroke((np.abs(x - px) + np.abs(y - py)) / math.sqrt(2) - 2.2, 0.8))
    return white_mask(core, 0.9, 0.5)


# ---------------------------------------------------------------- scrollbar
def render_scroll_track(Wd=24, Ht=128):
    x, y = grid(Wd, Ht)
    cx = Wd / 2
    dbody = -sd_box(x, y, cx, Ht / 2, cx - 1.5, Ht / 2 - 1.5, r=6.0)
    body = dbody > 0
    nz = detail("stone", (Ht - 32) * SS, x.shape, 151, ox=-16 * SS, oy=-16 * SS)
    dch = -sd_box(x, y, cx, Ht / 2, 4.0, Ht / 2 - 6.0, r=3.5)
    h = 4.0 * sstep(dbody / 1.8) + 0.15 * nz - 2.6 * sstep(dch / 1.4)
    alb = stone_alb(x.shape, nz, "stone")
    alb = np.where((dch > 0)[..., None], glass_color(x * 0 + Ht / 2, 0, Ht) * 0.9, alb)
    return compose(h, body.astype(float), alb, sh=(0.6, 0.9, 1.0, 0.45))


def render_scroll_handle(state, Wd=24, Ht=64):
    x, y = grid(Wd, Ht)
    cx = Wd / 2
    dbody = -sd_box(x, y, cx, Ht / 2, 8.5, Ht / 2 - 2.5, r=7.5)
    body = dbody > 0
    nz = detail("stone", (Ht - 32) * SS, x.shape, 161, ox=-16 * SS, oy=-16 * SS)
    h = 5.0 * sstep(dbody / 2.0) + 0.18 * nz
    ch = seg_dist(x, y, cx, 13, cx, Ht - 13)
    h -= 1.4 * groove(ch, 1.2)
    alb = stone_alb(x.shape, nz)
    if state == "pressed":
        alb = alb * 0.8
    g = {"normal": 0.0, "hover": 0.75, "pressed": 1.0}[state]
    return compose(h, body.astype(float), alb, g * groove(ch, 0.8) * body, sh=(0.8, 1.2, 1.2, 0.5),
                   gk=(0.85, 0.5, 0.2))


def hud_sprites():
    """M1 HUD set -> list of (name, img, border(l,b,r,t), token, imageType, fillCenter, notes, pivot|None)."""
    wx, wy = BAR_WIN
    bx, by = BAR["bx"], BAR["by"]
    out = []
    A = out.append
    A(("bar_frame", render_bar_frame(), (bx, by, bx, by), "bar.frame", "Tiled", True,
       f"carved stone resource-bar frame, caps fixed, rails tile with period {BAR['w'] - 2 * bx} px; window (trough/fill/"
       f"DoT/reserve rect) = inset x {wx}, y {wy} (sprite px) -> window height {BAR['h'] - 2 * wy}; left cap socket "
       f"centre ({BAR['m'] + BAR['cap'] // 2}, {BAR['h'] // 2}) r 16 for the pool glyph; same frame for HP/stamina/eitr", None))
    A(("bar_trough", render_trough(), (12, 12, 12, 12), "bar.trough", "Sliced", True,
       "dark recessed glass, drawn in the bar window under everything", None))
    A(("bar_fill", render_bar_fill(), (8, 0, 8, 0), "bar.fill", "Tiled", True,
       "white grayscale-shaded mask, tint per pool (pool_hp/stamina/eitr/adrenaline); right border = bright fill front; "
       "centre tiles with period 144 px", None))
    A(("bar_lag", render_bar_lag(), (4, 0, 4, 0), "bar.lag", "Sliced", True,
       "damage-lag ghost: flat pale mask (tint bar_lag), under the fill, soft right end", None))
    A(("bar_growth", render_bar_growth(), (0, 0, 0, 0), "bar.growth", "Tiled", True,
       "max-growth shimmer: faint diagonal sheen, alpha <= 0.3 (never reads as fill), tint bar_growth; transient", None))
    A(("bar_end_line", render_end_line(), (0, 0, 0, 0), "bar.end.line", "Simple", True,
       "old max end ghost line (tint bar_end_line), 56 px tall = window + 6 px above/below; fades 0.8 s", None))
    A(("rail_frame_es", render_rail_frame("es"), (RAIL["bx"], RAIL["by"], RAIL["bx"], RAIL["by"]), "rail.frame.es", "Tiled",
       True, f"thin ES rail ({RAIL['h']} px = 35 % of bar_frame), same native/outer width as bar_frame; window inset x "
       f"{RAIL['wx']}, y {RAIL['wy']} -> height {RAIL['h'] - 2 * RAIL['wy']}; cyan gem studs", None))
    A(("rail_frame_adrenaline", render_rail_frame("adrenaline"), (RAIL["bx"], RAIL["by"], RAIL["bx"], RAIL["by"]),
       "rail.frame.adrenaline", "Tiled", True, "thin adrenaline rail, same geometry as rail_frame_es; ember chevron caps",
       None))
    A(("rail_trough", render_rail_trough(), (4, 3, 4, 3), "rail.trough", "Sliced", True, "rail window trough", None))
    A(("rail_fill", render_rail_fill(), (4, 0, 4, 0), "rail.fill", "Sliced", True,
       "white rail fill mask (tint pool_es / pool_adrenaline), bright front at the right end", None))
    for k in ("bleed", "fire", "poison"):
        A((f"dot_{k}", render_dot(k), (0, 0, 0, 0), f"bar.dot.{k}", "Tiled", True,
           {"bleed": "thick '/' slashes", "fire": "thin '^' flame chevrons", "poison": "staggered round drops"}[k]
           + f"; white mask, tint dot_{k}; tiles horizontally (period 16), height = bar window", None))
    A(("bar_reserved", render_reserved(), (0, 0, 0, 0), "bar.reserved", "Tiled", True,
       "reserved segment: dark stone with carved '\\' hatching (opposite of bleed), opaque, period 64 px; right end of "
       "the window; bar_reserved_edge at its left edge, glyph_lock centred (hide < 36 sprite px)", None))
    A(("bar_reserved_edge", render_reserved_edge(), (0, 0, 0, 0), "bar.reserved.edge", "Simple", True,
       "thin bright divider (tint reserved_edge), line at x = 2.5; pivot left edge", [0.0, 0.5]))
    A(("glyph_lock", render_glyph("lock"), (0, 0, 0, 0), "glyph.lock", "Simple", True,
       "rune padlock (tiwaz keyhole), white mask + glow, tint glyph", None))
    A(("glyph_es", render_glyph("es"), (0, 0, 0, 0), "glyph.es", "Simple", True,
       "energy-shield glyph (shield + algiz), white mask, tint pool_es", None))
    A(("glyph_seal", render_glyph("seal"), (0, 0, 0, 0), "glyph.seal", "Simple", True,
       "conversion seal (double ring + ingwaz), white mask, tint glyph; sits on plaque_seal socket", None))
    A(("plaque_seal", render_seal_plaque(), (SEAL["bl"], SEAL["by"], SEAL["br"], SEAL["by"]), "plaque.seal", "Tiled",
       True, f"intact sealed conversion plaque, height = bar_frame; seal socket centre ({SEAL['m'] + SEAL['point'] + 22}, "
       f"{SEAL['h'] // 2}) r 20 for glyph_seal; label field x 72..W-34 (sprite px); no cracks", None))
    A(("overlay_frost", render_frost(), (0, 0, 0, 0), "overlay.frost", "Simple", True,
       "frost corner overlay, pivot = frame top-left corner, native size; frame only", [0.0, 1.0]))
    A(("overlay_wet", render_wet(), (0, 0, 0, 0), "overlay.wet", "Tiled", True,
       "wet drips hanging from the frame bottom edge, tile horizontally (period 128), pivot top, place at frame "
       "bottom - 4 px", [0.5, 1.0]))
    A(("socket_status", render_socket_status(False), (0, 0, 0, 0), "socket.status", "Simple", True,
       "round carved status-icon socket, glass well r 29 (icon 48 px)", None))
    A(("socket_status_more", render_socket_status(True), (0, 0, 0, 0), "socket.status.more", "Simple", True,
       "'+N' overflow stack socket (front socket centre (37,37) r 33, text = font)", None))
    A(("plate_timer", plate(72, 28, 2, 6, 5, 171, 16), (16, 10, 16, 10), "plate.timer", "Tiled", True,
       "timer plate under a status socket, text = font", None))
    A(("socket_food", render_socket_food(False), (0, 0, 0, 0), "socket.food", "Simple", True,
       "rhombus food socket, glass well |dx|+|dy| < 38 (icon 56 px)", None))
    A(("socket_food_gem", render_socket_food(True), (0, 0, 0, 0), "socket.food.gem", "Simple", True,
       "gem-granted food socket: double rim + four rune-glass gems at the points", None))
    A(("food_timer_ring", render_food_ring(), (0, 0, 0, 0), "food.timer.ring", "Simple", True,
       "rhombus timer track, white mask; Image.Filled Radial360 origin top, tint food_timer; same rect as socket_food",
       None))
    A(("socket_quick", render_square_socket(104, 4, 9, 12, False, 181), (0, 0, 0, 0), "socket.quick", "Simple", True,
       "quick-slot socket; plate_key centred on its bottom edge, overlapping 12 px", None))
    A(("plate_key", plate(44, 36, 2, 8, 6, 182, 14), (14, 12, 14, 12), "plate.key", "Tiled", True,
       "key-hint plate (Z/V/B = font), stretch for longer labels", None))
    A(("socket_hotbar", render_square_socket(112, 4, 10, 0, True, 183), (0, 0, 0, 0), "socket.hotbar", "Simple", True,
       "hotbar cell with corner rivets; plate_number at its top-left, offset (6,6)", None))
    A(("plate_number", plate(36, 36, 2, 9, 6, 184, 8), (0, 0, 0, 0), "plate.number", "Simple", True,
       "carved number plate for hotbar cells (digit = font)", None))
    A(("ring_select", render_ring_select(), (30, 30, 30, 30), "ring.select", "Sliced", False,
       "selected/highlight ring, white mask + glow, tint select_ring; 8 px outside the socket on each side", None))
    A(("scroll_track", render_scroll_track(), (8, 16, 8, 16), "scroll.track", "Tiled", True,
       "vertical scrollbar track, centre tiles in y (period 96)", None))
    for st in ("normal", "hover", "pressed"):
        A((f"scroll_handle_{st}", render_scroll_handle(st), (8, 16, 8, 16), f"scroll.handle.{st}", "Tiled", True,
           "scrollbar handle, centre tiles in y (period 32)" + ("; channel glow" if st != "normal" else ""), None))
    return out


# ---------------------------------------------------------------- manifest + main
def entry(name, img, border, token, image_type, fill_center=True, notes=""):
    l, b, r, t = border
    return {"name": name, "file": f"{name}.png", "size": list(img.size),
            "border": {"left": l, "bottom": b, "right": r, "top": t}, "pivot": [0.5, 0.5],
            "imageType": image_type, "fillCenter": fill_center, "token": token, "notes": notes}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sprites, entries = {}, []
    fr = render_frame()
    S, B = FR["size"], FR["border"]
    sprites["frame_panel"] = fr["frame"]
    entries.append(entry("frame_panel", fr["frame"], (B,) * 4, "frame.panel", "Tiled", False,
                         f"carved stone rail {FR['rail']} px + outer shadow {FR['margin']} px; place glass fill inset "
                         f"{FR['margin'] + FR['rail'] // 2} px (sprite px); edges tile with period {S - 2 * B} px"))
    sprites["frame_panel_glow"] = fr["glow"]
    entries.append(entry("frame_panel_glow", fr["glow"], (B,) * 4, "frame.panel.glow", "Tiled", False,
                         "white emissive rune/channel mask, tint with colour token rune_glow; same rect as frame_panel"))
    for key in ("tl", "tr", "bl", "br"):
        x0 = 0 if key[1] == "l" else S - B
        y0 = 0 if key[0] == "t" else S - B
        img = fr["caps"].crop((x0, y0, x0 + B, y0 + B))
        name = f"frame_panel_corner_{key}"
        sprites[name] = img
        piv = [0.0 if key[1] == "l" else 1.0, 1.0 if key[0] == "t" else 0.0]
        e = entry(name, img, (0, 0, 0, 0), f"frame.panel.corner.{key}", "Simple",
                  notes="leather wrap overlay, never stretched; anchor/pivot at the panel corner, native size")
        e["pivot"] = piv
        entries.append(e)
    g = render_glass()
    sprites["fill_glass"] = g
    entries.append(entry("fill_glass", g, (16, 16, 16, 32), "fill.glass", "Sliced", True,
                         "dark glass: vertical gradient + top highlight, no texture"))
    for st in ("normal", "hover", "pressed", "disabled", "selected"):
        im = render_button(st)
        sprites[f"button_{st}"] = im
        entries.append(entry(f"button_{st}", im, (52, 28, 52, 28), f"button.{st}", "Sliced", True,
                             "stone caps with carved knots + glass well; text area = centre"
                             + ("; selected = gamepad focus" if st == "selected" else "")))
    t = render_tooltip()
    sprites["tooltip_frame"] = t
    entries.append(entry("tooltip_frame", t, (32, 32, 32, 32), "frame.tooltip", "Sliced", True,
                         "light stone rail 7 px + diamond studs, glass fill included; content padding 20 px"))
    dv = render_divider()
    sprites["divider"] = dv
    entries.append(entry("divider", dv, (40, 0, 40, 0), "divider", "Sliced", True,
                         "engraved line, constant along x (stretch-safe), stone studs in the ends"))
    pl = render_plaque()
    sprites["header_plaque"] = pl
    entries.append(entry("header_plaque", pl, (PL["border_x"], PL["border_y"], PL["border_x"], PL["border_y"]),
                         "plaque.header", "Tiled", True, "text field = recessed centre; ends carry knots"))
    kn = render_knot()
    sprites["knot"] = kn
    entries.append(entry("knot", kn, (0, 0, 0, 0), "ornament.knot", "Simple", True,
                         "menu-entry ornament (vanilla BraidKnotMedium slot), symmetric, native aspect; shown on hover"))
    for name, im, border, token, itype, fc, notes, piv in hud_sprites():
        sprites[name] = im
        e = entry(name, im, border, token, itype, fc, notes)
        if piv:
            e["pivot"] = piv
        entries.append(e)
    for n, im in sprites.items():
        im.save(OUT / f"{n}.png", optimize=True)
    manifest = {"version": 1, "authoredScale": 2, "pixelsPerUnit": 200,
                "borderOrder": "sprite px; Unity spriteBorder = (left, bottom, right, top)",
                "light": "key light from top-left", "palette": PALETTE, "sprites": entries}
    (OUT / "sprites.json").write_text(json.dumps(manifest, indent=2) + "\n", newline="\n")
    print(f"wrote {len(entries)} sprites to {OUT}")


if __name__ == "__main__":
    main()
