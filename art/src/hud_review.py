"""M1 HUD review renders (review aid, not part of the build).

Usage: python -I art/src/hud_review.py <out_dir> [background.png]
-> contact-sheet-m1.png (M1 sprites; white masks shown tinted with their palette colour on dark glass),
   composite-hud.png (bottom-left HUD cluster drawn with Unity Sliced/Tiled rules from sprites.json),
   dot-grayscale.png (the three DoT patterns: raw mask, over the HP fill, and that in grayscale).
"""
import importlib.util
import json
import math
import pathlib
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps

ART = pathlib.Path(__file__).resolve().parents[1]
SP = ART / "sprites"
_spec = importlib.util.spec_from_file_location("contact_sheet", ART / "src" / "contact_sheet.py")
cs = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(cs)
nine = cs.nine
FONT_D, FONT_B = cs.FONT_D, cs.FONT_B

M0 = {"frame_panel", "frame_panel_glow", "frame_panel_corner_tl", "frame_panel_corner_tr", "frame_panel_corner_bl",
      "frame_panel_corner_br", "fill_glass", "button_normal", "button_hover", "button_pressed", "button_disabled",
      "button_selected", "tooltip_frame", "divider", "header_plaque", "knot"}
# preview tint (palette key) for white masks
TINT = {"bar_fill": "pool_hp", "bar_lag": "bar_lag", "bar_growth": "bar_growth", "bar_end_line": "bar_end_line",
        "rail_fill": "pool_es", "dot_bleed": "dot_bleed", "dot_fire": "dot_fire", "dot_poison": "dot_poison",
        "bar_reserved_edge": "reserved_edge", "glyph_lock": "glyph", "glyph_es": "pool_es", "glyph_seal": "glyph",
        "food_timer_ring": "food_timer", "ring_select": "select_ring"}


