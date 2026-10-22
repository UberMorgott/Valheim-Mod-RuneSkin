package main

import (
	"fmt"
	"image"
	"image/color"
	"math"
	"os"
	"path/filepath"
)

// compare-eb MOCKUP_ENEMY MOCKUP_BOSS BLOOD_PNG OUTDIR: static offline renders of the enemy-b tier bars and the boss-c
// world bar composed ONLY from the built sprites (Unity Sliced rules, window fitted like EnemyHudMath.Fit / BossBarHud),
// next to the matching mockup crop: OUTDIR/eb-<tier>.png, eb-tiers.png, eb-boss.png. Mechanical compositing, no drawing:
// flat rects are the sampled mockup trough / lag colours (theme tokens), the boss fill is BossAwakening's own blood
// strip (BossAwakening bar_blood_c.png, cut from the mockup fill) cropped by the fill edge.

type ebTier struct {
	name string
	crop image.Rectangle // mockup crop = the cut rect
}

var emptyPath string

var ebTiers = []ebTier{
	{"normal", image.Rect(228, 225, 228+357, 225+75)},
	{"magic", image.Rect(228, 383, 228+357, 383+82)},
	{"rare", image.Rect(225, 545, 225+365, 545+88)},
	{"unique", image.Rect(220, 703, 220+370, 703+127)},
}

func cmdCompareEB() {
	if len(os.Args) < 6 {
		fmt.Fprintln(os.Stderr, "usage: compare-eb MOCKUP_ENEMY MOCKUP_BOSS BLOOD_PNG OUTDIR")
		os.Exit(2)
	}
	loadSprites()
	mockE, mockB, blood, out := loadPNG(os.Args[2]), loadPNG(os.Args[3]), loadPNG(os.Args[4]), os.Args[5]
	emptyPath = filepath.Join(filepath.Dir(os.Args[4]), "bar_empty_c.png")
	must(os.MkdirAll(out, 0o755))
	bg := color.NRGBA{34, 40, 36, 255}
	var rows []*image.NRGBA
	for _, t := range ebTiers {
		ours := renderEnemyBar("frame_enemy_"+t.name, 0.6, 0.05, bg)
		mock := crop(mockE, t.crop)
		row := ebPair(mock, ours, 130)
		savePNG(filepath.Join(out, "eb-"+t.name+".png"), row)
		rows = append(rows, row)
	}
	savePNG(filepath.Join(out, "eb-tiers.png"), ebStack(rows))
	boss := renderBossBar(blood, 0.7, bg)
	savePNG(filepath.Join(out, "eb-boss.png"), ebStack([]*image.NRGBA{ebFit(crop(mockB, image.Rect(30, 60, 1645, 235)), 1600), ebFit(boss, 1600)}))
	fmt.Println("wrote", out)
}

// renderEnemyBar: window = the frame's own window height (Sliced borders at native size = window fitted), 96:10.
func renderEnemyBar(frame string, hp, ghost float64, bg color.NRGBA) *image.NRGBA {
	s := sprites[frame]
	H := s.img.Bounds().Dy()
	ch := H - s.b.Top - s.b.Bottom
	ww := int(math.Round(float64(ch) * 8.4)) // mockup windows 244 x 28 .. 223 x 31 (mean 8.4:1) = size.enemy.bar.w / .h
	pad := 8
	W := s.b.Left + ww + s.b.Right
	img := image.NewNRGBA(image.Rect(0, 0, W+2*pad, H+2*pad))
	fill(img, img.Bounds(), bg)
	win := image.Rect(pad+s.b.Left, pad+s.b.Top, pad+s.b.Left+ww, pad+s.b.Top+ch)
	over(img, resize(sprites["bar_enemy_trough_b"].img, ww, ch), win.Min)
	fw := int(float64(ww) * hp)
	gw := int(float64(ww) * (hp + ghost))
	over(img, resize(sprites["bar_enemy_lag_b"].img, gw, ch), win.Min)
	f := sprites["bar_enemy_fill_b"].img
	over(img, resize(f, fw, ch), win.Min)
	c := &canvas{img: img, K: 1}
	c.draw9(frame, image.Rect(pad, pad, pad+W, pad+H), color.NRGBA{})
	return img
}

