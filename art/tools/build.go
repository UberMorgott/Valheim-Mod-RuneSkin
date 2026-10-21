package main

import (
	"encoding/json"
	"fmt"
	"image"
	"image/color"
	"math"
	"os"
	"path/filepath"
	"sort"
)

// Repo-relative paths (run from the repo root).
const (
	kitDir     = "art/src/kit"
	spritesDir = "art/sprites"
)

type Border struct {
	Left   int `json:"left"`
	Bottom int `json:"bottom"`
	Right  int `json:"right"`
	Top    int `json:"top"`
}

type Nine struct {
	Src    Border `json:"src"`    // borders measured on the scaled crop (sprite px)
	Center [2]int `json:"center"` // centre segment size in the output
	Tile   bool   `json:"tile"`   // make edge/centre segments periodic (cross-fade)
}

type Cut struct {
	Name   string     `json:"name"`
	Sheet  string     `json:"sheet"`
	Rect   [4]int     `json:"rect"` // x,y,w,h on the sheet
	Key    string     `json:"key"`  // magenta | luma | none
	Luma   [2]float64 `json:"luma,omitempty"`
	Trim   bool       `json:"trim,omitempty"` // crop to alpha bbox after keying
	Pad    int        `json:"pad,omitempty"`  // transparent padding after trim (output px)
	Scale  float64    `json:"scale,omitempty"`
	Size   [2]int     `json:"size,omitempty"` // final size (non-nine)
	Nine   *Nine      `json:"nine,omitempty"`
	CellH  int        `json:"cellH,omitempty"`  // period cell height (non-uniform resize)
	Period int        `json:"period,omitempty"` // horizontal repeat: crop is one period, output = Size
	Alpha  float64    `json:"alpha,omitempty"`  // alpha multiplier
	Gray   float64    `json:"gray,omitempty"`   // desaturate amount 0..1
	Bright float64    `json:"bright,omitempty"` // brightness multiplier
	// Interior clear: inside this output rect, pixels that are not leather become transparent
	// (corner overlays sit over the translucent glass).
	ClearInterior []int `json:"keepBlock,omitempty"`
	OnlyCyan      bool  `json:"onlyCyan,omitempty"` // emissive extraction: keep cyan glow as white mask
	// ClearWindow: output rect made transparent except leather (bar windows: fill shows through, strap stays on top)
	ClearWindow []int      `json:"clearWindow,omitempty"`
	Leather     [2]float64 `json:"leather,omitempty"` // leather-only grade: saturation keep, brightness
	Fade        [2]int     `json:"fade,omitempty"`    // alpha ramp at the left/right ends (px)
	Flip        string     `json:"flip,omitempty"`    // mirror the crop: h | v | hv
	Border      Border     `json:"border"`
	Pivot       [2]float64 `json:"pivot"`
	ImageType   string     `json:"imageType"`
	FillCenter  bool       `json:"fillCenter"`
	Token       string     `json:"token"`
	Notes       string     `json:"notes"`
}

type Spec struct {
	Palette map[string]string `json:"palette"`
	Sprites []Cut             `json:"sprites"`
}

type OutSprite struct {
	Name       string     `json:"name"`
	File       string     `json:"file"`
	Size       [2]int     `json:"size"`
	Border     Border     `json:"border"`
	Pivot      [2]float64 `json:"pivot"`
	ImageType  string     `json:"imageType"`
	FillCenter bool       `json:"fillCenter"`
	Token      string     `json:"token"`
	Notes      string     `json:"notes"`
}

type Manifest struct {
	Version       int               `json:"version"`
	AuthoredScale int               `json:"authoredScale"`
	PixelsPerUnit int               `json:"pixelsPerUnit"`
	BorderOrder   string            `json:"borderOrder"`
	Source        string            `json:"source"`
	Palette       map[string]string `json:"palette"`
	Sprites       []OutSprite       `json:"sprites"`
}

func loadSpec() Spec {
	b, err := os.ReadFile(filepath.Join(kitDir, "cuts.json"))
	must(err)
	var s Spec
	must(json.Unmarshal(b, &s))
	return s
}

var sheetCache = map[string]*image.NRGBA{}