def hexc(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def tint(img, col, alpha=1.0):
    r, g, b, a = img.split()
    out = Image.merge("RGBA", (r.point(lambda v: v * col[0] // 255), g.point(lambda v: v * col[1] // 255),
                               b.point(lambda v: v * col[2] // 255), a.point(lambda v: int(v * alpha))))
    return out


def tile_x(img, w, h=None):
    """Tiled sprite without borders: repeat the whole sprite (Unity Image.Tiled, no border)."""
    h = h or img.height
    out = Image.new("RGBA", (w, h))
    for ty in range(0, h, img.height):
        for tx in range(0, w, img.width):
            out.paste(img, (tx, ty))
    return out


def text_c(d, box, s, font, fill):
    cs.text_center(d, box, s, font, fill)


def load():
    man = json.loads((SP / "sprites.json").read_text())
    return man, {e["name"]: (e, Image.open(SP / e["file"]).convert("RGBA")) for e in man["sprites"]}


def sheet(man, sprites, out):
    pal = man["palette"]
    items = [(n, v) for n, v in sprites.items() if n not in M0]
    pad, W = 24, 1800
    f = ImageFont.truetype(FONT_B, 16)
    # simple shelf packing
    x, y, rowh, pos = pad, pad, 0, []
    for n, (e, img) in items:
        w = max(img.width, 150)
        if x + w > W - pad:
            x, y, rowh = pad, y + rowh + 30 + pad, 0
        pos.append((n, e, img, x, y))
        x += w + pad
        rowh = max(rowh, img.height)
    H = y + rowh + 30 + pad
    im = Image.new("RGBA", (W, H), (38, 44, 40, 255))
    d = ImageDraw.Draw(im)
    for n, e, img, x, y in pos:
        if n in TINT:  # mask: dark glass behind, tinted
            d.rectangle((x, y, x + img.width - 1, y + img.height - 1), fill=(12, 16, 19, 255))
            im.alpha_composite(tint(img, hexc(pal[TINT[n]])), (x, y))
        else:
            for cy in range(0, img.height, 12):
                for cx in range(0, img.width, 12):
                    c = (70, 74, 72, 255) if (cx // 12 + cy // 12) % 2 else (95, 100, 98, 255)
                    d.rectangle((x + cx, y + cy, x + min(cx + 11, img.width - 1), y + min(cy + 11, img.height - 1)), fill=c)
            im.alpha_composite(img, (x, y))
        b = e["border"]
        if any(b.values()):
            d.rectangle((x + b["left"], y + b["top"], x + img.width - b["right"] - 1, y + img.height - b["bottom"] - 1),
                        outline=(255, 0, 200, 160))
        d.text((x, y + img.height + 4), f"{n} {img.width}x{img.height}", font=f, fill=(230, 230, 220, 255))
    im.convert("RGB").save(out)


class Hud:
    def __init__(self, man, sprites, canvas):
        self.pal, self.s, self.c = man["palette"], sprites, canvas

    def img(self, n):
        return self.s[n][1]

    def put(self, n, xy, w=None, h=None, col=None, alpha=1.0):
        e, img = self.s[n]
        if w or h:
            w, h = w or img.width, h or img.height
            if e["imageType"] in ("Sliced", "Tiled") and any(e["border"].values()):
                img = nine(img, e, w, h)
            elif e["imageType"] == "Tiled":
                img = tile_x(img, w, h)
            else:
                img = img.resize((w, h), Image.BICUBIC)
        if col:
            img = tint(img, hexc(self.pal[col]) if isinstance(col, str) else col, alpha)
        elif alpha < 1:
            img = tint(img, (255, 255, 255), alpha)
        self.c.alpha_composite(img, (int(xy[0]), int(xy[1])))
        return img.size

    def bar(self, x, y, W, pool, frac, lag=0.0, dots=(), res=0.0, overlay=None, growth=None, label=None):
        wx, wy = 76, 18  # bar_frame window inset (sprites.json notes)
        wh = 80 - 2 * wy
        win_w = W - 2 * wx
        X0, Y0 = x + wx, y + wy
        self.put("bar_trough", (X0, Y0), win_w, wh)
        fw = int(win_w * frac)
        if lag > frac:
            self.put("bar_lag", (X0, Y0), int(win_w * lag), wh, "bar_lag", 0.55)
        if fw > 16:
            self.put("bar_fill", (X0, Y0), fw, wh, pool)
        if growth:  # (from, to) fraction of the window: newly added trough
            g0, g1 = int(win_w * growth[0]), int(win_w * growth[1])
            self.put("bar_growth", (X0 + g0, Y0), g1 - g0, wh, "bar_growth")
            self.put("bar_end_line", (X0 + g0 - 4, Y0 - 6), col="bar_end_line", alpha=0.8)
        end = X0 + fw
        for kind, f in dots:  # right-aligned from the current fill end, inside the fill
            dw = int(win_w * f)
            self.put(f"dot_{kind}", (end - dw, Y0), dw, wh, f"dot_{kind}")
            end -= dw
        if res > 0:
            rw = int(win_w * res)
            rx = X0 + win_w - rw
            self.put("bar_reserved", (rx, Y0), rw, wh)
            self.put("bar_reserved_edge", (rx - 2, Y0), None, None, "reserved_edge")
            if rw >= 36:
                g = self.img("glyph_lock")
                self.put("glyph_lock", (rx + (rw - g.width) // 2, Y0 + (wh - g.height) // 2), col="glyph")
        self.put("bar_frame", (x, y), W, 80)
        if overlay == "frost":
            self.put("overlay_frost", (x, y))
        elif overlay == "wet":
            self.put("overlay_wet", (x + 40, y + 76), W - 80, 32)
        return X0, Y0, win_w, wh


def composite(man, sprites, bg_path, out):
    W, H = 1700, 1080
    if bg_path and pathlib.Path(bg_path).exists():
        src = Image.open(bg_path).convert("RGB")
        bg = src.resize((W * 2, int(src.height * W * 2 / src.width)))
        bg = bg.crop((bg.width - W, 0, bg.width, H)).filter(ImageFilter.GaussianBlur(8))  # scenery, no mock HUD
        bg = Image.blend(bg, Image.new("RGB", bg.size, (0, 0, 0)), 0.45).convert("RGBA")
    else:
        bg = Image.new("RGBA", (W, H), (40, 52, 48, 255))
    hud = Hud(man, sprites, bg)
    pal = man["palette"]
    d = ImageDraw.Draw(bg)
    fnum = ImageFont.truetype(FONT_D, 40)
    fsm = ImageFont.truetype(FONT_D, 26)
    fkey = ImageFont.truetype(FONT_D, 28)
    x0, BW = 48, 820
    # food row + quick slots
    fy = 118
    icons = [((176, 92, 60), "M"), ((205, 150, 80), "B"), ((170, 40, 70), "R"), ((150, 30, 40), "J")]
    for i in range(4):
        fx = x0 + i * 120
        hud.put("socket_food_gem" if i == 3 else "socket_food", (fx, fy))
        d.ellipse((fx + 36, fy + 36, fx + 76, fy + 76), fill=icons[i][0] + (255,))
        text_c(d, (fx, fy, fx + 112, fy + 112), icons[i][1], fsm, (30, 20, 15))
        ring = hud.img("food_timer_ring")
        frac = [0.8, 0.55, 0.3, 0.95][i]
        mask = Image.new("L", ring.size, 0)
        ImageDraw.Draw(mask).pieslice((-40, -40, 152, 152), -90, -90 + 360 * frac, fill=255)
        r = tint(ring, hexc(pal["food_timer"]))
        r.putalpha(Image.composite(r.split()[3], Image.new("L", r.size, 0), mask))
        bg.alpha_composite(tint(ring, (60, 70, 75), 0.6), (fx, fy))
        bg.alpha_composite(r, (fx, fy))
    qx = x0 + 4 * 120 + 24
    for i, (k, n, col) in enumerate((("Z", "5", (200, 40, 40)), ("V", "3", (60, 110, 230)), ("B", "32", (190, 150, 90)))):
        sx = qx + i * 116
        hud.put("socket_quick", (sx, fy + 2))
        d.rounded_rectangle((sx + 34, fy + 28, sx + 70, fy + 78), 8, fill=col + (255,))
        d.text((sx + 92, fy + 84), n, font=fsm, fill=hexc(pal["text_body"]), anchor="rs")
        pw = hud.put("plate_key", (sx + 30, fy + 2 + 104 - 12))
        text_c(d, (sx + 30, fy + 94, sx + 74, fy + 130), k, fkey, hexc(pal["text_body"]))
        if i == 0:
            hud.put("ring_select", (sx - 8, fy - 6), 120, 120, "select_ring")
    # ES rail
    y = 270
    rx, ry = 0, 0
    hud.put("rail_trough", (x0 + 30, y + 9), BW - 60, 10)
    hud.put("rail_fill", (x0 + 30, y + 9), int((BW - 60) * 0.76), 10, "pool_es")
    hud.put("rail_frame_es", (x0, y), BW, 28)
    d.text((x0 + BW - 40, y + 14), "38", font=ImageFont.truetype(FONT_D, 24), fill=hexc(pal["text_body"]), anchor="rm")
    # HP: 54/100 of B, reserve 25 % (Blood Magic), bleed + fire pending, lag ghost
    y += 32
    X0, Y0, ww, wh = hud.bar(x0, y, BW, "pool_hp", 0.54, lag=0.62, dots=(("bleed", 0.07), ("fire", 0.05)), res=0.25,
                             overlay="frost")
    text_c(d, (X0, Y0, X0 + ww * 0.75, Y0 + wh), "54/75", fnum, hexc(pal["text_body"]))
    # stamina: just ate, max grew (growth shimmer + old end line), wet overlay
    y += 84
    hud.bar(x0, y, BW, "pool_stamina", 0.58, growth=(0.80, 1.0), overlay="wet")
    # eitr converted -> sealed plaque
    y += 84
    pw = 300
    hud.put("plaque_seal", (x0, y), pw, 80)
    gs = hud.img("glyph_seal")
    hud.put("glyph_seal", (x0 + 40 - gs.width // 2, y + 40 - gs.height // 2), col="glyph")
    text_c(d, (x0 + 72, y, x0 + pw - 34, y + 80), "EITR → SHIELD", ImageFont.truetype(FONT_D, 26), hexc(pal["rune_glow"]))
    # adrenaline thin rail next to the plaque
    hud.put("rail_trough", (x0 + pw + 40 + 30, y + 26 + 9), BW - pw - 40 - 60, 10)
    hud.put("rail_fill", (x0 + pw + 40 + 30, y + 26 + 9), int((BW - pw - 100) * 0.4), 10, "pool_adrenaline")
    hud.put("rail_frame_adrenaline", (x0 + pw + 40, y + 26), BW - pw - 40, 28)
    # status strip: 6 icons + "+N"
    y += 100
    cols = [(220, 60, 50), (230, 170, 60), (90, 220, 220), (90, 170, 240), (240, 200, 120), (130, 220, 80)]
    times = ["14:32", "4:10", "18:05", "42s", "12s", "8s"]
    for i in range(7):
        sx = x0 + i * 96
        if i < 6:
            hud.put("socket_status", (sx, y))
            d.regular_polygon((sx + 40, y + 40, 17), 6 - i % 3, fill=cols[i] + (255,))
        else:
            hud.put("socket_status_more", (sx, y))
            text_c(d, (sx + 4, y + 4, sx + 70, y + 70), "+3", fnum, hexc(pal["text_body"]))
            continue
        hud.put("plate_timer", (sx + 4, y + 76))
        text_c(d, (sx + 4, y + 76, sx + 76, y + 104), times[i], ImageFont.truetype(FONT_B, 18), hexc(pal["text_body"]))
    # hotbar + scrollbar sample (right side)
    hx, hy = 1000, 150
    for i in range(4):
        cx = hx + i * 120
        hud.put("socket_hotbar", (cx, hy))
        hud.put("plate_number", (cx + 6, hy + 6))
        text_c(d, (cx + 6, hy + 6, cx + 42, hy + 42), str(i + 1), ImageFont.truetype(FONT_D, 24), hexc(pal["text_header"]))
    hud.put("ring_select", (hx + 120 - 4, hy - 4), 120, 120, "select_ring")
    sx = 1580
    hud.put("scroll_track", (sx, 330), 24, 420)
    hud.put("scroll_handle_hover", (sx, 420), 24, 140)
    bg.convert("RGB").save(out)


def dot_gray(man, sprites, out):
    pal = man["palette"]
    W, rowh = 3 * 300 + 4 * 30, 44
    im = Image.new("RGBA", (W, 30 + 3 * (rowh + 40)), (20, 24, 26, 255))
    d = ImageDraw.Draw(im)
    f = ImageFont.truetype(FONT_B, 18)
    fill = sprites["bar_fill"]
    for i, k in enumerate(("bleed", "fire", "poison")):
        x = 30 + i * 330
        e, img = sprites[f"dot_{k}"]
        raw = tile_x(img, 300, rowh)
        bg = Image.new("RGBA", raw.size, (0, 0, 0, 255))
        bg.alpha_composite(raw)
        im.alpha_composite(bg, (x, 30))
        hp = tint(nine(fill[1], fill[0], 300, rowh), hexc(pal["pool_hp"]))
        hp.alpha_composite(tint(raw, hexc(pal[f"dot_{k}"])))
        im.alpha_composite(hp, (x, 30 + rowh + 40))
        im.alpha_composite(ImageOps.grayscale(hp.convert("RGB")).convert("RGBA"), (x, 30 + 2 * (rowh + 40)))
        d.text((x, 6), f"dot_{k}: mask / over HP / grayscale", font=f, fill=(220, 220, 210))
    im.convert("RGB").save(out)


def main():
    out = pathlib.Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    man, sprites = load()
    sheet(man, sprites, out / "contact-sheet-m1.png")
    composite(man, sprites, sys.argv[2] if len(sys.argv) > 2 else None, out / "composite-hud.png")
    dot_gray(man, sprites, out / "dot-grayscale.png")
    print(f"wrote contact-sheet-m1.png, composite-hud.png, dot-grayscale.png to {out}")


if __name__ == "__main__":
    main()