// renderBossBar: boss-c proportions in reference units of BossBarHud (window 20 high, 28.6:1 like the mockup's
// 1146 x 40 window), frame window fitted, caps / plaque / markers at the theme ratios (size.boss.*).
func renderBossBar(blood *image.NRGBA, hp float64, bg color.NRGBA) *image.NRGBA {
	const bh = 40.0 // bar (window) height in output px
	ww := bh * 28.6
	capH, fangH := bh*2.6, bh*1.85 // mockup cap 104 / fang 73 px over a 40 px window
	fr := sprites["boss_frame"]
	k := bh / float64(fr.img.Bounds().Dy()-fr.b.Top-fr.b.Bottom) // frame px -> output px
	capS := sprites["boss_cap"]
	capW := capH * float64(capS.img.Bounds().Dx()) / float64(capS.img.Bounds().Dy())
	W := int(ww + 2*capW + 40)
	Hh := int(capH + bh*2.45 + 40)
	img := image.NewNRGBA(image.Rect(0, 0, W, Hh))
	fill(img, img.Bounds(), bg)
	cy := float64(Hh) - capH/2 - 10
	x0 := (float64(W) - ww) / 2
	win := image.Rect(int(x0), int(cy-bh/2), int(x0+ww), int(cy+bh/2))
	// empty trough: BossAwakening bar_empty_c strip (boss-c trough cut, BossBarHud empty trough) over its window
	if e, err := os.Stat(emptyPath); err == nil && !e.IsDir() {
		em := loadPNG(emptyPath)
		ew, eh := em.Bounds().Dx(), em.Bounds().Dy()
		over(img, resize(crop(em, image.Rect(int(0.09*float64(ew)), int(0.325*float64(eh)), int(0.91*float64(ew)), int(0.675*float64(eh)))), win.Dx(), win.Dy()), win.Min)
	} else {
		fill(img, win, hex("#070E11"))
	}
	// blood: the strip's window (0.82 x 0.35 of it, BossBarHud.WindowU/V) cropped by the fill edge, gain applied
	bw, bhh := blood.Bounds().Dx(), blood.Bounds().Dy()
	strip := crop(blood, image.Rect(int(0.09*float64(bw)), int(0.325*float64(bhh)), int((0.09+0.82*hp)*float64(bw)), int((0.325+0.35)*float64(bhh))))
	over(img, resize(strip, int(ww*hp), win.Dy()), win.Min)
	// frame, window fitted: borders at k
	scaled := resize(fr.img, int(float64(fr.img.Bounds().Dx())*k), int(float64(fr.img.Bounds().Dy())*k))
	tmp := sprites["boss_frame"]
	sprites["boss_frame__k"] = spr{scaled, Border{Left: int(float64(tmp.b.Left) * k), Bottom: int(float64(tmp.b.Bottom) * k), Right: int(float64(tmp.b.Right) * k), Top: int(float64(tmp.b.Top) * k)}, tmp.typ, tmp.fill, tmp.piv}
	c := &canvas{img: img, K: 1}
	ft, fb := float64(tmp.b.Top)*k, float64(tmp.b.Bottom)*k
	c.draw9("boss_frame__k", image.Rect(win.Min.X-int(capW*0.3), int(float64(win.Min.Y)-ft), win.Max.X+int(capW*0.3), int(float64(win.Max.Y)+fb)), color.NRGBA{})
	// fangs at 1/3, 2/3 (pivot 0.5, 0.72 at the bar top)
	m := sprites["boss_marker"].img
	mw := fangH * 0.32 // mockup fang 22 x 73 px (share.boss.marker.aspect)
	for i := 1; i < 3; i++ {
		fx := float64(win.Min.X) + ww*float64(i)/3
		over(img, resize(m, int(mw), int(fangH)), image.Pt(int(fx-mw/2), int(float64(win.Min.Y)-fangH*0.11)))
	}
	// caps: right edge on the bar start, the right one mirrored
	cp := resize(capS.img, int(capW), int(capH))
	over(img, cp, image.Pt(win.Min.X-int(capW)+int(ft), int(cy-capH/2)))
	over(img, flip(cp, true, false), image.Pt(win.Max.X-int(ft), int(cy-capH/2)))
	// plaque (Simple, native aspect, 98 / 40 of the window like the mockup) with its bottom on the window top; the name
	// centred in the field rect (theme rect.boss.plaque.field)
	pl := sprites["boss_plaque"].img
	ph := bh * 98 / 40
	pw := ph * float64(pl.Bounds().Dx()) / float64(pl.Bounds().Dy())
	pr := image.Rect(W/2-int(pw/2), win.Min.Y-int(ph), W/2+int(pw/2), win.Min.Y)
	over(img, resize(pl, pr.Dx(), pr.Dy()), pr.Min)
	cyName := float64(pr.Max.Y) - ph*(0.30+0.75)/2
	c.text("T H E   E L D E R", serif, ph*0.36, "#0B0F12", float64(W)/2+2, cyName+2, 1)
	c.text("T H E   E L D E R", serif, ph*0.36, "#D9DCE0", float64(W)/2, cyName, 1)
	return img
}

