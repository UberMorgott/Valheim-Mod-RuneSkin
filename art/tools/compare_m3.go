package main

import (
	"fmt"
	"image"
	"image/color"
	"image/draw"
)

// M3 review composites: the crafting column of mockup-1 (1672x941) and layout-a2 (1920x1080) rebuilt from
// our sprites. Item icons, the station icon and item art are content copied from the mockup (dark keyed out).

// catTabs: square glyph filter tabs with scroll arrows; sel = selected index, hov = hovered index.
func (c *canvas) catTabs(x, y, s, pitch float64, sel, hov int, arrows bool, glyphs ...string) {
	if arrows {
		c.glyph("glyph_arrow_left", x+s*0.3, y+s/2, s*0.55, palette["groove_light"], 1)
		x += s * 0.6
	}
	for i, g := range glyphs {
		n, col, a := "tab_square_normal", palette["text_body"], 0.85
		switch i {
		case sel:
			n, col, a = "tab_square_selected", palette["text_active"], 1
		case hov:
			n, col, a = "tab_square_hover", palette["rune_glow"], 1
		}
		cx := x + float64(i)*pitch
		c.sprite(n, cx, y, s, s)
		c.glyph(g, cx+s/2, y+s/2, s*0.62, col, a)
	}
	if arrows {
		c.glyph("glyph_arrow_right", x+float64(len(glyphs)-1)*pitch+s+s*0.3, y+s/2, s*0.55, palette["text_body"], 1)
	}
}

// reqSocket: requirement socket with label, item icon and have/need count.
func (c *canvas) reqSocket(mock *image.NRGBA, x, y, w, h float64, label, count string, ok bool, iconR [4]float64, fs float64) {
	n, col := "socket_req_missing", palette["text_missing"]
	if ok {
		n, col = "socket_req", palette["text_ok"]
	}
	c.sprite(n, x, y, w, h)
	if label != "" {
		c.text(label, sans, fs*0.62, palette["text_body"], x+w/2, y-fs*0.55, 1)
	}
	c.icon(mock, iconR[0], iconR[1], iconR[2], iconR[3])
	c.text(count, sans, fs, col, x+w/2, y+h*0.86, 1)
}

func (c *canvas) row(state string, x, y, w, h float64) {
	c.sprite("row_recipe_"+state, x, y, w, h)
}

