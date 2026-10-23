package main

import (
	"fmt"
	"image"
	"image/color"
	"path/filepath"
)

// paint4: composes the strip5_* source sheets from the Codex repaint tiles of the EMPTY hud-4M3 frame (strict edits of
// 2x mockup crops, tile scale 3.2 px per mockup px; prompts in art/src/kit/strip5_tiles/*.prompt.txt). Fixed ends are
// taken as painted, the straight runs (plates / rails) from a sample of the same tile, right ends = the left ones
// mirrored (the mockup frame is symmetric). Magenta stays the key colour.
//
//	paint4 <tile dir> <kit dir>

func tileRect(x0, y0, x1, y1 int) image.Rectangle { return image.Rect(x0, y0, x1, y1) }

// hcat joins images left to right (heights must match).
func hcat(parts ...*image.NRGBA) *image.NRGBA {
	w, h := 0, parts[0].Bounds().Dy()
	for _, p := range parts {
		w += p.Bounds().Dx()
	}
	out := image.NewNRGBA(image.Rect(0, 0, w, h))
	x := 0
	for _, p := range parts {
		over(out, p, image.Pt(x, 0))
		x += p.Bounds().Dx()
	}
	return out
}

// vcat joins images top to bottom (widths must match).
func vcat(parts ...*image.NRGBA) *image.NRGBA {
	w, h := parts[0].Bounds().Dx(), 0
	for _, p := range parts {
		h += p.Bounds().Dy()
	}
	out := image.NewNRGBA(image.Rect(0, 0, w, h))
	y := 0
	for _, p := range parts {
		over(out, p, image.Pt(0, y))
		y += p.Bounds().Dy()
	}
	return out
}

// sym builds left end | run | mirrored left end from one tile region: [x0, x1) = left end, [m0, m1) = run sample.
func sym(t *image.NRGBA, x0, x1, m0, m1, y0, y1 int) *image.NRGBA {
	l := crop(t, tileRect(x0, y0, x1, y1))
	m := crop(t, tileRect(m0, y0, m1, y1))
	return hcat(l, m, flip(l, true, false))
}

// clearInside paints the rect magenta (keyed transparent later): bar wells are open windows (trough / fill draw under).
func clearInside(img *image.NRGBA, r image.Rectangle) {
	fill(img, r.Intersect(img.Bounds()), color.NRGBA{255, 0, 255, 255})
}

func cmdPaint4(args []string) {
	if len(args) < 2 {
		must(fmt.Errorf("usage: paint4 <tile dir> <kit dir>"))
	}
	dir, kit := args[0], args[1]
	t3 := loadPNG(filepath.Join(dir, "gen-T3.png"))
	t3b := loadPNG(filepath.Join(dir, "gen-T3b.png"))
	t1a := loadPNG(filepath.Join(dir, "gen-T1a.png"))
	t1b := loadPNG(filepath.Join(dir, "gen-T1b.png"))
	t5a := loadPNG(filepath.Join(dir, "gen-T5a.png"))

	// hotbar block (cell-free plate): curled knot column left, top rail, bottom rail
	savePNG(filepath.Join(kit, "strip5_block.png"), sym(t3b, 40, 210, 700, 1000, 80, 600))
	// bar block without wells: top rail | plate | bottom rail rows, then left | run | mirror
	rows := func(x0, x1 int) *image.NRGBA {
		return vcat(crop(t3b, tileRect(x0, 595, x1, 640)), crop(t3b, tileRect(x0, 785, x1, 845)), crop(t3b, tileRect(x0, 975, x1, 1020)))
	}
	bl := rows(40, 160)
	savePNG(filepath.Join(kit, "strip5_bars.png"), hcat(bl, rows(700, 1000), flip(bl, true, false)))
	// wells (rounded ends fixed, run sampled), interiors cleared
	well := func(y0, y1 int) *image.NRGBA {
		w := sym(t3b, 45, 145, 700, 900, y0, y1)
		const rim = 20
		clearInside(w, image.Rect(rim+18, rim, w.Bounds().Dx()-rim-18, w.Bounds().Dy()-rim))
		return w
	}
	savePNG(filepath.Join(kit, "strip5_well_hp.png"), well(632, 782))
	savePNG(filepath.Join(kit, "strip5_well_row.png"), well(848, 972))
	// wing plate (left end-cap knot fixed, right edge tucks under the bar block), medallion, cell, sockets
	savePNG(filepath.Join(kit, "strip5_wing.png"), crop(t1b, tileRect(90, 560, 1000, 1015)))
	savePNG(filepath.Join(kit, "strip5_medallion.png"), crop(t1b, tileRect(880, 220, 1475, 630)))
	savePNG(filepath.Join(kit, "strip5_cell.png"), crop(t3, tileRect(190, 215, 463, 533)))
	round := crop(t1a, tileRect(315, 638, 597, 920))
	// only the ring and its bowl: the plate around it is the wing's own (a square of plate read as a tile)
	cx, cy, rr := float64(round.Bounds().Dx())/2, float64(round.Bounds().Dy())/2, float64(round.Bounds().Dx())/2-3
	for y := 0; y < round.Bounds().Dy(); y++ {
		for x := 0; x < round.Bounds().Dx(); x++ {
			if dx, dy := float64(x)+0.5-cx, float64(y)+0.5-cy; dx*dx+dy*dy > rr*rr {
				round.SetNRGBA(x, y, color.NRGBA{255, 0, 255, 255})
			}
		}
	}
	savePNG(filepath.Join(kit, "strip5_socket_round.png"), round)
	savePNG(filepath.Join(kit, "strip5_socket_square.png"), crop(t5a, tileRect(440, 642, 697, 932)))
	fmt.Println("paint4 ok")
}
