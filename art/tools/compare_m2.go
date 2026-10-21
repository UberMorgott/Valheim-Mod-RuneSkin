package main

import (
	"fmt"
	"image"
	"image/color"
	"math"

	"golang.org/x/image/font"
	"golang.org/x/image/math/fixed"
)

// M2 review composites: inventory layout A (layout-a2.png: chest dock left, doll centre, crafting right;
// layout-a3.png: chest + backpack stacked in the left dock) rebuilt from our sprites, coordinates in
// mockup px (1920x1080). Item icons and their counts are content copied from the mockup (dark keyed out).

// glyph draws a white-mask sprite tinted col at alpha a, centred at (cx,cy), size s (mockup px).
func (c *canvas) glyph(name string, cx, cy, s float64, col string, a float64) {
	t := hex(col)
	t.A = uint8(a * 255)
	c.draw9(name, c.R(cx-s/2, cy-s/2, s, s), t)
}

func (c *canvas) tint(name string, x, y, w, h float64, col string, a float64) {
	t := hex(col)
	t.A = uint8(a * 255)
	c.draw9(name, c.R(x, y, w, h), t)
}

// header: plaque + glyph + serif caps title (no text when title is empty).
func (c *canvas) header(x, y, w, h float64, glyph, title string) {
	c.sprite("header_plaque", x, y, w, h)
	if glyph != "" {
		c.glyph(glyph, x-h*0.45, y+h/2, h*0.8, palette["text_header"], 0.95)
	}
	if title != "" {
		c.text(title, serif, h*0.62, palette["text_header"], x+w/2, y+h/2, 1)
	}
}

// actionBar: glyph-only buttons centred at cx (first one hovered to show the state).
func (c *canvas) actionBar(cx, y, s float64, glyphs ...string) {
	gap := s * 0.22
	x := cx - (float64(len(glyphs))*s+float64(len(glyphs)-1)*gap)/2
	for i, g := range glyphs {
		b := "button_icon_normal"
		col := palette["text_body"]
		if i == 0 {
			b, col = "button_icon_hover", palette["rune_glow"]
		}
		c.sprite(b, x, y, s, s*0.975)
		c.glyph(g, x+s/2, y+s*0.49, s*0.6, col, 1)
		x += s + gap
	}
}

func (c *canvas) equipped(x, y, w float64) {
	c.glyph("marker_equipped", x+w-9, y+9, 15, palette["text_active"], 1)
}

func (c *canvas) locked(x, y, w, h float64) {
	c.tint("slot_lock_veil", x, y, w, h, palette["groove_dark"], 1)
	b := w * 0.38
	c.sprite("badge_lock", x+w-b-1, y+h-b-1, b, b)
	c.glyph("glyph_slot_locked", x+w-b/2-1, y+h-b/2-1, b*0.58, palette["text_active"], 1)
}

func (c *canvas) grid(mock *image.NRGBA, x0, y0, px, py, cs float64, cols, rows, iconRows, iconCols int) {
	for r := 0; r < rows; r++ {
		for k := 0; k < cols; k++ {
			x, y := x0+float64(k)*px, y0+float64(r)*py
			c.sprite("slot_cell", x, y, cs, cs)
			if r < iconRows && k < iconCols {
				c.icon(mock, x+2, y+2, cs-4, cs-4)
			}
		}
	}
}