// compareM3Mock1 rebuilds mockup-1's crafting panel (x 1128..1653, y 163..785).
func compareM3Mock1() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/mockup-1.png")
	c := newCanvas(loadPNG(mockDir+"/ref.png"), mock.Bounds().Dx(), mock.Bounds().Dy())
	body, head, gold := palette["text_body"], palette["text_header"], palette["text_active"]

	c.panel(1130, 163, 523, 622)
	c.icon(mock, 1165, 180, 60, 50) // station icon (content)
	c.text("ВЕРСТАК", serif, 30, head, 1393, 205, 1)
	c.sprite("badge_level_star", 1573, 178, 58, 58)
	c.text("1", serif, 26, gold, 1602, 207, 1)
	for i, t := range []struct {
		x, w float64
		s    string
	}{{1172, 104, "СОЗДАТЬ"}, {1286, 104, "УЛУЧШИТЬ"}, {1400, 100, "VNEI"}} {
		n, col := "tab_normal", body
		if i == 0 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, t.x, 236, t.w, 28)
		c.text(t.s, serif, 18, col, t.x+t.w/2, 250, 1)
	}
	// left column: filters, search, recipe list
	c.sprite("frame_inset", 1146, 268, 180, 507)
	c.catTabs(1150, 272, 19, 21, 0, 3, false, "glyph_cat_all", "glyph_cat_weapons", "glyph_cat_armor", "glyph_cat_tools",
		"glyph_cat_food", "glyph_cat_meads", "glyph_cat_ammo", "glyph_cat_misc")
	c.sprite("field_search", 1152, 300, 168, 26)
	c.glyph("glyph_search", 1164, 313, 15, palette["groove_light"], 1)
	c.text("поиск...", sans, 13, palette["text_disabled"], 1178, 313, 0)
	list := []string{"Деревянный болт x20", "Сгнивший плащ", "Черный плащ из кожи", "Crude Crossbow", "Кремневые кастеты",
		"Гигантская дубина", "Сырой луговой", "Засоленная грудинка", "Рогатый капюшон", "Огненная стрела x20", "Стрела с кремневым",
		"Деревянная стрела", "Древние топоры", "Кремневый топор"}
	ys := []float64{364, 393, 419, 456, 486, 516, 541, 573, 608, 638, 663, 697, 733, 762}
	for i, s := range list {
		st, col := "normal", body
		switch i {
		case 6:
			st = "selected"
		case 4:
			st = "hover"
		case 9, 10:
			st, col = "unavailable", palette["text_disabled"]
		}
		c.row(st, 1150, ys[i]-14, 162, 28)
		c.iconA(mock, 1152, ys[i]-12, 30, 26, iconAlpha(st))
		c.text(s, sans, 12.5, col, 1187, ys[i], 0)
	}
	c.sprite("scroll_track", 1314, 340, 8, 430)
	c.sprite("scroll_handle_normal", 1314, 360, 8, 70)
	// detail pane
	c.sprite("frame_inset", 1328, 268, 314, 345)
	c.icon(mock, 1336, 276, 52, 60)
	c.text("СЫРОЙ ЛУГОВОЙ ШАШЛЫЧОК", serif, 18, palette["text_item_title"], 1400, 294, 0)
	for i := 0; i < 3; i++ {
		n := "pip_quality_full"
		if i > 0 {
			n = "pip_quality_empty"
		}
		c.sprite(n, 1400+float64(i)*16, 308, 14, 14)
	}
	c.text("Мясо, гриб и одуванчик на палочке ждут", sans, 14, body, 1336, 347, 0)
	c.text("огня. Дожарь на вертеле.", sans, 14, body, 1336, 367, 0)
	c.sprite("row_stat", 1334, 398, 300, 24)
	c.text("Вес:", sans, 14, body, 1340, 410, 0)
	c.text("0,5 (0,5 Всего)", sans, 14, palette["text_stat_value"], 1372, 410, 0)
	// requirements
	for i, it := range []struct{ l, n string }{{"Кабанина", "0/1"}, {"Гриб", "0/2"}, {"Одуванчик", "0/1"}} {
		x := 1398 + float64(i)*66
		c.reqSocket(mock, x, 630, 60, 66, it.l, it.n, false, [4]float64{x + 6, 640, 48, 36}, 13)
	}
	drawFrameScaled(c, "button_normal", c.R(1330, 702, 312, 62))
	c.glyph("glyph_act_craft", 1396, 733, 28, head, 0.9)
	c.text("СОЗДАТЬ", serif, 30, head, 1500, 733, 1)
	// missing-materials box (RecipeChains)
	c.sprite("frame_missing", 848, 172, 272, 90)
	c.text("Не хватает:", sans, 13, body, 880, 191, 0)
	for i, s := range []string{"x Кабанина x1 (Добыча / дроп)", "x Гриб x2 (Добыча / дроп)", "x Одуванчик x1 (Добыча / дроп)"} {
		c.text(s, sans, 12, palette["text_missing"], 862, 210+float64(i)*17, 0)
	}
	c.flush()
	return mock, resize(c.img, mock.Bounds().Dx(), mock.Bounds().Dy())
}

