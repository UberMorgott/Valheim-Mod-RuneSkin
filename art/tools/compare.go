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
	"golang.org/x/image/font"
	"golang.org/x/image/font/opentype"
	"golang.org/x/image/math/fixed"
)

// Review composites: the mockup layouts rebuilt from our sprites only (Unity Sliced/Tiled rules),
// drawn in sprite space (2x of 1920 ref) and shown next to the mockup at its resolution.
// Item icons/glyph art are content, not skin: they are copied from the mockup with the dark
// background keyed out (pixels darker than the slot fill are skipped).

const mockDir = "D:/tmp/cx-augaskin"

type spr struct {
	img  *image.NRGBA
	b    Border
	typ  string
	fill bool
	piv  [2]float64
}

var sprites map[string]spr

func loadSprites() {
	b, err := os.ReadFile(filepath.Join(spritesDir, "sprites.json"))
	must(err)
	var man Manifest
	must(json.Unmarshal(b, &man))
	sprites = map[string]spr{}
	for _, s := range man.Sprites {
		sprites[s.Name] = spr{loadPNG(filepath.Join(spritesDir, s.File)), s.Border, s.ImageType, s.FillCenter, s.Pivot}
	}
}

// canvas in sprite px; K converts mockup px -> sprite px
type canvas struct {
	img  *image.NRGBA
	K    float64
	late []func() // panel corner caps: drawn last, above panel content
}

func (c *canvas) flush() {
	for _, f := range c.late {
		f()
	}
	c.late = nil
}

func (c *canvas) R(x, y, w, h float64) image.Rectangle {
	return image.Rect(int(math.Round(x*c.K)), int(math.Round(y*c.K)), int(math.Round((x+w)*c.K)), int(math.Round((y+h)*c.K)))
}

func tinted(src *image.NRGBA, t color.NRGBA) *image.NRGBA {
	out := image.NewNRGBA(src.Bounds())
	copy(out.Pix, src.Pix)
	for i := 0; i < len(out.Pix); i += 4 {
		out.Pix[i] = uint8(int(out.Pix[i]) * int(t.R) / 255)
		out.Pix[i+1] = uint8(int(out.Pix[i+1]) * int(t.G) / 255)
		out.Pix[i+2] = uint8(int(out.Pix[i+2]) * int(t.B) / 255)
		out.Pix[i+3] = uint8(int(out.Pix[i+3]) * int(t.A) / 255)
	}
	return out
}

func hex(s string) color.NRGBA {
	var r, g, b uint8
	_, err := fmt.Sscanf(s, "#%02x%02x%02x", &r, &g, &b)
	must(err)
	return color.NRGBA{r, g, b, 255}
}

// part draws src region sr into dst region dr: stretched (Sliced) or repeated (Tiled).
func part(dst *image.NRGBA, src *image.NRGBA, sr, dr image.Rectangle, tile bool) {
	if sr.Empty() || dr.Empty() {
		return
	}
	piece := crop(src, sr)
	if !tile {
		over(dst, resize(piece, dr.Dx(), dr.Dy()), dr.Min)
		return
	}
	// Unity tiles from the segment start; the last tile is clipped
	clip := image.NewNRGBA(image.Rect(0, 0, dr.Dx(), dr.Dy()))
	for y := 0; y < dr.Dy(); y += sr.Dy() {
		for x := 0; x < dr.Dx(); x += sr.Dx() {
			over(clip, piece, image.Pt(x, y))
		}
	}
	over(dst, clip, dr.Min)
}

