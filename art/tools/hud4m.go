package main

import (
	"fmt"
	"image"
	"image/color"
	"path/filepath"

	"golang.org/x/image/draw"
	"golang.org/x/image/font"
	"golang.org/x/image/font/basicfont"
	"golang.org/x/image/math/fixed"
)

// hud-4M / hud-4V: pixel-region rearrangement of hud-4.
//   M: slot row moves down into the medallion's spot, medallion moves up into the freed row spot.
//   V: slot row moves down (wing bottom), medallion stays; old row strip merges into the niche.
// Only moved destinations + an inpaint mask (seams/freed areas) may differ from hud-4.

type hud4Side struct {
	right bool
	row   image.Rectangle // slot row (sockets + plate) in hud-4
	rowDy int
}

var hud4Sides = []hud4Side{
	{false, image.Rect(104, 774, 479, 872), 113},
	{true, image.Rect(1462, 770, 1818, 866), 119},
}

const hud4MedDy = -112

// medallion knot region (left outline traced in hud4MedLine, right bound at the bar block).
func hud4InMed(x, y int, right bool) bool {
	if y < 870 || y >= 1017 {
		return false
	}
	if right {
		x = 1919 - x
	}
	return float64(x) >= hud4LineX(y) && x < 481
}

type hud4Layout struct {
	comp    *image.NRGBA
	mask    *image.Alpha // inpaint
	allowed *image.Alpha // inpaint ∪ moved destinations
}

func hud4Rearrange(h4 *image.NRGBA, variant string) hud4Layout {
	b := h4.Bounds()
	comp := image.NewNRGBA(b)
	draw.Draw(comp, b, h4, image.Point{}, draw.Src)
	dest := image.NewAlpha(b) // exact moved pixels
	mask := image.NewAlpha(b)
	set := func(m *image.Alpha, x, y int) { m.SetAlpha(x, y, color.Alpha{255}) }
	for _, s := range hud4Sides {
		rowDst := s.row.Add(image.Pt(0, s.rowDy))
		// freed areas -> inpaint
		for y := s.row.Min.Y; y < s.row.Max.Y; y++ {
			for x := s.row.Min.X; x < s.row.Max.X; x++ {
				set(mask, x, y)
			}
		}
		if variant == "M" {
			for y := 860; y < 1020; y++ {
				for x := 1300 * btoi(s.right); x < 620+1300*btoi(s.right); x++ {
					if hud4InMed(x, y, s.right) {
						set(mask, x, y)
					}
				}
			}
			// exposed bar-block end cap left by the medallion (bars themselves start at 485)
			capR := image.Rect(474, 872, 485, 1000)
			if s.right {
				capR = image.Rect(1919-484, 872, 1919-473, 1000)
			}
			draw.Draw(mask, capR, image.Opaque, image.Point{}, draw.Src)
		} else {
			// open the rail between niche and the old row so the niche becomes taller
			nr := image.Rect(100, 750, 500, 778)
			if s.right {
				nr = image.Rect(1452, 750, 1830, 774)
			}
			draw.Draw(mask, nr, image.Opaque, image.Point{}, draw.Src)
		}
		// seam band around the moved row
		draw.Draw(mask, rowDst.Inset(-7), image.Opaque, image.Point{}, draw.Src)
		// paste medallion (M) then row on top
		if variant == "M" {
			for y := 870; y < 1017; y++ {
				for x := 1300 * btoi(s.right); x < 620+1300*btoi(s.right); x++ {
					if !hud4InMed(x, y, s.right) {
						continue
					}
					c := h4.NRGBAAt(x, y)
					comp.SetNRGBA(x, y+hud4MedDy, c)
					// dark background showing between knot strands (outside the disc) is inpainted
					// so the plate/rail behind shows through; the rest of the medallion is exact.
					cx := 418
					if s.right {
						cx = 1501
					}
					lum := (int(c.R)*3 + int(c.G)*6 + int(c.B)) / 10
					ddx, ddy := x-cx, y-944
					if lum < 26 && ddx*ddx+ddy*ddy > 60*60 {
						set(mask, x, y+hud4MedDy)
						continue
					}
					set(dest, x, y+hud4MedDy)
				}
			}
			// the medallion's inner side was merged into the bar frame: complete it by inpaint
			cut := image.Rect(476, 758, 501, 874)
			if s.right {
				cut = image.Rect(1436, 758, 1447, 874)
			}
			for y := cut.Min.Y; y < cut.Max.Y; y++ {
				for x := cut.Min.X; x < cut.Max.X; x++ {
					set(mask, x, y)
					dest.SetAlpha(x, y, color.Alpha{})
				}
			}
		}
		for y := s.row.Min.Y; y < s.row.Max.Y; y++ {
			for x := s.row.Min.X; x < s.row.Max.X; x++ {
				comp.SetNRGBA(x, y+s.rowDy, h4.NRGBAAt(x, y))
				set(dest, x, y+s.rowDy)
			}
		}
		if variant == "V" { // medallion stays on top, unchanged
			for y := 870; y < 1017; y++ {
				for x := 1300 * btoi(s.right); x < 620+1300*btoi(s.right); x++ {
					if hud4InMed(x, y, s.right) {
						comp.SetNRGBA(x, y, h4.NRGBAAt(x, y))
						set(dest, x, y)
					}
				}
			}
		}
	}
	// inpaint never touches exact moved pixels or the bars
	draw.Draw(mask, image.Rect(479, 874, 1442, 1002), image.Transparent, image.Point{}, draw.Src)
	allowed := image.NewAlpha(b)
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			if dest.AlphaAt(x, y).A > 0 {
				mask.SetAlpha(x, y, color.Alpha{})
				set(allowed, x, y)
			}
			if mask.AlphaAt(x, y).A > 0 {
				set(allowed, x, y)
			}
			if allowed.AlphaAt(x, y).A == 0 { // stray pasted pixels outside the edit stay hud-4
				comp.SetNRGBA(x, y, h4.NRGBAAt(x, y))
			}
		}
	}
	return hud4Layout{comp, mask, allowed}
}

