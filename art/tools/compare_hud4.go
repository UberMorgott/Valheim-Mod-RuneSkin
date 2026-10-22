package main

import (
	"encoding/json"
	"fmt"
	"image"
	"image/color"
	"math"
	"os"
	"path/filepath"
	"strings"
)

// compare-hud4: offline render of the HUD strip as RuneSkin lays it out (HudStrip / StripMath / HudCluster.Relayout
// ported, values from the theme), drawn from art/sprites at the in-game pixel size of a resolution, next to the
// approved mockup hud-4M3 scaled to the same size. Item icons / numbers are content: icons copied from the mockup.

const hud4Mock = "art/src/kit/mockup-hud4m3.png"

var themeVals map[string]float64
var themeStr map[string]string

func loadTheme() {
	b, err := os.ReadFile("src/RuneSkin/Theme/default-theme.json")
	if err != nil {
		b, err = os.ReadFile("../src/RuneSkin/Theme/default-theme.json")
	}
	must(err)
	var doc map[string]json.RawMessage
	must(json.Unmarshal(b, &doc))
	themeVals = map[string]float64{}
	themeStr = map[string]string{}
	var walk func(raw json.RawMessage)
	walk = func(raw json.RawMessage) {
		var arr []map[string]any
		if json.Unmarshal(raw, &arr) == nil {
			for _, t := range arr {
				tok, _ := t["token"].(string)
				if tok == "" {
					continue
				}
				switch v := t["value"].(type) {
				case float64:
					themeVals[tok] = v
				case string:
					themeStr[tok] = v
				}
				if a, ok := t["asset"].(string); ok {
					themeStr[tok] = a
				}
			}
			return
		}
		var obj map[string]json.RawMessage
		if json.Unmarshal(raw, &obj) == nil {
			for _, v := range obj {
				walk(v)
			}
		}
	}
	for _, v := range doc {
		walk(v)
	}
}

func tv(tok string, def float64) float64 {
	if v, ok := themeVals[tok]; ok {
		return v
	}
	return def
}

func tcol(tok, def string) color.NRGBA {
	s := def
	if v, ok := themeStr[tok]; ok && strings.HasPrefix(v, "#") {
		s = v
	}
	var r, g, b, a uint8 = 0, 0, 0, 255
	if len(s) >= 9 {
		_, _ = fmt.Sscanf(s, "#%02x%02x%02x%02x", &r, &g, &b, &a)
	} else {
		_, _ = fmt.Sscanf(s, "#%02x%02x%02x", &r, &g, &b)
	}
	return color.NRGBA{r, g, b, a}
}

// ---- StripMath port --------------------------------------------------------------------------------------------

type s4In struct {
	W, Cell, CellH, Gap, ZoneGap, BlockPadX, BlockPadT, BlockPadB, DurH, BarsH, MinCenterW, WingH, KnotW, WingTuck float64
	MedW, MedH, MedY, MedTuck, FoodCell, PotCell, PotH, MinCell, SlotPad                                           float64
	Hot, Ab, Food, Pot                                                                                             int
}

type s4Out struct {
	H, CX, CW, BarsX, BarsW, BarsH, BlockY, BlockH, HotX, AbX, CellsY, DivX, MedLX, MedRX, MedY float64
	LeftW, RightX, RightW, FoodX, FoodCell, FoodStep, FoodY, PotX, PotCell, PotH, PotStep, PotY float64
}

func s4Slots(n int, want, span, minGap, minCell float64) (cell, gap float64) {
	cell = want
	if n <= 0 {
		return
	}
	even := (span - float64(n)*want) / float64(n+1)
	if even >= minGap {
		return want, even
	}
	gap = math.Max(0, minGap)
	cell = math.Max(math.Min(want, minCell), math.Min(want, (span-float64(n+1)*gap)/float64(n)))
	return
}

