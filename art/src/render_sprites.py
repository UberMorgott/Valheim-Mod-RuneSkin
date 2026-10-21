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
    for n, im in sprites.items():
        im.save(OUT / f"{n}.png", optimize=True)
    manifest = {"version": 1, "authoredScale": 2, "pixelsPerUnit": 200,
                "borderOrder": "sprite px; Unity spriteBorder = (left, bottom, right, top)",
                "light": "key light from top-left", "palette": PALETTE, "sprites": entries}
    (OUT / "sprites.json").write_text(json.dumps(manifest, indent=2) + "\n", newline="\n")
    print(f"wrote {len(entries)} sprites to {OUT}")


if __name__ == "__main__":
    main()
