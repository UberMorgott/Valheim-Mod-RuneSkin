package main

import (
	"fmt"
	"image"
	"image/color"
	"math"

	"golang.org/x/image/font"
	"golang.org/x/image/math/fixed"
)

// M4 review composites (1920x1080 mockup px): m4-hud-a (minimap ring, boss bar, notifications, damage text),
// m4-hud-b (enemy bars with Vaettir tier frames, centre message, biome banner) and m4-map-a (large map + legend),
// rebuilt from our sprites. Map art, pin/item icons and enemy figures are content copied from the mockups.

// textOutline: text with a dark 8-direction outline of o mockup px (damage numbers, world-space labels).
func (c *canvas) textOutline(s, file string, size float64, col string, x, y, o float64, align int) {
	fc := face(file, size*c.K)
	d := &font.Drawer{Face: fc}
	w := d.MeasureString(s).Round()
	px := int(x * c.K)
	if align == 1 {
		px -= w / 2
	}
	py := int(y*c.K + size*c.K*0.34)
	k := int(math.Round(o * c.K))
	dark := image.NewUniform(color.NRGBA{5, 8, 10, 230})
	for _, dd := range [][2]int{{-1, -1}, {0, -1}, {1, -1}, {-1, 0}, {1, 0}, {-1, 1}, {0, 1}, {1, 1}} {
		dr := &font.Drawer{Dst: c.img, Src: dark, Face: fc, Dot: fixed.P(px+dd[0]*k, py+dd[1]*k)}
		dr.DrawString(s)
	}
	// faux bold (TMP: Bold style / SDF dilate): the fill drawn at sub-outline offsets
	b := max(1, k)
	for _, dd := range [][2]int{{0, 0}, {b, 0}, {0, b}, {b, b}} {
		dr := &font.Drawer{Dst: c.img, Src: image.NewUniform(hex(col)), Face: fc, Dot: fixed.P(px+dd[0], py+dd[1])}
		dr.DrawString(s)
	}
}

// masked copies mockup content (the map) into r, clipped by a white mask sprite drawn into the same rect.
func (c *canvas) masked(mock *image.NRGBA, x, y, w, h float64, mask string) {
	r := c.R(x, y, w, h)
	p := resize(crop(mock, image.Rect(int(x), int(y), int(x+w), int(y+h))), r.Dx(), r.Dy())
	m := resize(sprites[mask].img, r.Dx(), r.Dy())
	for i := 3; i < len(p.Pix); i += 4 {
		p.Pix[i] = uint8(int(p.Pix[i]) * int(m.Pix[i]) / 255)
	}
	over(c.img, p, r.Min)
}

// copyMock copies mockup content unchanged (map image, figures).
func (c *canvas) copyMock(mock *image.NRGBA, x, y, w, h float64) {
	r := c.R(x, y, w, h)
	over(c.img, resize(crop(mock, image.Rect(int(x), int(y), int(x+w), int(y+h))), r.Dx(), r.Dy()), r.Min)
}

// hpBar: frame sprite over a trough + tinted fill (window inset in sprite px of the frame borders).
func (c *canvas) hpBar(frame string, x, y, w, h, frac float64, insetX, insetY float64) {
	ix, iy := insetX/c.K, insetY/c.K
	c.sprite("bar_trough", x+ix, y+iy, w-2*ix, h-2*iy)
	c.tint("bar_enemy_fill", x+ix, y+iy, (w-2*ix)*frac, h-2*iy, "#c0392b", 1)
	c.sprite(frame, x, y, w, h)
}