func compareM2() *image.NRGBA {
	mock := loadPNG(mockDir + "/layout-a2.png")
	c := newCanvas(loadPNG(mockDir+"/ref.png"), 1920, 1080)
	body, head, gold, cyan := palette["text_body"], palette["text_header"], palette["text_active"], palette["glyph"]
	carve := palette["groove_light"]

	// ---- left dock: chest
	c.panel(22, 22, 610, 850)
	c.header(150, 40, 440, 52, "glyph_chest", "СУНДУК")
	c.grid(mock, 56, 102, 70, 70, 64, 8, 10, 1, 8)
	c.tint("scroll_fade", 44, 766, 540, 36, palette["glass_bottom"], 0.9)
	c.actionBar(327, 812, 52, "glyph_act_take_all", "glyph_act_stack", "glyph_act_sort")

	// ---- centre: name, tabs, doll, grid, footer
	c.panel(638, 20, 656, 850)
	c.header(730, 38, 472, 50, "", "AUTOTESTCHAR")
	for i, x := range []float64{702, 810, 915, 1022, 1130} {
		if i == 0 {
			c.sprite("tab_selected", x, 95, 98, 75)
		} else {
			c.sprite("slot_cell", x, 95, 98, 75)
		}
		c.icon(mock, x+8, 100, 82, 65)
	}
	c.sprite("slot_cell", 660, 180, 600, 320) // doll well
	c.sprite("doll_backdrop", 940, 184, 200, 314)
	c.sprite("doll_silhouette", 966, 192, 150, 300)
	doll := [3][3]string{{"glyph_slot_helmet", "glyph_slot_back", "glyph_slot_light"},
		{"glyph_slot_chest", "glyph_slot_belt", "glyph_slot_necklace"},
		{"glyph_slot_legs", "glyph_slot_ring", "glyph_slot_ring"}}
	for r := 0; r < 3; r++ {
		for k := 0; k < 3; k++ {
			x, y := 672+float64(k)*91, 192+float64(r)*90
			c.sprite("slot_cell_recess", x, y, 85, 84)
			if r == 0 && k == 1 { // worn cape: item icon replaces the glyph
				c.icon(mock, x+4, y+4, 77, 76)
				continue
			}
			c.glyph(doll[r][k], x+42.5, y+42, 50, carve, 0.6)
		}
	}
	for i := 0; i < 3; i++ {
		y := 190 + float64(i)*97
		n := "slot_mirror"
		if i == 0 {
			n = "slot_mirror_linked"
		}
		c.sprite(n, 1145, y, 97, 94)
		c.icon(mock, 1154, y+8, 80, 78)
		c.equipped(1145, y, 97)
	}
	// hotbar header + grid
	c.sprite("hotbar_header", 674, 506, 578, 36)
	for k := 0; k < 8; k++ {
		cx := 715 + float64(k)*70.5
		c.text(fmt.Sprint(k+1), serif, 24, cyan, cx, 523, 1)
	}
	c.grid(mock, 682, 542, 70.5, 68.5, 66, 8, 4, 1, 8)
	for k := 0; k < 3; k++ {
		c.equipped(682+float64(k)*70.5, 542, 66)
	}
	c.locked(682+7*70.5, 542, 66, 66)
	// footer stat plates
	stat := func(x, w float64, g, v string) {
		drawFrameScaled(c, "plate_stat", c.R(x, 812, w, 48))
		c.glyph(g, x+23, 836, 24, head, 0.95)
		if v != "" {
			c.text(v, sans, 19, gold, x+46+(w-46)/2, 836, 1)
		}
	}
	stat(676, 124, "glyph_armor", "0")
	stat(806, 124, "glyph_coins", "0")
	stat(936, 196, "glyph_weight", "1/300")
	c.sprite("button_icon_normal", 1188, 812, 50, 49) // trash drop target
	c.glyph("glyph_trash", 1213, 836, 30, head, 0.95)

	// ---- crafting
	c.panel(1305, 20, 600, 850)
	c.icon(mock, 1384, 37, 93, 82)
	c.header(1478, 38, 250, 50, "", "ВЕРСТАК")
	c.icon(mock, 1796, 62, 63, 63)
	for i, t := range []struct {
		x, w float64
		s    string
	}{{1359, 166, "СОЗДАТЬ"}, {1534, 153, "УЛУЧШИТЬ"}, {1699, 153, "VNEI"}} {
		n, col := "tab_normal", body
		if i == 0 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, t.x, 135, t.w, 37)
		c.text(t.s, sans, 18, col, t.x+t.w/2, 153, 1)
	}
	for i := 0; i < 10; i++ {
		x := 1334 + float64(i)*52
		n := "slot_cell"
		if i == 5 {
			n = "slot_cell_selected"
		}
		c.sprite(n, x, 190, 48, 48)
		c.icon(mock, x+4, 194, 40, 40)
	}
	c.sprite("slot_cell", 1334, 250, 331, 35)
	c.icon(mock, 1340, 254, 26, 26)
	c.text("поиск...", sans, 16, palette["text_disabled"], 1374, 267, 0)
	c.sprite("slot_cell", 1325, 294, 242, 525)
	c.sprite("slot_cell_selected", 1330, 553, 228, 54)
	names := []string{"Деревянный болт x20", "Сгнивший плащ", "Черный плащ из кожи тролля", "Crude Crossbow", "Кремневые кастеты",
		"Гигантская дубина", "Сырой луговой шашлычок", "Засоленная грудинка x2", "Рогатый капюшон", "Огненная стрела x20",
		"Деревянная стрела x20", "Древние топоры"}
	for i, y := range []float64{322, 362, 401, 452, 494, 535, 579, 631, 675, 717, 756, 797} {
		c.icon(mock, 1334, y-18, 40, 36)
		c.text(names[i], sans, 14, body, 1382, y, 0)
	}
	c.sprite("scroll_track", 1552, 300, 10, 510)
	c.sprite("scroll_handle_normal", 1552, 330, 10, 160)
	c.sprite("slot_cell", 1571, 294, 331, 493)
	c.text("СЫРОЙ ЛУГОВОЙ ШАШЛЫЧОК", sans, 19, "#f0c419", 1586, 322, 0)
	c.icon(mock, 1652, 350, 132, 119)
	c.text("Мясо, гриб и одуванчик на палочке", sans, 15, body, 1586, 479, 0)
	c.text("ждут огня. Дожарь на вертеле.", sans, 15, body, 1586, 506, 0)
	c.text("Вес: 0,5", sans, 16, "#8ab4f0", 1586, 565, 0)
	for i, x := range []float64{1581, 1656, 1730, 1802} {
		c.sprite("slot_cell", x, 679, 70, 86)
		c.text([]string{"Кабанина", "Гриб", "Одуванчик", "Древесина"}[i], sans, 11, body, x+35, 690, 1)
		c.icon(mock, x+3, 700, 64, 62)
	}
	drawFrameScaled(c, "button_normal", c.R(1580, 794, 304, 72))
	c.text("СОЗДАТЬ", serif, 34, head, 1732, 830, 1)

	// ---- HUD band (M1 sprites; context only)
	c.sprite("frame_strip", 12, 868, 1046, 210)
	for i, cx := range []float64{141, 225, 309, 394} {
		n := "socket_food"
		if i == 3 {
			n = "socket_food_gem"
		}
		c.sprite(n, cx-40, 879, 80, 80)
		c.icon(mock, cx-26, 893, 52, 52)
	}
	for i, cx := range []float64{542, 645, 748} {
		c.sprite("socket_quick", cx-38, 893, 76, 76)
		c.icon(mock, cx-30, 905, 60, 60)
		c.text([]string{"Z", "V", "B"}[i], sans, 16, cyan, cx, 885, 1)
	}
	for i, p := range []string{"pool_hp", "pool_stamina", "pool_eitr"} {
		r := c.R(40, 966+float64(i)*21, 960, 20)
		over(c.img, barNative(r, []float64{0.97, 0.68, 0.5}[i], p, false), r.Min)
	}
	c.icon(mock, 44, 1034, 950, 40)

	c.flush()
	ours := resize(c.img, 1920, 1080)
	sideBySide(mock, ours, "art/review/compare-inventory-m2.png")
	return ours
}

