package main

import (
	"encoding/json"
	"image"
	"image/color"
	"math"
	"os"
	"strconv"
	"strings"

	"golang.org/x/image/font"
	"golang.org/x/image/math/fixed"
)

// compare-stats: offline compose of the character stats window (stats-full-b) from our sprites only, laid out with
// the same design-px tokens as InfoScreens/StatsWindow.cs (read from src/RuneSkin/Theme/default-theme.json), filled
// with the mockup's sample data; PT Sans / Cormorant text (the title's in-game rune face, the game's Norsebold, is
// not on disk: Cormorant stands in). Output art/review/compose-stats.png + compare-stats.png ([mockup | ours]).

const statsMock = "art/src/kit/stats/mockup-statsb.png"

var stf map[string]float64
var stc map[string]string

// elemTint: theme colour of a damage-type icon (color.stats.elem.<icon>), A=0 when none.
func elemTint(icon string) color.NRGBA {
	v, ok := stc["color.stats.elem."+icon]
	if !ok || len(v) < 7 {
		return color.NRGBA{}
	}
	return hex(v[:7])
}

func loadStatsTokens() {
	b, err := os.ReadFile("src/RuneSkin/Theme/default-theme.json")
	must(err)
	var t struct {
		Colors []struct {
			Token string `json:"token"`
			Value string `json:"value"`
		} `json:"colors"`
		Floats []struct {
			Token string      `json:"token"`
			Value interface{} `json:"value"`
		} `json:"floats"`
	}
	must(json.Unmarshal(b, &t))
	stc = map[string]string{}
	for _, c := range t.Colors {
		stc[c.Token] = c.Value
	}
	stf = map[string]float64{}
	for _, f := range t.Floats {
		if v, ok := f.Value.(float64); ok {
			stf[f.Token] = v
		}
	}
}

func st(token string, fallback float64) float64 {
	if v, ok := stf[token]; ok {
		return v
	}
	return fallback
}

type stRow struct {
	kind              string // pair, pairsub, bar, cell, dish, note, divider, tall, tallsub, wide, name
	icon, icon2       string
	label, value, sub string
	label2, value2    string
	vcol, v2col, scol string
	fill              float64
	bar               string
	dish              image.Rectangle // mockup rect of the dish icon (content)
}

const (
	colBody  = "#E3E6E8"
	colValue = "#F2F4F5"
	colSub   = "#A9B3B7"
	colGood  = "#7FD36B"
	colBad   = "#F08A3A"
	colGold  = "#E8B64A"
	colHead  = "#D9DCE0"
)