func ebFit(img *image.NRGBA, w int) *image.NRGBA {
	h := int(float64(img.Bounds().Dy()) * float64(w) / float64(img.Bounds().Dx()))
	return resize(img, w, h)
}

// ebPair: [mockup | ours], both scaled to height h, on dark grey.
func ebPair(a, b *image.NRGBA, h int) *image.NRGBA {
	aw := a.Bounds().Dx() * h / a.Bounds().Dy()
	bw := b.Bounds().Dx() * h / b.Bounds().Dy()
	img := image.NewNRGBA(image.Rect(0, 0, aw+bw+30, h))
	fill(img, img.Bounds(), color.NRGBA{30, 30, 30, 255})
	over(img, resize(a, aw, h), image.Point{})
	over(img, resize(b, bw, h), image.Pt(aw+30, 0))
	return img
}

func ebStack(rows []*image.NRGBA) *image.NRGBA {
	w, h := 0, 0
	for _, r := range rows {
		w = max(w, r.Bounds().Dx())
		h += r.Bounds().Dy() + 8
	}
	img := image.NewNRGBA(image.Rect(0, 0, w, h))
	fill(img, img.Bounds(), color.NRGBA{30, 30, 30, 255})
	y := 0
	for _, r := range rows {
		over(img, r, image.Pt(0, y))
		y += r.Bounds().Dy() + 8
	}
	return img
}

// strip IN x,y,w,h OUT: BossAwakening fill strip (1536 x 128, window 0.82 x 0.35 at 0.09 / 0.325, BossBarHud.Window*/Base*)
// from a clean mockup fill crop: scaled to the window height, mirror-tiled across the width (no seam: every joint meets
// its own mirror), rows above / below the window repeat the edge rows (the strip drifts +-0.012 v). Mechanical only.
func cmdStrip() {
	src := crop(loadPNG(os.Args[2]), parseRect(os.Args[3]))
	// args: SRC RECT OUT [H]: H = strip height (128 for the original Vaettir strips, 512 for the HD boss-c fills)
	W, H := 1536, 128
	if len(os.Args) > 5 {
		H = parseInts(os.Args[5])[0]
	}
	y0, wh := int(math.Round(0.325*float64(H))), int(math.Round(0.35*float64(H)))
	seg := resize(src, int(math.Round(float64(src.Bounds().Dx())*float64(wh)/float64(src.Bounds().Dy()))), wh)
	mir := flip(seg, true, false)
	out := image.NewNRGBA(image.Rect(0, 0, W, H))
	for x, i := 0, 0; x < W; x, i = x+seg.Bounds().Dx(), i+1 {
		p := seg
		if i%2 == 1 {
			p = mir
		}
		over(out, p, image.Pt(x, y0))
	}
	for y := 0; y < H; y++ {
		sy := min(max(y, y0), y0+wh-1)
		if sy == y {
			continue
		}
		copy(out.Pix[y*out.Stride:(y+1)*out.Stride], out.Pix[sy*out.Stride:(sy+1)*out.Stride])
	}
	savePNG(os.Args[4], out)
	fmt.Println("wrote", os.Args[4])
}