func btoi(b bool) int {
	if b {
		return 1
	}
	return 0
}

var hud4MCrops = []hud4Crop{
	{"L", image.Rect(30, 680, 540, 1020)},
	{"R", image.Rect(1380, 680, 1890, 1020)},
}

// cmdHud4M: hud4m prep|compose <hud-4.png> <hud-2.png> <dir> <M|V>
func cmdHud4M(args []string) {
	if len(args) < 5 {
		must(fmt.Errorf("usage: hud4m prep|compose <hud-4.png> <hud-2.png> <dir> <M|V>"))
	}
	h4, h2, dir, v := loadPNG(args[1]), loadPNG(args[2]), args[3], args[4]
	lay := hud4Rearrange(h4, v)
	switch args[0] {
	case "prep":
		for _, c := range hud4MCrops {
			mk := crop(lay.comp, c.R)
			for y := c.R.Min.Y; y < c.R.Max.Y; y++ {
				for x := c.R.Min.X; x < c.R.Max.X; x++ {
					if lay.mask.AlphaAt(x, y).A > 0 {
						mk.SetNRGBA(x-c.R.Min.X, y-c.R.Min.Y, color.NRGBA{255, 0, 255, 255})
					}
				}
			}
			savePNG(filepath.Join(dir, v+"-crop-"+c.Name+"-masked.png"), mk)
			savePNG(filepath.Join(dir, v+"-crop-"+c.Name+".png"), crop(lay.comp, c.R))
		}
		savePNG(filepath.Join(dir, v+"-comp.png"), lay.comp)
		fmt.Println("prep ok")
	case "compose":
		out := lay.comp
		for _, c := range hud4MCrops {
			raw := loadPNG(filepath.Join(dir, v+"-gen-"+c.Name+".png"))
			gen := image.NewNRGBA(image.Rect(0, 0, c.R.Dx(), c.R.Dy()))
			draw.CatmullRom.Scale(gen, gen.Bounds(), raw, raw.Bounds(), draw.Src, nil)
			dx, dy, rms := hud4Align(gen, crop(lay.comp, c.R), lay.mask, c.R.Min)
			fmt.Printf("%s crop %s: align dx=%d dy=%d rms=%.1f\n", v, c.Name, dx, dy, rms)
			for y := c.R.Min.Y; y < c.R.Max.Y; y++ {
				for x := c.R.Min.X; x < c.R.Max.X; x++ {
					if lay.mask.AlphaAt(x, y).A == 0 {
						continue
					}
					w := float64(hud4EdgeDist(lay.mask, x, y, 3)) / 3
					g := gen.NRGBAAt(x-c.R.Min.X+dx, y-c.R.Min.Y+dy)
					o := out.NRGBAAt(x, y)
					out.SetNRGBA(x, y, color.NRGBA{
						uint8(float64(o.R)*(1-w) + float64(g.R)*w + 0.5),
						uint8(float64(o.G)*(1-w) + float64(g.G)*w + 0.5),
						uint8(float64(o.B)*(1-w) + float64(g.B)*w + 0.5), 255})
				}
			}
		}
		name := "hud-4" + v
		savePNG(filepath.Join(dir, name+".png"), out)
		hud4MReport(h4, h2, out, lay.allowed, dir, name, "hud-2")
	default:
		must(fmt.Errorf("unknown mode %q", args[0]))
	}
}

