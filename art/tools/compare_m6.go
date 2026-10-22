package main

import (
	"fmt"
	"image"
)

// M6 review composites (1920x1080 mockup px): the picked vanilla in-game screens m6-build-a (+ stealth eye from B),
// m6-trader-a, m6-text-a (+ raven bubble from B), m6-load-b, m6-ship-b, m6-dialogs-a rebuilt from our sprites over a
// blurred copy of the scene. Item / piece / portrait icons, the owned HUD cluster and minimap are content copied
// from the mockups.

const ember = "#e0413a"

// titleKnots: serif caps title on the panel top rail with the kit knot ornament left and right (mockup look; no plaque).
func (c *canvas) titleKnots(cx, cy float64, title string, fs float64) {
	c.text(title, serif, fs, palette["text_header"], cx, cy, 1)
	w := float64(len([]rune(title))) * fs * 0.72
	kw, kh := fs*2.6, fs*1.1
	c.sprite("knot", cx-w/2-kw-22, cy-kh/2, kw, kh)
	c.spriteFlipX("knot", cx+w/2+22, cy-kh/2, kw, kh)
}

// progressScaled: bar_frame_action (window inset L/R 40, T/B 9 sprite px) over bar_trough + textured bar_fill (tint col).
func (c *canvas) progressScaled(x, y, w, h, frac float64, col string) {
	ix, iy := 40/c.K, 9/c.K
	c.sprite("bar_trough", x+ix, y+iy, w-2*ix, h-2*iy)
	c.tint("bar_fill", x+ix, y+iy, (w-2*ix)*frac, h-2*iy, col, 1)
	c.sprite("bar_frame_action", x, y, w, h)
}

// keyHint: plate_key with a key label + sans label right of it; returns the x after the label.
func (c *canvas) keyHint(x, cy float64, key, label string, kw float64) float64 {
	c.sprite("plate_key", x, cy-17, kw, 34)
	c.text(key, sans, 19, palette["text_body"], x+kw/2, cy, 1)
	if label != "" {
		c.text(label, sans, 20, palette["text_body"], x+kw+10, cy, 0)
	}
	return x + kw + 10 + float64(len([]rune(label)))*10.5 + 24
}