// cmdRowMatch: SRC RECT MOCK MOCKRECT OUT. Shifts each row of the SRC crop so its mean colour follows the MOCK crop's
// row profile at the same relative height (the Codex fill keeps its texture, the shading becomes the mockup's: an even
// glossy cylinder instead of a wide highlight band). Writes the corrected crop.
func cmdRowMatch() {
	src := crop(loadPNG(os.Args[2]), parseRect(os.Args[3]))
	mock := crop(loadPNG(os.Args[4]), parseRect(os.Args[5]))
	mean := func(img *image.NRGBA, y int) [3]float64 {
		b := img.Bounds()
		var s [3]float64
		for x := b.Min.X; x < b.Max.X; x++ {
			i := img.PixOffset(x, y)
			s[0] += float64(img.Pix[i])
			s[1] += float64(img.Pix[i+1])
			s[2] += float64(img.Pix[i+2])
		}
		n := float64(b.Dx())
		return [3]float64{s[0] / n, s[1] / n, s[2] / n}
	}
	sb, mb := src.Bounds(), mock.Bounds()
	for y := sb.Min.Y; y < sb.Max.Y; y++ {
		t := (float64(y-sb.Min.Y) + 0.5) / float64(sb.Dy())
		my := mb.Min.Y + min(mb.Dy()-1, int(t*float64(mb.Dy())))
		want, have := mean(mock, my), mean(src, y)
		for x := sb.Min.X; x < sb.Max.X; x++ {
			i := src.PixOffset(x, y)
			for k := 0; k < 3; k++ {
				v := float64(src.Pix[i+k]) - have[k] + want[k]
				src.Pix[i+k] = uint8(math.Max(0, math.Min(255, v)))
			}
		}
	}
	savePNG(os.Args[6], src)
	fmt.Println("wrote", os.Args[6])
}

// cmdMetalMatch: SRC MOCK MOCKRECT OUT [GAIN]. Removes the colour cast Codex puts on dark metal: the mean colour of SRC's
// dark pixels (luma < 0.35, not magenta / transparent) is moved to the mean of the mockup crop's dark pixels, weighted
// toward the darkest pixels (gold trim, glowing runes and highlights keep their colour). GAIN (default 1) scales the
// overall darkness toward the mockup's too.
func cmdMetalMatch() {
	src := loadPNG(os.Args[2])
	mock := crop(loadPNG(os.Args[3]), parseRect(os.Args[4]))
	luma := func(r, g, b float64) float64 { return 0.2126*r + 0.7152*g + 0.0722*b }
	isKey := func(r, g, b float64, a uint8) bool { return a < 128 || (r > 0.75 && b > 0.75 && g < 0.35) }
	mean := func(img *image.NRGBA) [3]float64 {
		var s [3]float64
		n := 0.0
		for i := 0; i < len(img.Pix); i += 4 {
			r, g, b := float64(img.Pix[i])/255, float64(img.Pix[i+1])/255, float64(img.Pix[i+2])/255
			if isKey(r, g, b, img.Pix[i+3]) || luma(r, g, b) >= 0.35 || r-b > 0.08 {
				continue
			}
			s[0] += r
			s[1] += g
			s[2] += b
			n++
		}
		return [3]float64{s[0] / n, s[1] / n, s[2] / n}
	}
	want, have := mean(mock), mean(src)
	gain := 1.0
	if len(os.Args) > 6 {
		if _, err := fmt.Sscanf(os.Args[6], "%g", &gain); err != nil {
			gain = 1
		}
	}
	// the mockup metal's tint (chroma per unit luma) replaces the source's on cool, non-glowing pixels
	wl := luma(want[0], want[1], want[2])
	tint := [3]float64{want[0] / wl, want[1] / wl, want[2] / wl}
	for i := 0; i < len(src.Pix); i += 4 {
		r, g, b := float64(src.Pix[i])/255, float64(src.Pix[i+1])/255, float64(src.Pix[i+2])/255
		if isKey(r, g, b, src.Pix[i+3]) {
			continue
		}
		l := luma(r, g, b)
		warm := clamp01((r - b - 0.05) / 0.15)
		bright := clamp01((l - 0.45) / 0.2) // highlights and the glowing runes stay
		w := (1 - warm) * (1 - bright)
		c := [3]float64{r, g, b}
		for k := 0; k < 3; k++ {
			target := l * tint[k] * gain
			v := c[k] + (target-c[k])*w
			src.Pix[i+k] = uint8(math.Max(0, math.Min(255, v*255)))
		}
	}
	savePNG(os.Args[5], src)
	fmt.Printf("wrote %s (dark mean %.3f,%.3f,%.3f -> %.3f,%.3f,%.3f)\n", os.Args[5], have[0], have[1], have[2], want[0], want[1], want[2])
}