func s4Compute(i s4In) s4Out {
	var o s4Out
	hotW := zone(i.Hot, i.Cell, i.Gap)
	abW := zone(i.Ab, i.Cell, i.Gap)
	topW := hotW
	if i.Ab > 0 {
		topW += i.ZoneGap + abW
	}
	o.CW = math.Max(topW+2*i.BlockPadX, i.MinCenterW)
	o.CX = (i.W - o.CW) / 2
	o.BarsX, o.BarsW, o.BarsH = o.CX, o.CW, i.BarsH
	o.BlockY = i.BarsH
	o.BlockH = i.BlockPadB + i.DurH + i.CellH + i.BlockPadT
	o.H = o.BlockY + o.BlockH
	o.CellsY = o.BlockY + i.BlockPadB + i.DurH
	o.HotX = o.CX + (o.CW-topW)/2
	o.AbX = o.HotX + hotW
	if i.Ab > 0 {
		o.AbX += i.ZoneGap
	}
	o.DivX = o.HotX + hotW + i.ZoneGap/2
	o.MedY = i.MedY - i.MedH/2
	o.MedLX = o.CX - i.MedW + i.MedTuck
	o.MedRX = o.CX + o.CW - i.MedTuck
	o.LeftW = math.Max(0, o.CX+i.WingTuck)
	o.RightX = o.CX + o.CW - i.WingTuck
	o.RightW = math.Max(0, i.W-o.RightX)
	span := math.Max(0, o.CX-i.KnotW-i.SlotPad)
	fc, fg := s4Slots(i.Food, i.FoodCell, span, i.Gap, i.MinCell)
	pc, pg := s4Slots(i.Pot, i.PotCell, span, i.Gap, i.MinCell)
	o.FoodCell, o.PotCell = fc, pc
	o.PotH = i.PotH * pc / math.Max(1, i.PotCell)
	o.FoodStep, o.PotStep = fc+fg, pc+pg
	o.FoodX = i.KnotW + (span-zone(i.Food, fc, fg))/2
	o.PotX = o.CX + o.CW + i.SlotPad + (span-zone(i.Pot, pc, pg))/2
	o.FoodY = (i.WingH - fc) / 2
	o.PotY = (i.WingH - o.PotH) / 2
	return o
}

// ---- drawing ---------------------------------------------------------------------------------------------------

// draw9s: Unity Sliced / Tiled with the sprite borders drawn at bs canvas px per sprite px (Image.pixelsPerUnit).
func draw9s(dst *image.NRGBA, name string, r image.Rectangle, bs float64, tint color.NRGBA, mirror bool) {
	s, ok := sprites[name]
	if !ok {
		must(fmt.Errorf("no sprite %s", name))
	}
	src := s.img
	if tint.A != 0 {
		src = tinted(src, tint)
	}
	if r.Dx() <= 0 || r.Dy() <= 0 {
		return
	}
	tmp := image.NewNRGBA(image.Rect(0, 0, r.Dx(), r.Dy()))
	W, H := src.Bounds().Dx(), src.Bounds().Dy()
	if s.typ == "Simple" || s.b == (Border{}) && s.typ != "Tiled" {
		over(tmp, resize(src, r.Dx(), r.Dy()), image.Point{})
	} else {
		l, rt, t, b := s.b.Left, s.b.Right, s.b.Top, s.b.Bottom
		L, RR, T, B := float64(l)*bs, float64(rt)*bs, float64(t)*bs, float64(b)*bs
		f := math.Min(1, math.Min(float64(r.Dx())/math.Max(1, L+RR), float64(r.Dy())/math.Max(1, T+B)))
		L, RR, T, B = L*f, RR*f, T*f, B*f
		sx := []int{0, l, W - rt, W}
		sy := []int{0, t, H - b, H}
		dx := []int{0, int(math.Round(L)), r.Dx() - int(math.Round(RR)), r.Dx()}
		dy := []int{0, int(math.Round(T)), r.Dy() - int(math.Round(B)), r.Dy()}
		for i := 0; i < 3; i++ {
			for j := 0; j < 3; j++ {
				sr := image.Rect(sx[j], sy[i], sx[j+1], sy[i+1])
				dr := image.Rect(dx[j], dy[i], dx[j+1], dy[i+1])
				if sr.Empty() || dr.Empty() {
					continue
				}
				corner := i != 1 && j != 1
				if s.typ == "Tiled" && !corner {
					// tiles at the border scale: segment size = source size x bs
					piece := crop(src, sr)
					tw := max(1, int(math.Round(float64(sr.Dx())*bs)))
					th := max(1, int(math.Round(float64(sr.Dy())*bs)))
					if j != 1 {
						tw = dr.Dx()
					}
					if i != 1 {
						th = dr.Dy()
					}
					tile := resize(piece, tw, th)
					clip := image.NewNRGBA(image.Rect(0, 0, dr.Dx(), dr.Dy()))
					for y := 0; y < dr.Dy(); y += th {
						for x := 0; x < dr.Dx(); x += tw {
							over(clip, tile, image.Pt(x, y))
						}
					}
					over(tmp, clip, dr.Min)
				} else {
					over(tmp, resize(crop(src, sr), dr.Dx(), dr.Dy()), dr.Min)
				}
			}
		}
	}
	if mirror {
		tmp = flip(tmp, true, false)
	}
	over(dst, tmp, r.Min)
}