// draw9 renders sprite name into r (sprite px) following its imageType; tint optional (A=0 -> none).
func (c *canvas) draw9(name string, r image.Rectangle, tint color.NRGBA) {
	s, ok := sprites[name]
	if !ok {
		must(fmt.Errorf("no sprite %s", name))
	}
	src := s.img
	if tint.A != 0 {
		src = tinted(src, tint)
	}
	W, H := src.Bounds().Dx(), src.Bounds().Dy()
	if s.typ == "Simple" || (s.b == Border{}) && s.typ != "Tiled" {
		over(c.img, resize(src, r.Dx(), r.Dy()), r.Min)
		return
	}
	l, rt, t, b := s.b.Left, s.b.Right, s.b.Top, s.b.Bottom
	// borders shrink proportionally when the rect is smaller than them (Unity behaviour)
	f := math.Min(1, math.Min(float64(r.Dx())/float64(max(1, l+rt)), float64(r.Dy())/float64(max(1, t+b))))
	L, RR, T, B := int(float64(l)*f), int(float64(rt)*f), int(float64(t)*f), int(float64(b)*f)
	tile := s.typ == "Tiled"
	sx := []int{0, l, W - rt, W}
	sy := []int{0, t, H - b, H}
	dx := []int{r.Min.X, r.Min.X + L, r.Max.X - RR, r.Max.X}
	dy := []int{r.Min.Y, r.Min.Y + T, r.Max.Y - B, r.Max.Y}
	for i := 0; i < 3; i++ {
		for j := 0; j < 3; j++ {
			if i == 1 && j == 1 && !s.fill {
				continue
			}
			sr := image.Rect(sx[j], sy[i], sx[j+1], sy[i+1])
			dr := image.Rect(dx[j], dy[i], dx[j+1], dy[i+1])
			isCorner := i != 1 && j != 1
			part(c.img, src, sr, dr, tile && !isCorner && f == 1)
		}
	}
}

// native draws a Simple sprite at native size with its pivot at p (sprite px).
func (c *canvas) native(name string, p image.Point) {
	s := sprites[name]
	w, h := s.img.Bounds().Dx(), s.img.Bounds().Dy()
	at := image.Pt(p.X-int(s.piv[0]*float64(w)), p.Y-int((1-s.piv[1])*float64(h)))
	over(c.img, s.img, at)
}

func (c *canvas) panel(x, y, w, h float64) {
	r := c.R(x, y, w, h)
	c.draw9("fill_glass", r.Inset(24), color.NRGBA{})
	c.draw9("frame_panel", r, color.NRGBA{})
	c.draw9("frame_panel_glow", r, hex(palette["rune_glow"]))
	c.late = append(c.late, func() {
		c.native("frame_panel_corner_tl", r.Min)
		c.native("frame_panel_corner_tr", image.Pt(r.Max.X, r.Min.Y))
		c.native("frame_panel_corner_bl", image.Pt(r.Min.X, r.Max.Y))
		c.native("frame_panel_corner_br", r.Max)
	})
}

func (c *canvas) sprite(name string, x, y, w, h float64) {
	c.draw9(name, c.R(x, y, w, h), color.NRGBA{})
}

var faces = map[string]*opentype.Font{}

func face(file string, px float64) font.Face {
	f, ok := faces[file]
	if !ok {
		b, err := os.ReadFile(filepath.Join("art/fonts", file))
		must(err)
		f, err = opentype.Parse(b)
		must(err)
		faces[file] = f
	}
	fc, err := opentype.NewFace(f, &opentype.FaceOptions{Size: px, DPI: 72, Hinting: font.HintingNone})
	must(err)
	return fc
}

const (
	serif = "Cormorant-Bold.otf"
	sans  = "PT_Sans-Web-Regular.ttf"
)

// text: (x,y) = mockup px of the text box centre-left (align 0) or centre (align 1); size in mockup px.
func (c *canvas) text(s string, file string, size float64, col string, x, y float64, align int) {
	fc := face(file, size*c.K)
	d := &font.Drawer{Dst: c.img, Src: image.NewUniform(hex(col)), Face: fc}
	w := d.MeasureString(s).Round()
	m := fc.Metrics()
	_ = m
	px := int(x * c.K)
	if align == 1 {
		px -= w / 2
	}
	py := int(y*c.K + size*c.K*0.34)
	d.Dot = fixed.P(px, py)
	d.DrawString(s)
}