// cmdPairDiff: MOCK OURS OUT. Same-size same-scale crops -> SSIM (luma, 8x8 windows) and CIE76 dE (Lab) stats, and a
// heatmap OUT (left: dE per pixel, 0 black .. 30+ red/yellow; right: 1-SSIM per window).
func cmdPairDiff() {
	a, b := loadPNG(os.Args[2]), loadPNG(os.Args[3])
	w, h := min(a.Bounds().Dx(), b.Bounds().Dx()), min(a.Bounds().Dy(), b.Bounds().Dy())
	lab := func(img *image.NRGBA, x, y int) (float64, float64, float64, float64) {
		i := img.PixOffset(img.Bounds().Min.X+x, img.Bounds().Min.Y+y)
		lin := func(v uint8) float64 {
			c := float64(v) / 255
			if c <= 0.04045 {
				return c / 12.92
			}
			return math.Pow((c+0.055)/1.055, 2.4)
		}
		r, g, bl := lin(img.Pix[i]), lin(img.Pix[i+1]), lin(img.Pix[i+2])
		X := (0.4124*r + 0.3576*g + 0.1805*bl) / 0.95047
		Y := 0.2126*r + 0.7152*g + 0.0722*bl
		Z := (0.0193*r + 0.1192*g + 0.9505*bl) / 1.08883
		f := func(t float64) float64 {
			if t > 0.008856 {
				return math.Cbrt(t)
			}
			return 7.787*t + 16.0/116
		}
		L := 116*f(Y) - 16
		return L, 500 * (f(X) - f(Y)), 200 * (f(Y) - f(Z)), Y
	}
	out := image.NewNRGBA(image.Rect(0, 0, w*2+8, h))
	heat := func(t float64) color.NRGBA {
		t = clamp01(t)
		return color.NRGBA{uint8(255 * clamp01(t*2)), uint8(255 * clamp01(t*2-1)), 0, 255}
	}
	sumDE := 0.0
	for y := 0; y < h; y++ {
		for x := 0; x < w; x++ {
			L1, a1, b1, _ := lab(a, x, y)
			L2, a2, b2, _ := lab(b, x, y)
			de := math.Sqrt((L1-L2)*(L1-L2) + (a1-a2)*(a1-a2) + (b1-b2)*(b1-b2))
			sumDE += de
			out.SetNRGBA(x, y, heat(de/30))
		}
	}
	const win = 8
	ssimSum, n := 0.0, 0
	for y := 0; y+win <= h; y += win {
		for x := 0; x+win <= w; x += win {
			var ma, mb, va, vb, cov float64
			for j := 0; j < win; j++ {
				for i := 0; i < win; i++ {
					_, _, _, ya := lab(a, x+i, y+j)
					_, _, _, yb := lab(b, x+i, y+j)
					ma += ya
					mb += yb
				}
			}
			ma /= win * win
			mb /= win * win
			for j := 0; j < win; j++ {
				for i := 0; i < win; i++ {
					_, _, _, ya := lab(a, x+i, y+j)
					_, _, _, yb := lab(b, x+i, y+j)
					va += (ya - ma) * (ya - ma)
					vb += (yb - mb) * (yb - mb)
					cov += (ya - ma) * (yb - mb)
				}
			}
			va /= win * win
			vb /= win * win
			cov /= win * win
			const c1, c2 = 0.0001, 0.0009
			s := ((2*ma*mb + c1) * (2*cov + c2)) / ((ma*ma + mb*mb + c1) * (va + vb + c2))
			ssimSum += s
			n++
			for j := 0; j < win; j++ {
				for i := 0; i < win; i++ {
					out.SetNRGBA(w+8+x+i, y+j, heat(1-s))
				}
			}
		}
	}
	savePNG(os.Args[4], out)
	fmt.Printf("SSIM %.3f  mean dE %.1f  %s\n", ssimSum/float64(max(1, n)), sumDE/float64(w*h), os.Args[4])
}