func alphaMul(img *image.NRGBA, a float64) *image.NRGBA {
	out := image.NewNRGBA(img.Bounds())
	copy(out.Pix, img.Pix)
	for i := 3; i < len(out.Pix); i += 4 {
		out.Pix[i] = uint8(float64(out.Pix[i]) * a)
	}
	return out
}

type hud4Opts struct {
	screenW, screenH int
	eitr, stamina    bool
	abilities        int
	shield           float64
}

// renderHud4 draws the strip on a dark background at the given screen size; returns the image and the strip box.
func renderHud4(opt hud4Opts) (*image.NRGBA, image.Rectangle, float64) {
	mock := loadPNG(hud4Mock)
	W, H := opt.screenW, opt.screenH
	img := image.NewNRGBA(image.Rect(0, 0, W, H))
	fill(img, img.Bounds(), color.NRGBA{24, 27, 25, 255})

	hp, row, gap := tv("size.hud.strip.bar.hp", 46), tv("size.hud.strip.bar.row", 40), tv("size.hud.strip.bar.gap", 12)
	padT, padB, padX := tv("size.hud.strip.bars.pad.t", 14), tv("size.hud.strip.bars.pad.b", 16), tv("size.hud.strip.bars.pad.x", 22)
	barsH := padT + hp + gap + row + padB
	in := s4In{Cell: tv("size.hud.strip.cell", 78), CellH: tv("size.hud.strip.cell.h", 92), Gap: tv("size.hud.strip.gap", 11), ZoneGap: tv("size.hud.strip.zone.gap", 30),
		BlockPadX: tv("size.hud.strip.block.pad.x", 26), BlockPadT: tv("size.hud.strip.block.pad.t", 18), BlockPadB: tv("size.hud.strip.block.pad.b", 8), DurH: tv("size.hud.strip.dur", 14),
		BarsH: barsH, MinCenterW: tv("size.hud.strip.min.bars", 760), WingH: tv("size.hud.strip.wing.h", 130), KnotW: tv("size.hud.strip.knot", 72), WingTuck: tv("size.hud.strip.wing.tuck", 14),
		MedW: tv("size.hud.strip.med.w", 218), MedH: tv("size.hud.strip.med.h", 142), MedY: tv("size.hud.strip.med.y", 172), MedTuck: tv("size.hud.strip.med.tuck", 14),
		FoodCell: tv("size.hud.strip.food.cell", 84), PotCell: tv("size.hud.strip.potion.cell", 76), PotH: tv("size.hud.strip.potion.h", 88), MinCell: tv("size.hud.strip.wing.min", 52),
		SlotPad: tv("size.hud.strip.slot.pad", 8), Hot: 8, Ab: opt.abilities, Food: 4, Pot: 4}
	designW := tv("size.hud.strip.width", 1860)
	in.W = designW
	natural := s4Compute(in).H
	// HudCluster.Relayout: px per unit = GUI scale (1 at 1080p ref, screen h / 1080) x strip height / natural.
	gui := float64(H) / 1080
	k := gui * math.Max(0.2, math.Min(2.5, 130/natural))
	widthPx := math.Min(designW*k, float64(W)*0.99)
	in.W = widthPx / k
	l := s4Compute(in)
	left := (float64(W) - widthPx) / 2
	bottom := float64(H) - tv("size.hud.strip.margin", 4)*gui
	X := func(u float64) float64 { return left + u*k }
	Y := func(v float64) float64 { return bottom - v*k }
	R := func(x, y, w, h float64) image.Rectangle {
		return image.Rect(int(math.Round(X(x))), int(math.Round(Y(y+h))), int(math.Round(X(x+w))), int(math.Round(Y(y))))
	}
	art := tv("share.hud.strip4.art", 0.5) * k // canvas px per sprite px of the strip4 frame pieces
	frame := tcol("color.hud.strip.frame", "#FFFFFFFF")

	// wings, bar block, hotbar block, divider
	draw9s(img, "strip4_wing", R(0, 0, l.LeftW, in.WingH), art, frame, false)
	draw9s(img, "strip4_wing", R(l.RightX, 0, l.RightW, in.WingH), art, frame, true)
	rowOn := opt.stamina || opt.eitr
	barsFrameH := barsH
	if !rowOn {
		barsFrameH = padT + hp + padB
	}
	draw9s(img, "strip4_bars", R(l.BarsX, l.BarsH-barsFrameH, l.BarsW, barsFrameH), art, frame, false)
	lap := tv("size.hud.strip.block.overlap", 6)
	draw9s(img, "strip4_block", R(l.CX, l.BlockY-lap, l.CW, l.BlockH+lap), art, frame, false)
	if opt.abilities > 0 {
		ds := sprites["strip4_divider"].img.Bounds()
		dh := in.CellH + in.DurH
		dw := dh * float64(ds.Dx()) / float64(ds.Dy())
		draw9s(img, "strip4_divider", R(l.DivX-dw/2, l.CellsY-in.DurH, dw, dh), art, frame, false)
	}

	// bars: well (frame) over trough + fill; BarView scale = wanted height / sprite height (sprite px = ref units)
	wellS := sprites["strip4_well"].img.Bounds()
	bar := func(x, y, w, h float64, frac float64, fillName string, value string, big bool, shield float64) {
		sc := h / float64(wellS.Dy())
		insL, insR, insT, insB := tv("window.strip4_well.left", 10)*sc, tv("window.strip4_well.right", 10)*sc, tv("window.strip4_well.top", 9)*sc, tv("window.strip4_well.bottom", 9)*sc
		wx, wy, ww, wh := x+insL, y+insB, w-insL-insR, h-insB-insT
		draw9s(img, "strip4_fill", R(wx, wy, ww, wh), sc*k, tcol("color.hud.strip.trough", "#7C7B7BFF"), false)
		if frac > 0 {
			draw9s(img, fillName, R(wx, wy, ww*frac, wh), sc*k, color.NRGBA{}, false)
		}
		if shield > 0 {
			sheen := tcol("color.hud.shield.sheen", "#5BE8F238")
			s := sprites["strip4_fill"].img
			t := tinted(s, color.NRGBA{sheen.R, sheen.G, sheen.B, 255})
			t = alphaMul(t, float64(sheen.A)/255)
			sprites["_sheen"] = spr{t, sprites["strip4_fill"].b, "Sliced", true, [2]float64{}}
			draw9s(img, "_sheen", R(wx, wy, ww*shield, wh), sc*k, color.NRGBA{}, false)
		}
		draw9s(img, "strip4_well", R(x, y, w, h), sc*k, frame, false)
		if shield > 0 {
			o := tv("size.hud.strip4.shield.out", 3)
			hh := h + 2*o
			cs := sprites["strip4_shield_l"].img.Bounds()
			cw := hh * float64(cs.Dx()) / float64(cs.Dy())
			straight := w + 2*o - 2*cw
			g := tcol("color.hud.shield.glow", "#5BE8F2FF")
			draw9s(img, "strip4_shield_l", R(x-o, y-o, cw, hh), sc*k, g, false)
			if straight*shield > 1 {
				draw9s(img, "strip4_shield_mid", R(x-o+cw, y-o, straight*shield, hh), sc*k, g, false)
			}
			if shield >= 0.995 {
				draw9s(img, "strip4_shield_r", R(x-o+cw+straight, y-o, cw, hh), sc*k, g, false)
			}
		}
		size := tv("size.hud.font.minor", 24)
		if big {
			size = tv("size.hud.font.value", 26)
		}
		cv := &canvas{img: img, K: 1}
		cv.textOutline(value, sans, size*k*1.15, "#F2F4F5", X(wx+ww/2), Y(wy+wh/2), 1.2, 1)
	}
	innerX, innerW := l.BarsX+padX, l.BarsW-2*padX
	hpY := l.BarsH - padT - hp
	bar(innerX, hpY, innerW, hp, 141.0/150.0, "strip4_fill_hp", "141", true, opt.shield)
	rowY := hpY - gap - row
	rg := tv("size.hud.strip.row.gap", 18)
	switch {
	case opt.stamina && opt.eitr:
		hw := (innerW - rg) / 2
		bar(innerX, rowY, hw, row, 1, "strip4_fill_stamina", "112", false, 0)
		bar(innerX+hw+rg, rowY, hw, row, 1, "strip4_fill_eitr", "85", false, 0)
		rs := sprites["strip4_rowdiv"].img.Bounds()
		dw := row * float64(rs.Dx()) / float64(rs.Dy())
		draw9s(img, "strip4_rowdiv", R(innerX+hw+rg/2-dw/2, rowY, dw, row), art, frame, false)
	case opt.stamina:
		bar(innerX, rowY, innerW, row, 1, "strip4_fill_stamina", "112", false, 0)
	}

	// medallions + glyph
	glyph := func(mx float64, mirror bool) {
		draw9s(img, "strip4_medallion", R(mx, l.MedY, in.MedW, in.MedH), art, frame, mirror)
		dx := tv("share.hud.strip4.med.disc.x", 0.556)
		if mirror {
			dx = 1 - dx
		}
		d := in.MedH * tv("share.hud.strip4.med.glyph", 0.4)
		cx, cy := mx+in.MedW*dx, l.MedY+in.MedH*tv("share.hud.strip4.med.disc.y", 0.5)
		draw9s(img, "ornament_triquetra", R(cx-d/2, cy-d/2, d, d), 1, tcol("color.ornament.glow", "#4FE0E6FF"), false)
	}
	glyph(l.MedLX, false)
	glyph(l.MedRX, true)

	// cells: socket art + content copied from the mockup (icons only, inner area keyed)
	cellArt := tv("share.hud.strip4.art", 0.5) * k
	mockIcon := func(sx, sy, sw, sh float64, x, y, w, h float64) {
		p := crop(mock, image.Rect(int(sx), int(sy), int(sx+sw), int(sy+sh)))
		for i := 0; i < len(p.Pix); i += 4 {
			mx := max(p.Pix[i], p.Pix[i+1], p.Pix[i+2])
			if mx < 55 {
				p.Pix[i+3] = 0
			} else if mx < 85 {
				p.Pix[i+3] = uint8(int(mx-55) * 255 / 30)
			}
		}
		r := R(x, y, w, h)
		over(img, resize(p, r.Dx(), r.Dy()), r.Min)
	}
	rims := []string{"#E8C04A", "#E8A030", "#3FA9F5", "#B05CF0", "", "", "#E03A3A", ""}
	for n := 0; n < 8; n++ {
		x := l.HotX + float64(n)*(in.Cell+in.Gap)
		draw9s(img, "strip4_cell", R(x, l.CellsY, in.Cell, in.CellH), cellArt, frame, false)
		if rims[n] != "" {
			ins := in.Cell * tv("share.hud.strip.rim.inset", 0.035)
			draw9s(img, "strip4_rim", R(x+ins, l.CellsY+ins, in.Cell-2*ins, in.CellH-2*ins), cellArt, hex(rims[n]), false)
		}
		// mockup cell n: x 530 + n*89 (+8 inset), y 761..854
		mx := 530 + float64(n)*89.2
		mockIcon(mx+14, 776, 50, 64, x+14, l.CellsY+(in.CellH-64)/2, 50, 64)
		cv := &canvas{img: img, K: 1}
		cv.textOutline(fmt.Sprint(n+1), sans, tv("size.hud.strip.key.font", 24)*k, "#F2F4F5", X(x+in.Cell*0.09), Y(l.CellsY+in.CellH*0.82), 1, 0)
		if n < 4 { // durability pills under the first four (mockup)
			fill(img, R(x+in.Cell*0.1, l.CellsY-tv("size.hud.strip.dur.gap", 3)-tv("size.hud.strip.dur.line", 5), in.Cell*0.8, tv("size.hud.strip.dur.line", 5)), hex("#E8B64A"))
		}
	}
	for n := 0; n < opt.abilities; n++ {
		x := l.AbX + float64(n)*(in.Cell+in.Gap)
		draw9s(img, "strip4_cell", R(x, l.CellsY, in.Cell, in.CellH), cellArt, frame, false)
		mx := 1267 + float64(n)*89
		mockIcon(mx+6, 768, 66, 80, x+6, l.CellsY+(in.CellH-80)/2, 66, 80)
	}
	for n := 0; n < 4; n++ {
		x := l.FoodX + float64(n)*l.FoodStep
		draw9s(img, "strip4_socket_round", R(x, l.FoodY, l.FoodCell, l.FoodCell), cellArt, tcol("color.hud.strip.bowl", "#B4BABDFF"), false)
		if n < 3 {
			cx := []float64{162, 255, 345}[n]
			mockIcon(cx-40, 893, 80, 80, x+l.FoodCell/2-40*l.FoodCell/84, l.FoodY, 80*l.FoodCell/84, 80*l.FoodCell/84)
		}
	}
	for n := 0; n < 4; n++ {
		x := l.PotX + float64(n)*l.PotStep
		draw9s(img, "strip4_socket_square", R(x, l.PotY, l.PotCell, l.PotH), cellArt, frame, false)
		mx := []float64{1475, 1559, 1643, 1727}[n]
		mockIcon(mx+4, 893, 68, 80, x+4*l.PotCell/76, l.PotY+4, 68*l.PotCell/76, l.PotH-8)
	}
	box := R(0, 0, in.W, l.H)
	return img, box, k
}