// icon copies mockup content (an item icon) into the same place, dark background keyed out.
func (c *canvas) icon(mock *image.NRGBA, x, y, w, h float64) { c.iconA(mock, x, y, w, h, 1) }

// iconA: icon at alpha a (muted content, e.g. unavailable recipe rows).
func (c *canvas) iconA(mock *image.NRGBA, x, y, w, h, a float64) {
	r := image.Rect(int(x), int(y), int(x+w), int(y+h))
	p := crop(mock, r)
	for i := 0; i < len(p.Pix); i += 4 {
		mx := max(p.Pix[i], p.Pix[i+1], p.Pix[i+2])
		if mx < 60 {
			p.Pix[i+3] = 0
		} else if mx < 90 {
			p.Pix[i+3] = uint8(int(mx-60) * 255 / 30)
		}
		p.Pix[i+3] = uint8(float64(p.Pix[i+3]) * a)
	}
	over(c.img, resize(p, int(w*c.K), int(h*c.K)), image.Pt(int(x*c.K), int(y*c.K)))
}

var palette map[string]string

func newCanvas(bg *image.NRGBA, mw, mh int) *canvas {
	K := 3840.0 / float64(mw)
	W, H := 3840, int(math.Round(float64(mh)*K))
	return &canvas{img: resize(bg, W, H), K: K}
}

func sideBySide(a, b *image.NRGBA, out string) {
	w, h := a.Bounds().Dx(), a.Bounds().Dy()
	img := image.NewNRGBA(image.Rect(0, 0, w*2+12, h))
	fill(img, img.Bounds(), color.NRGBA{255, 255, 255, 255})
	draw.Draw(img, a.Bounds(), a, image.Point{}, draw.Src)
	draw.Draw(img, a.Bounds().Add(image.Pt(w+12, 0)), b, image.Point{}, draw.Src)
	savePNG(out, img)
	fmt.Println("wrote", out)
}

func cmdCompare() {
	loadSprites()
	palette = loadSpec().Palette
	inv := compareInventory()
	hud := compareHUD()
	// corner pair: crafting panel bottom-right corner, 4x, [mockup | ours]
	mock := loadPNG(mockDir + "/mockup-1.png")
	cr := image.Rect(1560, 690, 1672, 800)
	sideBySide(resize(crop(mock, cr), 448, 440), resize(crop(inv, cr), 448, 440), "art/review/compare-corner.png")
	groups(mock, inv, loadPNG(mockDir+"/hud-status-b.png"), hud)
}

// groups writes art/review/compare-groups.png: per piece group a [mockup | ours] crop pair at the same scale.
func groups(mInv, oInv, mHud, oHud *image.NRGBA) {
	type g struct {
		name string
		hud  bool
		r    image.Rectangle
	}
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	list := []g{
		{"panel corner", false, R(1560, 690, 112, 110)},
		{"rail / edge", false, R(1600, 380, 70, 160)},
		{"panel fill", false, R(1490, 430, 150, 110)},
		{"slot cells", false, R(96, 94, 132, 132)},
		{"header plaque", false, R(1188, 24, 404, 52)},
		{"tabs normal + active", false, R(1165, 230, 345, 40)},
		{"button (craft)", false, R(1322, 690, 330, 76)},
		{"tooltip", false, R(842, 166, 284, 102)},
		{"quick-slot sockets", false, R(600, 236, 228, 88)},
		{"food sockets", false, R(254, 814, 198, 80)},
		{"HP bar", true, R(40, 478, 1070, 124)},
		{"stamina / eitr bars", true, R(40, 594, 1070, 126)},
		{"status icon + timer", true, R(40, 718, 380, 142)},
	}
	const H, gap, lab = 200, 16, 34
	maxW := 0
	for _, it := range list {
		maxW = max(maxW, it.r.Dx()*H/it.r.Dy())
	}
	maxW = min(maxW, 1100)
	W := 2*maxW + 3*gap
	img := image.NewNRGBA(image.Rect(0, 0, W, len(list)*(H+lab+gap)+gap))
	fill(img, img.Bounds(), color.NRGBA{24, 24, 28, 255})
	y := gap
	for _, it := range list {
		m, o := mInv, oInv
		if it.hud {
			m, o = mHud, oHud
		}
		s := math.Min(float64(H)/float64(it.r.Dy()), float64(maxW)/float64(it.r.Dx()))
		w, h := int(float64(it.r.Dx())*s), int(float64(it.r.Dy())*s)
		d := &font.Drawer{Dst: img, Src: image.NewUniform(color.NRGBA{230, 230, 230, 255}), Face: face(sans, 24)}
		d.Dot = fixed.P(gap, y+24)
		d.DrawString(it.name + "   [mockup | ours]")
		over(img, resize(crop(m, it.r), w, h), image.Pt(gap, y+lab))
		over(img, resize(crop(o, it.r), w, h), image.Pt(2*gap+maxW, y+lab))
		y += H + lab + gap
	}
	savePNG("art/review/compare-groups.png", img)
	fmt.Println("wrote art/review/compare-groups.png")
}

