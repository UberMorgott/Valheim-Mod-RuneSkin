package main

import (
	"image"
	"image/color"
	"math"

	"golang.org/x/image/font"
	"golang.org/x/image/math/fixed"
)

// compare-craft-progress <shot 1920x1080> <out.png>: offline preview of the crafting window's craft row (approved mockup
// D:\tmp\cx-runeskin\mockups\craft-progress.png) built from our sprites with CraftingMath.ActionRow's geometry and the
// theme's roles: left = idle ([-][N][+] craft.amount.step / craft.amount.field + craft.button), right = progress
// (bar_trough + teal bar_fill inside bar_frame_action, outlined state text, craft.button cancel). The rest of the window
// is the in-game shot's crafting column (41d-crafting-progress), its old craft row painted over.
func cmdCompareCraftProgress(args []string) {
	if len(args) < 2 {
		must(cpUsage("compare-craft-progress <shot> <out.png>"))
	}
	loadSprites()
	shot := loadPNG(args[0])
	col := image.Rect(1036, 20, 1628, 876)
	row := image.Rect(1283-col.Min.X, 778-col.Min.Y, 1283+316-col.Min.X, 778+64-col.Min.Y)
	gap := 24
	out := image.NewNRGBA(image.Rect(0, 0, col.Dx()*2+gap*3, col.Dy()+gap*2))
	fill(out, out.Bounds(), color.NRGBA{12, 16, 20, 255})
	for k, progress := range []bool{false, true} {
		p := crop(shot, col)
		cpClearRow(p, row)
		if progress {
			cpProgress(p, row)
		} else {
			cpIdle(p, row)
		}
		over(out, p, image.Pt(gap+k*(col.Dx()+gap), gap))
	}
	savePNG(args[1], out)
}

func cpUsage(s string) error { return &cpUsageErr{s} }

type cpUsageErr struct{ s string }

func (e *cpUsageErr) Error() string { return "usage: " + e.s }

// clearRow: the row repainted with the window glass column just left of it (no old bar / black box).
func cpClearRow(p *image.NRGBA, r image.Rectangle) {
	pad := image.Rect(r.Min.X-2, r.Min.Y-4, r.Max.X+2, r.Max.Y+4)
	src := r.Min.X - 6
	for y := pad.Min.Y; y < pad.Max.Y; y++ {
		c := p.NRGBAAt(src, y)
		for x := pad.Min.X; x < pad.Max.X; x++ {
			p.SetNRGBA(x, y, c)
		}
	}
}

type cpBox struct{ X, Y, W, H float64 }

func (b cpBox) rect() image.Rectangle {
	return image.Rect(int(math.Round(b.X)), int(math.Round(b.Y)), int(math.Round(b.X+b.W)), int(math.Round(b.Y+b.H)))
}

// actionRow mirrors CraftingMath.ActionRow (stepShare 0.55, fieldShare 0.8, maxShare 0.4, cancelShare 0.3).
func cpActionRow(c cpBox, gap float64) (minus, field, plus, button, bar, cancel cpBox) {
	h := c.H
	inner := gap * 0.35
	step, fw, sh := h*0.68, h*0.85, h*0.95
	need := 2*step + fw + 2*inner
	if mx := c.W * 0.4; need > mx {
		k := mx / need
		step, fw, inner = step*k, fw*k, inner*k
	}
	y := c.Y + (h-sh)/2
	minus = cpBox{c.X, y, step, sh}
	field = cpBox{minus.X + step + inner, y, fw, sh}
	plus = cpBox{field.X + fw + inner, y, step, sh}
	button = cpBox{plus.X + step + gap, c.Y, c.X + c.W - (plus.X + step + gap), h}
	cw := math.Min(math.Max(c.W*0.27, h*1.2), h*2.6)
	cw = math.Min(cw, c.W*0.45)
	cancel = cpBox{c.X + c.W - cw, c.Y, cw, h}
	bar = cpBox{c.X, c.Y, cancel.X - gap - c.X, h}
	return
}

const cpBold = "PT_Sans-Web-Bold.ttf"

func cpRowBox(r image.Rectangle) cpBox {
	return cpBox{float64(r.Min.X), float64(r.Min.Y), float64(r.Dx()), float64(r.Dy())}
}

