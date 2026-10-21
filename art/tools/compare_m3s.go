package main

import (
	"fmt"
	"image"
	"image/color"
)

// M3 skills (skills-a.png) and trophies (trophies-a.png) review composites, 1920x1080 mockup px, rebuilt from
// existing kit sprites + the m3glyphs set. Skill/trophy icons and tooltip art are mockup content copied in.

// charTabs: the five character-panel tabs (raven, valknut, knot, cup, rune); sel = selected index.
func (c *canvas) charTabs(mock *image.NRGBA, xs []float64, y, w, h float64, sel int) {
	g := []string{"glyph_tab_character", "glyph_tab_skills", "", "glyph_tab_trophies", ""}
	for i, x := range xs {
		n := "tab_square_normal"
		if i == sel {
			n = "tab_square_selected"
		}
		c.sprite(n, x, y, w, h)
		if g[i] == "" {
			c.icon(mock, x+w*0.2, y+h*0.12, w*0.6, h*0.76) // knot / rune emblem: content
			continue
		}
		c.glyph(g[i], x+w/2, y+h/2, h*0.68, palette["text_active"], 1)
	}
}

func (c *canvas) progress(x, y, w, h, frac float64) {
	c.sprite("bar_skill_trough", x, y, w, h)
	c.tint("bar_skill_fill", x+1.5, y+1.5, (w-3)*frac, h-3, palette["rune_glow"], 1)
}

// popup: tooltip-size panel = fill_glass + frame_panel (+glow) + the small corner caps.
func (c *canvas) popup(x, y, w, h float64) {
	r := c.R(x, y, w, h)
	c.draw9("fill_glass", r.Inset(24), color.NRGBA{})
	c.draw9("frame_panel", r, color.NRGBA{})
	c.draw9("frame_panel_glow", r, hex(palette["rune_glow"]))
	c.late = append(c.late, func() {
		c.native("frame_corner_small_tl", r.Min)
		c.native("frame_corner_small_tr", image.Pt(r.Max.X, r.Min.Y))
		c.native("frame_corner_small_bl", image.Pt(r.Min.X, r.Max.Y))
		c.native("frame_corner_small_br", r.Max)
	})
}

