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
	const W, H = 1536, 128
	y0, wh := int(math.Round(0.325*H)), int(math.Round(0.35*H))
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