func statsSample() [][]stRow {
	return [][]stRow{
		{ // resources
			{kind: "pair", icon: "heart", label: "Здоровье", value: "141 / 185"},
			{kind: "bar", bar: "hp", fill: 141.0 / 185},
			{kind: "pairsub", icon: "pulse", label: "Реген. здоровья", value: "+2,4/с", vcol: colGood, sub: "(тик через 6 с)"},
			{kind: "pair", icon: "bolt", label: "Выносливость", value: "112 / 160"},
			{kind: "bar", bar: "stamina", fill: 112.0 / 160},
			{kind: "pairsub", icon: "bolt2", label: "Реген. выносливости", value: "+7,5/с", vcol: colGood, sub: "(до +15,0/с при пустой)"},
			{kind: "pair", icon: "drop", label: "Эйтр", value: "85 / 120"},
			{kind: "bar", bar: "eitr", fill: 85.0 / 120},
			{kind: "pair", icon: "pulse2", label: "Реген. эйтра", value: "+6,0/с", vcol: colGood},
		},
		{ // resistances
			{kind: "cell", icon: "blunt", label: "Дробящий", value: "39%", icon2: "slash", label2: "Режущий", value2: "39%"},
			{kind: "cell", icon: "pierce", label: "Колющий", value: "39%", icon2: "fire", label2: "Огонь", value2: "69%", v2col: colGood},
			{kind: "cell", icon: "frost", label: "Мороз", value: "39%", icon2: "lightning", label2: "Молния", value2: "39%"},
			{kind: "cell", icon: "poison", label: "Яд", value: "50%", vcol: colGood, icon2: "spirit", label2: "Дух", value2: "39%"},
			{kind: "cell", icon: "chop", label: "Рубящий", value: "39%", icon2: "pickaxe", label2: "Кирка", value2: "39%"},
		},
		{ // movement
			{kind: "pair", icon: "walk", label: "Скорость ходьбы", value: "−5%", vcol: colBad},
			{kind: "pair", icon: "run", label: "Скорость бега", value: "+12%", vcol: colGood},
			{kind: "pair", icon: "swim", label: "Скорость плавания", value: "100%"},
			{kind: "pairsub", icon: "weight", label: "Переносимый вес", value: "212 / 450", sub: "(+150 Мегингьорд)", scol: colGood},
			{kind: "pair", icon: "rundrain", label: "Расход на бег", value: "−10%", vcol: colGood},
			{kind: "pair", icon: "dodge", label: "Цена уклонения", value: "9"},
			{kind: "pair", icon: "jump", label: "Цена прыжка", value: "10"},
		},
		{ // defense
			{kind: "tallsub", icon: "armor", label: "Броня", value: "64", sub: "(снижение 39%)"},
			{kind: "tall", icon: "block", label: "Блок", value: "52"},
			{kind: "tall", icon: "parry", label: "Парирование", value: "×1,5 = 78"},
		},
		{ // food
			{kind: "dish", label: "Жареное мясо", sub: "+30 ОЗ +10 ВН, реген 2", value: "18:40", dish: image.Rect(94, 680, 168, 733)},
			{kind: "dish", label: "Морковный суп", sub: "+15 ОЗ +45 ВН, реген 2", value: "31:10", dish: image.Rect(94, 750, 172, 816)},
			{kind: "dish", label: "Хлеб", sub: "+23 ОЗ +70 ВН, реген 3", value: "44:55", dish: image.Rect(92, 826, 168, 885)},
			{kind: "pair", icon: "fork", label: "Слоты еды", value: "3 / 4"},
			{kind: "pair", icon: "flask", label: "Слоты зелий", value: "4 / 5"},
		},
		{ // rest
			{kind: "tall", icon: "bed", label: "Комфорт", value: "12"},
			{kind: "tall", icon: "clock", label: "Отдохнувший", value: "16:15 осталось"},
		},
		{ // combat
			{kind: "name", icon: "sword", label: "Черный клинок"},
			{kind: "wide", icon: "flame", label: "Урон", value: "82 режущий + 12 огонь"},
			{kind: "pair", icon: "attackspeed", label: "Скорость атаки", value: "+8%", vcol: colGood},
			{kind: "pair", icon: "backstab", label: "Удар в спину", value: "×3"},
			{kind: "pair", icon: "double", label: "Шанс двойного урона", value: "6%", vcol: colGood},
			{kind: "pair", icon: "lifesteal", label: "Вампиризм", value: "3%", vcol: colGood},
			{kind: "pair", icon: "stagger", label: "Ошеломление", value: "+15%", vcol: colGood},
			{kind: "divider"},
			{kind: "note", icon: "boots", label: "+12% скорость (легендарные сапоги)"},
			{kind: "note", icon: "ring", label: "+150 вес (Мегингьорд)"},
		},
		{ // MorgottTweaks
			{kind: "pair", icon: "es", label: "Оберег", value: "40 / 60"},
			{kind: "bar", bar: "es", fill: 40.0 / 60},
			{kind: "pair", icon: "recharge", label: "Перезарядка", value: "+15,0/с через 3,0 с", vcol: colGood},
			{kind: "pair", icon: "breakdelay", label: "Задержка после пробоя", value: "10 с"},
			{kind: "pair", icon: "potion", label: "Слоты зелий", value: "4 / 5"},
			{kind: "pair", icon: "food", label: "Слоты еды", value: "3 / 4"},
			{kind: "pair", icon: "potency", label: "Эффект зелий", value: "+10%", vcol: colGood},
		},
	}
}