func compareInventory() *image.NRGBA {
	mock := loadPNG(mockDir + "/mockup-1.png")
	bg := loadPNG(mockDir + "/ref.png")
	c := newCanvas(bg, mock.Bounds().Dx(), mock.Bounds().Dy())
	gold, body, head := palette["text_active"], palette["text_body"], palette["text_header"]

	// inventory grid
	c.panel(22, 22, 515, 272)
	for row := 0; row < 4; row++ {
		for col := 0; col < 8; col++ {
			x, y := 38+float64(col)*61.6, 36+float64(row)*61.5
			c.sprite("slot_cell", x, y, 58, 58)
			if row == 0 {
				c.text(fmt.Sprint(col+1), sans, 15, []string{body, gold}[min(col, 1)], x+5, y+12, 0)
			}
		}
	}
	c.icon(mock, 40, 40, 56, 56)
	// stat column
	for i, s := range []string{"0", "0", "1/300", "Мусор"} {
		y := []float64{26, 94, 162, 230}[i]
		c.sprite("frame_small", 538, y, 56, 62)
		c.icon(mock, 548, y+2, 36, 30)
		c.text(s, sans, 14, gold, 566, y+44, 1)
	}
	// equipment
	c.panel(603, 22, 220, 205)
	for row := 0; row < 3; row++ {
		for col := 0; col < 3; col++ {
			x, y := 617+float64(col)*63, 34+float64(row)*61
			c.sprite("slot_cell", x, y, 58, 58)
			c.icon(mock, x+4, y+4, 50, 50)
		}
	}
	c.sprite("frame_small", 605, 240, 218, 78)
	for i, k := range []string{"Z", "V", "B"} {
		x := 619 + float64(i)*62
		c.sprite("slot_cell", x, 250, 58, 58)
		c.text(k, sans, 15, body, x+6, 262, 0)
	}
	// missing-materials tooltip
	c.sprite("tooltip_frame", 848, 172, 272, 90)
	c.text("Не хватает:", sans, 13, body, 862, 191, 0)
	for i, s := range []string{"x Кабанина x1 (Добыча / дроп)", "x Гриб x2 (Добыча / дроп)", "x Одуванчик x1 (Добыча / дроп)"} {
		c.text(s, sans, 12, "#e0413a", 862, 210+float64(i)*17, 0)
	}
	// round socket between panels
	c.sprite("socket_status", 1072, 280, 58, 62)
	// character header
	c.panel(1128, 22, 525, 130)
	c.sprite("header_plaque", 1198, 30, 380, 40)
	c.text("AUTOTESTCHAR", sans, 22, head, 1388, 50, 1)
	for i := 0; i < 5; i++ {
		x := 1176 + float64(i)*88
		c.sprite("slot_cell", x, 72, 74, 68)
		c.icon(mock, x+6, y0(76), 62, 60)
	}
	// crafting panel
	c.panel(1130, 163, 523, 622)
	c.icon(mock, 1165, 180, 60, 50)
	c.icon(mock, 1575, 180, 60, 52)
	c.text("ВЕРСТАК", sans, 28, head, 1393, 205, 1)
	tabs := []struct {
		x, w float64
		s    string
	}{{1172, 104, "СОЗДАТЬ"}, {1286, 104, "УЛУЧШИТЬ"}, {1400, 100, "VNEI"}}
	for i, t := range tabs {
		name, col := "tab_normal", body
		if i == 0 {
			name, col = "tab_selected", gold
		}
		c.sprite(name, t.x, 236, t.w, 28)
		c.text(t.s, sans, 15, col, t.x+t.w/2, 250, 1)
	}
	c.sprite("slot_cell", 1148, 268, 176, 505) // recipe list well
	c.sprite("slot_cell", 1326, 268, 316, 345) // detail well
	c.icon(mock, 1152, 272, 168, 26)           // filter glyph row
	c.sprite("button_normal", 1155, 302, 162, 24)
	c.text("поиск...", sans, 13, palette["text_disabled"], 1190, 314, 0)
	list := []string{"x20", "Деревянный болт x20", "Сгнивший плащ", "Черный плащ из кожи", "Crude Crossbow", "Кремневые кастеты",
		"Гигантская дубина", "Сырой луговой", "Засоленная грудинка", "Рогатый капюшон", "Огненная стрела x20", "Стрела с кремневым",
		"Деревянная стрела", "Древние топоры", "Кремневый топор"}
	ys := []float64{340, 364, 393, 419, 456, 486, 516, 541, 573, 608, 638, 663, 697, 733, 762}
	for i, s := range list {
		if i == 7 {
			c.sprite("slot_cell_selected", 1152, 531, 165, 30)
		}
		c.icon(mock, 1152, ys[i]-12, 30, 26)
		c.text(s, sans, 12.5, body, 1187, ys[i], 0)
	}
	c.icon(mock, 1336, 276, 52, 60)
	c.text("СЫРОЙ ЛУГОВОЙ ШАШЛЫЧОК", sans, 20, "#f0c419", 1400, 294, 0)
	c.text("Мясо, гриб и одуванчик на палочке ждут", sans, 14, body, 1336, 347, 0)
	c.text("огня. Дожарь на вертеле.", sans, 14, body, 1336, 367, 0)
	c.text("Вес: 0,5 (0,5 Всего)", sans, 14, "#8ab4f0", 1336, 410, 0)
	for i := 0; i < 5; i++ {
		x := 1333 + float64(i)*66
		c.sprite("slot_cell", x, 622, 62, 66)
		if i >= 1 && i <= 3 {
			c.icon(mock, x+2, 626, 58, 60)
		}
	}
	drawFrameScaled(c, "button_normal", c.R(1330, 698, 312, 62)) // UI-scaled, caps keep their aspect
	c.text("СОЗДАТЬ", serif, 30, head, 1486, 729, 1)

	// bottom-left HUD
	c.sprite("frame_strip", 30, 798, 228, 104)
	for i, p := range []string{"pool_hp", "pool_stamina", "pool_eitr"} {
		y := 806 + float64(i)*30
		r := c.R(40, y, 212, 28)
		over(c.img, barNative(r, []float64{0.4, 1, 1}[i], p, false), r.Min)
		k := float64(r.Dy()) / 46
		g := int(26 * k)
		gl := resize(pickGlyphAt(mock, 58, y+2, 24), g, g)
		over(c.img, gl, image.Pt(r.Min.X+int(49*k)-g/2, r.Min.Y+int(23*k)-g/2))
	}
	c.text("25", sans, 18, body, 196, 818, 1)
	c.sprite("frame_small", 258, 818, 188, 70)
	for i := 0; i < 3; i++ {
		x := 270 + float64(i)*57
		c.sprite("slot_cell", x, 828, 52, 52)
		c.icon(mock, x+4, y0(832), 44, 44)
	}

	c.flush()
	ours := resize(c.img, mock.Bounds().Dx(), mock.Bounds().Dy())
	sideBySide(mock, ours, "art/review/compare-inventory.png")
	return ours
}

