package main

import (
	"fmt"
	"image"
	"image/color"
	"math"
	"os"
	"path/filepath"

	"golang.org/x/image/draw"
)

// hud-4: masked edit of hud-2.png. Only masked pixels change; everything else is copied from base.

type hud4Crop struct {
	Name string
	R    image.Rectangle
}

type hud4Mask struct {
	Name string
	Crop string
	R    image.Rectangle
}

var hud4Crops = []hud4Crop{
	{"L", image.Rect(0, 400, 960, 1040)},
	{"C", image.Rect(480, 400, 1440, 1040)},
	{"R", image.Rect(960, 400, 1920, 1040)},
}

var hud4Masks = []hud4Mask{
	{"left wing extension", "L", image.Rect(36, 915, 299, 1012)},
	{"left niche", "L", image.Rect(48, 682, 519, 773)},
	{"FOOD caption", "L", image.Rect(238, 874, 318, 904)},
	{"selection annotation", "L", image.Rect(372, 652, 602, 713)},
	{"selection arrowhead", "L", image.Rect(522, 704, 546, 730)},
	{"rarity annotation", "L", image.Rect(606, 652, 784, 713)},
	{"rarity arrowhead", "L", image.Rect(606, 704, 630, 734)},
	{"set marker annotation", "C", image.Rect(866, 678, 984, 713)},
	{"set marker arrowhead", "C", image.Rect(868, 704, 888, 726)},
	{"effect row", "C", image.Rect(790, 606, 1126, 680)},
	{"HOTBAR caption", "C", image.Rect(738, 836, 1034, 868)},
	{"ABILITY caption", "C", image.Rect(1296, 836, 1404, 868)},
	{"HEALTH/SHIELD caption", "C", image.Rect(852, 935, 1068, 959)},
	{"STAMINA caption", "C", image.Rect(666, 1003, 780, 1028)},
	{"EITR caption", "C", image.Rect(1166, 1003, 1244, 1028)},
	{"right niche", "R", image.Rect(1438, 682, 1852, 773)},
	{"POTIONS caption", "R", image.Rect(1586, 874, 1700, 904)},
	{"potions annotation", "R", image.Rect(1600, 700, 1800, 773)},
	{"right wing extension", "R", image.Rect(1622, 915, 1890, 1012)},
}

// Status tiles in the base effect row (source rects), split buffs/debuffs.
var hud4Debuffs = []image.Rectangle{
	image.Rect(801, 617, 856, 672), // crossed swords
	image.Rect(867, 617, 922, 672), // frost
	image.Rect(932, 617, 987, 672), // poison
}
var hud4Buffs = []image.Rectangle{
	image.Rect(998, 617, 1053, 672),  // warm/rested
	image.Rect(1063, 617, 1118, 672), // guardian
}

func cmdHud4(args []string) {
	if len(args) < 2 {
		must(fmt.Errorf("usage: hud4 prep|compose <base.png> <dir> [tileSize nicheL nicheR nicheY]"))
	}
	base := loadPNG(args[1])
	dir := args[2]
	switch args[0] {
	case "prep":
		hud4Prep(base, dir)
	case "compose":
		hud4Compose(base, dir)
	default:
		must(fmt.Errorf("unknown hud4 mode %q", args[0]))
	}
}

// Outer outline of the left medallion's knot frame (x by y); the right medallion is its mirror about x=960.
var hud4MedLine = []image.Point{{372, 880}, {355, 903}, {333, 920}, {318, 929}, {300, 938}, {294, 944}, {300, 952}, {318, 970}, {330, 972}, {331, 988}, {345, 997}, {370, 1006}, {400, 1016}}

func hud4LineX(y int) float64 {
	p := hud4MedLine
	if y <= p[0].Y {
		return float64(p[0].X)
	}
	for i := 1; i < len(p); i++ {
		if y <= p[i].Y {
			t := float64(y-p[i-1].Y) / float64(p[i].Y-p[i-1].Y)
			return float64(p[i-1].X) + t*float64(p[i].X-p[i-1].X)
		}
	}
	return float64(p[len(p)-1].X)
}