func compareM6Build() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m6-build-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.copyMock(mock, 790, 150, 330, 290) // placement ghost (world content)
	c.copyMock(mock, 30, 690, 460, 350)  // owned HUD cluster
	c.copyMock(mock, 1600, 20, 300, 310) // owned minimap
	// crosshair + stealth eye (from B) + hover text + piece health
	c.glyph("glyph_stealth_eye", 960, 340, 40, body, 0.75)
	c.tint("bar_skill_fill", 936, 360, 48, 5, palette["rune_glow"], 0.9)
	c.glyph("glyph_crosshair", 960, 380, 24, body, 0.95)
	c.keyHint(905, 453, "E", "Использовать", 38)
	c.sprite("bar_skill_trough", 888, 477, 162, 14)
	c.tint("bar_skill_fill", 890, 479, 158*0.95, 10, palette["text_ok"], 1)
	// piece selection window
	c.panel(497, 585, 980, 482)
	c.sprite("tab_strip", 540, 640, 895, 12)
	for i, t := range []string{"Разное", "Ремесло", "Строительство", "Мебель", "Оборона", "Стены"} {
		x := 547 + float64(i)*133.5
		n, col := "tab_normal", body
		if i == 2 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, x, 603, 127, 38)
		c.text(t, serif, 20, col, x+63, 621, 1)
	}
	for r := 0; r < 4; r++ {
		for k := 0; k < 10; k++ {
			x, y := 528+float64(k)*88.4, 657+float64(r)*67
			n := "slot_cell_recess"
			if r == 0 && k == 2 {
				n = "slot_cell_selected"
			}
			c.sprite(n, x, y, 82, 62)
			c.icon(mock, x+6, y+5, 70, 52)
		}
	}
	c.sprite("scroll_track", 1442, 660, 14, 258)
	c.sprite("scroll_handle_normal", 1442, 668, 14, 90)
	// selected piece strip
	c.sprite("frame_inset", 513, 935, 948, 118)
	c.sprite("slot_cell_recess", 530, 944, 98, 96)
	c.icon(mock, 540, 952, 78, 80)
	c.text("Деревянная стена", serif, 28, head, 650, 966, 0)
	c.text("Простая стена из дерева для вашего дома.", sans, 17, body, 650, 998, 0)
	for i, ok := range []bool{true, false, true} {
		x := 1040 + float64(i)*86
		n := "socket_req"
		if !ok {
			n = "socket_req_missing"
		}
		c.sprite(n, x, 948, 74, 84)
		c.icon(mock, x+8, 954, 58, 56)
	}
	c.text("2/4", sans, 19, palette["text_ok"], 1077, 1019, 1)
	c.text("0/1", sans, 19, palette["text_missing"], 1163, 1019, 1)
	c.keyHint(1330, 1000, "ПКМ", "Выбор", 56)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM6Trader() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m6-trader-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	body, gold := palette["text_body"], palette["text_active"]
	c.copyMock(mock, 1620, 20, 290, 300) // owned minimap
	c.panel(347, 75, 762, 915)
	c.titleKnots(728, 115, "ХАЛЬДОР", 42)
	c.sprite("tab_strip", 400, 205, 655, 12)
	c.sprite("tab_selected", 461, 156, 261, 50)
	c.text("Купить", serif, 28, gold, 591, 181, 1)
	c.sprite("tab_normal", 742, 156, 261, 50)
	c.text("Продать", serif, 28, body, 873, 181, 1)
	c.sprite("frame_inset", 390, 220, 670, 495)
	items := []struct{ n, p string }{{"Мегингьорд", "950"}, {"Рыболовная удочка", "350"}, {"Ячмень x10", "100"},
		{"Фонарь Ымира", "120"}, {"Обруч дворфов", "620"}, {"Плоть Ымира", "120"}, {"Рыболовная наживка x20", "10"},
		{"Громовой камень", "1 500"}}
	for i, it := range items {
		y := 225 + float64(i)*60.3
		st := "normal"
		if i == 0 {
			st = "selected"
		}
		c.row(st, 397, y, 645, 58)
		c.sprite("slot_cell_recess", 401, y+3, 76, 52)
		c.icon(mock, 410, y+7, 58, 44)
		c.text(it.n, sans, 23, body, 495, y+29, 0)
		col := body
		if i == len(items)-1 {
			col = palette["text_missing"]
		}
		c.text(it.p, sans, 23, col, 980, y+29, 2)
		c.glyph("glyph_coins", 1010, y+29, 32, gold, 1)
	}
	c.sprite("scroll_track", 1058, 230, 14, 470)
	c.sprite("scroll_handle_normal", 1058, 238, 14, 110)
	c.sprite("frame_inset", 390, 728, 670, 172)
	c.sprite("slot_cell_recess", 402, 742, 150, 145)
	c.icon(mock, 418, 762, 118, 105)
	c.text("МЕГИНГЬОРД", serif, 32, palette["text_item_title"], 592, 763, 0)
	for i, s := range []string{"Пояс, наделяющий владельца", "силой великана.", "Увеличивает переносимый вес на 150."} {
		c.text(s, sans, 21, body, 592, 806+float64(i)*29, 0)
	}
	c.sprite("frame_small", 396, 906, 328, 68)
	c.icon(mock, 410, 916, 50, 50) // purse icon (content)
	c.text("Ваши монеты: 1 240", sans, 21, body, 467, 940, 0)
	c.glyph("glyph_coins", 688, 940, 30, gold, 1)
	c.btn("button_normal", 738, 903, 336, 74, "Купить", gold, 34)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM6Text() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m6-text-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.copyMock(mock, 1670, 15, 240, 240) // owned minimap
	c.copyMock(mock, 20, 930, 520, 120)  // owned HUD
	// sign input
	c.panel(181, 400, 724, 292)
	c.text("НАДПИСЬ НА ТАБЛИЧКЕ", serif, 28, head, 543, 435, 1)
	c.sprite("field_search", 244, 495, 598, 64)
	c.tint("ring_select", 240, 491, 606, 72, palette["rune_glow"], 0.8)
	c.text("Медовая кладовая |", sans, 26, body, 264, 527, 0)
	c.text("16/64", sans, 18, palette["text_disabled"], 836, 577, 2)
	c.btn("button_selected", 227, 610, 315, 60, "Готово", gold, 27)
	c.btn("button_normal", 553, 610, 310, 60, "Отмена", head, 27)
	// runestone viewer
	c.panel(940, 258, 876, 595)
	c.titleKnots(1373, 293, "РУНИЧЕСКИЙ КАМЕНЬ", 31)
	c.sprite("frame_inset", 966, 330, 800, 445)
	c.sprite("band_runes", 1000, 360, 740, 30)
	c.sprite("band_runes", 1000, 727, 740, 30)
	for i, s := range []string{"Из моря поднялся наш первый чертог.", "Под сводами ясеня клятвы звучали.",
		"Мы шли за вороном сквозь ветер и мрак,", "И руны на камне наш путь сохраняли.",
		"Пусть помнит потомок у тихих огней:", "Отвага живёт дольше славы царей."} {
		c.text(s, serif, 31, body, 1385, 433+float64(i)*48.3, 1)
	}
	c.sprite("scroll_track", 1757, 340, 14, 440)
	c.sprite("scroll_handle_normal", 1757, 425, 14, 70)
	c.btn("button_normal", 1205, 788, 355, 62, "", head, 26)
	c.keyHint(1290, 819, "E", "Закрыть", 40)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