// designScale: the 4x redraws (sprite px = art scale x design px) as a design-px copy (image and 9-slice borders
// divided), so draw9 tiles / slices at the in-game proportions.
// gK: output px per design px (1 = 1080p, 4/3 = 1440p); sprites are resampled from their high-res art straight to
// the output size, text is rasterised at the output size.
var gK = 1.0

func designScale(name string) string {
	s := sprites[name]
	k := st("size.stats.art.scale", 4)
	if strings.HasPrefix(name, "stats_chrome") {
		k = st("size.stats.chrome.scale", 2)
	}
	if k <= 1 || !strings.HasPrefix(name, "stats_") || name == "stats_close" {
		return name
	}
	k /= gK
	dn := name + "@design" + strconv.FormatFloat(gK, 'f', 3, 64)
	if _, ok := sprites[dn]; ok {
		return dn
	}
	w := max(1, int(math.Round(float64(s.img.Bounds().Dx())/k)))
	h := max(1, int(math.Round(float64(s.img.Bounds().Dy())/k)))
	b := Border{Left: int(math.Round(float64(s.b.Left) / k)), Bottom: int(math.Round(float64(s.b.Bottom) / k)), Right: int(math.Round(float64(s.b.Right) / k)), Top: int(math.Round(float64(s.b.Top) / k))}
	sprites[dn] = spr{resize(s.img, w, h), b, s.typ, s.fill, s.piv}
	return dn
}

// statsPieces mirrors StatsWindow.PieceDefs (frame bands / rails / knot corners / plaque parts, design px).
var statsPieces = []struct {
	name, sprite string
	x, y, w, h   float64
}{
	{"ChromeTL", "stats_chrome_tl", 0, 0, 960, 540},
	{"ChromeTR", "stats_chrome_tr", 960, 0, 960, 540},
	{"ChromeBL", "stats_chrome_bl", 0, 540, 960, 540},
	{"ChromeBR", "stats_chrome_br", 960, 540, 960, 540},
}

var statsTitles = []string{"Ресурсы", "Сопротивления", "Передвижение и вес", "Защита", "Еда", "Отдых", "Боевые чары", "Оберег и запасы"}
var statsHeads = []string{"res", "resist", "move", "defense", "food", "rest", "combat", "mt"}

func rowHeight(card int, kind string) float64 {
	switch kind {
	case "bar":
		return st("size.stats.row.bar", 26)
	case "divider":
		return st("size.stats.row.divider", 14)
	case "note":
		return st("size.stats.row.note", 27)
	case "cell":
		return st("size.stats.row.cell", 58)
	case "dish":
		return st("size.stats.row.dish", 76)
	case "tall":
		return st("size.stats.row.tall", 90)
	case "tallsub":
		return st("size.stats.row.tallsub", 94)
	case "wide":
		return st("size.stats.row.wide", 46)
	case "name":
		return st("size.stats.row.name", 34)
	case "pairsub":
		if card == 0 {
			return st("size.stats.row.res.sub", 48)
		}
		return st("size.stats.row.pairsub", 58)
	}
	switch card {
	case 0:
		return st("size.stats.row.res", 32)
	case 4:
		return st("size.stats.row.slots", 37)
	case 6:
		return st("size.stats.row.combat", 31.5)
	case 7:
		return st("size.stats.row.mt", 42)
	}
	return st("size.stats.row.pair", 38)
}