func sheet(name string) *image.NRGBA {
	if s, ok := sheetCache[name]; ok {
		return s
	}
	s := loadPNG(filepath.Join(kitDir, name))
	sheetCache[name] = s
	return s
}

func renderCut(c Cut) *image.NRGBA {
	src := crop(sheet(c.Sheet), image.Rect(c.Rect[0], c.Rect[1], c.Rect[0]+c.Rect[2], c.Rect[1]+c.Rect[3]))
	switch c.Key {
	case "magenta":
		keyMagenta(src)
	case "shade":
		shadeMask(src)
	case "luma":
		bw := c.Luma
		if bw[1] == 0 {
			bw = [2]float64{0.04, 0.95}
		}
		lumaMask(src, bw[0], bw[1])
	}
	switch c.Flip {
	case "v":
		src = flip(src, false, true)
	case "h":
		src = flip(src, true, false)
	case "hv":
		src = flip(src, true, true)
	}
	if c.OnlyCyan {
		cyanMask(src)
	}
	if c.Trim {
		bb := alphaBBox(src, 8)
		src = crop(src, bb)
	}
	img := src
	if c.Scale > 0 && c.Scale != 1 {
		img = resize(img, int(math.Round(float64(img.Bounds().Dx())*c.Scale)), int(math.Round(float64(img.Bounds().Dy())*c.Scale)))
	}
	switch {
	case c.Nine != nil:
		img = nineRebuild(img, *c.Nine)
	case c.Period > 0:
		// crop = exactly one period; keep its aspect, centre it vertically
		ch := int(math.Round(float64(img.Bounds().Dy()) * float64(c.Period) / float64(img.Bounds().Dx())))
		if c.CellH > 0 {
			ch = c.CellH
		}
		cell := resize(img, c.Period, min(ch, c.Size[1]))
		out := image.NewNRGBA(image.Rect(0, 0, c.Size[0], c.Size[1]))
		for x := 0; x < c.Size[0]; x += c.Period {
			over(out, cell, image.Pt(x, (c.Size[1]-cell.Bounds().Dy())/2))
		}
		img = out
	case c.Size[0] > 0:
		if c.Pad > 0 {
			// fit inside the padded box, aspect kept, centred
			bw, bh := float64(c.Size[0]-2*c.Pad), float64(c.Size[1]-2*c.Pad)
			f := math.Min(bw/float64(img.Bounds().Dx()), bh/float64(img.Bounds().Dy()))
			inner := resize(img, int(math.Round(float64(img.Bounds().Dx())*f)), int(math.Round(float64(img.Bounds().Dy())*f)))
			out := image.NewNRGBA(image.Rect(0, 0, c.Size[0], c.Size[1]))
			over(out, inner, image.Pt((c.Size[0]-inner.Bounds().Dx())/2, (c.Size[1]-inner.Bounds().Dy())/2))
			img = out
		} else {
			img = resize(img, c.Size[0], c.Size[1])
		}
	}
	if c.ClearInterior != nil {
		clearInterior(img, image.Rect(c.ClearInterior[0], c.ClearInterior[1], c.ClearInterior[0]+c.ClearInterior[2], c.ClearInterior[1]+c.ClearInterior[3]))
	}
	if c.ClearWindow != nil {
		w := image.Rect(c.ClearWindow[0], c.ClearWindow[1], c.ClearWindow[0]+c.ClearWindow[2], c.ClearWindow[1]+c.ClearWindow[3]).Intersect(img.Bounds())
		for y := w.Min.Y; y < w.Max.Y; y++ {
			for x := w.Min.X; x < w.Max.X; x++ {
				i := img.PixOffset(x, y)
				if !isLeather(img.Pix[i], img.Pix[i+1], img.Pix[i+2]) {
					img.Pix[i+3] = 0
				}
			}
		}
	}
	if c.Leather[1] > 0 {
		for i := 0; i < len(img.Pix); i += 4 {
			if img.Pix[i+3] > 0 && isLeather(img.Pix[i], img.Pix[i+1], img.Pix[i+2]) {
				r, g, bl := float64(img.Pix[i]), float64(img.Pix[i+1]), float64(img.Pix[i+2])
				l := 0.2126*r + 0.7152*g + 0.0722*bl
				k := c.Leather[0] // saturation keep
				img.Pix[i] = uint8(math.Min(255, (l+(r-l)*k)*c.Leather[1]))
				img.Pix[i+1] = uint8(math.Min(255, (l+(g-l)*k)*c.Leather[1]))
				img.Pix[i+2] = uint8(math.Min(255, (l+(bl-l)*k)*c.Leather[1]))
			}
		}
	}
	if c.Fade[0] > 0 || c.Fade[1] > 0 {
		W := img.Bounds().Dx()
		for y := 0; y < img.Bounds().Dy(); y++ {
			for x := 0; x < W; x++ {
				f := 1.0
				if c.Fade[0] > 0 && x < c.Fade[0] {
					f = math.Min(f, (float64(x)+0.5)/float64(c.Fade[0]))
				}
				if c.Fade[1] > 0 && x >= W-c.Fade[1] {
					f = math.Min(f, (float64(W-x)-0.5)/float64(c.Fade[1]))
				}
				i := img.PixOffset(x, y)
				img.Pix[i+3] = uint8(float64(img.Pix[i+3]) * f * f * (3 - 2*f))
			}
		}
	}
	adjust(img, c.Alpha, c.Gray, c.Bright)
	return img
}

