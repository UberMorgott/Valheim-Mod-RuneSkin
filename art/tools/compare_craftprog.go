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
	inner := gap * 0.5
	step, fw, sh := h*0.55, h*0.75, h*0.95
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
	cw := math.Min(math.Max(c.W*0.26, h*1.2), h*2.6)
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
	minus, field, plus, button, _, _ := cpActionRow(c, c.H*0.125)
	gold := hex("#F0C419")
	draw9s(p, "button_icon_normal", minus.rect(), 1/1.8, color.NRGBA{}, false)
	draw9s(p, "button_icon_normal", plus.rect(), 1/1.8, color.NRGBA{}, false)
	draw9s(p, "field_search", field.rect(), 1, color.NRGBA{}, false)
	cpLabel(p, "\u2013", minus, minus.H*0.4, gold, false)
	cpLabel(p, "+", plus, plus.H*0.4, gold, false)
	cpLabel(p, "5", field, c.H*0.3, hex("#E3E6E8"), false)
	draw9s(p, "button_normal", button.rect(), 1/4.2, color.NRGBA{}, false)
	text := "СОЗДАТЬ X 5"
	// CraftGlyph: glyph left of the label, at least h * 0.3 in; PlateLabelFit centres the label in the span right of it.
	g := c.H * 0.6
	gl := button.X + c.H*0.3
	if s, ok := sprites["glyph_act_craft"]; ok {
		gr := cpBox{gl, button.Y + (button.H-g)/2, g, g}.rect()
		over(p, resize(tinted(s.img, hex("#4FE0E6")), gr.Dx(), gr.Dy()), gr.Min)
	}
	span := cpBox{gl + g + 4, button.Y, button.X + button.W - c.H*0.3 - (gl + g + 4), button.H}
	cpLabel(p, text, span, cpFit(text, c.H*0.42, span.W), hex("#E3E6E8"), false)
}

func cpProgress(p *image.NRGBA, r image.Rectangle) {
	c := cpRowBox(r)
	_, _, _, _, bar, cancel := cpActionRow(c, c.H*0.125)
	frame := cpBox{bar.X, bar.Y + bar.H*0.025, bar.W, bar.H * 0.95}
	iy := frame.H * 0.08
	ix := math.Min(iy*42/9, frame.W*0.25)
	trough := cpBox{frame.X + ix, frame.Y + iy, frame.W - 2*ix, frame.H - 2*iy}
	draw9s(p, "bar_trough", trough.rect(), trough.H/47, hex("#323B42"), false)
	fin := trough.H * 0.14
	fillB := cpBox{trough.X + fin*0.5, trough.Y + fin, (trough.W - fin) * 0.42, trough.H - 2*fin}
	draw9s(p, "bar_fill", fillB.rect(), fillB.H/47, hex("#12B9D0"), false)
	ew := math.Max(2, math.Round(fillB.H*6/48))
	er := cpBox{fillB.X + fillB.W - ew, fillB.Y, ew, fillB.H}.rect()
	over(p, resize(tinted(sprites["bar_edge"].img, hex("#E8E2D6")), er.Dx(), er.Dy()), er.Min)
	draw9s(p, "bar_frame_action", frame.rect(), iy/9, color.NRGBA{}, false)
	text := "СОЗДАЕТСЯ X 5"
	inner := cpBox{trough.X + trough.H*0.25, frame.Y, trough.W - trough.H*0.5, frame.H}
	cpLabel(p, text, inner, cpFit(text, bar.H*0.28, inner.W), hex("#E3E6E8"), true)
	draw9s(p, "button_normal", cancel.rect(), 1/4.2, color.NRGBA{}, false)
	t := "ОТМЕНА"
	cpLabel(p, t, cancel, cpFit(t, cancel.H*0.3, cancel.W*0.75), hex("#E3E6E8"), false)
}

func cpMeasure(s string, size float64) float64 {
	d := &font.Drawer{Face: face(cpBold, size)}
	return float64(d.MeasureString(s).Round())
}

// fitSize: TMP auto-size down from max until the text fits w.
func cpFit(s string, max, w float64) float64 {
	size := max
	for size > 10 && cpMeasure(s, size) > w {
		size -= 0.5
	}
	return size
}

// label: centred in b, optional dark outline (theme size.text.outline on the SDF ~ 2 px at these sizes).
func cpLabel(p *image.NRGBA, s string, b cpBox, size float64, col color.NRGBA, outline bool) {
	fc := face(cpBold, size)
	d := &font.Drawer{Face: fc}
	w := d.MeasureString(s).Round()
	m := fc.Metrics()
	asc, desc := float64(m.Ascent.Round()), float64(m.Descent.Round())
	px := int(b.X + (b.W-float64(w))/2)
	py := int(b.Y + (b.H+asc-desc)/2 - asc*0.1)
	if outline {
		dark := image.NewUniform(color.NRGBA{6, 9, 11, 240})
		for _, dd := range [][2]int{{-2, 0}, {2, 0}, {0, -2}, {0, 2}, {-1, -1}, {1, -1}, {-1, 1}, {1, 1}} {
			dr := &font.Drawer{Dst: p, Src: dark, Face: fc, Dot: fixed.P(px+dd[0], py+dd[1])}
			dr.DrawString(s)
		}
	}
	dr := &font.Drawer{Dst: p, Src: image.NewUniform(col), Face: fc, Dot: fixed.P(px, py)}
	dr.DrawString(s)
}