// textBox draws s inside the box (x, y, w, h) vertically centred, aligned 0 left / 1 centre / 2 right; spacing in
// px per glyph; shrinks to fit the width down to min.
func (c *canvas) textBox(s, file string, size, minSize float64, col string, x, y, w, h float64, align int, spacing float64, upper bool) {
	if upper {
		s = strings.ToUpper(s)
	}
	for size > minSize && measure(s, file, size, spacing) > w {
		size -= 0.5
	}
	fc := face(file, size*gK)
	tw := measure(s, file, size, spacing)
	px := x
	switch align {
	case 1:
		px = x + (w-tw)/2
	case 2:
		px = x + w - tw
	}
	m := fc.Metrics()
	asc := float64(m.Ascent.Round())
	desc := float64(m.Descent.Round())
	px *= gK
	py := y*gK + (h*gK-(asc+desc))/2 + asc
	d := &font.Drawer{Dst: c.img, Src: image.NewUniform(hex(col)), Face: fc}
	// dark outline (values / labels in the mockup carry a thin shadow)
	sh := &font.Drawer{Dst: c.img, Src: image.NewUniform(color.NRGBA{0, 0, 0, 200}), Face: fc}
	draw := func(dd *font.Drawer, ox, oy float64) {
		cx := px + ox
		for _, r := range s {
			dd.Dot = fixed.Point26_6{X: fixed.Int26_6(cx * 64), Y: fixed.Int26_6(math.Round(py+oy) * 64)}
			dd.DrawString(string(r))
			adv := font.MeasureString(fc, string(r))
			cx += float64(adv.Round()) + spacing*gK
		}
	}
	if file == sans {
		for _, o := range [][2]float64{{-1, 0}, {1, 0}, {0, -1}, {0, 1}, {1, 1}} {
			draw(sh, o[0], o[1])
		}
		draw(d, 0, 0)
		draw(d, 0.5, 0)
	} else {
		draw(sh, 1, 1)
		draw(d, 0, 0)
	}
}

func measure(s, file string, size, spacing float64) float64 {
	fc := face(file, size)
	w := 0.0
	n := 0
	for _, r := range s {
		w += float64(font.MeasureString(fc, string(r)).Round())
		n++
	}
	if n > 1 {
		w += spacing * float64(n-1)
	}
	return w
}

// wrapLines splits words into lines no wider than w.
func wrapLines(s, file string, size, spacing, w float64) []string {
	words := strings.Fields(strings.ToUpper(s))
	var lines []string
	cur := ""
	for _, word := range words {
		try := word
		if cur != "" {
			try = cur + " " + word
		}
		if cur != "" && measure(try, file, size, spacing) > w {
			lines = append(lines, cur)
			cur = word
		} else {
			cur = try
		}
	}
	if cur != "" {
		lines = append(lines, cur)
	}
	return lines
}

func (c *canvas) spr(name string, x, y, w, h float64) {
	if _, ok := sprites[name]; !ok {
		return
	}
	name = designScale(name)
	tint := color.NRGBA{}
	if name == "stats_plate" {
		tint = color.NRGBA{255, 255, 255, 0x8C} // color.stats.plate
	}
	c.draw9(name, image.Rect(int(math.Round(x*gK)), int(math.Round(y*gK)), int(math.Round((x+w)*gK)), int(math.Round((y+h)*gK))), tint)
}

// sprFitTint: sprFit multiplied by a colour (Image.color).
func (c *canvas) sprFitTint(name string, x, y, w, h float64, tint color.NRGBA) {
	if tint.A == 0 {
		c.sprFit(name, x, y, w, h)
		return
	}
	s, ok := sprites[name]
	if !ok {
		return
	}
	tn := name + "@" + string(rune(tint.R)) + string(rune(tint.G)) + string(rune(tint.B))
	if _, ok := sprites[tn]; !ok {
		sprites[tn] = spr{tinted(s.img, tint), s.b, s.typ, s.fill, s.piv}
	}
	c.sprFit(tn, x, y, w, h)
}

