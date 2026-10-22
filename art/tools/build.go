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

	"golang.org/x/image/draw"
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
	Src    Border `json:"src"`            // borders measured on the scaled crop (sprite px)
	Center [2]int `json:"center"`         // centre segment size in the output
	Tile   bool   `json:"tile"`           // make edge/centre segments periodic (cross-fade)
	MidX   int    `json:"midX,omitempty"` // source x of the horizontal middle segment (0 = centred), e.g. to skip a centred ornament cut as its own sprite
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
	ScaleY float64    `json:"scaleY,omitempty"` // extra vertical factor after Scale (a rail painted thicker than the mockup's)
	Size   [2]int     `json:"size,omitempty"`   // final size (non-nine)
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
	ClearWindow []int        `json:"clearWindow,omitempty"`
	Leather     [2]float64   `json:"leather,omitempty"`     // leather-only grade: saturation keep, brightness
	Fade        [2]int       `json:"fade,omitempty"`        // alpha ramp at the left/right ends (px)
	FadeV       [2]int       `json:"fadeV,omitempty"`       // alpha ramp at the top/bottom ends (px)
	Flip        string       `json:"flip,omitempty"`        // mirror the crop: h | v | hv
	FlatEnds    [2]int       `json:"flatEnds,omitempty"`    // left/right output columns whose RGB is replaced by the centre column (same row), alpha kept
	Despill     bool         `json:"despill,omitempty"`     // neutralise violet magenta spill (r and b both above g) to the glass hue
	Hole        bool         `json:"hole,omitempty"`        // output = white opaque mask of the transparent region connected to the centre (ring hole -> circular map mask)
	Bold        int          `json:"bold,omitempty"`        // alpha max-filter radius (output px): thickens thin white glyph masks
	Sector      []int        `json:"sector,omitempty"`      // [index, count]: keep only pie sector index of count (clockwise from 12 o'clock, centred on index*360/count) around the output centre
	Norm        bool         `json:"norm,omitempty"`        // scale alpha so the most opaque pixel is fully opaque (equal brightness across slices)
	BgTol       float64      `json:"bgTol,omitempty"`       // key "bg": colour step that still counts as the smooth background (keyBackground)
	BgSides     string       `json:"bgSides,omitempty"`     // key "bg": crop sides the background flood starts from (default tlrb)
	Clear       [][4]int     `json:"clear,omitempty"`       // crop-space rects made transparent after keying (bar windows)
	Clone       []int        `json:"clone,omitempty"`       // [x, y, w, h, srcX, srcW] crop space: fill the rect with mirror-tiled source columns srcX..+srcW of the same rows (clear a baked caption off a plate field)
	SquashRight [][4]float64 `json:"squashRight,omitempty"` // [y0, y1, factor, x0]: rows y0..y1 resampled only for columns >= x0, re-centred (a bar window thinner next to a round socket that keeps its shape)
	Squash      [][3]float64 `json:"squash,omitempty"`      // crop-space row bands [y0, y1, factor] resampled vertically (a rail painted thicker than the mockup's)
	Holes       [][][2]int   `json:"holes,omitempty"`       // crop-space polygons made transparent after keying (gaps inside a knot where the fill must show)
	Solid       [][2]int     `json:"solid,omitempty"`       // crop-space polygon forced opaque after keying (a dark blade the bg flood ate)
	MirrorLeft  int          `json:"mirrorLeft,omitempty"`  // crop space, after clear/clone: the leftmost N columns become the mirror of the rightmost N (a socket-less bar gets the right end cap at both ends)
	KeyRed      bool         `json:"keyRed,omitempty"`      // also key out saturated HP-fill red (R > 110, G and B < 60): a cap cut beside a mockup fill
	Border      Border       `json:"border"`
	Pivot       [2]float64   `json:"pivot"`
	ImageType   string       `json:"imageType"`
	FillCenter  bool         `json:"fillCenter"`
	Token       string       `json:"token"`
	Notes       string       `json:"notes"`
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
	case "bg":
		keyBackground(src, c.BgTol, c.BgSides)
	case "black":
		keyBlack(src)
	case "green":
		keyGreen(src)
	case "shade":
		shadeMask(src)
	case "luma":
		bw := c.Luma
		if bw[1] == 0 {
			bw = [2]float64{0.04, 0.95}
		}
		lumaMask(src, bw[0], bw[1])
	}
	if len(c.Clear) > 0 {
		clearRects(src, c.Clear)
	}
	if len(c.Clone) == 6 {
		cloneColumns(src, c.Clone)
	}
	for _, q := range c.SquashRight {
		squashRight(src, int(q[0]), int(q[1]), q[2], int(q[3]))
	}
	for i := len(c.Squash) - 1; i >= 0; i-- {
		src = squashRows(src, int(c.Squash[i][0]), int(c.Squash[i][1]), c.Squash[i][2])
	}
	for _, h := range c.Holes {
		polygonAlpha(src, h, 0)
	}
	if len(c.Solid) >= 3 {
		solidPolygon(src, c.Solid)
	}
	if c.MirrorLeft > 0 {
		mirrorLeft(src, c.MirrorLeft)
	}
	if c.KeyRed {
		keyRed(src)
	}
	switch c.Flip {
	case "v":
		src = flip(src, false, true)
	case "h":
		src = flip(src, true, false)
	case "hv":
		src = flip(src, true, true)
	}
	if c.Despill {
		despillViolet(src)
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
	if c.ScaleY > 0 && c.ScaleY != 1 {
		img = resize(img, img.Bounds().Dx(), int(math.Round(float64(img.Bounds().Dy())*c.ScaleY)))
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
	if c.FlatEnds[0] > 0 || c.FlatEnds[1] > 0 {
		flatEnds(img, c.FlatEnds[0], c.FlatEnds[1])
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
	if c.FadeV[0] > 0 || c.FadeV[1] > 0 {
		H := img.Bounds().Dy()
		for y := 0; y < H; y++ {
			f := 1.0
			if c.FadeV[0] > 0 && y < c.FadeV[0] {
				f = math.Min(f, (float64(y)+0.5)/float64(c.FadeV[0]))
			}
			if c.FadeV[1] > 0 && y >= H-c.FadeV[1] {
				f = math.Min(f, (float64(H-y)-0.5)/float64(c.FadeV[1]))
			}
			for x := 0; x < img.Bounds().Dx(); x++ {
				i := img.PixOffset(x, y)
				img.Pix[i+3] = uint8(float64(img.Pix[i+3]) * f * f * (3 - 2*f))
			}
		}
	}
	if c.Hole {
		img = holeMask(img)
	}
	if c.Bold > 0 {
		img = boldAlpha(img, c.Bold)
	}
	if len(c.Sector) == 2 && c.Sector[1] > 0 {
		sectorMask(img, c.Sector[0], c.Sector[1])
	}
	if c.Norm {
		normAlpha(img)
	}
	adjust(img, c.Alpha, c.Gray, c.Bright)
	return img
}

// sectorMask clears every pixel outside pie sector index of count around the image centre (angles clockwise from
// 12 o'clock; sector index spans index*360/count +- 180/count degrees): one rune of a ring per slice.
func sectorMask(img *image.NRGBA, index, count int) {
	W, H := img.Bounds().Dx(), img.Bounds().Dy()
	cx, cy := float64(W)/2, float64(H)/2
	centre := float64(index) * 360 / float64(count)
	half := 180 / float64(count)
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			a := math.Atan2(float64(x)+0.5-cx, cy-(float64(y)+0.5)) * 180 / math.Pi
			d := math.Abs(math.Mod(a-centre+540, 360) - 180)
			if d > half {
				img.Pix[img.PixOffset(x, y)+3] = 0
			}
		}
	}
}