// nineRebuild keeps the four corners of the scaled source and takes the edge/centre segments from
// the middle of each edge, so the output is 9-slice-able at the requested centre size.
func nineRebuild(s *image.NRGBA, n Nine) *image.NRGBA {
	W, H := s.Bounds().Dx(), s.Bounds().Dy()
	l, r, t, b := n.Src.Left, n.Src.Right, n.Src.Top, n.Src.Bottom
	cw, ch := n.Center[0], n.Center[1]
	ow, oh := l+cw+r, t+ch+b
	out := image.NewNRGBA(image.Rect(0, 0, ow, oh))
	kx, ky := 0, 0
	if n.Tile {
		kx = min(cw, 32) / 2
		if W-l-r < cw+kx {
			kx = 0
		}
		ky = min(ch, 32) / 2
		if H-t-b < ch+ky {
			ky = 0
		}
	}
	mx := l + (W-l-r-cw-kx)/2 // start of the horizontal middle segment
	my := t + (H-t-b-ch-ky)/2
	if mx < l || my < t {
		must(fmt.Errorf("nine: source %dx%d too small for centre %dx%d", W, H, cw, ch))
	}
	cols := [][2]int{{0, l}, {mx, mx + cw}, {W - r, W}}
	rows := [][2]int{{0, t}, {my, my + ch}, {H - b, H}}
	ox := []int{0, l, l + cw}
	oy := []int{0, t, t + ch}
	for i, rr := range rows {
		for j, cc := range cols {
			if cc[1] <= cc[0] || rr[1] <= rr[0] {
				continue
			}
			ex, ey := 0, 0
			if j == 1 {
				ex = kx
			}
			if i == 1 {
				ey = ky
			}
			p := crop(s, image.Rect(cc[0], rr[0], cc[1]+ex, rr[1]+ey))
			if ex > 0 {
				p = periodicX(p, cw, ex)
			}
			if ey > 0 {
				p = periodicY(p, ch, ey)
			}
			over(out, p, image.Pt(ox[j], oy[i]))
		}
	}
	return out
}

// periodicX: src width n+k -> width n whose right edge continues into its left edge.
func periodicX(src *image.NRGBA, n, k int) *image.NRGBA {
	h := src.Bounds().Dy()
	out := crop(src, image.Rect(0, 0, n, h))
	for y := 0; y < h; y++ {
		for i := 0; i < k; i++ {
			w := float64(i) / float64(k)
			a, b := src.PixOffset(n+i, y), out.PixOffset(i, y)
			for c := 0; c < 4; c++ {
				out.Pix[b+c] = uint8(float64(src.Pix[a+c])*(1-w) + float64(out.Pix[b+c])*w + 0.5)
			}
		}
	}
	return out
}