// icon keeps aspect inside the box (Image.preserveAspect).
func (c *canvas) sprFit(name string, x, y, w, h float64) {
	s, ok := sprites[name]
	if !ok {
		return
	}
	sw, sh := float64(s.img.Bounds().Dx()), float64(s.img.Bounds().Dy())
	f := math.Min(w/sw, h/sh)
	dw, dh := sw*f, sh*f
	c.spr(name, x+(w-dw)/2, y+(h-dh)/2, dw, dh)
}

type stEffect struct {
	text string
	bad  bool
}

func itoa(n int) string { return strconv.Itoa(n) }

// cmdCompareStats [variant]: "" = the mockup data; "full" = 4 dishes + 25 active effects; "empty" = no food, no effects.
func cmdCompareStats() {
	variant := ""
	if len(os.Args) > 2 {
		variant = os.Args[2]
	}
	suffix := ""
	if variant != "" {
		suffix = "-" + variant
	}
	loadSprites()
	loadStatsTokens()
	mock := loadPNG(statsMock)
	// background: the mockup's scene outside the window, a dim slate inside
	if len(os.Args) > 3 && os.Args[3] != "" {
		gK, _ = strconv.ParseFloat(os.Args[3], 64)
		suffix += "-" + os.Args[3]
	}
	bg := resize(mock, int(1920*gK), int(1080*gK))
	fill(bg, image.Rect(int(36*gK), int(40*gK), int(1884*gK), int(1062*gK)), color.NRGBA{4, 9, 12, 255})
	c := &canvas{img: bg, K: 1}
	// window glass, cards, frame halves, plaque, title, close
	sample := statsSample()
	effects := []stEffect{{"Медовуха сопротивления огню", false}, {"Мокрый: −25% реген. выносливости", true}}
	switch variant {
	case "full":
		sample[4] = append([]stRow{{kind: "dish", label: "Кровяная колбаса", sub: "+25 ОЗ +12 ВН +30 ЭЙ, реген 3", value: "12:05", dish: image.Rect(94, 680, 168, 733)}}, sample[4]...)
		names := []string{"Отдохнувший 12:30", "Укрытие", "Медовуха здоровья 4:10", "Медовуха выносливости 3:55", "Сила Эйктюра 4:41", "Тёплый", "Мокрый: −25% реген. выносливости", "Медовуха сопротивления огню 9:12", "Сытость", "Медовуха сопротивления яду 7:01", "Холод: −50% реген. здоровья", "Бодрость 2:15", "Защита от мороза", "Дар Модера 3:40", "Эйтр-настой 5:20", "Отравлен: −3/с", "Лёгкость", "Шёпот предков", "Сила троля 6:33", "Ловкость рыси 2:02", "Ярость берсерка 0:45", "Тихая поступь", "Опьянение 1:12", "Каменная кожа 8:18", "Свет Мунина"}
		effects = effects[:0]
		for _, n := range names {
			effects = append(effects, stEffect{n, strings.Contains(n, ":") && strings.Contains(n, "−")})
		}
	case "empty":
		var rows []stRow
		for _, r := range sample[4] {
			if r.kind != "dish" {
				rows = append(rows, r)
			}
		}
		sample[4] = rows
		sample[4][0].value = "0 / 3"
		effects = nil
	}
	for _, p := range statsPieces {
		k := "size.stats.piece." + p.name
		c.spr(p.sprite, st(k+".x", p.x), st(k+".y", p.y), st(k+".w", p.w), st(k+".h", p.h))
	}
	for i := 0; i < 8; i++ {
		statsCard(c, i, sample[i], mock)
	}
	c.textBox("Характеристики", serif, st("size.stats.font.title", 52), 30, colHead, st("size.stats.title.x", 760), st("size.stats.title.y", 34), st("size.stats.title.w", 522), st("size.stats.title.h", 62), 1, st("size.stats.title.spacing", 6), true)
	// active effects band: entries flow centred, the rest counted in "+N" (StatsWindow.LayoutStrip)
	y, h := st("size.stats.strip.y", 1000), st("size.stats.strip.h", 44)
	capt := "Активные эффекты:"
	capSp := st("size.stats.caption.spacing", 3)
	capF := st("size.stats.font.caption", 21)
	capW := measure(strings.ToUpper(capt), serif, capF, capSp)
	fs := st("size.stats.font.strip", 20)
	ic, gap := st("size.stats.strip.icon", 28), st("size.stats.strip.gap", 16)
	maxW := st("size.stats.strip.w", 1500)
	widths, shown := 0.0, 0
	for i, e := range effects {
		ew := gap + 10 + gap + ic + 8 + measure(e.text, sans, fs, 0)
		hint := 0.0
		if i+1 < len(effects) {
			hint = st("size.stats.strip.more", 90)
		}
		if capW+widths+ew+hint > maxW {
			break
		}
		widths += ew
		shown++
	}
	more := ""
	switch {
	case len(effects) == 0:
		more = "нет"
	case shown < len(effects):
		more = "+" + itoa(len(effects)-shown) + " ещё"
	}
	moreW := 0.0
	if more != "" {
		moreW = measure(more, sans, fs, 0) + gap
	}
	x := (1920 - (capW + widths + moreW)) / 2
	c.textBox(capt, serif, capF, 14, colHead, x, y, capW+4, h, 0, capSp, true)
	x += capW
	icons := []image.Rectangle{image.Rect(674, 1004, 702, 1040), image.Rect(1084, 1004, 1108, 1039)}
	for i := 0; i < shown; i++ {
		e := effects[i]
		c.textBox("|", sans, fs, fs, colGold, x+gap, y, 10, h, 1, 0, false)
		x += gap + 10 + gap
		over(c.img, resize(keyDark(crop(mock, icons[i%2])), int(ic*gK), int(ic*gK)), image.Pt(int(x*gK), int((y+(h-ic)/2)*gK)))
		x += ic + 8
		col := colGood
		if e.bad {
			col = colBad
		}
		w := measure(e.text, sans, fs, 0)
		c.textBox(e.text, sans, fs, fs, col, x, y, w+4, h, 0, 0, false)
		x += w
	}
	if more != "" {
		c.textBox(more, sans, fs, fs, colSub, x+gap, y, moreW+4, h, 0, 0, false)
	}
	out := "art/review/compose-stats" + suffix + ".png"
	must(os.MkdirAll("art/review", 0o755))
	savePNG(out, c.img)
	sideBySide(resize(mock, c.img.Bounds().Dx(), c.img.Bounds().Dy()), c.img, "art/review/compare-stats"+suffix+".png")
}