// normAlpha scales alpha so the most opaque pixel becomes 255 (a dim slice of a graded ring at full strength).
func normAlpha(img *image.NRGBA) {
	m := uint8(0)
	for i := 3; i < len(img.Pix); i += 4 {
		if img.Pix[i] > m {
			m = img.Pix[i]
		}
	}
	if m == 0 || m == 255 {
		return
	}
	k := 255 / float64(m)
	for i := 3; i < len(img.Pix); i += 4 {
		img.Pix[i] = uint8(math.Min(255, math.Round(float64(img.Pix[i])*k)))
	}
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
	if n.MidX > 0 {
		mx = n.MidX
	}
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
	buildTextures()
	cmdCheck()
}

// Standalone textures (not atlas sprites): art/src/kit/textures.json cuts -> art/textures/<name>.png +
// textures.json. BuildBundles imports them as readable RGBA32 textures (cursor: Cursor.SetCursor needs a
// readable, uncompressed texture, which an atlas sprite never is).
const texturesDir = "art/textures"

type TexCut struct {
	Cut
	Usage   string     `json:"usage"`   // cursor
	Hotspot [2]float64 `json:"hotspot"` // output px from the top-left (cursor click point)
}

type OutTexture struct {
	Name    string     `json:"name"`
	File    string     `json:"file"`
	Size    [2]int     `json:"size"`
	Usage   string     `json:"usage"`
	Hotspot [2]float64 `json:"hotspot"`
	Notes   string     `json:"notes"`
}