func cmdCompareHud4() {
	loadSprites()
	loadTheme()
	mock := loadPNG(hud4Mock)
	out := "D:/tmp/claude/E--DEV/d62659f5-98fd-415e-8c61-9c596d73bdf9/scratchpad/hud4impl"
	must(os.MkdirAll(out, 0o755))
	// mockup strip box (hud-4M3, 1080p mock px): x 36..1890, y 742..1004 (strip units = mock px)
	mockBox := image.Rect(20, 690, 1900, 1010)
	for _, res := range [][2]int{{1920, 1080}, {2560, 1440}} {
		for _, v := range []struct {
			name       string
			eitr, stam bool
			ab         int
			shield     float64
		}{{"main", true, true, 2, 0.5}, {"noeitr", false, true, 2, 0.5}, {"1ability", true, true, 1, 0.5}, {"nostam-noeitr", false, false, 2, 0.5}} {
			img, box, k := renderHud4(hud4Opts{screenW: res[0], screenH: res[1], eitr: v.eitr, stamina: v.stam, abilities: v.ab, shield: v.shield})
			// ours: the strip region (+ margin above for the medallion tops)
			pad := int(40 * k)
			ours := crop(img, image.Rect(max(0, box.Min.X-pad), max(0, box.Min.Y-pad), min(res[0], box.Max.X+pad), res[1]))
			// mockup at the same scale: mock px x k
			mk := resize(crop(mock, mockBox), int(float64(mockBox.Dx())*k), int(float64(mockBox.Dy())*k))
			name := fmt.Sprintf("%dp-%s", res[1], v.name)
			savePNG(filepath.Join(out, "ours-"+name+".png"), ours)
			if v.name == "main" || v.name == "noeitr" {
				savePNG(filepath.Join(out, "mock-"+name+".png"), mk)
				sideBySideV(mk, ours, filepath.Join(out, "sbs-"+name+".png"))
				// metrics on the strip boxes: mockup (36,742)-(1890,1004) at k vs ours' strip box resized to it
				mb := image.Rect(36, 742, 1890, 1004)
				mw, mh := int(float64(mb.Dx())*k), int(float64(mb.Dy())*k)
				ms := resize(crop(mock, mb), mw, mh)
				ours2 := resize(crop(img, box), mw, mh)
				ours2 = hud4AlignTo(ms, ours2)
				ssim, de, heat := hud4Metrics(ms, ours2)
				savePNG(filepath.Join(out, "heat-"+name+".png"), heat)
				fmt.Printf("%s: SSIM %.3f, mean dE76 %.1f\n", name, ssim, de)
			}
			fmt.Printf("%s: k=%.3f strip %dx%d px\n", name, k, box.Dx(), box.Dy())
		}
	}
}

