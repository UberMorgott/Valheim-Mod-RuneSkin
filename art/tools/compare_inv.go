package main

import (
	"encoding/json"
	"fmt"
	"image"
	"os"
)

// compare-inv: offline 1080p preview of the windowed inventory screen (mockup inv-layout-A, user 2026-10-26) from the
// layout math. The boxes come from InventoryWindowsMath + StripMath at 1920x1080 (tests/RuneSkin.Tests
// InventoryWindowsTests.ExportPreviewLayout, RUNESKIN_INV_PREVIEW=<json>); frames, cells, headers, glyphs and the
// bottom panel are our sprites drawn by Unity's Sliced / Tiled rules. Content (item icons, the character, the
// crafting window's inside, the bar fills) is the mockup's own pixels moved into our boxes, so the comparison is
// about geometry and frames. Writes [mockup | ours] side by side.
//
//	go -C art/tools run . compare-inv LAYOUT.json MOCKUP.png OUT.png

type ibox struct{ X, Y, W, H float64 }

type icontainer struct {
	Box, View, Actions, Header ibox
	Cells                      []ibox
	ScrollH                    bool
	Columns, VisibleColumns    int
}

type invLayout struct {
	Inventory, InventoryHeader                ibox
	Player, Plates                            []ibox
	Doll, DollHeader, DollFigure, DollPreview ibox
	DollCells                                 []struct {
		Glyph string
		Box   ibox
	}
	Backpack, Chest icontainer
	Crafting        ibox
	Strip           []struct {
		Name string
		Box  ibox
	}
	Hotbar, Potions, Foods []ibox
	Ability, Bars          ibox
}

// mockup A cell centres (measured on inv-layout-A.png): source of the item icons copied into our cells
var (
	mockInvX    = []float64{637, 720, 803, 884, 965, 1048, 1130, 1212}
	mockInvY    = []float64{682, 750, 818}
	mockPackX   = []float64{118, 189, 258, 327, 396, 465}
	mockPackY   = []float64{225, 311, 397}
	mockChestX  = []float64{105, 165, 225, 286, 346, 406, 466, 527}
	mockChestY  = []float64{601, 677, 752, 828}
	mockDollL   = []float64{153, 232, 310, 387, 463, 541}
	mockDollR   = []float64{387, 463, 541} // rings, lantern
	mockHotbarX = []float64{636, 715, 790, 866, 943, 1023, 1101, 1184}
)