func periodicY(src *image.NRGBA, n, k int) *image.NRGBA {
	w := src.Bounds().Dx()
	out := crop(src, image.Rect(0, 0, w, n))
	for i := 0; i < k; i++ {
		f := float64(i) / float64(k)
		for x := 0; x < w; x++ {
			a, b := src.PixOffset(x, n+i), out.PixOffset(x, i)
			for c := 0; c < 4; c++ {
				out.Pix[b+c] = uint8(float64(src.Pix[a+c])*(1-f) + float64(out.Pix[b+c])*f + 0.5)
			}
		}
	}
	return out
}

func isLeather(r, g, b uint8) bool {
	return int(r) > int(b)+18 && int(r) > int(g)+6 && r > 40
}

// clearInterior keeps the corner block rect r and the leather strap; everything else becomes transparent
// (the thin rail and glass under it come from frame_panel / fill_glass).
func clearInterior(img *image.NRGBA, r image.Rectangle) {
	b := img.Bounds()
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			if image.Pt(x, y).In(r) {
				continue
			}
			i := img.PixOffset(x, y)
			if !isLeather(img.Pix[i], img.Pix[i+1], img.Pix[i+2]) {
				img.Pix[i+3] = 0
			}
		}
	}
}

// cyanMask keeps only cyan emissive pixels as a white mask (alpha = cyan strength).
func cyanMask(img *image.NRGBA) {
	b := img.Bounds()
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			i := img.PixOffset(x, y)
			r, g, bl := float64(img.Pix[i]), float64(img.Pix[i+1]), float64(img.Pix[i+2])
			s := clamp01((math.Min(g, bl) - r - 30) / 120)
			a := s * float64(img.Pix[i+3]) / 255
			img.Pix[i], img.Pix[i+1], img.Pix[i+2], img.Pix[i+3] = 255, 255, 255, uint8(a*255)
		}
	}
}

func adjust(img *image.NRGBA, alpha, gray, bright float64) {
	if alpha == 0 && gray == 0 && bright == 0 {
		return
	}
	if alpha == 0 {
		alpha = 1
	}
	if bright == 0 {
		bright = 1
	}
	for i := 0; i < len(img.Pix); i += 4 {
		r, g, b := float64(img.Pix[i]), float64(img.Pix[i+1]), float64(img.Pix[i+2])
		l := 0.2126*r + 0.7152*g + 0.0722*b
		r, g, b = r+(l-r)*gray, g+(l-g)*gray, b+(l-b)*gray
		img.Pix[i] = uint8(math.Min(255, r*bright))
		img.Pix[i+1] = uint8(math.Min(255, g*bright))
		img.Pix[i+2] = uint8(math.Min(255, b*bright))
		img.Pix[i+3] = uint8(float64(img.Pix[i+3]) * alpha)
	}
}

func cmdBuild() {
	spec := loadSpec()
	man := Manifest{Version: 2, AuthoredScale: 2, PixelsPerUnit: 200,
		BorderOrder: "sprite px; Unity spriteBorder = (left, bottom, right, top)",
		Source:      "Codex image-gen kit sheets art/src/kit/*.png, cut by art/tools (go run ./art/tools build)",
		Palette:     spec.Palette}
	for _, c := range spec.Sprites {
		img := renderCut(c)
		file := c.Name + ".png"
		savePNG(filepath.Join(spritesDir, file), img)
		man.Sprites = append(man.Sprites, OutSprite{c.Name, file, [2]int{img.Bounds().Dx(), img.Bounds().Dy()},
			c.Border, c.Pivot, c.ImageType, c.FillCenter, c.Token, c.Notes})
	}
	b, err := json.MarshalIndent(man, "", "  ")
	must(err)
	must(os.WriteFile(filepath.Join(spritesDir, "sprites.json"), append(b, '\n'), 0o644))
	fmt.Printf("build: %d sprites -> %s\n", len(man.Sprites), spritesDir)
	cmdCheck()
}

