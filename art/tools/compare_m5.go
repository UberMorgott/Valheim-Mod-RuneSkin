package main

import (
	"fmt"
	"image"
)

// M5 review composites (1920x1080 mockup px): the picked pre-world screens m5-menu-a, m5-char-a, m5-world-a,
// m5-load-a, m5-settings-a rebuilt from our sprites over a blurred copy of the scene. Logo, portraits, world
// thumbnails, character-creation swatches/colour gradients and the loading art are content copied from the mockups.

// btn: stone button at a UI scale whose native height fills h, label centred (serif caps).
func (c *canvas) btn(name string, x, y, w, h float64, label, col string, fs float64) {
	drawFrameScaled(c, name, c.R(x, y, w, h))
	if label != "" {
		c.text(label, serif, fs, col, x+w/2, y+h/2, 1)
	}
}

// slider: carved groove track, rune_glow fill to frac, diamond handle.
func (c *canvas) slider(x, y, w, h, frac float64, handle string) {
	c.sprite("bar_skill_trough", x, y-2, w, h+4)
	c.tint("bar_skill_fill", x+2, y+2, (w-4)*frac, h-4, palette["rune_glow"], 1)
	s := h * 2.4
	c.sprite(handle, x+w*frac-s/2, y+h/2-s/2, s, s)
}

func (c *canvas) checkbox(x, y, s float64, on bool) {
	c.sprite("checkbox_box", x, y, s, s)
	if on {
		c.glyph("glyph_check", x+s/2, y+s/2, s*0.75, palette["rune_glow"], 1)
	}
}

