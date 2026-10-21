"""Contact sheet + mock pause-menu composite of art/sprites (review aid; output path given on the command line).

Usage: python -I art/src/contact_sheet.py <out_dir> [background.png]
Emulates Unity Image Sliced/Tiled 9-slice drawing from sprites.json so frames are judged the way the game draws them.
"""
import json
import pathlib
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ART = pathlib.Path(__file__).resolve().parents[1]
SP = ART / "sprites"
FONT_D = str(ART / "fonts" / "Cormorant-Bold.otf")
FONT_B = str(ART / "fonts" / "PT_Sans-Web-Regular.ttf")


def load():
    man = json.loads((SP / "sprites.json").read_text())
    return man, {e["name"]: (e, Image.open(SP / e["file"]).convert("RGBA")) for e in man["sprites"]}


def nine(img, e, w, h, mode=None):
    mode = mode or e["imageType"]
    b = e["border"]
    L, R, T, B = b["left"], b["right"], b["top"], b["bottom"]
    sw, sh = img.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    xs = [(0, L, 0, L), (L, sw - R, L, w - R), (sw - R, sw, w - R, w)]
    ys = [(0, T, 0, T), (T, sh - B, T, h - B), (sh - B, sh, h - B, h)]
    for i, (sx0, sx1, dx0, dx1) in enumerate(xs):
        for j, (sy0, sy1, dy0, dy1) in enumerate(ys):
            if i == 1 and j == 1 and not e.get("fillCenter", True):
                continue
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            part = img.crop((sx0, sy0, sx1, sy1))
            if mode == "Tiled" and (i == 1 or j == 1):
                tile = Image.new("RGBA", (dx1 - dx0, dy1 - dy0))
                for ty in range(0, dy1 - dy0, part.height if j == 1 else 10 ** 6):
                    for tx in range(0, dx1 - dx0, part.width if i == 1 else 10 ** 6):
                        tile.paste(part.resize((part.width if i == 1 else dx1 - dx0, part.height if j == 1 else dy1 - dy0)), (tx, ty))
                out.alpha_composite(tile, (dx0, dy0))
            else:
                out.alpha_composite(part.resize((dx1 - dx0, dy1 - dy0), Image.BICUBIC), (dx0, dy0))
    return out


def text_center(d, box, s, font, fill):
    x0, y0, x1, y1 = box
    l, t, r, b = d.textbbox((0, 0), s, font=font)
    d.text(((x0 + x1 - (r - l)) / 2 - l, (y0 + y1 - (b - t)) / 2 - t), s, font=font, fill=fill)


