package main

import (
	"fmt"
	"image"
	"image/color"
	"path/filepath"

	"golang.org/x/image/draw"
)

// hud-4M2: from hud-4M remove the upper wing area (niche + empty plate) so each wing is only its
// bottom slot row; medallions stay (attached to the hotbar block); buffs/debuffs float above the rows.

// hud4MedKnot marks the medallion's knot/disc pixels at their hud-4M position (moved up by hud4MedDy),
// excluding the dark gaps between strands.
func hud4MedKnot(h4 *image.NRGBA, m *image.Alpha, right bool) {
	cx := 418
	x0 := 0
	if right {
		cx, x0 = 1501, 1300
	}
	for y := 870; y < 1017; y++ {
		for x := x0; x < x0+620; x++ {
			if !hud4InMed(x, y, right) {
				continue
			}
			c := h4.NRGBAAt(x, y)
			lum := (int(c.R)*3 + int(c.G)*6 + int(c.B)) / 10
			dx, dy := x-cx, y-944
			if lum < 26 && dx*dx+dy*dy > 60*60 {
				continue
			}
			m.SetAlpha(x, y+hud4MedDy, color.Alpha{255})
		}
	}
}

var hud4M2Crops = []hud4Crop{
	{"L", image.Rect(0, 600, 540, 960)},
	{"R", image.Rect(1380, 600, 1920, 960)},
}

func hud4M2Mask(h4 *image.NRGBA) *image.Alpha {
	b := h4.Bounds()
	mask := image.NewAlpha(b)
	keep := image.NewAlpha(b)
	hud4MedKnot(h4, keep, false)
	hud4MedKnot(h4, keep, true)
	// medallion inner sides completed in hud-4M (attached to the hotbar block) stay
	draw.Draw(keep, image.Rect(476, 758, 501, 874), image.Opaque, image.Point{}, draw.Src)
	draw.Draw(keep, image.Rect(1436, 758, 1447, 874), image.Opaque, image.Point{}, draw.Src)
	for _, r := range []image.Rectangle{
		image.Rect(30, 672, 522, 878),    // left upper wing
		image.Rect(30, 866, 300, 891),    // left row top-edge seam
		image.Rect(1434, 672, 1892, 882), // right upper wing
		image.Rect(1625, 866, 1892, 893), // right row top-edge seam
	} {
		draw.Draw(mask, r, image.Opaque, image.Point{}, draw.Src)
	}
	draw.Draw(mask, image.Rect(479, 874, 1442, 1002), image.Transparent, image.Point{}, draw.Src) // bars
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			if keep.AlphaAt(x, y).A > 0 {
				mask.SetAlpha(x, y, color.Alpha{})
			}
		}
	}
	return mask
}

// cmdHud4M2: hud4m2 prep|compose <hud-4.png> <hud-4M.png> <dir> <hud-2.png (status tile source)>
func cmdHud4M2(args []string) {
	if len(args) < 5 {
		must(fmt.Errorf("usage: hud4m2 prep|compose <hud-4.png> <hud-4M.png> <dir> <hud-2.png>"))
	}
	h4, m, dir, h2 := loadPNG(args[1]), loadPNG(args[2]), args[3], loadPNG(args[4])
	mask := hud4M2Mask(h4)
	switch args[0] {
	case "prep":
		for _, c := range hud4M2Crops {
			mk := crop(m, c.R)
			for y := c.R.Min.Y; y < c.R.Max.Y; y++ {
				for x := c.R.Min.X; x < c.R.Max.X; x++ {
					if mask.AlphaAt(x, y).A > 0 {
						mk.SetNRGBA(x-c.R.Min.X, y-c.R.Min.Y, color.NRGBA{255, 0, 255, 255})
					}
				}
			}
			savePNG(filepath.Join(dir, "M2-crop-"+c.Name+"-masked.png"), mk)
			savePNG(filepath.Join(dir, "M2-crop-"+c.Name+".png"), crop(m, c.R))
		}
		fmt.Println("prep ok")
	case "compose":
		out := image.NewNRGBA(m.Bounds())
		draw.Draw(out, out.Bounds(), m, image.Point{}, draw.Src)
		for _, c := range hud4M2Crops {
			raw := loadPNG(filepath.Join(dir, "M2-gen-"+c.Name+".png"))
			gen := image.NewNRGBA(image.Rect(0, 0, c.R.Dx(), c.R.Dy()))
			draw.CatmullRom.Scale(gen, gen.Bounds(), raw, raw.Bounds(), draw.Src, nil)
			dx, dy, rms := hud4Align(gen, crop(m, c.R), mask, c.R.Min)
			fmt.Printf("M2 crop %s: align dx=%d dy=%d rms=%.1f\n", c.Name, dx, dy, rms)
			for y := c.R.Min.Y; y < c.R.Max.Y; y++ {
				for x := c.R.Min.X; x < c.R.Max.X; x++ {
					if mask.AlphaAt(x, y).A == 0 {
						continue
					}
					w := float64(hud4EdgeDist(mask, x, y, 3)) / 3
					g := gen.NRGBAAt(x-c.R.Min.X+dx, y-c.R.Min.Y+dy)
					o := out.NRGBAAt(x, y)
					out.SetNRGBA(x, y, color.NRGBA{
						uint8(float64(o.R)*(1-w) + float64(g.R)*w + 0.5),
						uint8(float64(o.G)*(1-w) + float64(g.G)*w + 0.5),
						uint8(float64(o.B)*(1-w) + float64(g.B)*w + 0.5), 255})
				}
			}
		}
		allowed := image.NewAlpha(m.Bounds())
		draw.Draw(allowed, allowed.Bounds(), mask, image.Point{}, draw.Src)
		// floating status tiles (base's own tiles): buffs left from the outer edge, debuffs right
		const size, gap, y0 = 46, 8, 820
		for i, s := range hud4Buffs {
			r := image.Rect(112+i*(size+gap), y0, 112+i*(size+gap)+size, y0+size)
			draw.CatmullRom.Scale(out, r, h2, s, draw.Src, nil)
			draw.Draw(allowed, r, image.Opaque, image.Point{}, draw.Src)
		}
		for i, s := range hud4Debuffs {
			x1 := 1808 - i*(size+gap)
			r := image.Rect(x1-size, y0, x1, y0+size)
			draw.CatmullRom.Scale(out, r, h2, s, draw.Src, nil)
			draw.Draw(allowed, r, image.Opaque, image.Point{}, draw.Src)
		}
		savePNG(filepath.Join(dir, "hud-4M2.png"), out)
		hud4MReport(m, m, out, allowed, dir, "hud-4M2", "hud-4M")
	default:
		must(fmt.Errorf("unknown mode %q", args[0]))
	}
}