func buildTextures() {
	b, err := os.ReadFile(filepath.Join(kitDir, "textures.json"))
	if os.IsNotExist(err) {
		return
	}
	must(err)
	var spec struct {
		Textures []TexCut `json:"textures"`
	}
	must(json.Unmarshal(b, &spec))
	must(os.MkdirAll(texturesDir, 0o755))
	var out struct {
		Source   string       `json:"source"`
		Textures []OutTexture `json:"textures"`
	}
	out.Source = "Codex image-gen kit sheets art/src/kit/*.png, cut by art/tools (go run ./art/tools build)"
	for _, t := range spec.Textures {
		img := renderCut(t.Cut)
		file := t.Name + ".png"
		savePNG(filepath.Join(texturesDir, file), img)
		w, h := img.Bounds().Dx(), img.Bounds().Dy()
		if t.Hotspot[0] < 0 || t.Hotspot[1] < 0 || t.Hotspot[0] >= float64(w) || t.Hotspot[1] >= float64(h) {
			fmt.Printf("%s: hotspot %v outside %dx%d\n", t.Name, t.Hotspot, w, h)
			os.Exit(1)
		}
		out.Textures = append(out.Textures, OutTexture{t.Name, file, [2]int{w, h}, t.Usage, t.Hotspot, t.Notes})
	}
	ob, err := json.MarshalIndent(out, "", "  ")
	must(err)
	must(os.WriteFile(filepath.Join(texturesDir, "textures.json"), append(ob, '\n'), 0o644))
	fmt.Printf("build: %d textures -> %s\n", len(out.Textures), texturesDir)
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

// keyGreen keys a flat pure-green background (purple smoke cannot sit on magenta) with the same unblend as keyMagenta.
func keyGreen(img *image.NRGBA) {
	for i := 0; i < len(img.Pix); i += 4 {
		r, g, b := float64(img.Pix[i])/255, float64(img.Pix[i+1])/255, float64(img.Pix[i+2])/255
		m := g - math.Max(r, b)
		a := clamp01(1 - (m-0.1)/0.5)
		if a <= 0 {
			img.Pix[i], img.Pix[i+1], img.Pix[i+2], img.Pix[i+3] = 0, 0, 0, 0
			continue
		}
		if a < 1 {
			// observed = a*fg + (1-a)*green -> fg
			img.Pix[i] = uint8(255 * clamp01(r/a))
			img.Pix[i+1] = uint8(255 * clamp01((g-(1-a))/a))
			img.Pix[i+2] = uint8(255 * clamp01(b/a))
		}
		img.Pix[i+3] = uint8(255 * a)
		// despill: no green left in the smoke (green-over-violet fringes read teal)
		// violet smoke: green stays well below red and blue; semi-transparent fringe pixels take the violet hue
		if gg, lim := img.Pix[i+1], uint8(min(int(img.Pix[i]), int(img.Pix[i+2]))*55/100); gg > lim {
			img.Pix[i+1] = lim
		}
		if a < 0.9 && int(img.Pix[i]) > int(img.Pix[i+2])*8/10 {
			img.Pix[i] = uint8(int(img.Pix[i+2]) * 8 / 10)
		}
	}
}

// keyBlack turns a glow drawn on flat black into straight alpha (alpha = brightest channel, colour un-premultiplied),
// so an additive-looking effect sheet blends with the default UI material.
func keyBlack(img *image.NRGBA) {
	const blackFloor = 10 // generated "pure black" carries noise: below this it is background
	for i := 0; i < len(img.Pix); i += 4 {
		r, g, b := img.Pix[i], img.Pix[i+1], img.Pix[i+2]
		m := max(r, g, b)
		if m <= blackFloor {
			img.Pix[i], img.Pix[i+1], img.Pix[i+2], img.Pix[i+3] = 0, 0, 0, 0
			continue
		}
		img.Pix[i] = uint8(int(r) * 255 / int(m))
		img.Pix[i+1] = uint8(int(g) * 255 / int(m))
		img.Pix[i+2] = uint8(int(b) * 255 / int(m))
		img.Pix[i+3] = uint8((int(m) - blackFloor) * 255 / (255 - blackFloor))
	}
}

// squashRows resamples rows [y0, y1) of img (crop space) to factor x their height; the rows above / below stay.
func squashRows(img *image.NRGBA, y0, y1 int, factor float64) *image.NRGBA {
	b := img.Bounds()
	W := b.Dx()
	band := crop(img, image.Rect(b.Min.X, b.Min.Y+y0, b.Max.X, b.Min.Y+y1))
	nh := max(1, int(math.Round(float64(y1-y0)*factor)))
	band = resize(band, W, nh)
	out := image.NewNRGBA(image.Rect(0, 0, W, b.Dy()-(y1-y0)+nh))
	draw.Draw(out, image.Rect(0, 0, W, y0), img, b.Min, draw.Src)
	draw.Draw(out, image.Rect(0, y0, W, y0+nh), band, band.Bounds().Min, draw.Src)
	draw.Draw(out, image.Rect(0, y0+nh, W, out.Bounds().Dy()), img, image.Pt(b.Min.X, b.Min.Y+y1), draw.Src)
	return out
}

// squashRight resamples rows [y0, y1) to factor x their height for the columns >= x0 only, padding the removed height
// half above / half below with transparency so that part stays centred on the untouched left part.
func squashRight(img *image.NRGBA, y0, y1 int, factor float64, x0 int) {
	b := img.Bounds()
	H := b.Dy()
	nh := max(1, int(math.Round(float64(y1-y0)*factor)))
	d := (y1 - y0) - nh
	top := d / 2
	right := crop(img, image.Rect(b.Min.X+x0, b.Min.Y, b.Max.X, b.Max.Y))
	W := right.Bounds().Dx()
	band := resize(crop(right, image.Rect(right.Bounds().Min.X, right.Bounds().Min.Y+y0, right.Bounds().Max.X, right.Bounds().Min.Y+y1)), W, nh)
	col := image.NewNRGBA(image.Rect(0, 0, W, H))
	draw.Draw(col, image.Rect(0, top, W, top+y0), right, right.Bounds().Min, draw.Src)
	draw.Draw(col, image.Rect(0, top+y0, W, top+y0+nh), band, band.Bounds().Min, draw.Src)
	draw.Draw(col, image.Rect(0, top+y0+nh, W, top+H-d), right, image.Pt(right.Bounds().Min.X, right.Bounds().Min.Y+y1), draw.Src)
	draw.Draw(img, image.Rect(b.Min.X+x0, b.Min.Y, b.Max.X, b.Max.Y), col, image.Point{}, draw.Src)
}

// solidPolygon makes every pixel inside the polygon (crop space, even-odd rule, pixel centres) fully opaque.
func solidPolygon(img *image.NRGBA, poly [][2]int) { polygonAlpha(img, poly, 255) }

// polygonAlpha sets the alpha of every pixel inside the polygon (crop space, even-odd rule, pixel centres).
func polygonAlpha(img *image.NRGBA, poly [][2]int, alpha uint8) {
	b := img.Bounds()
	for y := b.Min.Y; y < b.Max.Y; y++ {
		py := float64(y-b.Min.Y) + 0.5
		for x := b.Min.X; x < b.Max.X; x++ {
			px := float64(x-b.Min.X) + 0.5
			in := false
			for i, j := 0, len(poly)-1; i < len(poly); j, i = i, i+1 {
				xi, yi := float64(poly[i][0]), float64(poly[i][1])
				xj, yj := float64(poly[j][0]), float64(poly[j][1])
				if (yi > py) != (yj > py) && px < (xj-xi)*(py-yi)/(yj-yi)+xi {
					in = !in
				}
			}
			if in {
				img.Pix[img.PixOffset(x, y)+3] = alpha
			}
		}
	}
}

// mirrorLeft overwrites the leftmost n columns with the rightmost n columns mirrored (RGBA, every row).
func mirrorLeft(img *image.NRGBA, n int) {
	b := img.Bounds()
	W := b.Dx()
	if n > W/2 {
		n = W / 2
	}
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := 0; x < n; x++ {
			d := img.PixOffset(b.Min.X+x, y)
			s := img.PixOffset(b.Min.X+W-1-x, y)
			copy(img.Pix[d:d+4], img.Pix[s:s+4])
		}
	}
}

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