func cpIdle(p *image.NRGBA, r image.Rectangle) {
	c := cpRowBox(r)
	minus, field, plus, button, _, _ := cpActionRow(c, c.H*0.1)
	gold := hex("#F0C419")
	draw9s(p, "craft_step_normal", minus.rect(), 0.5/0.78, color.NRGBA{}, false)
	draw9s(p, "craft_step_normal", plus.rect(), 0.5/0.78, color.NRGBA{}, false)
	draw9s(p, "craft_field", field.rect(), 0.5/0.78, color.NRGBA{}, false)
	cpLabel(p, "\u2013", minus, minus.H*0.4, gold, false)
	cpLabel(p, "+", plus, plus.H*0.4, gold, false)
	cpLabel(p, "5", field, c.H*0.3, hex("#E3E6E8"), false)
	draw9s(p, "craft_plate_normal", button.rect(), 0.5/0.6, color.NRGBA{}, false)
	text := "СОЗДАТЬ X 5"
	// CraftGlyph: a g x g box (preserveAspect) left of the label, at least h * 0.3 in; PlateLabelFit centres the label
	// in the span right of it.
	g := c.H * 0.6
	gl := button.X + c.H*0.3
	if s, ok := sprites["craft_glyph_anvil"]; ok {
		sw, sh := float64(s.img.Bounds().Dx()), float64(s.img.Bounds().Dy())
		k := math.Min(g/sw, g/sh)
		gr := cpBox{gl + (g-sw*k)/2, button.Y + (button.H-sh*k)/2, sw * k, sh * k}.rect()
		over(p, resize(s.img, gr.Dx(), gr.Dy()), gr.Min)
	}
	sx := gl + g + c.H*0.12
	span := cpBox{sx, button.Y, button.X + button.W - c.H*0.3 - sx, button.H}
	cpLabel(p, text, span, cpFit(text, c.H*0.42, span.W), hex("#E3E6E8"), false)
}

func cpProgress(p *image.NRGBA, r image.Rectangle) {
	c := cpRowBox(r)
	_, _, _, _, bar, cancel := cpActionRow(c, c.H*0.1)
	frame := cpBox{bar.X, bar.Y + bar.H*0.05, bar.W, bar.H * 0.9}
	iy := frame.H * 0.19
	ix := math.Min(frame.H*0.35, frame.W*0.3)
	capW := math.Min(frame.H*42/66, frame.W*0.3)
	trough := cpBox{frame.X + ix, frame.Y + iy, frame.W - 2*ix, frame.H - 2*iy}
	draw9s(p, "craft_bar_trough", trough.rect(), 0.5, color.NRGBA{}, false)
	fin := trough.H * 0.08
	fillB := cpBox{trough.X + fin*0.5, trough.Y + fin, (trough.W - fin) * 0.33, trough.H - 2*fin}
	draw9s(p, "craft_bar_fill", fillB.rect(), 0.5, hex("#C4DADE"), false)
	draw9s(p, "craft_bar_frame", frame.rect(), frame.H/66, color.NRGBA{}, false)
	text := "СОЗДАЕТСЯ X 5"
	inner := cpBox{frame.X + capW, frame.Y, frame.W - 2*capW, frame.H}
	cpLabel(p, text, inner, cpFit(text, bar.H*0.28, inner.W), hex("#E3E6E8"), true)
	draw9s(p, "craft_plate_normal", cancel.rect(), 0.5/0.6, color.NRGBA{}, false)
	t := "ОТМЕНА"
	cpLabel(p, t, cancel, cpFit(t, cancel.H*0.3, cancel.W*0.75), hex("#E3E6E8"), false)
}

// cpTrack: TMP characterSpacing size.craft.label.tracking (-1 em/100) per glyph; cpCondense: the labels' localScale.x
// (share.craft.label.condense).
const (
	cpTrack    = -0.01
	cpCondense = 0.88
)

func cpMeasure(s string, size float64) float64 {
	d := &font.Drawer{Face: face(cpBold, size)}
	n := float64(len([]rune(s)))
	return (float64(d.MeasureString(s).Round()) + cpTrack*size*(n-1)) * cpCondense
}

// cpFit: TMP auto-size down from max until the text fits w.
func cpFit(s string, max, w float64) float64 {
	size := max
	for size > 10 && cpMeasure(s, size) > w {
		size -= 0.5
	}
	return size
}

// cpLabel: centred in b, tracked + condensed like the game labels, optional thin dark outline (size.text.outline).
func cpLabel(p *image.NRGBA, s string, b cpBox, size float64, col color.NRGBA, outline bool) {
	fc := face(cpBold, size)
	m := fc.Metrics()
	asc, desc := float64(m.Ascent.Round()), float64(m.Descent.Round())
	wide := int(math.Ceil(cpMeasure(s, size)/cpCondense)) + 4
	hgt := int(asc+desc) + 4
	tmp := image.NewNRGBA(image.Rect(0, 0, wide, hgt))
	py := int(asc) + 2
	draw := func(src image.Image, dx, dy int) {
		x := 2.0
		for _, r := range s {
			d := &font.Drawer{Dst: tmp, Src: src, Face: fc, Dot: fixed.P(int(math.Round(x))+dx, py+dy)}
			d.DrawString(string(r))
			adv, _ := fc.GlyphAdvance(r)
			x += float64(adv.Round()) + cpTrack*size
		}
	}
	if outline {
		dark := image.NewUniform(color.NRGBA{6, 9, 11, 240})
		for _, dd := range [][2]int{{-1, 0}, {1, 0}, {0, -1}, {0, 1}} {
			draw(dark, dd[0], dd[1])
		}
	}
	draw(image.NewUniform(col), 0, 0)
	sw := int(math.Round(float64(wide) * cpCondense))
	img := resize(tmp, sw, hgt)
	x0 := int(b.X + (b.W-float64(sw))/2)
	y0 := int(b.Y+(b.H+asc-desc)/2-asc*0.1) - py
	over(p, img, image.Pt(x0, y0))
}
