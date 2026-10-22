package main

import (
	"fmt"
	"image"
	"math"
)

// Track H review composite (1920x1080 mockup px): the compact bottom HUD strip of mockup hud-3 rebuilt from our sprites
// (frame / bars / cells / ghosts cut from hud-3 itself, art/src/kit/mockup-hud3.png) with the geometry of
// src/RuneSkin/Hud/StripMath.cs (ported below; default theme + config: width 0.96, height 126 reference px), over the
// mockup scene. Item / food / potion / ability / status icons are content copied from the mockup's own cells.
// Output: side by side [mockup | ours] + strip crops.

const hud3Mock = "art/src/kit/mockup-hud3.png"
const hud2Out = "D:/tmp/claude/E--DEV/d62659f5-98fd-415e-8c61-9c596d73bdf9/scratchpad/hud-design/"

type stripIn struct {
	W, Cell, CellH, Gap, ZoneGap, Pad, DurH, BarsH, MinBarsW, KnotW, FoodCell, PotCell, PotH, TimerH float64
	Hot, Ab, Food, Pot                                                                               int
}

type stripOut struct {
	H, CX, CW, HotX, AbX, CellsY, HotW, AbW, BarsX, BarsY, BarsW         float64
	FoodX, FoodCell, FoodStep, FoodY, PotX, PotCell, PotH, PotStep, PotY float64
}

func zone(n int, cell, gap float64) float64 {
	if n <= 0 {
		return 0
	}
	return float64(n)*cell + float64(n-1)*gap
}

// Port of StripMath.Compute (keep in step with the C# source).
func stripCompute(i stripIn) stripOut {
	var o stripOut
	o.HotW = zone(i.Hot, i.Cell, i.Gap)
	o.AbW = zone(i.Ab, i.Cell, i.Gap)
	top := o.HotW
	if i.Ab > 0 {
		top += i.ZoneGap + o.AbW
	}
	inner := math.Max(top, i.MinBarsW)
	o.CW = inner + 2*i.Pad
	o.CX = (i.W - o.CW) / 2
	o.BarsX, o.BarsW, o.BarsY = o.CX+i.Pad, inner, i.Pad
	o.CellsY = o.BarsY + i.BarsH + i.DurH
	o.H = o.CellsY + i.CellH + i.Pad
	o.HotX = o.CX + i.Pad + (inner-top)/2
	o.AbX = o.HotX + o.HotW
	if i.Ab > 0 {
		o.AbX += i.ZoneGap
	}
	knot := i.KnotW * 0.6
	span := math.Max(0, o.CX-knot-2*i.Pad)
	wingSlots := func(n int, want float64) (float64, float64) {
		if n <= 0 {
			return want, 0
		}
		even := (span - float64(n)*want) / float64(n+1)
		if even >= i.Gap {
			return want, even
		}
		return math.Max(math.Min(want, 48), math.Min(want, (span-float64(n+1)*i.Gap)/float64(n))), i.Gap
	}
	var fg, pg float64
	o.FoodCell, fg = wingSlots(i.Food, i.FoodCell)
	o.PotCell, pg = wingSlots(i.Pot, i.PotCell)
	o.PotH = i.PotH * o.PotCell / i.PotCell
	o.FoodStep, o.PotStep = o.FoodCell+fg, o.PotCell+pg
	foodCX := knot + i.Pad + span/2
	potCX := o.CX + o.CW + i.Pad + span/2
	o.FoodX = foodCX - zone(i.Food, o.FoodCell, fg)/2
	o.PotX = potCX - zone(i.Pot, o.PotCell, pg)/2
	o.FoodY = o.BarsY + i.TimerH
	o.PotY = o.BarsY
	return o
}

// hudOpts: hud-3c variants (wings on the bottom, status niches above them, medallions, shield line, live counts).
type hudOpts struct {
	food, pot, buffs, debuffs int
	niches                    bool
}