def composite(sprites, bg_path, out):
    W, H = 1500, 1100
    if bg_path and pathlib.Path(bg_path).exists():
        bg = Image.open(bg_path).convert("RGB").resize((W, H)).filter(ImageFilter.GaussianBlur(6))
        bg = Image.blend(bg, Image.new("RGB", bg.size, (0, 0, 0)), 0.35).convert("RGBA")
    else:
        bg = Image.new("RGBA", (W, H), (60, 80, 70, 255))
    pal = json.loads((SP / "sprites.json").read_text())["palette"]
    hexc = lambda h: tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))
    px, py, pw, ph = 120, 110, 760, 900
    fe, fimg = sprites["frame_panel"]
    inset = 8 + 15
    ge, gimg = sprites["fill_glass"]
    bg.alpha_composite(nine(gimg, ge, pw - 2 * inset, ph - 2 * inset), (px + inset, py + inset))
    bg.alpha_composite(nine(fimg, fe, pw, ph), (px, py))
    we, wimg = sprites["frame_panel_glow"]
    glow = nine(wimg, we, pw, ph)
    r, g, b, a = glow.split()
    gc = Image.new("RGBA", glow.size, hexc(pal["rune_glow"]) + (255,))
    gc.putalpha(a)
    bg.alpha_composite(gc, (px, py))
    for key in ("tl", "tr", "bl", "br"):
        ce, cimg = sprites[f"frame_panel_corner_{key}"]
        cx = px if key[1] == "l" else px + pw - cimg.width
        cy = py if key[0] == "t" else py + ph - cimg.height
        bg.alpha_composite(cimg, (cx, cy))
    pe, pimg = sprites["header_plaque"]
    plq = nine(pimg, pe, 420, 72)
    bg.alpha_composite(plq, (px + (pw - 420) // 2, py - 30))
    d = ImageDraw.Draw(bg)
    fd = ImageFont.truetype(FONT_D, 46)
    fb = ImageFont.truetype(FONT_B, 30)
    text_center(d, (px + (pw - 420) // 2, py - 30, px + (pw + 420) // 2, py + 42), "МЕНЮ", fd, hexc(pal["text_header"]))
    labels = [("normal", "Продолжить"), ("hover", "Настройки"), ("selected", "Сохранить игру"),
              ("pressed", "Выйти в меню"), ("disabled", "Выход из игры")]
    by = py + 120
    for i, (st, s) in enumerate(labels):
        be, bimg = sprites[f"button_{st}"]
        bw = 520
        btn = nine(bimg, be, bw, 96)
        bx = px + (pw - bw) // 2
        bg.alpha_composite(btn, (bx, by))
        col = hexc(pal["text_disabled"]) if st == "disabled" else hexc(pal["text_body"])
        text_center(d, (bx, by + (2 if st == "pressed" else 0), bx + bw, by + 96), s, ImageFont.truetype(FONT_D, 40), col)
        by += 110
        if i == 2:
            de, dimg = sprites["divider"]
            bg.alpha_composite(nine(dimg, de, 600, 24), (px + 80, by))
            by += 40
    d.text((px + 70, by + 30), "Ёжик №7 «Вальхейм» — съешь же ещё этих мягких булок.", font=ImageFont.truetype(FONT_B, 26),
           fill=hexc(pal["text_body"]))
    te, timg = sprites["tooltip_frame"]
    tt = nine(timg, te, 470, 210)
    bg.alpha_composite(tt, (950, 300))
    d.text((978, 326), "Сохранить игру", font=ImageFont.truetype(FONT_D, 38), fill=hexc(pal["text_header"]))
    de, dimg = sprites["divider"]
    bg.alpha_composite(nine(dimg, de, 420, 24), (975, 376))
    d.multiline_text((978, 410), "Записать мир и персонажа.\nQuick save: F5  ·  «Ёлка» №1", font=fb,
                     fill=hexc(pal["text_body"]), spacing=8)
    bg.convert("RGB").save(out)


def sheet(man, sprites, out):
    pad, cols = 24, 4
    items = list(sprites.items())
    cw = max(i[1][1].width for i in items) + pad
    ch = max(i[1][1].height for i in items) + pad + 30
    rows = (len(items) + cols - 1) // cols
    im = Image.new("RGBA", (cols * cw + pad, rows * ch + pad), (38, 44, 40, 255))
    d = ImageDraw.Draw(im)
    f = ImageFont.truetype(FONT_B, 18)
    for k, (n, (e, img)) in enumerate(items):
        x, y = pad + (k % cols) * cw, pad + (k // cols) * ch
        for cy in range(0, img.height, 16):  # checker behind each sprite shows the alpha
            for cx in range(0, img.width, 16):
                c = (70, 74, 72, 255) if (cx // 16 + cy // 16) % 2 else (95, 100, 98, 255)
                d.rectangle((x + cx, y + cy, x + min(cx + 15, img.width - 1), y + min(cy + 15, img.height - 1)), fill=c)
        im.alpha_composite(img, (x, y))
        b = e["border"]
        if any(b.values()):
            d.rectangle((x + b["left"], y + b["top"], x + img.width - b["right"] - 1, y + img.height - b["bottom"] - 1),
                        outline=(255, 0, 200, 160))
        d.text((x, y + img.height + 4), f"{n} {img.width}x{img.height}", font=f, fill=(230, 230, 220, 255))
    im.convert("RGB").save(out)


def main():
    out = pathlib.Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    man, sprites = load()
    sheet(man, sprites, out / "contact-sheet.png")
    composite(sprites, sys.argv[2] if len(sys.argv) > 2 else None, out / "composite-pause-menu.png")
    print(f"wrote {out / 'contact-sheet.png'} and {out / 'composite-pause-menu.png'}")


if __name__ == "__main__":
    main()