// hud4Metrics: mean SSIM (luma, 8x8 windows, stride 4) and mean CIE76 dE between two same-size images, plus a dE
// heatmap (black 0 -> red 30 -> yellow 60+).
func hud4Metrics(a, b *image.NRGBA) (float64, float64, *image.NRGBA) {
	W, H := a.Bounds().Dx(), a.Bounds().Dy()
	la := make([]float64, W*H)
	lb := make([]float64, W*H)
	heat := image.NewNRGBA(image.Rect(0, 0, W, H))
	var deSum float64
	cnt := 0
	// HUD pixels only: ours differs from the flat render background (24, 27, 25)
	hud := make([]bool, W*H)
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			pb := b.NRGBAAt(x, y)
			hud[y*W+x] = absI(int(pb.R)-24)+absI(int(pb.G)-27)+absI(int(pb.B)-25) > 9
		}
	}
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			pa, pb := a.NRGBAAt(x, y), b.NRGBAAt(x, y)
			la[y*W+x] = 0.299*float64(pa.R) + 0.587*float64(pa.G) + 0.114*float64(pa.B)
			lb[y*W+x] = 0.299*float64(pb.R) + 0.587*float64(pb.G) + 0.114*float64(pb.B)
			l1, a1, b1 := lab(pa)
			l2, a2, b2 := lab(pb)
			d := math.Sqrt((l1-l2)*(l1-l2) + (a1-a2)*(a1-a2) + (b1-b2)*(b1-b2))
			if !hud[y*W+x] {
				continue
			}
			deSum += d
			cnt++
			r := clamp01(d / 30)
			g := clamp01((d - 30) / 30)
			heat.SetNRGBA(x, y, color.NRGBA{uint8(255 * r), uint8(255 * g), 0, 255})
		}
	}
	const c1, c2 = 6.5025, 58.5225
	var ss float64
	n := 0
	for y := 0; y+8 <= H; y += 4 {
		for x := 0; x+8 <= W; x += 4 {
			var ma, mb, va, vb, cov float64
			in := 0
			for j := 0; j < 8; j++ {
				for i := 0; i < 8; i++ {
					if hud[(y+j)*W+x+i] {
						in++
					}
				}
			}
			if in < 48 {
				continue
			}
			for j := 0; j < 8; j++ {
				for i := 0; i < 8; i++ {
					ma += la[(y+j)*W+x+i]
					mb += lb[(y+j)*W+x+i]
				}
			}
			ma /= 64
			mb /= 64
			for j := 0; j < 8; j++ {
				for i := 0; i < 8; i++ {
					da, db := la[(y+j)*W+x+i]-ma, lb[(y+j)*W+x+i]-mb
					va += da * da
					vb += db * db
					cov += da * db
				}
			}
			va /= 63
			vb /= 63
			cov /= 63
			ss += ((2*ma*mb + c1) * (2*cov + c2)) / ((ma*ma + mb*mb + c1) * (va + vb + c2))
			n++
		}
	}
	return ss / float64(max(1, n)), deSum / float64(max(1, cnt)), heat
}