// hud4Ext marks the wing-extension pixels: everything left of the medallion knot outline, so the
// medallion itself stays base pixels.
func hud4Ext(m *image.Alpha, right bool) {
	const y0, y1, x0 = 915, 1012, 36
	for y := y0; y < y1; y++ {
		lx := hud4LineX(y)
		for x := x0; float64(x) < lx; x++ {
			mx := x
			if right {
				mx = 1919 - x
			}
			m.SetAlpha(mx, y, color.Alpha{255})
		}
	}
}

func hud4MaskImg(b image.Rectangle, crop string) *image.Alpha {
	m := image.NewAlpha(b)
	for _, k := range hud4Masks {
		if crop != "" && k.Crop != crop {
			continue
		}
		switch k.Name {
		case "left wing extension":
			hud4Ext(m, false)
		case "right wing extension":
			hud4Ext(m, true)
		default:
			draw.Draw(m, k.R, image.Opaque, image.Point{}, draw.Src)
		}
	}
	return m
}

func hud4Prep(base *image.NRGBA, dir string) {
	for _, c := range hud4Crops {
		clean := crop(base, c.R)
		savePNG(filepath.Join(dir, "crop-"+c.Name+".png"), clean)
		mk := crop(base, c.R)
		for _, k := range hud4Masks {
			if k.Crop != c.Name {
				continue
			}
			r := k.R.Sub(c.R.Min)
			draw.Draw(mk, r, image.NewUniform(color.NRGBA{255, 0, 255, 255}), image.Point{}, draw.Src)
		}
		savePNG(filepath.Join(dir, "crop-"+c.Name+"-masked.png"), mk)
	}
	m := hud4MaskImg(base.Bounds(), "")
	savePNG(filepath.Join(dir, "mask.png"), m)
	fmt.Println("prep ok")
}

// align finds the integer offset (dx,dy) of gen vs base crop minimizing SSD on unmasked pixels.
func hud4Align(gen, ref *image.NRGBA, mask *image.Alpha, off image.Point) (int, int, float64) {
	bestDx, bestDy, best := 0, 0, math.MaxFloat64
	b := ref.Bounds()
	for dy := -14; dy <= 14; dy++ {
		for dx := -14; dx <= 14; dx++ {
			var s float64
			n := 0
			for y := b.Min.Y + 20; y < b.Max.Y-20; y += 3 {
				for x := b.Min.X + 20; x < b.Max.X-20; x += 3 {
					if mask.AlphaAt(x+off.X, y+off.Y).A > 0 {
						continue
					}
					a := ref.NRGBAAt(x, y)
					g := gen.NRGBAAt(x+dx, y+dy)
					dr, dg, db := float64(a.R)-float64(g.R), float64(a.G)-float64(g.G), float64(a.B)-float64(g.B)
					s += dr*dr + dg*dg + db*db
					n++
				}
			}
			if n > 0 && s/float64(n) < best {
				best, bestDx, bestDy = s/float64(n), dx, dy
			}
		}
	}
	return bestDx, bestDy, math.Sqrt(best / 3)
}

func hud4Compose(base *image.NRGBA, dir string) {
	out := image.NewNRGBA(base.Bounds())
	draw.Draw(out, out.Bounds(), base, image.Point{}, draw.Src)
	full := hud4MaskImg(base.Bounds(), "")
	for _, c := range hud4Crops {
		gp := filepath.Join(dir, "gen-"+c.Name+".png")
		if _, err := os.Stat(gp); err != nil {
			must(fmt.Errorf("missing %s", gp))
		}
		raw := loadPNG(gp)
		gen := image.NewNRGBA(image.Rect(0, 0, c.R.Dx(), c.R.Dy()))
		draw.CatmullRom.Scale(gen, gen.Bounds(), raw, raw.Bounds(), draw.Src, nil)
		ref := crop(base, c.R)
		dx, dy, rms := hud4Align(gen, ref, full, c.R.Min)
		fmt.Printf("crop %s: gen %dx%d align dx=%d dy=%d rms=%.1f\n", c.Name, raw.Bounds().Dx(), raw.Bounds().Dy(), dx, dy, rms)
		cm := hud4MaskImg(base.Bounds(), c.Name)
		for y := c.R.Min.Y; y < c.R.Max.Y; y++ {
			for x := c.R.Min.X; x < c.R.Max.X; x++ {
				if cm.AlphaAt(x, y).A == 0 {
					continue
				}
				// feather inward: blend weight by distance to mask edge (<=4px)
				d := hud4EdgeDist(cm, x, y, 4)
				w := float64(d) / 4
				g := gen.NRGBAAt(x-c.R.Min.X+dx, y-c.R.Min.Y+dy)
				o := base.NRGBAAt(x, y)
				out.SetNRGBA(x, y, color.NRGBA{
					uint8(float64(o.R)*(1-w) + float64(g.R)*w + 0.5),
					uint8(float64(o.G)*(1-w) + float64(g.G)*w + 0.5),
					uint8(float64(o.B)*(1-w) + float64(g.B)*w + 0.5), 255})
			}
		}
	}
	hud4Tiles(base, out)
	savePNG(filepath.Join(dir, "hud-4.png"), out)
	hud4Report(base, out, full, dir)
}