func y0(v float64) float64 { return v }

func compareHUD() *image.NRGBA {
	mock := loadPNG(mockDir + "/hud-status-b.png")
	// our HUD is drawn over the mockup's own HUD (frames/troughs are opaque, so theirs does not show)
	c := newCanvas(mock, mock.Bounds().Dx(), mock.Bounds().Dy())
	body := palette["text_body"]
	bars := []struct {
		y, h, fw float64
		pool     string
		dots     bool
	}{{490, 104, 0.47, "pool_hp", true}, {598, 58, 0.48, "pool_stamina", false}, {658, 58, 0.47, "pool_eitr", false}}
	for _, b := range bars {
		r := c.R(50, b.y, 1048, b.h)
		over(c.img, barNative(r, b.fw, b.pool, b.dots), r.Min)
		if !b.dots {
			// pool glyph in the slim frame's icon recess (centre (49,23) of 46 px height)
			k := float64(r.Dy()) / 46
			cx, cy := float64(r.Min.X)+49*k, float64(r.Min.Y)+23*k
			g := 26 * k
			gl := pickGlyph(mock, b.y, b.h)
			over(c.img, resize(gl, int(g), int(g)), image.Pt(int(cx-g/2), int(cy-g/2)))
		}
	}
	c.text("54/100", sans, 36, body, 552, 549, 1)
	// status strip: panel + icons + timers (the mockup has no sockets here)
	c.sprite("frame_strip", 50, 722, 1040, 134)
	for i, t := range []string{"14:32", "4:10", "18:05", "42s", "12s", "8s", "21s", "16s"} {
		x := 128 + float64(i)*123.5
		c.icon(mock, x-30, 736, 60, 58)
		c.text(t, sans, 25, body, x, 816, 1)
		if i > 0 {
			c.native("separator_v", image.Pt(int((x-62)*c.K), int(778*c.K)))
		}
	}
	c.flush()
	ours := resize(c.img, mock.Bounds().Dx(), mock.Bounds().Dy())
	sideBySide(mock, ours, "art/review/compare-hud.png")
	return ours
}