func hud4MReport(h4, h2, out *image.NRGBA, allowed *image.Alpha, dir, name, refLabel string) {
	b := h4.Bounds()
	diff := image.NewNRGBA(b)
	var outside, inside int
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			a, o := h4.NRGBAAt(x, y), out.NRGBAAt(x, y)
			d := hud4Abs(int(a.R)-int(o.R)) + hud4Abs(int(a.G)-int(o.G)) + hud4Abs(int(a.B)-int(o.B))
			v := hud4U8(d * 3)
			c := color.NRGBA{v, v, v, 255}
			if allowed.AlphaAt(x, y).A > 0 {
				c = color.NRGBA{v, hud4U8(int(v) + 40), v, 255}
				if d > 0 {
					inside++
				}
			} else if d > 0 {
				c = color.NRGBA{255, 0, 0, 255}
				outside++
			}
			diff.SetNRGBA(x, y, c)
		}
	}
	savePNG(filepath.Join(dir, name+"-diff.png"), diff)
	strip := image.Rect(0, 590, 1920, 1040)
	h := strip.Dy()
	sv := image.NewNRGBA(image.Rect(0, 0, 1920, h*2))
	draw.Draw(sv, image.Rect(0, 0, 1920, h), h2, strip.Min, draw.Src)
	draw.Draw(sv, image.Rect(0, h, 1920, h*2), out, strip.Min, draw.Src)
	hud4Label(sv, 8, 18, refLabel)
	hud4Label(sv, 8, h+18, name)
	savePNG(filepath.Join(dir, name+"-vs-"+refLabel+".png"), sv)
	fmt.Printf("%s: changed px outside moved+inpaint regions=%d; inside=%d\n", name, outside, inside)
}

func hud4Label(img *image.NRGBA, x, y int, s string) {
	w := len(s)*7 + 10
	draw.Draw(img, image.Rect(x-4, y-14, x+w, y+6), image.NewUniform(color.NRGBA{0, 0, 0, 200}), image.Point{}, draw.Over)
	d := font.Drawer{Dst: img, Src: image.NewUniform(color.NRGBA{255, 255, 0, 255}), Face: basicfont.Face7x13, Dot: fixed.P(x, y)}
	d.DrawString(s)
}