func compareHud3(heightRef float64, opt hudOpts) (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(hud3Mock)
	c := newCanvas(blurBG(mock), 1920, 1080)
	c.copyMock(mock, 0, 0, 1920, 700) // scene, minimap, enemy (not ours)

	barGap, hpH, slimH := 2.0, 26.0, 26.0
	in := stripIn{Cell: 80, CellH: 86, Gap: 8, ZoneGap: 8, Pad: 13, DurH: 24, BarsH: slimH + barGap + hpH, MinBarsW: 600, KnotW: 38,
		FoodCell: 85, PotCell: 62, PotH: 86, TimerH: 26, Hot: 8, Ab: 3, Food: opt.food, Pot: opt.pot}
	if opt.niches {
		// wing contents shrink to leave the niche band above them (wing slots: food 72 + timer 22, potions 60 x 76)
		in.FoodCell, in.TimerH, in.PotCell, in.PotH = 72, 22, 60, 76
	}
	// HudCluster.Relayout: mockup proportions scaled uniformly, design width 1900 units at the height scale
	in.W = 1900
	k := heightRef / stripCompute(in).H
	widthPx := math.Min(1900*k, 1920*0.99)
	in.W = widthPx / k
	l := stripCompute(in)
	left, bottom := (1920-widthPx)/2, 1080-4.0
	X := func(u float64) float64 { return left + u*k }
	Y := func(v float64) float64 { return bottom - v*k }
	box := func(name string, x, y, w, h float64) { c.sprite(name, X(x), Y(y+h), w*k, h*k) }
	tbox := func(name string, x, y, w, h float64, col string, a float64) {
		c.tint(name, X(x), Y(y+h), w*k, h*k, col, a)
	}
	native := func(name string, x, y, h, px float64) {
		s := sprites[name].img.Bounds()
		w := h * float64(s.Dx()) / float64(s.Dy())
		box(name, x-w*px, y, w, h)
	}
	outl := func(s string, size float64, col string, x, y float64, align int) {
		c.textOutline(s, sans, size*k, col, X(x), Y(y), 1.0, align)
	}

	// frame: body, ends, joints, bar knots
	// plate: Unity Tiled from the bottom-left, tile = 0.7 x the strip height (share.hud.strip.plate), plate tint
	bs := sprites["strip3_body"].img.Bounds()
	th := 0.7 * l.H
	tw := th * float64(bs.Dx()) / float64(bs.Dy())
	plate := tinted(sprites["strip3_body"].img, hex("#a3a9ac"))
	for ty := 0.0; ty < l.H; ty += th {
		for tx := 0.0; tx < in.W; tx += tw {
			w, h := math.Min(tw, in.W-tx), math.Min(th, l.H-ty)
			src := crop(plate, image.Rect(0, bs.Dy()-int(float64(bs.Dy())*h/th), int(float64(bs.Dx())*w/tw), bs.Dy()))
			r := c.R(X(tx), Y(ty+h), w*k, h*k)
			over(c.img, resize(src, r.Dx(), r.Dy()), r.Min)
		}
	}
	box("strip3_rail", 0, 0, in.W, l.H)
	native("strip3_end_l", 0, 0, l.H, 0)
	native("strip3_end_r", in.W, 0, l.H, 1)
	jointH := l.H - (l.BarsY + in.BarsH + in.Pad)
	native("strip3_joint_l", l.CX, l.H-jointH, jointH, 0.5)
	native("strip3_joint_r", l.CX+l.CW, l.H-jointH, jointH, 0.5)
	barsTop := l.BarsY + in.BarsH
	if opt.niches {
		// canonical knotwork + triquetra medallion (cut from mockup hud-2) on both bar block ends
		mh := in.BarsH + 2*in.Pad + 18
		native("ornament_medallion", l.BarsX+4, l.BarsY-in.Pad-9, mh, 1)
		native("ornament_medallion", l.BarsX+l.BarsW-4, l.BarsY-in.Pad-9, mh, 0)
	} else {
		native("strip3_barknot_l", l.BarsX, l.BarsY-in.Pad, in.BarsH+2*in.Pad, 1)
		native("strip3_barknot_r", l.BarsX+l.BarsW, l.BarsY-in.Pad, in.BarsH+2*in.Pad, 0)
	}
	_ = barsTop

	teal, gold := palette["rune_glow"], "#e8b64a"
	// bars: window = frame minus its rails (6 / 26 of the height) and the rounded left end; cap 8 units at the right
	half := (l.BarsW - barGap) / 2
	bar := func(x, y, w, h, frac float64, fill, value string, shield float64) {
		iy := 6.0 / 52 * h
		ix := 28.0 / 52 * h
		capW := 16.0 * h / 26
		wx, ww := x+ix*0.5, w-ix*0.5-capW
		tbox("bar_fill", wx, y+iy, ww, h-2*iy, "#83807f", 1) // missing part: the mockup's grey segment
		box(fill, wx, y+iy, ww*frac, h-2*iy)
		if shield > 0 && !opt.niches {
			tbox("strip3_shield", wx, y+h-iy-(h-2*iy)*0.4, ww*shield, (h-2*iy)*0.4, "#5fd0e0", 1)
		} else if shield > 0 {
			tbox("strip3_shield", wx, y+h+1, ww*shield, 6, "#5fd0e0", 1) // thin glowing line above the HP well
		}
		box("strip3_bar_frame", x, y, w-capW+1, h)
		box("strip3_bar_cap", x+w-capW, y, capW, h)
		outl(value, 26, "#f2f4f5", wx+ww/2, y+h/2, 1)
	}
	y := l.BarsY
	bar(l.BarsX, y, half, slimH, 1.0, "strip3_fill_st", "112", 0)
	bar(l.BarsX+half+barGap, y, half, slimH, 1.0, "strip3_fill_ei", "85", 0)
	y += slimH + barGap
	bar(l.BarsX, y, l.BarsW, hpH, 0.93, "strip3_fill_hp", "141", 0.8)

	// hotbar: mockup cells 465 + i * 88.3, 886..972 (content), rarity / selection as the mockup
	rar := []string{rarity["legendary"], rarity["rare"], rarity["magic"], rarity["epic"], "", "", "", ""}
	for i := 0; i < 8; i++ {
		x := l.HotX + float64(i)*(in.Cell+in.Gap)
		cy := l.CellsY
		cw, ch := in.Cell, in.CellH
		if i == 0 {
			g := ch * 0.3
			tbox("strip_glow", x-g/2, cy-g/2, cw+g, ch+g, teal, 0.45)
			s := ch * 0.05
			tbox("strip_rim", x-s/2, cy-s/2, cw+s, ch+s, teal, 1)
		}
		sd := ch * 0.08
		tbox("strip3_cell", x-sd/2, cy-sd/2, cw+sd, ch+sd, "#000000", 0.69)
		box("strip3_cell", x, cy, cw, ch)
		if rar[i] != "" {
			ri := ch * 0.035
			tbox("strip_rim", x+ri, cy+ri, cw-2*ri, ch-2*ri, rar[i], 1)
		}
		ic := ch * 0.72
		mx := 465 + float64(i)*88.3
		if i != 6 {
			c.iconAt(mock, mx+10, 897, 60, 66, X(x+(cw-ic)/2), Y(cy+(ch+ic)/2), ic*k, ic*k)
		}
		outl(fmt.Sprint(i+1), 24, "#f2f4f5", x+ch*0.09, cy+ch*0.82, 0)
		if i == 7 {
			outl("12", 26, "#f2f4f5", x+cw-ch*0.09, cy+ch*0.16, 2)
		}
		if i == 3 {
			tbox("gem_rarity", x+cw-ch*0.3, cy+ch*0.70, ch*0.24, ch*0.24, "#5fd35f", 1)
		}
		if i < 6 {
			tbox("strip_dur", x+cw*0.1, cy-6, cw*0.8, 4, "#05090c", 0.7)
			tbox("strip_dur", x+cw*0.1, cy-6, cw*0.8*[]float64{0.9, 0.7, 0.8, 0.6, 0.85, 0.5}[i], 4, gold, 1)
		}
	}
	// abilities: Q dash, G bash (cooldown 12), empty slot (rune ghost), guardian power F
	abMock := []float64{1170, 1258, 1440}
	for i := 0; i < 3; i++ {
		x := l.AbX + float64(i)*(in.Cell+in.Gap)
		cy := l.CellsY
		cw, ch := in.Cell, in.CellH
		sd := ch * 0.08
		tbox("strip3_cell", x-sd/2, cy-sd/2, cw+sd, ch+sd, "#000000", 0.69)
		box("strip3_cell", x, cy, cw, ch)
		ic := ch * 0.72
		c.iconAt(mock, abMock[i]+12, 897, 62, 66, X(x+(cw-ic)/2), Y(cy+(ch+ic)/2), ic*k, ic*k)
		outl([]string{"Q", "G", "F"}[i], 24, "#f2f4f5", x+ch*0.09, cy+ch*0.82, 0)
		if i == 1 {
			tbox("strip3_cell", x+4, cy+4, cw-8, ch-8, "#000000", 0.45)
			outl("12", 24, "#f2f4f5", x+cw/2, cy+ch/2, 1)
		}
	}
	// food bowls (mockup centres 90 / 188 / 287, y 952), 4th empty with the bowl ghost; time arcs + timers
	foodMock := []float64{90, 188, 287}
	for i := 0; i < opt.food; i++ {
		x := l.FoodX + float64(i)*l.FoodStep
		fy := l.FoodY
		tbox("strip_bowl", x, fy, l.FoodCell, l.FoodCell, "#b4babd", 1)
		if i >= 3 {
			g := l.FoodCell * 0.62
			tbox("strip_ghost_food", x+(l.FoodCell-g)/2, fy+(l.FoodCell-g)/2, g, g, "#ffffff", 0.36)
			continue
		}
		c.arc("gp_ring", X(x+l.FoodCell*0.04), Y(fy+l.FoodCell*0.96), l.FoodCell*0.92*k, gold, []float64{0.72, 0.55, 0.3}[i])
		ic := l.FoodCell * 0.72
		c.iconAt(mock, foodMock[i]-30, 922, 60, 60, X(x+(l.FoodCell-ic)/2), Y(fy+(l.FoodCell+ic)/2), ic*k, ic*k)
		outl([]string{"12:40", "08:15", "03:02"}[i], 24, "#e3e6e8", x+l.FoodCell/2, fy-in.TimerH/2, 1)
	}
	// potions (mockup cells 1553 + i * 70, 912..1000), 2 empty with the flask ghost
	for i := 0; i < opt.pot; i++ {
		x := l.PotX + float64(i)*l.PotStep
		py := l.PotY
		pw, ph := l.PotCell, l.PotH
		sd := pw * 0.08
		tbox("strip3_cell", x-sd/2, py-sd/2, pw+sd, ph+sd, "#000000", 0.69)
		box("strip3_cell", x, py, pw, ph)
		if i < 3 {
			ic := pw * 0.72
			c.iconAt(mock, 1553+float64(i)*70+8, 925, 48, 70, X(x+(pw-ic)/2), Y(py+(ph+ic)/2), ic*k, ic*k)
			outl([]string{"3", "5", "1"}[i], 26, "#f2f4f5", x+pw*0.91, py+ph*0.16, 2)
		} else {
			g := pw * 0.62
			tbox("strip_ghost_potion", x+(pw-g)/2, py+(ph-g)/2, g, g, "#ffffff", 0.36)
		}
		outl([]string{"Z", "X", "C", "V", "B"}[i], 24, "#f2f4f5", x+pw*0.09, py+ph*0.82, 0)
	}
	// status groups: the mockup's own buff / debuff cells (content) - in the niches above the wings (hud-3c), else above
	// the strip at its sides; rows of N grow upward, max 3 rows, then a "+N" chip; buffs left, debuffs right
	buffCell := func(i int) (float64, float64) { // hud-3 buff cells: bottom row x 40 + i * 63.4, y 797; top row y 733
		j := i % 13
		if j < 10 {
			return 40 + float64(j)*63.4, 797
		}
		return 40 + float64(j-10)*63.4, 733
	}
	debuffCell := func(i int) (float64, float64) { return 1700 + float64(i%3)*63.4, 797 }
	group := func(n int, cellAt func(int) (float64, float64), x0, x1, yBottom, cs float64, right bool) {
		gap := 6.0
		per := int((x1 - x0 + gap) / (cs + gap))
		if per < 1 {
			per = 1
		}
		shown := n
		more := 0
		if n > per*3 {
			shown = per*3 - 1
			more = n - shown
		}
		for i := 0; i <= shown; i++ {
			if i == shown && more == 0 {
				break
			}
			col, row := i%per, i/per
			x := x0 + float64(col)*(cs+gap)
			if right {
				x = x1 - float64(col+1)*(cs+gap) + gap
			}
			y := yBottom + float64(row)*(cs+gap)
			if i == shown {
				box("socket_status_more", x, y, cs, cs)
				outl(fmt.Sprintf("+%d", more), 22, "#f2f4f5", x+cs/2, y+cs/2, 1)
				continue
			}
			sx, sy := cellAt(i)
			p := crop(mock, image.Rect(int(sx), int(sy), int(sx+56), int(sy+62)))
			r := c.R(X(x), Y(y+cs), cs*k, cs*k)
			over(c.img, resize(p, r.Dx(), r.Dy()), r.Min)
		}
	}
	if opt.niches {
		// niche = the upper band of each wing (the wing contents sit on the panel bottom): a carved double-rail ledge
		// with the knot joints closes it at the bottom, icons stand on the ledge; overflow rows grow upward
		cs := 48.0
		ledge := l.H - 10 - cs - 14
		knot := in.KnotW * 0.6
		box("strip3_rail", knot, ledge, l.CX-knot+6, 14)
		box("strip3_rail", l.CX+l.CW-6, ledge, in.W-knot-l.CX-l.CW+6, 14)
		group(opt.buffs, buffCell, knot+in.Pad, l.CX-in.Pad, ledge+14, cs, false)
		group(opt.debuffs, debuffCell, l.CX+l.CW+in.Pad, in.W-knot-in.Pad, ledge+14, cs, true)
	} else {
		inset := (in.KnotW*0.6 + 6) * k
		place := func(sx, sy, sw, sh, dx, dyBottom float64) {
			p := crop(mock, image.Rect(int(sx), int(sy), int(sx+sw), int(sy+sh)))
			r := c.R(dx, dyBottom-sh*k, sw*k, sh*k)
			over(c.img, resize(p, r.Dx(), r.Dy()), r.Min)
		}
		stripTop := Y(l.H) - 6
		place(38, 730, 630, 132, left+inset, stripTop)
		place(1698, 795, 182, 70, left+widthPx-inset-182*k, stripTop)
	}
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

// arc draws a ring sprite tinted, kept only over `frac` of the turn counter-clockwise from 12 o'clock (Unity
// Image.Filled Radial360, fillOrigin Top, fillClockwise false), at (x, y) mockup px, side s.
func (c *canvas) arc(name string, x, y, s float64, col string, frac float64) {
	r := c.R(x, y, s, s)
	img := resize(tinted(sprites[name].img, hex(col)), r.Dx(), r.Dy())
	cx, cy := float64(r.Dx())/2, float64(r.Dy())/2
	for py := 0; py < r.Dy(); py++ {
		for px := 0; px < r.Dx(); px++ {
			a := math.Atan2(-(float64(px) - cx), -(float64(py) - cy)) // 0 at top, counter-clockwise positive
			if a < 0 {
				a += 2 * math.Pi
			}
			if a > frac*2*math.Pi {
				img.Pix[(py*r.Dx()+px)*4+3] = 0
			}
		}
	}
	over(c.img, img, r.Min)
}

// iconAt copies mockup content from (sx, sy, sw, sh) mockup px to (x, y, w, h) mockup px, dark background keyed.
func (c *canvas) iconAt(mock *image.NRGBA, sx, sy, sw, sh, x, y, w, h float64) {
	p := crop(mock, image.Rect(int(sx), int(sy), int(sx+sw), int(sy+sh)))
	for i := 0; i < len(p.Pix); i += 4 {
		mx := max(p.Pix[i], p.Pix[i+1], p.Pix[i+2])
		if mx < 60 {
			p.Pix[i+3] = 0
		} else if mx < 90 {
			p.Pix[i+3] = uint8(int(mx-60) * 255 / 30)
		}
	}
	over(c.img, resize(p, int(w*c.K), int(h*c.K)), image.Pt(int(x*c.K), int(y*c.K)))
}

// compare-hud2 [heightRefPx]: offline composite vs mockup hud-3 (default height 126 = Hud.StripHeight).
func cmdCompareHud2(height float64) {
	loadSprites()
	palette = loadSpec().Palette
	m, o := compareHud3(height, hudOpts{food: 4, pot: 3, buffs: 13, debuffs: 3})
	savePNG(hud2Out+"h-offline-1080.png", o)
	savePNG(hud2Out+"h-offline-1440.png", resize(o, 2560, 1440))
	sideBySide(m, o, hud2Out+"h-offline-sbs-1080.png")
	sideBySide(resize(m, 2560, 1440), resize(o, 2560, 1440), hud2Out+"h-offline-sbs-1440.png")
	sideBySideV(crop(m, image.Rect(0, 700, 1920, 1080)), crop(o, image.Rect(0, 700, 1920, 1080)), hud2Out+"h-offline-strip-1080.png")
	sideBySide(m, o, fmt.Sprintf("%scompare-hud3-%.0f.png", hud2Out, height))
	strip := image.Rect(0, 700, 1920, 1080)
	sideBySideV(crop(m, strip), crop(o, strip), fmt.Sprintf("%scompare-hud3-strip-%.0f.png", hud2Out, height))
}

// cmdCompareHud2Shot: the in-game shot (any size, scaled to 1920x1080) under the mockup, strip region of both.
func cmdCompareHud2Shot(shot string) {
	m := loadPNG(hud3Mock)
	s := resize(loadPNG(shot), 1920, 1080)
	strip := image.Rect(0, 700, 1920, 1080)
	sideBySideV(crop(m, strip), crop(s, strip), hud2Out+"compare-hud3-ingame.png")
}

// sideBySideV stacks a over b (wide crops read better one above the other).
func sideBySideV(a, b *image.NRGBA, out string) {
	w, h := a.Bounds().Dx(), a.Bounds().Dy()
	img := image.NewNRGBA(image.Rect(0, 0, w, h*2+12))
	fill(img, img.Bounds(), hex("#ffffff"))
	over(img, a, image.Point{})
	over(img, b, image.Pt(0, h+12))
	savePNG(out, img)
	fmt.Println("wrote", out)
}