// compareM3A2 rebuilds layout-a2's crafting panel (x 1305..1905).
func compareM3A2() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/layout-a2.png")
	c := newCanvas(loadPNG(mockDir+"/ref.png"), 1920, 1080)
	body, head, gold := palette["text_body"], palette["text_header"], palette["text_active"]
	c.panel(1305, 20, 600, 850)
	c.icon(mock, 1384, 37, 93, 82)
	c.text("ВЕРСТАК", serif, 40, head, 1604, 66, 1)
	c.sprite("badge_level_star", 1790, 56, 76, 76)
	c.text("1", serif, 34, gold, 1828, 93, 1)
	for i, t := range []struct {
		x, w float64
		s    string
	}{{1359, 166, "СОЗДАТЬ"}, {1534, 153, "УЛУЧШИТЬ"}, {1699, 153, "VNEI"}} {
		n, col := "tab_normal", body
		if i == 0 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, t.x, 135, t.w, 37)
		c.text(t.s, serif, 22, col, t.x+t.w/2, 153, 1)
	}
	c.catTabs(1334, 190, 48, 52, 0, 5, false, "glyph_cat_all", "glyph_cat_weapons", "glyph_cat_armor", "glyph_cat_utility",
		"glyph_cat_meads", "glyph_cat_food", "glyph_cat_ammo", "glyph_cat_tools", "glyph_cat_shields", "glyph_cat_misc")
	c.sprite("field_search", 1334, 250, 331, 38)
	c.glyph("glyph_search", 1355, 269, 24, palette["groove_light"], 1)
	c.text("поиск...", sans, 18, palette["text_disabled"], 1374, 269, 0)
	c.sprite("frame_inset", 1325, 294, 242, 525)
	names := []string{"Деревянный болт x20", "Сгнивший плащ", "Черный плащ из кожи тролля", "Crude Crossbow", "Кремневые кастеты",
		"Гигантская дубина", "Сырой луговой шашлычок", "Засоленная грудинка x2", "Рогатый капюшон", "Огненная стрела x20",
		"Деревянная стрела x20", "Древние топоры"}
	for i, y := range []float64{322, 362, 401, 452, 494, 535, 579, 631, 675, 717, 756, 797} {
		st, col := "normal", body
		switch i {
		case 6:
			st = "selected"
		case 2:
			st = "hover"
		case 9:
			st, col = "unavailable", palette["text_disabled"]
		}
		c.row(st, 1331, y-19, 220, 38)
		c.iconA(mock, 1334, y-18, 40, 36, iconAlpha(st))
		c.text(names[i], sans, 14, col, 1382, y, 0)
	}
	c.sprite("scroll_track", 1552, 300, 10, 510)
	c.sprite("scroll_handle_normal", 1552, 330, 10, 160)
	c.sprite("frame_inset", 1571, 294, 318, 375)
	c.text("СЫРОЙ ЛУГОВОЙ ШАШЛЫЧОК", serif, 20, palette["text_item_title"], 1586, 322, 0)
	for i := 0; i < 3; i++ {
		n := "pip_quality_full"
		if i > 0 {
			n = "pip_quality_empty"
		}
		c.sprite(n, 1588+float64(i)*20, 336, 18, 18)
	}
	c.icon(mock, 1652, 350, 132, 119)
	c.text("Мясо, гриб и одуванчик на палочке", sans, 15, body, 1586, 489, 0)
	c.text("ждут огня. Дожарь на вертеле.", sans, 15, body, 1586, 513, 0)
	c.sprite("row_stat", 1582, 550, 296, 30)
	c.text("Вес:", sans, 16, body, 1590, 565, 0)
	c.text("0,5", sans, 16, palette["text_stat_value"], 1630, 565, 0)
	for i, it := range []struct{ l, n string }{{"Кабанина", "0/1"}, {"Гриб", "0/2"}, {"Одуванчик", "0/1"}, {"Древесина", "1/1"}} {
		x := 1581 + float64(i)*74
		c.reqSocket(mock, x, 694, 70, 86, it.l, it.n, i == 3, [4]float64{x + 6, 700, 58, 52}, 21)
	}
	drawFrameScaled(c, "button_normal", c.R(1580, 794, 304, 72))
	c.glyph("glyph_act_craft", 1650, 830, 34, head, 0.9)
	c.text("СОЗДАТЬ", serif, 34, head, 1752, 830, 1)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

// variants: craft-button family + upgrade/repair, tab family incl. EpicLoot-style enchant tabs, requirement states.
func variantsM3() *image.NRGBA {
	c := &canvas{img: image.NewNRGBA(image.Rect(0, 0, 1800, 560)), K: 1}
	fill(c.img, c.img.Bounds(), hex(palette["glass_bottom"]))
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	for i, b := range []struct{ n, g, s string }{{"button_normal", "glyph_act_craft", "СОЗДАТЬ"}, {"button_hover", "glyph_act_upgrade", "УЛУЧШИТЬ"},
		{"button_normal", "glyph_act_repair", "ПОЧИНИТЬ"}, {"button_disabled", "glyph_act_craft", "СОЗДАТЬ"}} {
		x := 20 + float64(i)*440
		drawFrameScaled(c, b.n, c.R(x, 20, 420, 90))
		col := head
		if b.n == "button_disabled" {
			col = palette["text_disabled"]
		}
		c.glyph(b.g, x+110, 65, 44, col, 0.9)
		c.text(b.s, serif, 40, col, x+250, 65, 1)
	}
	// EpicLoot enchanting tabs in the same tab style
	for i, t := range []struct{ g, s string }{{"glyph_ench_sacrifice", "ЖЕРТВА"}, {"glyph_ench_enchant", "ЗАЧАРОВАТЬ"},
		{"glyph_ench_augment", "УСИЛИТЬ"}, {"glyph_act_upgrade", "УЛУЧШИТЬ"}} {
		x := 20 + float64(i)*260
		n, col := "tab_normal", body
		if i == 1 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, x, 140, 250, 52)
		c.glyph(t.g, x+30, 166, 30, col, 1)
		c.text(t.s, sans, 22, col, x+140, 166, 1)
	}
	// round icon buttons (repair / upgrade shortcuts)
	for i, g := range []string{"glyph_act_repair", "glyph_act_upgrade"} {
		x := 1100 + float64(i)*110
		c.sprite("button_round", x, 130, 90, 90)
		c.glyph(g, x+45, 175, 46, head, 0.95)
	}
	// upgrade row: item -> arrow -> target quality
	c.sprite("socket_req_upgrade", 20, 240, 120, 150)
	c.sprite("plaque_arrow", 150, 290, 90, 60)
	c.sprite("socket_req_upgrade", 250, 240, 120, 150)
	for k := 0; k < 4; k++ {
		n := "pip_quality_full"
		if k == 3 {
			n = "pip_quality_empty"
		}
		c.sprite(n, 270+float64(k)*22, 405, 20, 20)
	}
	c.text("3 → 4", sans, 26, gold, 310, 362, 1)
	// requirement states: have / missing + check/x marks
	for i, ok := range []bool{true, true, false, false} {
		x := 420 + float64(i)*140
		n, col, g, t := "socket_req", palette["text_ok"], "glyph_check", "5/2"
		if !ok {
			n, col, g, t = "socket_req_missing", palette["text_missing"], "glyph_missing", "0/3"
		}
		c.sprite(n, x, 240, 120, 150)
		c.glyph(g, x+100, 256, 26, col, 1)
		c.text(t, sans, 26, col, x+60, 368, 1)
	}
	// stars + level badge
	for k := 0; k < 5; k++ {
		g := "glyph_star_full"
		if k > 2 {
			g = "glyph_star_empty"
		}
		c.glyph(g, 1020+float64(k)*44, 300, 38, gold, 1)
	}
	c.sprite("badge_level_star", 1260, 240, 130, 130)
	c.text("3", serif, 60, gold, 1325, 308, 1)
	// all category glyphs on square tabs
	c.catTabs(20, 450, 72, 80, 0, 1, true, "glyph_cat_all", "glyph_cat_weapons", "glyph_cat_armor", "glyph_cat_tools", "glyph_cat_food",
		"glyph_cat_meads", "glyph_cat_ammo", "glyph_cat_shields", "glyph_cat_materials", "glyph_cat_utility", "glyph_cat_misc")
	return c.img
}

