package main

import (
	"fmt"
	"image"
	"image/color"
	"math"
	"path/filepath"
)

// compare-panel: offline check of the bottom panel (mockup bottom-hud-center-v2, user approved 2026-10-26). The
// panel_* sprites (column slices of the mockup's food 3 / potion 4 row, art/src/kit/panel_row.png) are composed per
// slot count exactly like StripMath.Compute (centre fixed, wings grown outward, segment art by SegArt) and compared
// with the mockup's own rows: food 1 / potions 1 (row 1), 2 / 3 (row 2), 3 / 4 (row 3). The overlays (icons, keys,
// counts, timers, bar fills) are the mockup row 3's own pixels inside Codex's removed-areas mask, moved with their
// cells, so the comparison isolates the frame. Output art/review/panel/sbs-f<f>p<p>.png ([mockup | ours] stacked) and
// diff-f<f>p<p>.png; prints the mean abs RGB difference over our opaque frame pixels.
//
//	go -C art/tools run . compare-panel [mockup.png] [row-2x.png] [removed_areas.png]
const (
	panelRowY0   = 676 // row 3 crop top in the mockup (1x)
	panelRowX0   = 170
	panelSheetY0 = 22 // slices start at sheet row 22 (2x)
	panelBlockL  = 774
	panelBlockR  = 2388
	panelLap     = 2 // sheet px each slice reaches into its neighbours
)

func panelSegArt(k, n, arts int) int {
	if arts <= 1 || k <= 0 {
		return 0
	}
	if k >= n-1 {
		return arts - 1
	}
	if arts == 2 {
		return 1
	}
	return 1 + (k-1)%(arts-2)
}

// panelCompose returns the 2x composition on a sheet-sized canvas plus the shift of every row-3 overlay region:
// food i (reading order) and potion i land at their slots for f / p.
func panelCompose(f, p int, sprites string, row2x, mask *image.NRGBA) (*image.NRGBA, *image.NRGBA) {
	W, H := row2x.Bounds().Dx(), row2x.Bounds().Dy()
	out := image.NewNRGBA(image.Rect(0, 0, W, H))
	ovl := image.NewNRGBA(image.Rect(0, 0, W, H))
	put := func(name string, x int) int {
		s := loadPNG(filepath.Join(sprites, name+".png"))
		over(out, s, image.Pt(x-panelLap, panelSheetY0)) // slices overlap their neighbours (size.hud.panel.lap)
		return s.Bounds().Dx() - 2*panelLap
	}
	// centre: one ability slice (the F cell)
	x := panelBlockL
	x += put("panel_block_l", x)
	x += put("panel_hotbar", x)
	x += put("panel_ability", x)
	put("panel_block_r", x)
	// row 3 slot ranges (sheet x): food seg k (k = 0 innermost) and potion seg k
	foodSeg := []int{566, 401, 236}
	potSeg := []int{2447, 2574, 2701, 2828}
	const foodW, potW = 165, 127
	moveOverlay := func(srcX, w, dstX int) {
		for yy := 0; yy < H; yy++ {
			for xx := srcX; xx < srcX+w; xx++ {
				if mask.NRGBAAt(xx, yy).R < 128 {
					continue
				}
				c := row2x.NRGBAAt(xx, yy)
				c.A = 255
				ovl.SetNRGBA(xx-srcX+dstX, yy, c)
			}
		}
	}
	// hotbar + bars overlays stay in place
	moveOverlay(panelBlockL, panelBlockR-panelBlockL, panelBlockL)
	// left wing
	x = panelBlockL - put0(sprites, "panel_food_in")
	put("panel_food_in", x)
	for k := 0; k < f; k++ {
		x -= foodW
		put(fmt.Sprintf("panel_food_seg%d", panelSegArt(k, f, 3)), x)
		i := f - 1 - k // reading order index of the food in slot k
		if i < 3 {
			moveOverlay(foodSeg[2-i], foodW, x)
		}
	}
	x -= put0(sprites, "panel_food_end")
	put("panel_food_end", x)
	// right wing
	x = panelBlockR
	x += put("panel_potion_in", x)
	for k := 0; k < p; k++ {
		put(fmt.Sprintf("panel_potion_seg%d", panelSegArt(k, p, 4)), x)
		if k < 4 {
			moveOverlay(potSeg[k], potW, x)
		}
		x += potW
	}
	put("panel_potion_end", x)
	return out, ovl
}

func put0(sprites, name string) int {
	return loadPNG(filepath.Join(sprites, name+".png")).Bounds().Dx() - 2*panelLap
}