func compareM4HudA() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m4-hud-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	// minimap: masked map, ring, rune stones, wind socket, sun, biome plaque
	cx, cy, d := 1730.0, 186.0, 292.0
	c.masked(mock, cx-d/2, cy-d/2, d, d, "minimap_mask")
	c.sprite("minimap_ring", cx-d/2, cy-d/2, d, d)
	rr := d/2 - d*0.05
	for _, s := range []struct {
		l      string
		dx, dy float64
	}{{"N", 0, -1}, {"E", 1, 0}, {"S", 0, 1}, {"W", -1, 0}} {
		sx, sy := cx+s.dx*rr, cy+s.dy*rr
		c.sprite("minimap_rune_stone", sx-25, sy-25, 50, 50)
		c.textOutline(s.l, serif, 25, palette["rune_glow"], sx, sy, 0.6, 1)
	}
	c.sprite("minimap_wind_socket", 1820, 74, 40, 50)
	c.glyph("glyph_wind", 1838, 103, 26, palette["rune_glow"], 1)
	c.glyph("glyph_time_sun", cx, 34, 30, gold, 1)
	c.sprite("tab_normal", 1636, 330, 190, 36)
	c.text("Чёрный лес", serif, 22, head, 1731, 348, 1)
	// boss bar + name plaque
	c.hpBar("frame_boss", 540, 66, 840, 72, 0.7, 154, 34)
	c.header(852, 40, 216, 32, "", "")
	c.text("ЭЙКТЮР", serif, 20, head, 960, 56, 1)
	// notifications (top-left)
	for i, n := range []string{"Получено: Дерево x10", "Получено: Камень x4", "Навык повышен: Рубка 21"} {
		y := 116 + float64(i)*54.5
		c.sprite("frame_small", 39, y, 336, 47)
		c.icon(mock, 47, y+4, 44, 39)
		c.text(n, serif, 21, body, 105, y+23, 0)
	}
	// damage numbers (font only)
	for _, n := range []struct {
		s, col string
		x, y   float64
		sz     float64
	}{{"34", body, 822, 550, 30}, {"71", gold, 982, 569, 44}, {"12", "#f08a3a", 1333, 544, 30}, {"9", "#8ac8f0", 1356, 605, 28}, {"6", "#9fd66b", 1434, 646, 28}} {
		c.textOutline(n.s, sans, n.sz*1.05, n.col, n.x, n.y, 1.2, 1)
	}
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM4HudB() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m4-hud-b.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body := palette["text_header"], palette["text_body"]
	// centre message + biome banner on the soft band
	c.sprite("band_message", 760, 170, 400, 40)
	c.text("Вы отдохнули", serif, 27, body, 960, 190, 1)
	c.sprite("band_message", 560, 218, 800, 70)
	c.sprite("divider", 660, 213, 600, 11)
	c.sprite("divider", 660, 283, 600, 11)
	c.sprite("banner_rule", 610, 238, 180, 29)
	c.spriteFlipX("banner_rule", 1130, 238, 180, 29) // right rule = mirrored sprite
	c.text("ЧЁРНЫЙ ЛЕС", serif, 54, head, 960, 251, 1)
	c.text("Новые земли открыты", sans, 21, body, 960, 307, 1)
	// enemy bars: normal / magic / rare (Vaettir tier frames over the health bar)
	for _, e := range []struct {
		f         string
		x, y, w   float64
		frac      float64
		nameY     float64
		creatureX float64
	}{{"frame_enemy_normal", 651, 431, 120, 0.45, 420, 0}, {"frame_enemy_magic", 914, 426, 163, 0.32, 414, 0}, {"frame_enemy_rare", 1232, 461, 160, 0.55, 449, 0}} {
		c.hpBar(e.f, e.x-10, e.y-5, e.w+20, 28, e.frac, 36, 14)
		c.textOutline("Грейдворф", sans, 16, body, e.x+e.w/2, e.nameY, 0.8, 1)
	}
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM4Map() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m4-map-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.panel(14, 14, 1892, 1054)
	c.copyMock(mock, 52, 72, 1440, 922) // map art = content
	c.sprite("separator_v", 1494, 72, 8, 922)
	c.header(763, 18, 394, 56, "", "КАРТА")
	// legend column
	c.text("МЕТКИ", serif, 30, head, 1692, 96, 1)
	c.sprite("divider", 1520, 112, 344, 16)
	for i, n := range []string{"Огонь", "Дом", "Молот", "Точка", "Пещера", "Портал", "Босс"} {
		y := 130 + float64(i)*61
		c.sprite("slot_cell_recess", 1532, y, 60, 56)
		c.icon(mock, 1536, y+4, 52, 48)
		c.text(n, sans, 21, body, 1618, y+28, 0)
		c.sprite("checkbox_box", 1790, y+12, 32, 32)
		if i != 2 && i != 5 {
			c.glyph("glyph_check", 1806, y+28, 24, palette["rune_glow"], 1)
		}
	}
	c.sprite("scroll_track", 1856, 130, 12, 420)
	c.sprite("scroll_handle_normal", 1856, 140, 12, 120)
	c.sprite("tab_selected", 1530, 572, 160, 46)
	c.text("Все", sans, 22, gold, 1610, 595, 1)
	c.sprite("tab_normal", 1700, 572, 166, 46)
	c.text("Мои", sans, 22, body, 1783, 595, 1)
	c.sprite("divider", 1520, 636, 344, 16)
	c.text("ИГРОКИ", serif, 30, head, 1692, 673, 1)
	for i, n := range []string{"Морготт", "Эйрик"} {
		y := 722 + float64(i)*44
		c.icon(mock, 1534, y-14, 40, 28)
		c.text(n, sans, 21, body, 1600, y, 0)
		c.sprite("checkbox_box", 1790, y-16, 32, 32)
		if i == 0 {
			c.glyph("glyph_check", 1806, y, 24, palette["rune_glow"], 1)
		}
	}
	c.sprite("divider", 1520, 796, 344, 16)
	c.sprite("field_search", 1530, 838, 260, 52)
	c.text("Имя метки...", sans, 20, palette["text_disabled"], 1548, 864, 0)
	c.sprite("button_round", 1810, 836, 44, 44)
	c.glyph("glyph_plus", 1832, 858, 22, head, 1)
	c.sprite("bar_skill_trough", 1826, 884, 12, 52)
	c.tint("bar_skill_fill", 1828, 912, 8, 22, palette["rune_glow"], 1)
	c.sprite("slider_handle_hover", 1813, 893, 38, 38)
	c.sprite("button_round", 1810, 940, 44, 44)
	c.glyph("glyph_minus", 1832, 962, 22, head, 1)
	c.text("Масштаб", sans, 20, body, 1530, 935, 0)
	// selected pin + label
	c.sprite("ring_pin_select", 672, 451, 62, 62)
	c.sprite("tooltip_frame", 726, 438, 162, 40)
	c.text("Дом у озера", sans, 18, body, 807, 458, 1)
	c.text("[ЛКМ] метка   [ПКМ] удалить   [Колесо] масштаб", sans, 20, body, 960, 1016, 1)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func cmdCompareM4() {
	loadSprites()
	palette = loadSpec().Palette
	am, ao := compareM4HudA()
	bm, bo := compareM4HudB()
	mm, mo := compareM4Map()
	sideBySide(am, ao, "art/review/compare-hud-m4a.png")
	sideBySide(bm, bo, "art/review/compare-hud-m4b.png")
	sideBySide(mm, mo, "art/review/compare-map-m4.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	list := []pair{
		same("minimap ring + rune stones + wind + sun + biome plaque   [m4-hud-a | ours]", am, ao, R(1560, 10, 350, 370)),
		same("boss bar (unique frame) + name plaque", am, ao, R(500, 35, 920, 100)),
		same("notifications top-left", am, ao, R(30, 105, 360, 180)),
		same("damage numbers (font + outline only)", am, ao, R(780, 500, 700, 180)),
		same("enemy bars: normal / magic / rare tier frames   [m4-hud-b | ours]", bm, bo, R(630, 400, 790, 100)),
		same("centre message + biome-discovered banner", bm, bo, R(600, 165, 720, 165)),
		same("large map: header + legend top   [m4-map-a | ours]", mm, mo, R(1480, 10, 430, 420)),
		same("large map: legend bottom (players, name field, zoom)", mm, mo, R(1480, 560, 430, 450)),
		same("large map: selected pin ring + label", mm, mo, R(560, 380, 380, 160)),
	}
	groupsM2(list, "art/review/compare-groups-m4.png")
	fmt.Println("compare-m4 done")
}

// spriteFlipX draws a sprite mirrored horizontally (Unity scale.x = -1).
func (c *canvas) spriteFlipX(name string, x, y, w, h float64) {
	r := c.R(x, y, w, h)
	tmp := &canvas{img: image.NewNRGBA(image.Rect(0, 0, r.Dx(), r.Dy())), K: c.K}
	tmp.draw9(name, tmp.img.Bounds(), color.NRGBA{})
	over(c.img, flip(tmp.img, true, false), r.Min)
}

// blurBG: the mockup scene heavily blurred (UI melts into the scene) as a neutral composite background.
func blurBG(mock *image.NRGBA) *image.NRGBA {
	w, h := mock.Bounds().Dx(), mock.Bounds().Dy()
	return resize(resize(mock, w/24, h/24), w, h)
}