func cmdCompareM3() {
	loadSprites()
	palette = loadSpec().Palette
	m1, o1 := compareM3Mock1()
	a2, o2 := compareM3A2()
	// compare-crafting-m3: [mockup-1 column | ours] over [layout-a2 column | ours]
	r1 := image.Rect(1124, 158, 1660, 790)
	r2 := image.Rect(1300, 15, 1910, 875)
	s2 := float64(r1.Dy()) / float64(r2.Dy())
	w2 := int(float64(r2.Dx()) * s2)
	W := r1.Dx()*2 + w2*2 + 36
	img := image.NewNRGBA(image.Rect(0, 0, W, r1.Dy()))
	fill(img, img.Bounds(), color.NRGBA{255, 255, 255, 255})
	x := 0
	for _, p := range []*image.NRGBA{crop(m1, r1), crop(o1, r1), resize(crop(a2, r2), w2, r1.Dy()), resize(crop(o2, r2), w2, r1.Dy())} {
		draw.Draw(img, p.Bounds().Add(image.Pt(x, 0)), p, image.Point{}, draw.Src)
		x += p.Bounds().Dx() + 12
	}
	savePNG("art/review/compare-crafting-m3.png", img)
	fmt.Println("wrote art/review/compare-crafting-m3.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	v := variantsM3()
	list := []pair{
		same("station header + level star badge + mode tabs   [a2 | ours]", a2, o2, R(1310, 25, 590, 155)),
		same("category glyph tabs + search field", a2, o2, R(1320, 180, 580, 115)),
		same("recipe list rows: hover + scroll (top)", a2, o2, R(1320, 290, 250, 200)),
		same("recipe list rows: selected / unavailable", a2, o2, R(1320, 540, 250, 200)),
		same("detail pane: title, quality pips, art, description, stat line", a2, o2, R(1568, 290, 330, 300)),
		same("requirement sockets (have/need) + craft button", a2, o2, R(1570, 665, 330, 210)),
		same("mockup-1 filters + search + list top   [m1 | ours]", m1, o1, R(1140, 262, 190, 200)),
		same("mockup-1 requirement sockets + craft button", m1, o1, R(1326, 615, 322, 160)),
		same("missing-materials box (RecipeChains)", m1, o1, R(840, 165, 290, 105)),
		{"variants: craft/upgrade/repair, EpicLoot-style tabs, upgrade & req states, stars, all category tabs   [ours only]", v, v, v.Bounds(), v.Bounds()},
	}
	groupsM2(list, "art/review/compare-groups-m3.png")
}

// iconAlpha: unavailable rows show muted content.
func iconAlpha(state string) float64 {
	if state == "unavailable" {
		return 0.5
	}
	return 1
}