// panelRowY finds the mockup row crop top whose hotbar block matches row 3's best (rows share x, mockup prompt).
func panelRowY(mock *image.NRGBA, guess int) int {
	best, bestY := math.MaxFloat64, guess
	for dy := -8; dy <= 8; dy++ {
		var s float64
		n := 0
		for y := 12; y < 170; y += 2 {
			for x := 620; x < 1300; x += 3 {
				a := mock.NRGBAAt(x, panelRowY0+y)
				b := mock.NRGBAAt(x, guess+dy+y)
				s += math.Abs(float64(a.R)-float64(b.R)) + math.Abs(float64(a.G)-float64(b.G)) + math.Abs(float64(a.B)-float64(b.B))
				n++
			}
		}
		if v := s / float64(n); v < best {
			best, bestY = v, guess+dy
		}
	}
	return bestY
}

func cmdComparePanel(args []string) {
	mockPath := `D:\tmp\cx-runeskin\mockups\bottom-hud-center-v2.png`
	rowPath := `D:\tmp\cx-runeskin\fresh\row-2x.png`
	maskPath := `D:\tmp\cx-runeskin\fresh\removed_areas.png`
	if len(args) > 0 {
		mockPath = args[0]
	}
	if len(args) > 1 {
		rowPath = args[1]
	}
	if len(args) > 2 {
		maskPath = args[2]
	}
	mock, row2x, mask := loadPNG(mockPath), loadPNG(rowPath), loadPNG(maskPath)
	sprites := filepath.Join("art", "sprites")
	outDir := filepath.Join("art", "review", "panel")
	W1, H1 := row2x.Bounds().Dx()/2, row2x.Bounds().Dy()/2
	bg := color.NRGBA{38, 33, 24, 255} // mean of the mockup's blurred ground
	for _, c := range []struct{ f, p, guess int }{{1, 1, 129}, {2, 3, 402}, {3, 4, panelRowY0}} {
		frame, ovl := panelCompose(c.f, c.p, sprites, row2x, mask)
		full := image.NewNRGBA(frame.Bounds())
		over(full, frame, image.Point{})
		over(full, ovl, image.Point{})
		ours1 := resize(full, W1, H1)
		frame1 := resize(frame, W1, H1)
		ry := c.guess
		if c.guess != panelRowY0 {
			ry = panelRowY(mock, c.guess)
		}
		ref := crop(mock, image.Rect(panelRowX0, ry, panelRowX0+W1, ry+H1))
		withBg := image.NewNRGBA(image.Rect(0, 0, W1, H1))
		fill(withBg, withBg.Bounds(), bg)
		over(withBg, ours1, image.Point{})
		sbs := image.NewNRGBA(image.Rect(0, 0, W1, 2*H1+6))
		fill(sbs, sbs.Bounds(), color.NRGBA{0, 0, 0, 255})
		over(sbs, ref, image.Point{})
		over(sbs, withBg, image.Pt(0, H1+6))
		savePNG(filepath.Join(outDir, fmt.Sprintf("sbs-f%dp%d.png", c.f, c.p)), sbs)
		savePNG(filepath.Join(outDir, fmt.Sprintf("frame2x-f%dp%d.png", c.f, c.p)), frame)
		// diff over our opaque frame pixels (overlay cells excluded)
		diff := image.NewNRGBA(image.Rect(0, 0, W1, H1))
		var sum float64
		n := 0
		for y := 0; y < H1; y++ {
			for x := 0; x < W1; x++ {
				fa := frame1.NRGBAAt(x, y)
				if fa.A < 240 || ovl.NRGBAAt(2*x, 2*y).A > 0 {
					continue
				}
				a, b := ours1.NRGBAAt(x, y), ref.NRGBAAt(x, y)
				d := (math.Abs(float64(a.R)-float64(b.R)) + math.Abs(float64(a.G)-float64(b.G)) + math.Abs(float64(a.B)-float64(b.B))) / 3
				sum += d
				n++
				v := uint8(math.Min(255, d*4))
				diff.SetNRGBA(x, y, color.NRGBA{v, v, v, 255})
			}
		}
		savePNG(filepath.Join(outDir, fmt.Sprintf("diff-f%dp%d.png", c.f, c.p)), diff)
		fmt.Printf("f%dp%d: mockup row y %d, frame px %d, mean abs RGB diff %.2f\n", c.f, c.p, ry, n, sum/math.Max(1, float64(n)))
	}
}
