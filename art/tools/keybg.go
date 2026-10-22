package main

import (
	"image"
	"math"
)

// keyBackground keys out a smooth (blurred) mockup background around crisp UI art: a flood fill from the
// crop's border pixels on the listed sides ("t", "l", "r", "b") that spreads to a neighbour while the colour
// step between them stays below tol (0..1, RGB distance / sqrt 3). The blurred scene changes slowly, the UI
// silhouette has a hard edge, so the fill stops there. Flooded pixels become transparent; UI pixels touching
// them keep 60 % alpha (1 px feather, softened further by the later downscale). Mechanical only: no pixel of
// the art is drawn or changed, only its alpha.
func keyBackground(img *image.NRGBA, tol float64, sides string) {
	if tol <= 0 {
		tol = 0.035
	}
	if sides == "" {
		sides = "tlrb"
	}
	W, H := img.Bounds().Dx(), img.Bounds().Dy()
	bg := make([]bool, W*H)
	queue := make([]int, 0, W*H/4)
	push := func(x, y int) {
		i := y*W + x
		if !bg[i] {
			bg[i] = true
			queue = append(queue, i)
		}
	}
	has := func(s byte) bool {
		for i := 0; i < len(sides); i++ {
			if sides[i] == s {
				return true
			}
		}
		return false
	}
	for x := 0; x < W; x++ {
		if has('t') {
			push(x, 0)
		}
		if has('b') {
			push(x, H-1)
		}
	}
	for y := 0; y < H; y++ {
		if has('l') {
			push(0, y)
		}
		if has('r') {
			push(W-1, y)
		}
	}
	dist := func(a, b int) float64 {
		pa, pb := img.PixOffset(a%W, a/W), img.PixOffset(b%W, b/W)
		dr := float64(img.Pix[pa]) - float64(img.Pix[pb])
		dg := float64(img.Pix[pa+1]) - float64(img.Pix[pb+1])
		db := float64(img.Pix[pa+2]) - float64(img.Pix[pb+2])
		return math.Sqrt(dr*dr+dg*dg+db*db) / (255 * math.Sqrt(3))
	}
	for len(queue) > 0 {
		i := queue[len(queue)-1]
		queue = queue[:len(queue)-1]
		x, y := i%W, i/W
		for _, d := range [][2]int{{1, 0}, {-1, 0}, {0, 1}, {0, -1}} {
			nx, ny := x+d[0], y+d[1]
			if nx < 0 || ny < 0 || nx >= W || ny >= H {
				continue
			}
			j := ny*W + nx
			if !bg[j] && dist(i, j) < tol {
				bg[j] = true
				queue = append(queue, j)
			}
		}
	}
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			i := y*W + x
			p := img.PixOffset(x, y)
			if bg[i] {
				img.Pix[p+3] = 0
				continue
			}
			edge := false
			for _, d := range [][2]int{{1, 0}, {-1, 0}, {0, 1}, {0, -1}} {
				nx, ny := x+d[0], y+d[1]
				if nx >= 0 && ny >= 0 && nx < W && ny < H && bg[ny*W+nx] {
					edge = true
					break
				}
			}
			if edge {
				img.Pix[p+3] = uint8(float64(img.Pix[p+3]) * 0.6)
			}
		}
	}
}

// clearRects makes crop-space rects fully transparent (bar windows cut out of a mockup crop: the live fill shows
// through).
func clearRects(img *image.NRGBA, rects [][4]int) {
	for _, r := range rects {
		w := image.Rect(r[0], r[1], r[0]+r[2], r[1]+r[3]).Intersect(img.Bounds())
		for y := w.Min.Y; y < w.Max.Y; y++ {
			for x := w.Min.X; x < w.Max.X; x++ {
				img.Pix[img.PixOffset(x, y)+3] = 0
			}
		}
	}
}

// clearDark makes the dark pixels (brightest channel <= max) of crop-space rects transparent: the glass inside an
// ornament corner goes, the lighter metal stays.
func clearDark(img *image.NRGBA, rects [][5]int) {
	for _, r := range rects {
		w := image.Rect(r[0], r[1], r[0]+r[2], r[1]+r[3]).Intersect(img.Bounds())
		for y := w.Min.Y; y < w.Max.Y; y++ {
			for x := w.Min.X; x < w.Max.X; x++ {
				i := img.PixOffset(x, y)
				if int(max(img.Pix[i], img.Pix[i+1], img.Pix[i+2])) <= r[4] {
					img.Pix[i+3] = 0
				}
			}
		}
	}
}

// keyRed clears saturated HP-fill red pixels (R > 110, G < 60, B < 60) with their soft fringe (R > 2.2 x max(G, B)).
func keyRed(img *image.NRGBA) {
	for i := 0; i < len(img.Pix); i += 4 {
		r, g, b := float64(img.Pix[i]), float64(img.Pix[i+1]), float64(img.Pix[i+2])
		if (r > 110 && g < 60 && b < 60) || (r > 70 && r > 2.2*math.Max(g, b)) {
			img.Pix[i+3] = 0
		}
	}
}

// cloneColumns fills rect (x, y, w, h) with the source columns srcX..srcX+srcW of the same rows, mirror-tiled (every
// joint meets its own mirror image): a baked caption on a stone field replaced by that field's own texture.
func cloneColumns(img *image.NRGBA, a []int) {
	x0, y0, w, h, sx, sw := a[0], a[1], a[2], a[3], a[4], a[5]
	for y := y0; y < y0+h; y++ {
		for x := x0; x < x0+w; x++ {
			k := (x - x0) % (2 * sw)
			if k >= sw {
				k = 2*sw - 1 - k
			}
			s, d := img.PixOffset(sx+k, y), img.PixOffset(x, y)
			copy(img.Pix[d:d+4], img.Pix[s:s+4])
		}
	}
}