// pickGlyph copies the pool icon of the mockup bar (left end), dark keyed out.
func pickGlyph(mock *image.NRGBA, y, h float64) *image.NRGBA {
	sz := math.Min(h*0.62, 44)
	x0 := 92.0
	if h > 80 {
		x0 = 70
	}
	p := crop(mock, image.Rect(int(x0), int(y+h/2-sz/2), int(x0+sz), int(y+h/2+sz/2)))
	for i := 0; i < len(p.Pix); i += 4 {
		g, b := p.Pix[i+1], p.Pix[i+2]
		if max(g, b) < 120 || int(p.Pix[i])+20 > int(max(g, b)) && max(g, b) < 200 {
			p.Pix[i+3] = 0
		}
	}
	return p
}

// barNative builds trough + fill (+ DoT strips) + bar_frame at the frame's native 87 px height and
// scales the result to r, like a UI scale would.
func barNative(r image.Rectangle, frac float64, pool string, dots bool) *image.NRGBA {
	frame, bw := "bar_frame", barWin
	if !dots {
		frame, bw = "bar_frame_slim", slimWin
	}
	s := sprites[frame]
	H := s.img.Bounds().Dy()
	k := float64(r.Dy()) / float64(H)
	W := int(float64(r.Dx()) / k)
	t := &canvas{img: image.NewNRGBA(image.Rect(0, 0, W, H)), K: 1}
	win := image.Rect(bw.Min.X, bw.Min.Y, W-(s.img.Bounds().Dx()-bw.Max.X), bw.Max.Y)
	t.draw9("bar_trough", win, color.NRGBA{})
	fw := image.Rect(win.Min.X, win.Min.Y, win.Min.X+int(float64(win.Dx())*frac), win.Max.Y)
	t.draw9("bar_fill", fw, hex(palette[pool]))
	if dots {
		x := fw.Max.X + 4
		for _, d := range []struct{ n, col string }{{"dot_bleed", "#e04040"}, {"dot_fire", palette["dot_fire"]}, {"dot_poison", palette["dot_poison"]}} {
			t.draw9(d.n, image.Rect(x, win.Min.Y, x+34, win.Max.Y), hex(d.col))
			x += 40
		}
	}
	t.draw9(frame, t.img.Bounds(), color.NRGBA{})
	return resize(t.img, r.Dx(), r.Dy())
}