// cmdSymCheck: SRC [THRESH]. Rotational symmetry of a glow glyph on black: mask = luma > THRESH (default 0.5), centroid,
// then IoU of the mask with itself rotated 120 and 240 degrees about the centroid, and the centroid's offset from the
// canvas centre. A clean triquetra gives IoU close to 1.
func cmdSymCheck() {
	img := loadPNG(os.Args[2])
	th := 0.5
	if len(os.Args) > 3 {
		if _, err := fmt.Sscanf(os.Args[3], "%g", &th); err != nil {
			th = 0.5
		}
	}
	b := img.Bounds()
	W, H := b.Dx(), b.Dy()
	mask := make([]bool, W*H)
	var cx, cy, n float64
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			i := img.PixOffset(b.Min.X+x, b.Min.Y+y)
			l := (0.2126*float64(img.Pix[i]) + 0.7152*float64(img.Pix[i+1]) + 0.0722*float64(img.Pix[i+2])) / 255
			if l > th {
				mask[y*W+x] = true
				cx += float64(x)
				cy += float64(y)
				n++
			}
		}
	}
	cx /= n
	cy /= n
	iou := func(deg float64) float64 {
		s, c := math.Sincos(deg * math.Pi / 180)
		inter, union := 0, 0
		for y := 0; y < H; y++ {
			for x := 0; x < W; x++ {
				dx, dy := float64(x)-cx, float64(y)-cy
				rx, ry := int(math.Round(cx+dx*c-dy*s)), int(math.Round(cy+dx*s+dy*c))
				r := rx >= 0 && ry >= 0 && rx < W && ry < H && mask[ry*W+rx]
				m := mask[y*W+x]
				if m && r {
					inter++
				}
				if m || r {
					union++
				}
			}
		}
		return float64(inter) / float64(max(1, union))
	}
	fmt.Printf("centroid %.1f,%.1f (canvas centre %.1f,%.1f, off %.1f px)  IoU120 %.3f  IoU240 %.3f\n", cx, cy, float64(W)/2, float64(H)/2,
		math.Hypot(cx-float64(W)/2, cy-float64(H)/2), iou(120), iou(240))
}

// cmdEmblem: BASE SOCKET OX,OY GLYPH GCX,GCY SCALE DCX,DCY OUT. Mechanical composite: the SOCKET image (magenta keyed) is
// pasted over BASE at OX,OY; then GLYPH (glow on black) is scaled by SCALE and added (light) so its point GCX,GCY lands on
// DCX,DCY of the socket image. Used to put the Codex-drawn symmetric triquetra into the Codex-drawn empty magic socket.
func cmdEmblem() {
	base := loadPNG(os.Args[2])
	sock := loadPNG(os.Args[3])
	keyMagenta(sock)
	o := parseInts(os.Args[4])
	glyph := loadPNG(os.Args[5])
	g := parseInts(os.Args[6])
	var sc float64
	if _, err := fmt.Sscanf(os.Args[7], "%g", &sc); err != nil {
		fmt.Println("bad scale")
		os.Exit(1)
	}
	d := parseInts(os.Args[8])
	over(base, sock, image.Pt(o[0], o[1]))
	gs := resize(glyph, int(math.Round(float64(glyph.Bounds().Dx())*sc)), int(math.Round(float64(glyph.Bounds().Dy())*sc)))
	px := o[0] + d[0] - int(math.Round(float64(g[0])*sc))
	py := o[1] + d[1] - int(math.Round(float64(g[1])*sc))
	for y := 0; y < gs.Bounds().Dy(); y++ {
		for x := 0; x < gs.Bounds().Dx(); x++ {
			bx, by := px+x, py+y
			if !image.Pt(bx, by).In(base.Bounds()) {
				continue
			}
			si, bi := gs.PixOffset(x, y), base.PixOffset(bx, by)
			if base.Pix[bi+3] == 0 {
				continue
			}
			for k := 0; k < 3; k++ {
				base.Pix[bi+k] = uint8(min(255, int(base.Pix[bi+k])+int(gs.Pix[si+k])))
			}
		}
	}
	savePNG(os.Args[9], base)
	fmt.Println("wrote", os.Args[9])
}