// despillViolet: soft piece ends painted into the magenta background keep a violet cast after keying
// (r and b both above g); map those pixels to the glass hue (r = 0.41 g, b = 1.33 g, from glass #0b1b24)
// keeping their luminance.
func despillViolet(img *image.NRGBA) {
	for i := 0; i < len(img.Pix); i += 4 {
		r, g, b := float64(img.Pix[i]), float64(img.Pix[i+1]), float64(img.Pix[i+2])
		if r <= g+4 || b <= g+4 {
			continue
		}
		l := 0.2126*r + 0.7152*g + 0.0722*b
		k := l / (0.2126*0.41 + 0.7152 + 0.0722*1.33)
		img.Pix[i] = uint8(math.Min(255, 0.41*k))
		img.Pix[i+1] = uint8(math.Min(255, k))
		img.Pix[i+2] = uint8(math.Min(255, 1.33*k))
	}
}

// flatEnds copies the centre column's RGB (same row) over the first l and last r columns, keeping alpha:
// borderless strips whose painted ends carry background spill get a hue continuous with the sliced centre.
func flatEnds(img *image.NRGBA, l, r int) {
	W, H := img.Bounds().Dx(), img.Bounds().Dy()
	for y := 0; y < H; y++ {
		s := img.PixOffset(W/2, y)
		for x := 0; x < W; x++ {
			if x >= l && x < W-r {
				continue
			}
			i := img.PixOffset(x, y)
			copy(img.Pix[i:i+3], img.Pix[s:s+3])
		}
	}
}