func lab(p color.NRGBA) (float64, float64, float64) {
	lin := func(v uint8) float64 {
		c := float64(v) / 255
		if c <= 0.04045 {
			return c / 12.92
		}
		return math.Pow((c+0.055)/1.055, 2.4)
	}
	r, g, b := lin(p.R), lin(p.G), lin(p.B)
	X := (0.4124*r + 0.3576*g + 0.1805*b) / 0.95047
	Y := 0.2126*r + 0.7152*g + 0.0722*b
	Z := (0.0193*r + 0.1192*g + 0.9505*b) / 1.08883
	f := func(t float64) float64 {
		if t > 0.008856 {
			return math.Cbrt(t)
		}
		return 7.787*t + 16.0/116
	}
	fx, fy, fz := f(X), f(Y), f(Z)
	return 116*fy - 16, 500 * (fx - fy), 200 * (fy - fz)
}

func absI(v int) int {
	if v < 0 {
		return -v
	}
	return v
}

// hud4AlignTo shifts b (+-16 px) to best match a on luma (SSD over b's HUD pixels); uncovered = render background.
func hud4AlignTo(a, b *image.NRGBA) *image.NRGBA {
	W, H := a.Bounds().Dx(), a.Bounds().Dy()
	luma := func(p color.NRGBA) float64 { return 0.299*float64(p.R) + 0.587*float64(p.G) + 0.114*float64(p.B) }
	best, bx, by := math.MaxFloat64, 0, 0
	for dy := -16; dy <= 16; dy++ {
		for dx := -16; dx <= 16; dx++ {
			var s float64
			n := 0
			for y := 20; y < H-20; y += 2 {
				for x := 20; x < W-20; x += 2 {
					pb := b.NRGBAAt(x+dx, y+dy)
					if absI(int(pb.R)-24)+absI(int(pb.G)-27)+absI(int(pb.B)-25) <= 9 {
						continue
					}
					d := luma(a.NRGBAAt(x, y)) - luma(pb)
					s += d * d
					n++
				}
			}
			if n > 0 && s/float64(n) < best {
				best, bx, by = s/float64(n), dx, dy
			}
		}
	}
	out := image.NewNRGBA(b.Bounds())
	fill(out, out.Bounds(), color.NRGBA{24, 27, 25, 255})
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			if image.Pt(x+bx, y+by).In(b.Bounds()) {
				out.SetNRGBA(x, y, b.NRGBAAt(x+bx, y+by))
			}
		}
	}
	fmt.Printf("  align dx=%d dy=%d\n", bx, by)
	return out
}