// compareM2Dock rebuilds layout-a3's left dock (chest over backpack).
func compareM2Dock() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/layout-a3.png")
	c := newCanvas(loadPNG(mockDir+"/ref.png"), 1920, 1080)
	c.panel(24, 27, 570, 450)
	c.header(150, 43, 400, 48, "glyph_chest", "СУНДУК")
	c.grid(mock, 53, 103, 83, 79, 78, 6, 4, 2, 6)
	c.actionBar(306, 424, 46, "glyph_act_take_all", "glyph_act_stack", "glyph_act_sort")
	c.panel(24, 496, 570, 347)
	c.header(150, 512, 400, 48, "glyph_backpack", "РЮКЗАК")
	c.grid(mock, 53, 573, 83, 79, 78, 6, 3, 1, 4)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

type pair struct {
	name string
	a, b *image.NRGBA
	ra   image.Rectangle
	rb   image.Rectangle
}

func groupsM2(list []pair, out string) {
	const H, gap, lab = 220, 16, 34
	maxW := 0
	for _, it := range list {
		maxW = max(maxW, it.ra.Dx()*H/it.ra.Dy())
	}
	maxW = min(maxW, 1000)
	img := image.NewNRGBA(image.Rect(0, 0, 2*maxW+3*gap, len(list)*(H+lab+gap)+gap))
	fill(img, img.Bounds(), color.NRGBA{24, 24, 28, 255})
	y := gap
	for _, it := range list {
		s := math.Min(float64(H)/float64(it.ra.Dy()), float64(maxW)/float64(it.ra.Dx()))
		w, h := int(float64(it.ra.Dx())*s), int(float64(it.ra.Dy())*s)
		d := &font.Drawer{Dst: img, Src: image.NewUniform(color.NRGBA{230, 230, 230, 255}), Face: face(sans, 24)}
		d.Dot = fixed.P(gap, y+24)
		d.DrawString(it.name)
		over(img, resize(crop(it.a, it.ra), w, h), image.Pt(gap, y+lab))
		over(img, resize(crop(it.b, it.rb), w, h), image.Pt(2*gap+maxW, y+lab))
		y += H + lab + gap
	}
	savePNG(out, img)
	fmt.Println("wrote", out)
}