// boldAlpha: alpha = max over a (2r+1)^2 neighbourhood (mask stroke thickening), RGB kept.
func boldAlpha(src *image.NRGBA, r int) *image.NRGBA {
	W, H := src.Bounds().Dx(), src.Bounds().Dy()
	out := image.NewNRGBA(src.Bounds())
	copy(out.Pix, src.Pix)
	for y := 0; y < H; y++ {
		for x := 0; x < W; x++ {
			m := uint8(0)
			for dy := -r; dy <= r; dy++ {
				for dx := -r; dx <= r; dx++ {
					px, py := x+dx, y+dy
					if px < 0 || py < 0 || px >= W || py >= H {
						continue
					}
					m = max(m, src.Pix[src.PixOffset(px, py)+3])
				}
			}
			out.Pix[out.PixOffset(x, y)+3] = m
		}
	}
	return out
}

// holeMask flood-fills the transparent region connected to the image centre (alpha < 128) and returns it as an
// opaque white mask, everything else transparent: the hole of a keyed ring becomes its Unity Mask sprite.
func holeMask(src *image.NRGBA) *image.NRGBA {
	W, H := src.Bounds().Dx(), src.Bounds().Dy()
	out := image.NewNRGBA(src.Bounds())
	seen := make([]bool, W*H)
	stack := []int{(H/2)*W + W/2}
	seen[stack[0]] = true
	for len(stack) > 0 {
		q := stack[len(stack)-1]
		stack = stack[:len(stack)-1]
		x, y := q%W, q/W
		i := out.PixOffset(x, y)
		out.Pix[i], out.Pix[i+1], out.Pix[i+2], out.Pix[i+3] = 255, 255, 255, 255
		for _, d := range [][2]int{{1, 0}, {-1, 0}, {0, 1}, {0, -1}} {
			nx, ny := x+d[0], y+d[1]
			if nx < 0 || ny < 0 || nx >= W || ny >= H {
				continue
			}
			n := ny*W + nx
			if !seen[n] && src.Pix[src.PixOffset(nx, ny)+3] < 128 {
				seen[n] = true
				stack = append(stack, n)
			}
		}
	}
	return out
}