func cmdCheck() {
	b, err := os.ReadFile(filepath.Join(spritesDir, "sprites.json"))
	must(err)
	var man Manifest
	must(json.Unmarshal(b, &man))
	bad := 0
	seen := map[string]bool{}
	for _, s := range man.Sprites {
		p := filepath.Join(spritesDir, s.File)
		if _, err := os.Stat(p); err != nil {
			fmt.Println("missing file", p)
			bad++
			continue
		}
		img := loadPNG(p)
		w, h := img.Bounds().Dx(), img.Bounds().Dy()
		if w != s.Size[0] || h != s.Size[1] {
			fmt.Printf("%s: size %dx%d != json %v\n", s.Name, w, h, s.Size)
			bad++
		}
		if s.Border.Left+s.Border.Right > w || s.Border.Top+s.Border.Bottom > h {
			fmt.Printf("%s: border %+v exceeds %dx%d\n", s.Name, s.Border, w, h)
			bad++
		}
		if seen[s.Name] {
			fmt.Println("duplicate", s.Name)
			bad++
		}
		seen[s.Name] = true
	}
	entries, _ := os.ReadDir(spritesDir)
	for _, e := range entries {
		if filepath.Ext(e.Name()) == ".png" && !seen[e.Name()[:len(e.Name())-4]] {
			fmt.Println("orphan file", e.Name())
			bad++
		}
	}
	fmt.Printf("check: %d sprites, %d problems\n", len(man.Sprites), bad)
	if bad > 0 {
		os.Exit(1)
	}
}

// cmdBlobs prints bounding boxes of isolated pieces on a keyed sheet (layout aid).
func cmdBlobs(path, key string, minArea int) {
	img := loadPNG(path)
	if key == "luma" {
		lumaMask(img, 0.06, 0.9)
	} else {
		keyMagenta(img)
	}
	W, H := img.Bounds().Dx(), img.Bounds().Dy()
	lab := make([]int32, W*H)
	var rects []image.Rectangle
	stack := []int{}
	id := int32(0)
	for p := 0; p < W*H; p++ {
		if lab[p] != 0 || img.Pix[p*4+3] < 40 {
			continue
		}
		id++
		r := image.Rect(p%W, p/W, p%W+1, p/W+1)
		n := 0
		stack = append(stack[:0], p)
		lab[p] = id
		for len(stack) > 0 {
			q := stack[len(stack)-1]
			stack = stack[:len(stack)-1]
			n++
			x, y := q%W, q/W
			r = r.Union(image.Rect(x, y, x+1, y+1))
			for _, d := range [][2]int{{1, 0}, {-1, 0}, {0, 1}, {0, -1}} {
				nx, ny := x+d[0], y+d[1]
				if nx < 0 || ny < 0 || nx >= W || ny >= H {
					continue
				}
				nq := ny*W + nx
				if lab[nq] == 0 && img.Pix[nq*4+3] >= 40 {
					lab[nq] = id
					stack = append(stack, nq)
				}
			}
		}
		if n >= minArea {
			rects = append(rects, r)
		}
	}
	sort.Slice(rects, func(i, j int) bool {
		if rects[i].Min.Y/60 != rects[j].Min.Y/60 {
			return rects[i].Min.Y < rects[j].Min.Y
		}
		return rects[i].Min.X < rects[j].Min.X
	})
	for _, r := range rects {
		fmt.Printf("[%d,%d,%d,%d]\n", r.Min.X, r.Min.Y, r.Dx(), r.Dy())
	}
}

var _ = color.NRGBA{}

func flip(src *image.NRGBA, h, v bool) *image.NRGBA {
	W, H := src.Bounds().Dx(), src.Bounds().Dy()
	out := image.NewNRGBA(image.Rect(0, 0, W, H))
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			sx, sy := x, y
			if h {
				sx = W - 1 - x
			}
			if v {
				sy = H - 1 - y
			}
			copy(out.Pix[out.PixOffset(x, y):out.PixOffset(x, y)+4], src.Pix[src.PixOffset(sx, sy):src.PixOffset(sx, sy)+4])
		}
	}
	return out
}

// shadeMask: white-on-black painted fill -> opaque piece whose grey = shading (Image.color tints it).
func shadeMask(img *image.NRGBA) {
	for i := 0; i < len(img.Pix); i += 4 {
		l := (0.2126*float64(img.Pix[i]) + 0.7152*float64(img.Pix[i+1]) + 0.0722*float64(img.Pix[i+2])) / 255
		a := clamp01((l - 0.03) / 0.08)
		v := uint8(clamp01(0.35+l*0.75) * 255)
		img.Pix[i], img.Pix[i+1], img.Pix[i+2], img.Pix[i+3] = v, v, v, uint8(a*255)
	}
}