// bar window inside bar_frame (sprite px), measured on the built sprite; see sprites.json notes
var barWin = image.Rect(95, 28, 388, 70)

var slimWin = image.Rect(70, 13, 344, 35)

func pickGlyphAt(mock *image.NRGBA, x, y, sz float64) *image.NRGBA {
	p := crop(mock, image.Rect(int(x), int(y), int(x+sz), int(y+sz)))
	for i := 0; i < len(p.Pix); i += 4 {
		if max(p.Pix[i], p.Pix[i+1], p.Pix[i+2]) < 90 {
			p.Pix[i+3] = 0
		}
	}
	return p
}

// cmdSheet writes art/review/contact-sheet.png: every sprite at native size on a checker, with its name.
func cmdSheet() {
	loadSprites()
	names := make([]string, 0, len(sprites))
	for n := range sprites {
		names = append(names, n)
	}
	sort.Strings(names)
	const W, pad, lab = 2400, 16, 26
	x, y, rowH := pad, pad, 0
	type pos struct {
		n    string
		x, y int
	}
	var ps []pos
	for _, n := range names {
		s := sprites[n].img
		w, h := s.Bounds().Dx(), s.Bounds().Dy()
		if x+max(w, 200) > W {
			x, y, rowH = pad, y+rowH+lab+pad, 0
		}
		ps = append(ps, pos{n, x, y})
		x += max(w, 200) + pad
		rowH = max(rowH, h)
	}
	img := image.NewNRGBA(image.Rect(0, 0, W, y+rowH+lab+2*pad))
	for yy := 0; yy < img.Bounds().Dy(); yy += 16 {
		for xx := 0; xx < W; xx += 16 {
			v := uint8(70)
			if (xx/16+yy/16)%2 == 0 {
				v = 90
			}
			fill(img, image.Rect(xx, yy, xx+16, yy+16), color.NRGBA{v, v, v, 255})
		}
	}
	for _, p := range ps {
		d := &font.Drawer{Dst: img, Src: image.NewUniform(color.NRGBA{255, 255, 255, 255}), Face: face(sans, 18)}
		d.Dot = fixed.P(p.x, p.y+18)
		d.DrawString(p.n)
		over(img, sprites[p.n].img, image.Pt(p.x, p.y+lab))
	}
	savePNG("art/review/contact-sheet.png", img)
	fmt.Println("wrote art/review/contact-sheet.png")
}

// drawFrameScaled draws a sprite at a UI scale chosen so its native height fills r (borders keep their aspect).
func drawFrameScaled(c *canvas, name string, r image.Rectangle) {
	s := sprites[name]
	k := float64(r.Dy()) / float64(s.img.Bounds().Dy())
	tmp := &canvas{img: image.NewNRGBA(image.Rect(0, 0, int(float64(r.Dx())/k), s.img.Bounds().Dy())), K: 1}
	tmp.draw9(name, tmp.img.Bounds(), color.NRGBA{})
	over(c.img, resize(tmp.img, r.Dx(), r.Dy()), r.Min)
}