func cmdCompareInv(args []string) {
	if len(args) < 3 {
		must(fmt.Errorf("usage: compare-inv LAYOUT.json MOCKUP.png OUT.png"))
	}
	b, err := os.ReadFile(args[0])
	must(err)
	var L invLayout
	must(json.Unmarshal(b, &L))
	loadSprites()
	palette = loadSpec().Palette
	mock := loadPNG(args[1])
	// scene: the mockup's own background, blurred (down / up) - the world behind the windows
	bg := resize(resize(mock, 64, 36), 1920, 1080)
	c := newCanvas(bg, 1920, 1080)
	head, gold, carve := palette["text_header"], palette["text_active"], palette["groove_light"]
	R := func(b ibox) image.Rectangle { return c.R(b.X, b.Y, b.W, b.H) }
	iconInto := func(mx, my, ms float64, b ibox) {
		p := crop(mock, image.Rect(int(mx-ms/2), int(my-ms/2), int(mx+ms/2), int(my+ms/2)))
		for i := 0; i < len(p.Pix); i += 4 {
			mxv := max(p.Pix[i], p.Pix[i+1], p.Pix[i+2])
			if mxv < 60 {
				p.Pix[i+3] = 0
			} else if mxv < 90 {
				p.Pix[i+3] = uint8(int(mxv-60) * 255 / 30)
			}
		}
		r := R(ibox{b.X + b.W*0.06, b.Y + b.H*0.06, b.W * 0.88, b.H * 0.88})
		over(c.img, resize(p, r.Dx(), r.Dy()), r.Min)
	}

	// ---- bottom panel (StripMath boxes, panel_* slices with their 1-unit lap)
	for _, p := range L.Strip {
		r := R(ibox{p.Box.X - 1, p.Box.Y, p.Box.W + 2, p.Box.H})
		over(c.img, resize(sprites[p.Name].img, r.Dx(), r.Dy()), r.Min)
	}
	// bars: the mockup's bar rows moved onto our bar box
	{
		r := R(L.Bars)
		src := crop(mock, image.Rect(626, 996, 1292, 1049)) // the mockup's HP + resource rows
		over(c.img, resize(src, r.Dx(), r.Dy()), r.Min)
	}
	for j, h := range L.Hotbar {
		iconInto(mockHotbarX[j], 958, 64, h)
	}
	iconInto(1270, 958, 64, L.Ability)
	for i, p := range L.Potions {
		iconInto([]float64{1430, 1502, 1572}[i], 1006, 56, p)
	}
	for i, f := range L.Foods {
		iconInto([]float64{352, 424, 496}[i], 1000, 56, f)
	}

	// ---- windows
	win := func(b ibox) { c.panel(b.X, b.Y, b.W, b.H) }
	// header band: plate over the band, rune title centred (RuneWindowHeader)
	header := func(b ibox, glyph, title string) {
		c.sprite("header_plaque", b.X, b.Y, b.W, b.H)
		if glyph != "" {
			c.glyph(glyph, b.X+b.H*0.7, b.Y+b.H/2, b.H*0.7, head, 0.95)
		}
		c.text(title, serif, b.H*0.62, head, b.X+b.W/2, b.Y+b.H/2, 1)
	}

	// inventory window
	win(L.Inventory)
	header(ibox{L.InventoryHeader.X + 12, L.InventoryHeader.Y + 4, L.InventoryHeader.W - 24, L.InventoryHeader.H}, "", "INVENTORY")
	c.actionBar(L.Inventory.X+L.Inventory.W-70, L.InventoryHeader.Y+6, 30, "glyph_act_sort", "glyph_win_restore")
	for i, p := range L.Player {
		c.sprite("slot_cell", p.X, p.Y, p.W, p.H)
		iconInto(mockInvX[i%8], mockInvY[i/8], 66, p)
	}
	plates := []struct{ g, v string }{{"glyph_trash", ""}, {"glyph_armor", "42"}, {"glyph_weight", "187/300"}, {"glyph_coins", "340"}}
	for i, p := range L.Plates {
		c.sprite("slot_cell", p.X, p.Y, p.W, p.H)
		vh := 0.0
		if plates[i].v != "" {
			vh = p.H * 0.36
		}
		s := min(p.W, p.H-vh) * 0.62
		c.glyph(plates[i].g, p.X+p.W/2, p.Y+(p.H-vh)/2+2, s, head, 0.95)
		if plates[i].v != "" {
			c.text(plates[i].v, sans, vh*0.85, gold, p.X+p.W/2, p.Y+p.H-vh/2-2, 1)
		}
	}

	// doll window: name header, tab row, equipment columns, alcove, the (smaller) live preview
	win(L.Doll)
	header(ibox{L.DollHeader.X, L.DollHeader.Y + 4, L.DollHeader.W, 40}, "", "MORGOTT")
	tabs := []string{"glyph_tab_character", "glyph_tab_skills", "glyph_tab_trophies", "glyph_board_treasure", "glyph_settings"}
	tw := 52.0
	tx := L.DollHeader.X + (L.DollHeader.W-(5*tw+4*8))/2
	for i, g := range tabs {
		n := "tab_square_normal"
		if i == 0 {
			n = "tab_square_selected"
		}
		c.sprite(n, tx, L.DollHeader.Y+52, tw, 44)
		c.glyph(g, tx+tw/2, L.DollHeader.Y+74, 28, head, 0.95)
		tx += tw + 8
	}
	c.sprite("doll_backdrop", L.DollFigure.X, L.DollFigure.Y, L.DollFigure.W, L.DollFigure.H)
	{
		// the mockup's character (x 840..1080, y 105..505) shrunk into the preview rect
		src := crop(mock, image.Rect(840, 105, 1082, 505))
		r := R(L.DollPreview)
		over(c.img, resize(src, r.Dx(), r.Dy()), r.Min)
	}
	glyphs := map[string]string{"head": "glyph_slot_helmet", "chest": "glyph_slot_chest", "legs": "glyph_slot_legs", "back": "glyph_slot_back",
		"belt": "glyph_slot_belt", "neck": "glyph_slot_necklace", "ring": "glyph_slot_ring", "light": "glyph_slot_light"}
	li, ri := 0, 0
	for _, d := range L.DollCells {
		c.sprite("slot_cell_recess", d.Box.X, d.Box.Y, d.Box.W, d.Box.H)
		if d.Box.X < L.DollFigure.X {
			iconInto(690, mockDollL[li], 66, d.Box)
			li++
		} else {
			if ri < 2 {
				c.glyph(glyphs[d.Glyph], d.Box.X+d.Box.W/2, d.Box.Y+d.Box.H/2, d.Box.W*0.55, carve, 0.6)
			} else {
				iconInto(1228, mockDollR[ri], 66, d.Box)
			}
			ri++
		}
	}

	// backpack / chest
	container := func(k icontainer, glyph, title string, mx, my []float64) {
		win(k.Box)
		hh := 40.0
		header(ibox{k.Header.X, k.Header.Y + (k.Header.H-hh)/2, k.Header.W, hh}, glyph, title)
		for i, cb := range k.Cells {
			c.sprite("slot_cell", cb.X, cb.Y, cb.W, cb.H)
			col, row := i%k.VisibleColumns, i/k.VisibleColumns
			if row < len(my) && col < len(mx) {
				iconInto(mx[col], my[row], 56, cb)
			}
		}
		if k.ScrollH && len(k.Cells) > 0 {
			last := k.Cells[len(k.Cells)-1]
			x0 := k.Cells[0].X
			w := last.X + last.W - x0
			y := last.Y + last.H + 2
			c.sprite("scroll_track", x0, y, w, 6)
			c.sprite("scroll_handle_hover", x0, y, w*float64(k.VisibleColumns)/float64(k.Columns), 6)
		}
		c.actionBar(k.Actions.X+k.Actions.W-80, k.Actions.Y+(k.Actions.H-38)/2, 38, "glyph_act_take_all", "glyph_act_stack")
	}
	container(L.Backpack, "glyph_backpack", "BACKPACK", mockPackX, mockPackY)
	container(L.Chest, "glyph_chest", "REINFORCED CHEST", mockChestX, mockChestY)

	// crafting: the mockup's crafting window inside (layout unchanged by this track) moved into our box
	win(L.Crafting)
	{
		src := crop(mock, image.Rect(1366, 96, 1852, 878))
		in := ibox{L.Crafting.X + 14, L.Crafting.Y + 12, L.Crafting.W - 28, L.Crafting.H - 24}
		r := R(in)
		over(c.img, resize(src, r.Dx(), r.Dy()), r.Min)
	}
	c.flush()
	ours := resize(c.img, 1920, 1080)
	savePNG(args[2]+".ours.png", ours)
	sideBySide(mock, ours, args[2])
}