func compareM5Menu() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m5-menu-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.copyMock(mock, 96, 96, 580, 210) // game logo = content
	for i, s := range []string{"НАЧАТЬ ИГРУ", "ПРИСОЕДИНИТЬСЯ", "НАСТРОЙКИ", "АВТОРЫ", "ВЫХОД"} {
		n := "button_normal"
		if i == 0 {
			n = "button_hover"
		}
		c.btn(n, 96, 358+float64(i)*98, 501, 75, s, head, 30)
	}
	c.panel(1326, 305, 545, 512)
	c.header(1440, 316, 316, 56, "", "НОВОСТИ")
	c.text("Обновление 1.0.20", serif, 30, gold, 1366, 428, 0)
	for i, s := range []string{"Викинги, новый поход начинается!", "Улучшена устойчивость миров.", "Исправлены ошибки строительства.",
		"Обновлены звуки и освещение.", "Настроен баланс оружия и брони.", "Спасибо за ваши отзывы!"} {
		c.text(s, sans, 21, body, 1366, 476+float64(i)*37.7, 0)
	}
	c.sprite("scroll_track", 1812, 414, 14, 275)
	c.sprite("scroll_handle_normal", 1812, 420, 14, 90)
	c.btn("button_normal", 1431, 722, 335, 60, "ПОДРОБНЕЕ", head, 24)
	for i, g := range []string{"glyph_tab_skills", "glyph_cat_misc", "glyph_settings"} {
		x := 70 + float64(i)*91
		c.sprite("button_icon_normal", x, 951, 73, 70)
		c.glyph(g, x+36, 986, 40, palette["rune_glow"], 1)
	}
	c.text("1.0.20 • RuneSkin 0.4", sans, 18, palette["text_disabled"], 1780, 1036, 1)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM5Char() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m5-char-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.panel(44, 91, 628, 860)
	c.header(170, 106, 376, 56, "", "ПЕРСОНАЖИ")
	c.sprite("frame_inset", 70, 172, 556, 470)
	for i, n := range []string{"Моргот", "Сигрид", "Бьорн", "Хальвдан"} {
		y := 180 + float64(i)*117.5
		st := "normal"
		if i == 0 {
			st = "selected"
		}
		c.sprite("frame_inset", 87, y, 518, 104)
		if st == "selected" {
			c.row(st, 87, y, 518, 104)
		}
		c.sprite("slot_cell_recess", 96, y+8, 92, 88)
		c.icon(mock, 100, y+12, 84, 80)
		c.text(n, sans, 30, body, 210, y+38, 0)
		c.text("Последний мир: Мидгард • 12 ч", sans, 18, palette["text_disabled"], 210, y+73, 0)
	}
	c.sprite("scroll_track", 612, 186, 14, 450)
	c.sprite("scroll_handle_normal", 612, 192, 14, 110)
	// delete confirm (modal)
	c.popup(79, 654, 558, 166)
	c.text("Удалить персонажа Бьорн?", sans, 26, body, 358, 701, 1)
	c.btn("button_danger_hover", 131, 742, 218, 57, "Удалить", head, 24)
	c.btn("button_normal", 368, 742, 213, 57, "Отмена", head, 24)
	c.btn("button_normal", 96, 867, 410, 67, "Новый персонаж", head, 26)
	c.sprite("button_icon_normal", 517, 867, 97, 67)
	c.glyph("glyph_trash", 565, 900, 40, body, 1)
	c.btn("button_normal", 729, 916, 463, 96, "НАЧАТЬ", gold, 46)
	// create panel
	c.panel(1335, 134, 544, 787)
	c.header(1430, 150, 354, 52, "", "НОВЫЙ ПЕРСОНАЖ")
	c.sprite("field_search", 1384, 222, 448, 50)
	c.text("Имя...", sans, 22, palette["text_disabled"], 1402, 247, 0)
	c.text("Тип тела", sans, 21, body, 1384, 305, 0)
	for i := 0; i < 2; i++ {
		x := 1384 + float64(i)*101
		n := "tab_square_normal"
		if i == 0 {
			n = "tab_square_selected"
		}
		c.sprite(n, x, 326, 87, 88)
		c.icon(mock, x+12, 336, 63, 68)
	}
	for k, t := range []string{"Волосы", "Борода"} {
		c.text(t, sans, 21, body, 1384, 443+float64(k)*124, 0)
		for i := 0; i < 6; i++ {
			x := 1387 + float64(i)*76
			y := 466 + float64(k)*122
			n := "slot_cell_recess"
			if i == 2 {
				n = "slot_cell_selected"
			}
			c.sprite(n, x, y, 68, 68)
			c.icon(mock, x+6, y+6, 56, 56)
		}
	}
	for k, t := range []struct {
		s    string
		y, h float64
	}{{"Цвет кожи", 695, 1645}, {"Цвет волос", 770, 1705}} {
		y := t.y + 17
		c.text(t.s, sans, 21, body, 1384, t.y, 0)
		c.sprite("bar_skill_trough", 1384, y, 448, 28)
		c.copyMock(mock, 1388, y+4, 440, 20) // vanilla colour gradient = content
		c.sprite("slider_handle_normal", t.h-20, y-6, 40, 40)
		_ = k
	}
	c.btn("button_normal", 1384, 846, 448, 61, "Создать", head, 28)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM5World() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m5-world-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.panel(480, 136, 955, 808)
	c.header(760, 152, 396, 58, "", "ВЫБОР МИРА")
	c.sprite("tab_strip", 520, 286, 870, 14)
	c.sprite("tab_selected", 533, 235, 422, 56)
	c.text("МИРЫ", serif, 28, gold, 744, 263, 1)
	c.sprite("tab_normal", 968, 235, 408, 56)
	c.text("СЕРВЕРЫ", serif, 28, body, 1172, 263, 1)
	for i, n := range []string{"Мидгард", "Ётунхейм", "Тестовый"} {
		y := 315 + float64(i)*137.5
		st := "normal"
		if i == 0 {
			st = "selected"
		}
		c.row(st, 515, y, 842, 124)
		c.sprite("slot_cell_recess", 524, y+8, 197, 108)
		c.copyMock(mock, 530, y+12, 185, 100)
		c.text(n, sans, 32, body, 736, y+50, 0)
		c.text("Сид: Kx9aP2 • Последний вход: вчера", sans, 19, palette["text_disabled"], 736, y+85, 0)
		if i == 0 {
			c.sprite("button_icon_normal", 1280, y+24, 54, 54)
			c.glyph("glyph_tab_skills", 1307, y+51, 32, palette["rune_glow"], 1)
		}
	}
	c.sprite("scroll_track", 1374, 315, 14, 397)
	c.sprite("scroll_handle_normal", 1374, 322, 14, 80)
	c.btn("button_normal", 528, 757, 304, 64, "НОВЫЙ МИР", head, 24)
	c.btn("button_normal", 850, 757, 185, 64, "", head, 24)
	c.glyph("glyph_trash", 885, 789, 30, body, 1)
	c.text("УДАЛИТЬ", serif, 24, head, 960, 789, 1)
	c.btn("button_normal", 1056, 757, 333, 64, "МОДИФИКАТОРЫ", head, 24)
	c.checkbox(550, 859, 45, true)
	c.text("Открытый сервер", sans, 21, body, 610, 882, 0)
	c.sprite("field_search", 814, 856, 236, 50)
	c.text("•••••••", sans, 22, body, 832, 881, 0)
	c.btn("button_normal", 1091, 845, 304, 75, "НАЧАТЬ", gold, 34)
	// join dialog
	c.panel(1211, 405, 616, 323)
	c.text("ПОДКЛЮЧЕНИЕ К СЕРВЕРУ", serif, 30, head, 1519, 446, 1)
	c.text("Адрес:", sans, 21, body, 1272, 528, 0)
	c.sprite("field_search", 1371, 506, 395, 46)
	c.text("192.168.0.10:2456", sans, 22, body, 1390, 529, 0)
	c.text("Пароль:", sans, 21, body, 1272, 594, 0)
	c.sprite("field_search", 1371, 570, 395, 48)
	c.text("••••••••", sans, 24, body, 1390, 594, 0)
	c.btn("button_selected", 1267, 640, 277, 61, "ПОДКЛЮЧИТЬСЯ", head, 22)
	c.btn("button_normal", 1563, 640, 207, 61, "ОТМЕНА", head, 22)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM5Load() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m5-load-a.png")
	c := newCanvas(mock, 1920, 1080) // loading art = content; our pieces drawn over it
	head, body := palette["text_header"], palette["text_body"]
	c.copyMock(blurBG(mock), 600, 80, 720, 300)
	c.copyMock(blurBG(mock), 290, 780, 1340, 200)
	c.panel(629, 93, 661, 272)
	c.text("ПОДКЛЮЧЕНИЕ...", serif, 32, head, 960, 128, 1)
	c.tint("spinner_runes", 680, 172, 128, 128, palette["rune_glow"], 1)
	c.text("Северный форпост • 192.168.0.10", sans, 23, body, 829, 234, 0)
	c.btn("button_normal", 819, 290, 283, 52, "Отмена", head, 24)
	c.sprite("frame_strip", 539, 785, 840, 72)
	c.text("Совет: еда с высоким здоровьем увеличивает запас здоровья.", sans, 21, body, 960, 821, 1)
	c.text("Загрузка мира... 62%", sans, 24, body, 960, 882, 1)
	ix, iy := 154/c.K, 34/c.K
	c.sprite("bar_trough", 304+ix, 898+iy, 1309-2*ix, 72-2*iy)
	c.tint("rail_fill", 304+ix, 898+iy, (1309-2*ix)*0.62, 72-2*iy, palette["rune_glow"], 1)
	c.tint("bar_end_line", 304+ix+(1309-2*ix)*0.62-3, 898+iy-4, 6, 72-2*iy+8, "#e8fbff", 1)
	c.sprite("frame_boss", 304, 898, 1309, 72)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM5Settings() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m5-settings-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.panel(355, 106, 1210, 854)
	c.header(800, 118, 320, 56, "", "НАСТРОЙКИ")
	for i, t := range []string{"Геймплей", "Управление", "Графика", "Звук", "Доступность", "Моды"} {
		y := 192 + float64(i)*81.6
		n, col := "tab_normal", body
		if i == 1 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, 389, y, 269, 72)
		c.text(t, sans, 25, col, 523, y+36, 1)
	}
	c.sprite("frame_inset", 672, 179, 861, 685)
	rows := []string{"Чувствительность мыши", "Масштаб интерфейса", "Инвертировать мышь", "Подсказки клавиш", "Язык"}
	ys := []float64{229, 283, 338, 390, 448}
	for i, r := range rows {
		c.text(r, sans, 21, body, 728, ys[i], 0)
		c.sprite("divider", 700, ys[i]+22, 800, 6)
	}
	c.slider(1018, 222, 358, 16, 0.4, "slider_handle_normal")
	c.sprite("field_search", 1397, 211, 78, 40)
	c.text("40", sans, 21, body, 1436, 231, 1)
	c.slider(1018, 276, 358, 16, 0.98, "slider_handle_hover")
	c.sprite("field_search", 1397, 265, 78, 40)
	c.text("100", sans, 21, body, 1436, 285, 1)
	c.checkbox(1018, 320, 36, false)
	c.checkbox(1018, 372, 36, true)
	// dropdown (open)
	c.sprite("field_search", 1018, 426, 454, 44)
	c.text("Русский", sans, 21, body, 1036, 448, 0)
	c.glyph("glyph_chevron_down", 1445, 448, 22, body, 1)
	c.sprite("frame_inset", 1018, 472, 454, 125)
	for i, l := range []string{"English", "Русский", "Deutsch"} {
		y := 476 + float64(i)*40
		if i == 1 {
			c.row("selected", 1021, y, 448, 40)
		}
		c.text(l, sans, 21, body, 1040, y+20, 0)
	}
	c.text("КЛАВИШИ", serif, 32, head, 728, 608, 0)
	keys := []struct{ l, k string }{{"Вперёд", "W"}, {"Прыжок", "Space"}, {"Инвентарь", "Tab"}, {"Действие", ""}}
	for i, k := range keys {
		y := 662 + float64(i)*55
		c.text(k.l, sans, 21, body, 728, y, 0)
		if k.k == "" {
			c.sprite("plate_key", 1208, y-21, 264, 42)
			c.tint("ring_select", 1206, y-23, 268, 46, palette["rune_glow"], 1)
			c.text("Нажмите клавишу...", sans, 20, palette["rune_glow"], 1340, y, 1)
			continue
		}
		c.sprite("plate_key", 1208, y-21, 211, 42)
		c.text(k.k, sans, 20, body, 1313, y, 1)
	}
	c.sprite("scroll_track", 1508, 200, 12, 648)
	c.sprite("scroll_handle_normal", 1508, 270, 12, 80)
	c.btn("button_normal", 400, 872, 256, 69, "Сбросить", head, 26)
	c.btn("button_normal", 1051, 872, 234, 69, "Отмена", head, 26)
	c.btn("button_normal", 1293, 872, 227, 69, "Применить", gold, 26)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func cmdCompareM5() {
	loadSprites()
	palette = loadSpec().Palette
	mm, mo := compareM5Menu()
	cm, co := compareM5Char()
	wm, wo := compareM5World()
	lm, lo := compareM5Load()
	sm, so := compareM5Settings()
	sideBySide(mm, mo, "art/review/compare-menu-m5.png")
	sideBySide(cm, co, "art/review/compare-char-m5.png")
	sideBySide(wm, wo, "art/review/compare-world-m5.png")
	sideBySide(lm, lo, "art/review/compare-load-m5.png")
	sideBySide(sm, so, "art/review/compare-settings-m5.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	list := []pair{
		same("main menu: button column   [m5-menu-a | ours]", mm, mo, R(80, 340, 540, 500)),
		same("main menu: news panel + version", mm, mo, R(1310, 290, 600, 780)),
		same("character list + delete confirm   [m5-char-a | ours]", cm, co, R(30, 80, 660, 880)),
		same("create panel: name, body type, swatches, colour sliders", cm, co, R(1320, 120, 580, 820)),
		same("world list + tabs + actions   [m5-world-a | ours]", wm, wo, R(470, 125, 975, 830)),
		same("join dialog", wm, wo, R(1200, 395, 640, 345)),
		same("connecting dialog + spinner   [m5-load-a | ours]", lm, lo, R(615, 80, 690, 300)),
		same("loading tip + progress bar", lm, lo, R(290, 775, 1340, 200)),
		same("settings: tabs + sliders + toggles   [m5-settings-a | ours]", sm, so, R(345, 95, 1230, 420)),
		same("settings: dropdown + keybind rows + bottom buttons", sm, so, R(345, 410, 1230, 550)),
	}
	groupsM2(list, "art/review/compare-groups-m5.png")
	fmt.Println("compare-m5 done")
}
