package main

import (
	"image"
	"image/color"
)

// w29 tab review composite (1920x1080 mockup px): the picked tab mockup docs/mockups/tabs-ref/tabs-c-glass-rune.png
// rebuilt from our tab_* sprites over a blurred copy of its scene. Panels = our panel kit; tabs = Unity Sliced at the
// mockup's tab size (border scale = tab height / sprite height, the concept sheet draws tabs ~3x game size).

func compareTabs() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG("docs/mockups/tabs-ref/tabs-c-glass-rune.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	body, gold := palette["text_body"], palette["text_active"]
	c.panel(130, 160, 1660, 170)
	c.panel(135, 400, 1655, 380)
	c.flush()
	tab := func(name string, x, y, w, h float64) {
		s := sprites[name]
		bs := h * c.K / float64(s.img.Bounds().Dy())
		draw9s(c.img, name, c.R(x, y, w, h), bs, color.NRGBA{}, false)
	}
	label := func(t string, x, y, w, h, fs float64, sel bool) {
		col := body
		if sel {
			col = gold
		}
		c.text(t, serif, fs, col, x+w/2, y+h/2, 1)
	}
	states := []string{"tab_selected", "tab_hover", "tab_normal", "tab_normal"}
	names := []string{"CRAFT", "UPGRADE", "STASH", "BOUNTY"}
	// row 1: horizontal strip on the top panel edge
	for i, x := range []float64{280, 625, 965, 1305} {
		w := 335.0
		tab(states[i], x, 92, w, 84)
		label(names[i], x, 92, w, 84, 34, i == 0)
	}
	// row 2: vertical column inside the lower panel
	for i, y := range []float64{410, 500, 590, 680} {
		tab(states[i], 958, y, 315, 86)
		label(names[i], 958, y, 315, 86, 32, i == 0)
	}
	// row 3: enlarged single tab states + square icon tabs
	big := []string{"tab_normal", "tab_hover", "tab_selected"}
	for i, x := range []float64{78, 503, 928} {
		tab(big[i], x, 862, 404, 102)
		label("CRAFT", x, 862, 404, 102, 40, i == 2)
		c.text([]string{"normal", "hover", "selected"}[i], serif, 30, body, x+202, 997, 1)
	}
	sq := []string{"tab_square_normal", "tab_square_hover", "tab_square_selected"}
	for i, x := range []float64{1395, 1550, 1705} {
		tab(sq[i], x, 865, 122, 115)
	}
	return mock, resize(c.img, 1920, 1080)
}

func cmdCompareTabs() {
	loadSprites()
	palette = loadSpec().Palette
	m, o := compareTabs()
	sideBySide(m, o, "art/review/compare-tabs.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	groupsM2([]pair{
		same("tab strip on the panel top edge   [tabs-c | ours]", m, o, R(120, 80, 1680, 110)),
		same("vertical tab column", m, o, R(940, 400, 350, 375)),
		same("enlarged states normal / hover / selected", m, o, R(60, 850, 1300, 170)),
		same("square icon tabs", m, o, R(1380, 850, 470, 140)),
	}, "art/review/compare-groups-tabs.png")
}