// raven tutorial bubble (from m6-text-b, top-right)
func compareM6Raven() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m6-text-b.png")
	c := newCanvas(mock, 1920, 1080)
	body := palette["text_body"]
	x, y, w, h := ravenBox[0], ravenBox[1], ravenBox[2], ravenBox[3]
	c.copyMock(blurBG(mock), x-10, y-10, w+20, h+20)
	c.panel(x, y, w, h)
	c.icon(mock, x+55, y+25, 90, 75) // raven portrait (content)
	c.text("Дай имя порталу,", sans, 22, body, x+152, y+47, 0)
	c.text("чтобы найти дорогу домой.", sans, 22, body, x+152, y+79, 0)
	c.btn("button_normal", x+140, y+h-24, 215, 48, "[E] Понятно", palette["text_header"], 19)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

var ravenBox = [4]float64{1403, 62, 477, 132}

func compareM6Load() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m6-load-b.png")
	c := newCanvas(mock, 1920, 1080) // loading art + portal mist = content
	head, body := palette["text_header"], palette["text_body"]
	bl := blurBG(mock)
	fill(c.img, c.R(80, 735, 800, 75), hex("#0c1016"))
	c.copyMock(bl, 20, 810, 920, 210)
	c.copyMock(bl, 980, 810, 920, 210)
	c.copyMock(bl, 1220, 175, 440, 440)
	// respawn: no real progress -> rune spinner, tip banner
	c.sprite("button_round", 425, 718, 110, 110)
	c.tint("spinner_runes", 437, 730, 86, 86, palette["rune_glow"], 1)
	c.panel(38, 822, 885, 183)
	c.titleKnots(480, 862, "ВОЗРОЖДЕНИЕ", 36)
	c.sprite("divider", 130, 884, 700, 14)
	c.text("После смерти ваши вещи остаются в могиле.", sans, 22, body, 480, 917, 1)
	c.text("Вернитесь к ней, чтобы забрать снаряжение.", sans, 22, body, 480, 950, 1)
	// teleport: ring + swirl
	c.sprite("minimap_ring", 1240, 195, 400, 400)
	c.tint("glyph_teleport_swirl", 1300, 255, 280, 280, palette["rune_glow"], 1)
	c.text("Перемещение...", serif, 46, head, 1443, 652, 1)
	c.panel(998, 822, 885, 183)
	c.sprite("divider", 1090, 884, 700, 14)
	c.text("Порталы соединяют далекие берега.", sans, 22, body, 1440, 917, 1)
	c.text("Проверьте имя портала перед путешествием.", sans, 22, body, 1440, 950, 1)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM6Ship() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m6-ship-b.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.copyMock(mock, 1160, 0, 400, 760)  // sail + ship (world)
	c.copyMock(mock, 30, 770, 540, 290)  // owned HUD cluster
	c.copyMock(mock, 1580, 20, 300, 320) // owned minimap
	// raid / event line
	c.sprite("band_message", 640, 22, 640, 52)
	c.sprite("banner_rule", 718, 36, 110, 22)
	c.spriteFlipX("banner_rule", 1092, 36, 110, 22)
	c.text("Скелеты наступают!", serif, 30, ember, 960, 47, 1)
	// ship speed ladder (vertical) + rudder
	c.popup(1757, 363, 133, 452)
	gl := []string{"glyph_ship_sail_full", "glyph_ship_sail_half", "glyph_ship_oar", "glyph_stop", "glyph_chevron_down"}
	for i, g := range gl {
		y := 408 + float64(i)*63
		col := body
		if i == 1 {
			c.sprite("slot_cell_selected", 1785, y-30, 76, 60)
			col = palette["rune_glow"]
		}
		if i > 0 {
			c.sprite("divider", 1785, y-34, 76, 6)
		}
		c.glyph(g, 1823, y, 46, col, 1)
	}
	c.sprite("button_round", 1772, 700, 104, 104)
	c.glyph("glyph_rudder", 1824, 752, 70, body, 1)
	c.tint("glyph_wind", 1814, 700, 20, 20, palette["rune_glow"], 1)
	// mount panel
	c.popup(33, 423, 415, 105)
	c.sprite("button_round", 48, 430, 94, 94)
	c.icon(mock, 58, 440, 74, 74)
	c.text("Лохматый", serif, 23, head, 165, 451, 0)
	c.sprite("bar_skill_trough", 158, 467, 258, 20)
	c.tint("bar_skill_fill", 160, 470, 238, 14, "#c0392b", 1)
	c.sprite("bar_skill_trough", 158, 495, 258, 20)
	c.tint("bar_skill_fill", 160, 498, 222, 14, "#e8c04a", 1)
	// chat
	c.popup(33, 545, 542, 215)
	for i, l := range []struct{ n, s string }{{"Эйрик:", "Берём курс на юг."}, {"Сигрид:", "Ветер попутный."},
		{"Эйрик:", "Вижу берег."}, {"Сигрид:", "Причалим у леса."}, {"Эйрик:", "Держись крепче."}} {
		y := 575 + float64(i)*30
		c.text(l.n, sans, 19, gold, 62, y, 0)
		c.text(l.s, sans, 19, body, 62+float64(len([]rune(l.n)))*10+8, y, 0)
	}
	c.sprite("field_search", 55, 712, 497, 40)
	c.text("Написать сообщение...", sans, 18, palette["text_disabled"], 72, 732, 0)
	// action progress
	c.progressScaled(752, 936, 416, 38, 0.3, palette["rune_glow"])
	c.text("Ремонт", serif, 24, head, 960, 955, 1)
	// key hints
	x := 1350.0
	for _, k := range []struct{ k, l string }{{"W", "Парус"}, {"S", "Снизить"}, {"A/D", "Руль"}, {"E", "Отпустить"}} {
		kw := 36.0
		if len(k.k) > 1 {
			kw = 58
		}
		x = c.keyHint(x, 1031, k.k, k.l, kw) - 14
	}
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM6Dialogs() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m6-dialogs-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.copyMock(mock, 1640, 20, 260, 290) // owned minimap
	c.copyMock(mock, 35, 935, 690, 110)  // owned HUD
	// compendium
	c.panel(87, 143, 940, 742)
	c.titleKnots(552, 176, "СВЕДЕНИЯ", 38)
	c.sprite("tab_strip", 130, 252, 860, 12)
	c.sprite("tab_selected", 351, 214, 196, 42)
	c.text("Тексты", sans, 21, gold, 449, 235, 1)
	c.sprite("tab_normal", 563, 214, 198, 42)
	c.text("Трофеи", sans, 21, body, 662, 235, 1)
	c.sprite("frame_inset", 116, 264, 354, 584)
	for i, t := range []string{"Вороны Одина", "Пчёлы", "Зелья", "Кузница", "Первые шаги", "Дом и очаг", "Пища", "Море", "Чёрный лес"} {
		y := 272 + float64(i)*64.3
		st := "normal"
		if i == 0 {
			st = "selected"
		}
		c.row(st, 125, y, 336, 58)
		c.icon(mock, 136, y+6, 50, 46)
		c.text(t, sans, 21, body, 205, y+30, 0)
		if i == 0 || i == 1 || i == 3 || i == 4 || i == 6 {
			c.sprite("pip_quality_full", 426, y+18, 20, 21)
		}
	}
	c.sprite("frame_strip", 474, 262, 528, 588)
	c.text("Вороны Одина", serif, 36, palette["text_item_title"], 504, 305, 0)
	for i, s := range []string{"Хугин и Мунин — глаза Всеотца.", "Они видят всё, что скрыто от людей.",
		"Ворон прилетит, когда придёт пора.", "Выслушай его и запомни совет.", "В этом мире знание бережёт жизнь.",
		"Исследуй леса и древние руины.", "Новые открытия останутся здесь.", "Пусть Один направляет твой путь."} {
		c.text(s, sans, 21, body, 504, 368+float64(i)*37.4, 0)
	}
	c.sprite("scroll_track", 972, 280, 14, 545)
	c.sprite("scroll_handle_normal", 972, 380, 14, 80)
	// split stack
	c.panel(1088, 150, 515, 275)
	c.text("Разделить: Дерево", serif, 30, head, 1347, 184, 1)
	c.sprite("slot_cell_recess", 1127, 225, 92, 92)
	c.icon(mock, 1135, 232, 76, 78)
	c.slider(1240, 256, 318, 18, 0.47, "slider_handle_hover")
	c.text("25 / 50", sans, 25, body, 1393, 303, 1)
	c.btn("button_selected", 1125, 340, 217, 58, "Разделить", gold, 24)
	c.btn("button_normal", 1355, 340, 215, 58, "Отмена", head, 24)
	// barber
	c.panel(1112, 455, 685, 507)
	c.header(1270, 462, 380, 54, "", "ВНЕШНОСТЬ")
	for k, t := range []string{"Волосы", "Борода"} {
		c.text(t, sans, 22, body, 1172, 541+float64(k)*137, 0)
		for i := 0; i < 4; i++ {
			x, y := 1237+float64(i)*116.5, 562+float64(k)*134
			n := "slot_cell_recess"
			if i == 0 {
				n = "tab_square_selected"
			}
			c.sprite(n, x, y, 94, 92)
			c.icon(mock, x+10, y+8, 74, 76)
		}
	}
	c.text("Цвет волос", sans, 22, body, 1172, 812, 0)
	c.sprite("bar_skill_trough", 1172, 833, 565, 34)
	c.copyMock(mock, 1180, 840, 549, 20) // vanilla colour gradient = content
	c.sprite("slider_handle_hover", 1522, 828, 44, 44)
	c.btn("button_selected", 1225, 884, 220, 60, "Применить", gold, 25)
	c.btn("button_normal", 1467, 884, 220, 60, "Отмена", head, 25)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func cmdCompareM6() {
	loadSprites()
	palette = loadSpec().Palette
	bm, bo := compareM6Build()
	tm, to := compareM6Trader()
	xm, xo := compareM6Text()
	rm, ro := compareM6Raven()
	lm, lo := compareM6Load()
	sm, so := compareM6Ship()
	dm, dO := compareM6Dialogs()
	sideBySide(bm, bo, "art/review/compare-build-m6.png")
	sideBySide(tm, to, "art/review/compare-trader-m6.png")
	sideBySide(xm, xo, "art/review/compare-text-m6.png")
	sideBySide(lm, lo, "art/review/compare-load-m6.png")
	sideBySide(sm, so, "art/review/compare-ship-m6.png")
	sideBySide(dm, dO, "art/review/compare-dialogs-m6.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	rb := ravenBox
	list := []pair{
		same("build: piece window tabs + grid   [m6-build-a | ours]", bm, bo, R(490, 575, 995, 360)),
		same("build: selected piece + requirements + key hint", bm, bo, R(490, 925, 995, 150)),
		same("build: crosshair, stealth eye (B), hover text, piece health", bm, bo, R(850, 290, 240, 210)),
		same("trader: header, tabs, price list   [m6-trader-a | ours]", tm, to, R(340, 65, 780, 660)),
		same("trader: detail + purse + buy button", tm, to, R(340, 715, 780, 280)),
		same("sign input dialog   [m6-text-a | ours]", xm, xo, R(170, 390, 745, 315)),
		same("runestone text viewer", xm, xo, R(930, 250, 900, 615)),
		same("tutorial raven bubble   [m6-text-b | ours]", rm, ro, R(int(rb[0])-20, int(rb[1])-20, int(rb[2])+40, int(rb[3])+40)),
		same("respawn: spinner + tip banner   [m6-load-b | ours]", lm, lo, R(20, 720, 920, 300)),
		same("teleport: rune ring + swirl + tip", lm, lo, R(980, 170, 920, 850)),
		same("ship: speed ladder + rudder   [m6-ship-b | ours]", sm, so, R(1740, 350, 165, 480)),
		same("mount panel + chat", sm, so, R(20, 410, 570, 360)),
		same("raid line + action progress + key hints", sm, so, R(620, 10, 680, 80)),
		same("action progress + key hints", sm, so, R(740, 925, 1170, 130)),
		same("compendium list + reading pane   [m6-dialogs-a | ours]", dm, dO, R(80, 135, 960, 760)),
		same("split stack + barber", dm, dO, R(1080, 140, 730, 830)),
	}
	groupsM2(list, "art/review/compare-groups-m6.png")
	fmt.Println("compare-m6 done")
}