// wetPair: [old overlay_wet | new overlay_wet variants] hanging from a socket_quick + slot_cell edge, 2x.
func wetPair(old *image.NRGBA) (*image.NRGBA, *image.NRGBA) {
	mk := func(drips []*image.NRGBA) *image.NRGBA {
		c := &canvas{img: image.NewNRGBA(image.Rect(0, 0, 600, 240)), K: 1}
		fill(c.img, c.img.Bounds(), color.NRGBA{40, 60, 50, 255})
		c.draw9("frame_strip", image.Rect(10, 20, 590, 120), color.NRGBA{})
		water := hex(palette["water"])
		for i, d := range drips {
			x := 60 + i*170
			over(c.img, tinted(d, water), image.Pt(x, 112))
		}
		return resize(c.img, 1200, 480)
	}
	o := tinted(old, color.NRGBA{255, 255, 255, 255})
	return mk([]*image.NRGBA{o, o, o}), mk([]*image.NRGBA{sprites["overlay_wet"].img, sprites["overlay_wet_2"].img, sprites["overlay_wet_3"].img})
}

func cmdCompareM2(oldWet string) {
	loadSprites()
	palette = loadSpec().Palette
	a2 := loadPNG(mockDir + "/layout-a2.png")
	ours := compareM2()
	a3, dock := compareM2Dock()
	sideBySide(a3, dock, "art/review/compare-dock-m2.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	list := []pair{
		same("doll storage cells: empty = carved glyph   [a2 | ours]", a2, ours, R(660, 180, 285, 285)),
		same("doll centre: backdrop + silhouette", a2, ours, R(940, 178, 200, 325)),
		same("hand mirrors (linked, equipped marker)", a2, ours, R(1135, 180, 125, 300)),
		same("hotbar header + row 1 (numbers, equipped markers, slot lock)", a2, ours, R(668, 500, 590, 120)),
		same("footer stat plates (armor, coins, weight, trash)", a2, ours, R(668, 802, 590, 66)),
		same("chest panel header", a2, ours, R(22, 22, 610, 85)),
		same("container action bar (glyph-only) vs text button", a2, ours, R(22, 790, 610, 82)),
		same("chest grid bottom (scroll fade)", a2, ours, R(30, 600, 600, 210)),
		same("backpack panel header   [a3 | ours]", a3, dock, R(24, 496, 570, 75)),
	}
	if oldWet != "" { // optional: previous overlay_wet.png for an [old | new] pair
		o, n := wetPair(loadPNG(oldWet))
		list = append(list, pair{"wet drip overlay   [old | new]", o, n, o.Bounds(), n.Bounds()})
	}
	groupsM2(list, "art/review/compare-groups-m2.png")
}