// hud4Edge returns min(limit, distance in px to the nearest unmasked pixel).
func hud4EdgeDist(m *image.Alpha, x, y, limit int) int {
	for d := 1; d <= limit; d++ {
		for i := -d; i <= d; i++ {
			if m.AlphaAt(x+i, y-d).A == 0 || m.AlphaAt(x+i, y+d).A == 0 || m.AlphaAt(x-d, y+i).A == 0 || m.AlphaAt(x+d, y+i).A == 0 {
				return d
			}
		}
	}
	return limit
}

// hud4Tiles pastes the base's own status tiles (resized) into the niches: buffs left, debuffs right,
// each group aligned toward screen center.
func hud4Tiles(base, out *image.NRGBA) {
	const size, gap, cy = 46, 8, 726
	place := func(srcs []image.Rectangle, anchorX int, rightAlign bool) {
		n := len(srcs)
		w := n*size + (n-1)*gap
		x0 := anchorX
		if rightAlign {
			x0 = anchorX - w
		}
		for i, s := range srcs {
			dst := image.Rect(x0+i*(size+gap), cy-size/2, x0+i*(size+gap)+size, cy-size/2+size)
			draw.CatmullRom.Scale(out, dst, base, s, draw.Src, nil)
		}
	}
	place(hud4Buffs, 484, true)
	place(hud4Debuffs, 1474, false)
}

func hud4Report(base, out *image.NRGBA, mask *image.Alpha, dir string) {
	b := base.Bounds()
	diff := image.NewNRGBA(b)
	var outsideMax, outsideCnt, insideCnt int
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			a, o := base.NRGBAAt(x, y), out.NRGBAAt(x, y)
			d := hud4Abs(int(a.R)-int(o.R)) + hud4Abs(int(a.G)-int(o.G)) + hud4Abs(int(a.B)-int(o.B))
			in := mask.AlphaAt(x, y).A > 0
			v := hud4U8(d * 3)
			c := color.NRGBA{v, v, v, 255}
			if in {
				c = color.NRGBA{v, hud4U8(int(v) + 40), v, 255}
				if d > 0 {
					insideCnt++
				}
			} else if d > 0 {
				c = color.NRGBA{255, 0, 0, 255}
				outsideCnt++
				outsideMax = max(outsideMax, d)
			}
			diff.SetNRGBA(x, y, c)
		}
	}
	savePNG(filepath.Join(dir, "hud-4-diff.png"), diff)
	sbs := image.NewNRGBA(image.Rect(0, 0, b.Dx()*2, b.Dy()))
	draw.Draw(sbs, b, base, image.Point{}, draw.Src)
	draw.Draw(sbs, b.Add(image.Pt(b.Dx(), 0)), out, image.Point{}, draw.Src)
	savePNG(filepath.Join(dir, "hud-4-sbs.png"), sbs)
	strip := image.Rect(0, 590, 1920, 1040)
	sv := image.NewNRGBA(image.Rect(0, 0, 1920, strip.Dy()*2))
	draw.Draw(sv, image.Rect(0, 0, 1920, strip.Dy()), base, strip.Min, draw.Src)
	draw.Draw(sv, image.Rect(0, strip.Dy(), 1920, strip.Dy()*2), out, strip.Min, draw.Src)
	savePNG(filepath.Join(dir, "hud-4-strip-sbs.png"), sv)
	fmt.Printf("outside-mask changed px=%d maxdiff=%d; inside-mask changed px=%d\n", outsideCnt, outsideMax, insideCnt)
}

func hud4U8(v int) uint8 {
	return uint8(max(0, min(255, v)))
}

func hud4Abs(v int) int {
	if v < 0 {
		return -v
	}
	return v
}