func keyDark(p *image.NRGBA) *image.NRGBA {
	for i := 0; i < len(p.Pix); i += 4 {
		mx := max(p.Pix[i], p.Pix[i+1], p.Pix[i+2])
		if mx < 50 {
			p.Pix[i+3] = 0
		} else if mx < 80 {
			p.Pix[i+3] = uint8(int(mx-50) * 255 / 30)
		}
	}
	return p
}

func statsCard(c *canvas, i int, rows []stRow, mock *image.NRGBA) {
	col, row := float64(i%4), float64(i/4)
	bx := st("size.stats.card.x0", 68) + col*st("size.stats.card.px", 448.3)
	by := st("size.stats.card.y0", 133) + row*st("size.stats.card.py", 435)
	bw, bh := st("size.stats.card.w", 439), st("size.stats.card.h", 421)
	pad := st("size.stats.card.pad", 20)
	headH := st("size.stats.card.header", 88)
	hs := sprites["stats_head_"+statsHeads[i]]
	sw, sh := float64(hs.img.Bounds().Dx()), float64(hs.img.Bounds().Dy())
	f := math.Min(1, math.Min(st("size.stats.card.icon.max", 150)/sw, st("size.stats.card.icon.maxh", 92)/sh))
	iconW, iconH := sw*f, sh*f
	tx := bx + pad + iconW + st("size.stats.card.title.gap", 16)
	tw := bx + bw - pad - tx
	size := st("size.stats.font.header", 27.5)
	spacing := st("size.stats.header.spacing", 1.5)
	full := size
	lines := wrapLines(statsTitles[i], serif, size, spacing, tw)
	tooWide := func(ls []string, sz float64) bool {
		for _, l := range ls {
			if measure(l, serif, sz, spacing) > tw {
				return true
			}
		}
		return false
	}
	for (len(lines) > 2 || tooWide(lines, size)) && size > st("size.stats.font.header.min", 22) {
		size -= 0.5
		lines = wrapLines(statsTitles[i], serif, size, spacing, tw)
	}
	if len(lines) > 2 {
		size = full
		lines = wrapLines(statsTitles[i], serif, size, spacing, tw)
	}
	lineH := size * 1.21 * (1 + st("size.stats.header.line", -4)/100)
	headH = math.Max(headH, float64(len(lines))*lineH+st("size.stats.card.header.pad", 16))
	c.sprFit("stats_head_"+statsHeads[i], bx+pad, by+(headH-iconH)/2+2, iconW, iconH)
	boxH := headH - 8
	ty := by + st("size.stats.card.title.top", 10) + (boxH-float64(len(lines))*lineH)/2
	for _, l := range lines {
		c.textBox(l, serif, size, size, colGold, tx, ty, tw, lineH, 0, spacing, false)
		ty += lineH
	}
	dh := st("size.stats.divider.h", 8)
	c.spr("stats_divider", bx+pad, by+headH-dh/2, bw-2*pad, dh)
	gapTop := st("size.stats.card.gap", 14)
	if i == 0 {
		gapTop = st("size.stats.card.gap.res", 6)
	}
	viewTop := by + headH + gapTop
	vx, vw := bx+pad-4, bw-2*pad+8
	y := viewTop
	vBottom := by + bh - st("size.stats.card.bottom", 16)
	// food: dish rows shrink evenly to fit (StatsWindow.CompressDishes / StatsMath.Compress)
	dishH := rowHeight(i, "dish")
	if i == 4 {
		total, dishes := 0.0, 0
		for _, r := range rows {
			total += rowHeight(i, r.kind)
			if r.kind == "dish" {
				dishes++
			}
		}
		if dishes > 0 && total > vBottom-viewTop {
			dishH = math.Max(st("size.stats.row.dish.min", 54), dishH-(total-(vBottom-viewTop))/float64(dishes))
		}
	}
	for _, r := range rows {
		h := rowHeight(i, r.kind)
		if r.kind == "dish" {
			h = dishH
		}
		if y+h > vBottom+0.5 {
			break // scrolls in game
		}
		statsRow(c, r, vx+4, y, vw-8, h, mock)
		y += h
	}
}