// renderGauges: the centre gauges (CenterGauges: ring 76 ref px, column 16 x 88 at +22 px right of the ring) over the
// hud-4M3 scene at 1080p, rage share r (clockwise from 12), stagger share s, staggered = burst fill.
func renderGauges(r, s float64, staggered bool) *image.NRGBA {
	bg := loadPNG(hud4Mock)
	img := crop(bg, image.Rect(720, 380, 1200, 700))
	cx, cy := 960.0-720, 540.0-380
	d := tv("size.hud.gauge.ring", 76)
	op := 0.85
	put := func(name string, x, y, w, h float64, a float64, mask func(px, py int) bool) {
		s := sprites[name].img
		t := resize(s, int(math.Round(w)), int(math.Round(h)))
		for py := 0; py < t.Bounds().Dy(); py++ {
			for px := 0; px < t.Bounds().Dx(); px++ {
				i := t.PixOffset(px, py)
				if mask != nil && !mask(px, py) {
					t.Pix[i+3] = 0
					continue
				}
				t.Pix[i+3] = uint8(float64(t.Pix[i+3]) * a)
			}
		}
		over(img, t, image.Pt(int(math.Round(x)), int(math.Round(y))))
	}
	put("gauge_rage_frame", cx-d/2, cy-d/2, d, d, op, nil)
	put("gauge_rage_fill", cx-d/2, cy-d/2, d, d, op, func(px, py int) bool {
		a := math.Atan2(float64(px)+0.5-d/2, d/2-(float64(py)+0.5))
		if a < 0 {
			a += 2 * math.Pi
		}
		return a <= r*2*math.Pi
	})
	tk := d * tv("share.hud.gauge.tick", 0.2)
	put("gauge_rage_tick", cx-tk*0.375, cy-d/2-tk/2, tk*0.75, tk, op, nil)
	cw, ch := tv("size.hud.gauge.stagger.w", 16), tv("size.hud.gauge.stagger.h", 88)
	colX := cx + d/2 + tv("size.hud.gauge.stagger.gap", 22)
	ins := cw * tv("share.hud.gauge.stagger.inset", 0.2)
	name := "gauge_stagger_fill"
	if staggered {
		name = "gauge_stagger_burst"
		ins *= 0.6
	}
	fh := ch - 2*ch*0.06
	put("gauge_stagger_frame", colX, cy-ch/2, cw, ch, op, nil)
	put(name, colX+ins, cy-ch/2+ch*0.06, cw-2*ins, fh, op, func(px, py int) bool { return float64(py) >= fh*(1-s) })
	return img
}

func cmdCompareGauges() {
	loadSprites()
	loadTheme()
	out := "D:/tmp/claude/E--DEV/d62659f5-98fd-415e-8c61-9c596d73bdf9/scratchpad/hud4impl"
	c1 := crop(loadPNG("D:/tmp/claude/E--DEV/d62659f5-98fd-415e-8c61-9c596d73bdf9/scratchpad/hud4/rs-C1.png"), image.Rect(720, 380, 1200, 700))
	c2 := crop(loadPNG("D:/tmp/claude/E--DEV/d62659f5-98fd-415e-8c61-9c596d73bdf9/scratchpad/hud4/rs-C2.png"), image.Rect(720, 380, 1200, 700))
	sideBySideV(c1, renderGauges(0.7, 0.5, false), filepath.Join(out, "sbs-gauges-C1.png"))
	sideBySideV(c2, renderGauges(1, 0.7, true), filepath.Join(out, "sbs-gauges-C2.png"))
}