func compareSkills() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/skills-a.png")
	c := newCanvas(loadPNG(mockDir+"/ref.png"), 1920, 1080)
	body, head, gold := palette["text_body"], palette["text_header"], palette["text_active"]
	c.panel(1040, 35, 778, 1037)
	c.header(1222, 46, 380, 50, "", "НАВЫКИ")
	c.charTabs(mock, []float64{1110, 1228, 1355, 1487, 1617}, 105, 118, 83, 1)
	// summary row: death penalty + sort toggles
	c.sprite("row_recipe_normal", 1052, 205, 742, 54)
	c.sprite("slot_cell_recess", 1066, 210, 48, 44)
	c.glyph("glyph_death", 1090, 232, 30, head, 0.9)
	c.text("Смерть: -5% навыков", sans, 21, palette["text_disabled"], 1130, 231, 0)
	c.sprite("tab_square_selected", 1655, 207, 48, 48)
	c.glyph("glyph_sort_level", 1679, 231, 28, gold, 1)
	c.sprite("tab_square_normal", 1727, 207, 48, 48)
	c.glyph("glyph_sort_name", 1751, 231, 28, body, 0.9)
	// list
	c.sprite("frame_inset", 1052, 262, 742, 808)
	skills := []struct {
		n    string
		lv   int
		frac float64
	}{{"Мечи", 23, .47}, {"Топоры", 16, .28}, {"Луки", 31, .62}, {"Дубины", 12, .19}, {"Копья", 20, .41}, {"Ножи", 15, .33},
		{"Древковое оружие", 14, .26}, {"Блокирование", 22, .48}, {"Кирки", 18, .37}, {"Рубка деревьев", 35, .71}, {"Бег", 27, .53},
		{"Прыжки", 21, .46}, {"Плавание", 13, .29}, {"Скрытность", 17, .38}}
	for i, s := range skills {
		y := 270 + float64(i)*57.6
		if y+55 > 1068 {
			break
		}
		st := "normal"
		if i == 0 {
			st = "selected"
		}
		c.row(st, 1058, y, 694, 56)
		c.sprite("slot_cell_recess", 1068, y+3, 68, 52)
		c.icon(mock, 1074, y+7, 56, 44)
		c.text(s.n, sans, 22, body, 1160, y+22, 0)
		c.progress(1160, y+37, 390, 10, s.frac)
		c.text(fmt.Sprintf("%d%%", int(s.frac*100)), sans, 16, palette["text_disabled"], 1568, y+44, 0)
		c.text(fmt.Sprint(s.lv), sans, 36, gold, 1698, y+27, 1)
	}
	c.sprite("scroll_track", 1770, 280, 20, 760)
	c.sprite("scroll_handle_normal", 1770, 355, 20, 75)
	// tooltip
	c.popup(470, 186, 550, 260)
	c.sprite("slot_cell_recess", 497, 212, 82, 82)
	c.icon(mock, 503, 218, 70, 70)
	c.text("МЕЧИ", serif, 32, head, 592, 237, 0)
	c.text("Владение одноручными и двуручными", sans, 19, body, 592, 273, 0)
	c.text("мечами.", sans, 19, body, 592, 299, 0)
	c.text("Уровень", sans, 20, body, 592, 339, 0)
	c.text("23", sans, 26, gold, 678, 337, 0)
	c.text("(47%)", sans, 20, body, 712, 339, 0)
	c.text("Урон мечами: +23%", sans, 19, body, 592, 375, 0)
	c.text("Расход выносливости: -8%", sans, 19, body, 592, 403, 0)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareTrophies() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/trophies-a.png")
	c := newCanvas(loadPNG(mockDir+"/ref.png"), 1920, 1080)
	body, head, gold := palette["text_body"], palette["text_header"], palette["text_active"]
	c.panel(888, 38, 1000, 988)
	c.header(1176, 48, 380, 50, "", "ТРОФЕИ")
	c.text("Трофеи: 27 / 52", sans, 21, body, 1730, 73, 1)
	c.charTabs(mock, []float64{1013, 1153, 1300, 1453, 1607}, 112, 128, 80, 3)
	// boss strip
	c.sprite("frame_inset", 900, 208, 976, 182)
	bosses := []string{"Эйктюр", "Древний", "Масса костей", "Модер", "Яглут", "Королева", "Отец"}
	for i, n := range bosses {
		x := []float64{912, 1046, 1180, 1313, 1450, 1586, 1730}[i]
		c.sprite("tab_square_selected", x, 220, 120, 128)
		c.icon(mock, x+8, 236, 104, 104)
		c.glyph("glyph_boss", x+100, 240, 26, gold, 1)
		c.text(n, sans, 17, body, x+60, 365, 1)
	}
	// grid
	c.sprite("frame_inset", 900, 396, 976, 600)
	for r := 0; r < 6; r++ {
		for k := 0; k < 9; k++ {
			x, y := 908+float64(k)*105.5, 403+float64(r)*97.5
			n := "slot_cell_recess"
			if r == 1 && k == 2 {
				n = "slot_cell_selected"
			}
			c.sprite(n, x, y, 100, 92)
			c.icon(mock, x+8, y+6, 84, 80)
		}
	}
	// tooltip
	c.popup(500, 425, 388, 223)
	c.text("Трофей волка", serif, 28, palette["text_item_title"], 551, 466, 0)
	c.sprite("slot_cell_recess", 537, 493, 110, 108)
	c.icon(mock, 545, 500, 94, 94)
	c.text("В его глазах застыл", sans, 17, body, 661, 514, 0)
	c.text("холод горных вершин.", sans, 17, body, 661, 539, 0)
	c.text("Открыто", sans, 18, palette["rune_glow"], 551, 618, 0)
	_ = head
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func cmdCompareM3S() {
	loadSprites()
	palette = loadSpec().Palette
	sm, so := compareSkills()
	tm, to := compareTrophies()
	sideBySide(sm, so, "art/review/compare-skills-m3.png")
	sideBySide(tm, to, "art/review/compare-trophies-m3.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	list := []pair{
		same("skills: header + character tabs   [skills-a | ours]", sm, so, R(1040, 35, 778, 160)),
		same("skills: summary row (death penalty, sort toggles)", sm, so, R(1045, 198, 770, 66)),
		same("skills: rows (selected, normal), level, progress", sm, so, R(1045, 262, 770, 250)),
		same("skills: tooltip", sm, so, R(465, 180, 560, 272)),
		same("trophies: header + tabs   [trophies-a | ours]", tm, to, R(888, 38, 1000, 165)),
		same("trophies: boss strip (gold cells, boss emblem)", tm, to, R(895, 205, 985, 190)),
		same("trophies: grid (unlocked, locked, selected)", tm, to, R(900, 396, 976, 300)),
		same("trophies: tooltip", tm, to, R(495, 420, 398, 235)),
	}
	groupsM2(list, "art/review/compare-groups-m3s.png")
	_ = color.NRGBA{}
}