func statsRow(c *canvas, r stRow, x, y, w, h float64, mock *image.NRGBA) {
	label, value, sub := st("size.stats.font.label", 21), st("size.stats.font.value", 22), st("size.stats.font.sub", 16)
	iconW := st("size.stats.icon", 30)
	textX := x + st("size.stats.label.x", 50)
	right := x + w - st("size.stats.value.pad", 10)
	vcol := r.vcol
	if vcol == "" {
		vcol = colValue
	}
	switch r.kind {
	case "divider":
		c.spr("stats_divider", x, y+h/2-1.5, w, 3)
		return
	case "bar":
		bh := st("size.stats.bar.h", 22)
		bx, by, bw := textX-6, y+(h-bh)/2-2, right-textX+6
		c.spr("stats_bar_trough", bx, by, bw, bh)
		inset := st("size.stats.bar.inset", 4)
		fw := (bw - 2*inset) * r.fill
		c.spr("stats_bar_"+r.bar, bx+inset, by+inset, fw, bh-2*inset)
		return
	case "cell":
		half := (w - 6) / 2
		cell := func(cx float64, icon, l, v, vc string) {
			if vc == "" {
				vc = colValue
			}
			cy, ch := y+2, h-6
			c.spr("stats_plate", cx, cy, half, ch)
			iw := st("size.stats.icon.cell", 32)
			c.sprFitTint("stats_icon_"+icon, cx+6, cy+(ch-iw)/2, iw, iw, elemTint(icon))
			lx := cx + iw + 14
			vw := st("size.stats.cell.value.w", 52)
			c.textBox(l, sans, label, st("size.stats.font.min", 14), colBody, lx, cy, cx+half-vw-6-lx, ch, 0, 0, false)
			c.textBox(v, sans, value, value, vc, cx+half-vw-6, cy, vw, ch, 2, 0, false)
		}
		cell(x, r.icon, r.label, r.value, r.vcol)
		cell(x+half+6, r.icon2, r.label2, r.value2, r.v2col)
		return
	case "dish":
		c.spr("stats_plate", x, y+2, w, h-4)
		ih := h - 14
		over(c.img, resize(keyDark(crop(mock, r.dish)), int(ih*1.25*gK), int(ih*gK)), image.Pt(int((x+6)*gK), int((y+7)*gK)))
		nx := x + st("size.stats.dish.text.x", 100)
		timerW := st("size.stats.dish.timer.w", 66)
		glass := st("size.stats.dish.glass", 22)
		c.textBox(r.label, sans, label, label, colBody, nx, y+6, right-timerW-glass-8-nx, h*0.45, 0, 0, false)
		c.textBox(r.sub, sans, st("size.stats.font.dish.sub", 17.5), 12, colGood, nx, y+h*0.48, right-timerW-glass-8-nx, h*0.42, 0, 0, false)
		c.sprFit("stats_icon_hourglass", right-timerW-glass-2, y+(h-glass)/2, glass, glass)
		c.textBox(r.value, sans, value, value, colValue, right-timerW, y, timerW, h, 2, 0, false)
		return
	case "note":
		ni := st("size.stats.icon.note", 22)
		c.sprFit("stats_icon_"+r.icon, x+14, y+(h-ni)/2, ni, ni)
		c.textBox(r.label, sans, st("size.stats.font.note", 17.5), 12, colSub, x+56, y, right-x-56, h, 0, 0, false)
		return
	}
	tall := r.kind == "tall" || r.kind == "tallsub"
	if r.kind != "name" {
		c.spr("stats_plate", x, y+2, w, h-4)
	}
	iw := iconW
	if tall {
		iw = st("size.stats.icon.tall", 42)
		textX = x + st("size.stats.label.x.tall", 74)
	}
	lineH := h
	switch r.kind {
	case "pairsub":
		lineH = h * 0.56
	case "tallsub":
		lineH = h * 0.68
	}
	iy := y + (h-iw)/2
	if r.kind == "pairsub" {
		iy = y + (lineH-iw)/2
	}
	c.sprFit("stats_icon_"+r.icon, x+6, iy, iw, iw)
	valueW := (right - textX) * 0.5
	if r.kind == "wide" {
		valueW = (right - textX) * 0.66
	}
	if r.kind == "name" {
		valueW = 1
	}
	c.textBox(r.label, sans, label, label, colBody, textX, y, right-textX-valueW-6, lineH, 0, 0, false)
	if r.value != "" {
		c.textBox(r.value, sans, value, st("size.stats.font.min", 14), vcol, right-valueW, y, valueW, lineH, 2, 0, false)
	}
	if r.sub != "" {
		scol := r.scol
		if scol == "" {
			scol = colSub
		}
		sy, sh := y+lineH-6, 0.0
		sh = y + h - sy
		if r.kind == "tallsub" {
			sy, sh = y+h*0.56, h*0.32
		}
		c.textBox(r.sub, sans, sub, sub, scol, textX, sy, right-textX, sh, 2, 0, false)
	}
}
