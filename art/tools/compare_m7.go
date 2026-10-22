package main

import (
	"fmt"
	"image"
)

// M7 review composites (1920x1080 mockup px): the picked mod panels m7-ench-a (Epic Loot enchanting table),
// m7-board-a (Epic Loot adventure board), m7-modhud-a (Vaettir target panel + raid bar, Hamingja bleed-out; ally
// markers in the compact housing of m7-modhud-b) rebuilt from our sprites over a blurred copy of the scene. Item,
// creature and status icons, the owned HUD cluster and minimap are content copied from the mockups.

var rarity = map[string]string{"magic": "#4fa8f0", "rare": "#f0c419", "epic": "#b062f0", "legendary": "#f08a3a", "mythic": "#e0413a"}

func compareM7Ench() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m7-ench-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	c.copyMock(mock, 1670, 15, 240, 250) // owned minimap
	c.copyMock(mock, 20, 900, 470, 130)  // owned HUD
	c.panel(318, 160, 1287, 720)
	c.titleKnots(940, 172, "СТОЛ ЗАЧАРОВАНИЯ", 40)
	c.sprite("badge_level_star", 1255, 143, 58, 58)
	c.text("3", serif, 26, gold, 1284, 173, 1)
	// tab column (labels from Epic Loot config, any count)
	tabs := []struct{ g, t string }{{"glyph_ench_sacrifice", "Жертвование"}, {"glyph_ench_convert", "Преобразование"},
		{"glyph_ench_enchant", "Зачарование"}, {"glyph_ench_augment", "Аугментация"}, {"glyph_ench_disenchant", "Расколдовать"},
		{"glyph_act_upgrade", "Улучшение стола"}}
	for i, t := range tabs {
		y := 222 + float64(i)*98.8
		n, col := "tab_normal", body
		if i == 2 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, 342, y, 290, 82)
		c.glyph(t.g, 390, y+41, 46, col, 1)
		c.text(t.t, serif, 27, col, 437, y+41, 0)
	}
	// item list (MultiSelectItemList)
	c.sprite("frame_inset", 643, 215, 395, 645)
	c.text("ПРЕДМЕТЫ", serif, 26, head, 837, 242, 1)
	items := []struct{ n, r, k string }{{"Железный меч", "Магический", "magic"}, {"Бронзовый топор", "Редкий", "rare"},
		{"Лук охотника", "Эпический", "epic"}, {"Серебряный щит", "Легендарный", "legendary"},
		{"Железная булава", "Магический", "magic"}, {"Шлем викинга", "Редкий", "rare"}}
	for i, it := range items {
		y := 272 + float64(i)*94.3
		st := "normal"
		if i == 0 {
			st = "selected"
		}
		c.row(st, 655, y, 345, 84)
		c.sprite("slot_cell_recess", 660, y+5, 76, 74)
		c.tint("ring_select", 658, y+3, 80, 78, rarity[it.k], 0.85)
		c.icon(mock, 668, y+12, 60, 60)
		c.text(it.n, sans, 23, rarity[it.k], 750, y+28, 0)
		c.text(it.r, sans, 19, rarity[it.k], 750, y+56, 0)
		c.checkbox(960, y+24, 34, i == 0)
	}
	c.sprite("scroll_track", 1012, 280, 14, 560)
	c.sprite("scroll_handle_normal", 1012, 288, 14, 90)
	// detail
	c.sprite("frame_inset", 1048, 215, 540, 570)
	c.sprite("slot_cell_recess", 1065, 235, 118, 118)
	c.icon(mock, 1078, 246, 94, 96)
	c.text("Железный меч", serif, 38, head, 1205, 258, 0)
	c.text("РЕДКОСТЬ", sans, 18, body, 1205, 300, 0)
	c.sprite("divider", 1200, 312, 370, 8)
	for i, k := range []struct{ k, l string }{{"magic", "Магический"}, {"rare", "Редкий"}, {"epic", "Эпический"},
		{"legendary", "Легендарный"}, {"mythic", "Мифический"}} {
		x := 1227 + float64(i)*75
		c.tint("gem_rarity", x-24, 332, 48, 48, rarity[k.k], 1)
		if i == 0 {
			c.sprite("gem_setting", x-36, 320, 72, 72)
		}
		c.text(k.l, sans, 14, rarity[k.k], x, 400, 1)
	}
	c.text("ВОЗМОЖНЫЕ ЭФФЕКТЫ", sans, 19, head, 1072, 446, 0)
	c.sprite("divider", 1068, 458, 505, 8)
	for i, s := range []string{"+15% к урону", "+10% к скорости атаки", "+20 к выносливости"} {
		y := 486 + float64(i)*31.5
		c.glyph("glyph_ench_enchant", 1085, y, 24, palette["rune_glow"], 1)
		c.text(s, sans, 20, body, 1110, y, 0)
	}
	c.text("СТОИМОСТЬ", sans, 19, head, 1072, 591, 0)
	c.sprite("divider", 1068, 603, 505, 8)
	for i, l := range []struct {
		n, v string
		ok   bool
	}{{"Пыль", "24/10", true}, {"Эссенция", "8/5", true}, {"Рунный камень", "0/1", false}} {
		x := 1113 + float64(i)*108
		n, col := "socket_req", palette["text_ok"]
		if !l.ok {
			n, col = "socket_req_missing", palette["text_missing"]
		}
		c.sprite(n, x, 612, 96, 92)
		c.icon(mock, x+10, 620, 76, 76)
		c.text(l.n, sans, 15, body, x+48, 716, 1)
		c.text(l.v, sans, 22, col, x+48, 739, 1)
	}
	c.text("Шанс: 100%", sans, 20, body, 1312, 773, 1)
	c.btn("button_normal", 1060, 792, 520, 72, "", head, 34)
	c.glyph("glyph_ench_enchant", 1216, 828, 44, palette["rune_glow"], 1)
	c.text("Зачаровать", serif, 36, head, 1258, 828, 0)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM7Board() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m7-board-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body, gold := palette["text_header"], palette["text_body"], palette["text_active"]
	dis := palette["text_disabled"]
	c.copyMock(mock, 1630, 10, 280, 270) // owned minimap
	c.panel(333, 140, 1255, 820)
	c.titleKnots(960, 180, "ДОСКА ПРИКЛЮЧЕНИЙ", 40)
	c.sprite("tab_strip", 380, 280, 1160, 12)
	for i, t := range []struct{ g, t string }{{"glyph_board_stash", "Тайник"}, {"glyph_board_gamble", "Азарт"},
		{"glyph_board_treasure", "Карты сокровищ"}, {"glyph_board_bounty", "Награды"}} {
		x := 403 + float64(i)*280.5
		n, col := "tab_normal", body
		if i == 3 {
			n, col = "tab_selected", gold
		}
		c.sprite(n, x, 226, 262, 54)
		c.glyph(t.g, x+30, 253, 32, col, 1)
		c.text(t.t, serif, 24, col, x+146, 253, 1)
	}
	c.sprite("frame_inset", 358, 302, 1172, 560)
	type st struct{ n, b, s, col string }
	rows := []st{{"Тролль", "Чёрный лес", "Доступно", gold}, {"Драугр-элита", "Болота", "Принято", palette["rune_glow"]},
		{"Тролль", "Чёрный лес", "Выполнено", palette["text_ok"]}, {"Драугр-элита", "Болота", "Доступно", gold}}
	for i, r := range rows {
		y := 313 + float64(i)*136
		s := "normal"
		if i == 0 {
			s = "selected"
		}
		c.row(s, 366, y, 1155, 128)
		c.sprite("button_round", 386, y+5, 120, 120)
		c.icon(mock, 400, y+18, 92, 94)
		c.text(r.n, serif, 36, head, 537, y+45, 0)
		c.text(r.b, sans, 22, dis, 537, y+90, 0)
		for k := 0; k < 2; k++ {
			x := 815 + float64(k)*115
			c.sprite("socket_req", x, y+18, 98, 98)
			c.icon(mock, x+14, y+24, 70, 62)
		}
		v := []string{"450", "20"}
		if i%2 == 1 {
			v = []string{"650", "30"}
		}
		c.text(v[0], sans, 23, body, 900, y+95, 2)
		c.text(v[1], sans, 23, body, 1016, y+95, 2)
		c.sprite("field_search", 1058, y+49, 177, 42)
		c.text(r.s, sans, 20, r.col, 1146, y+70, 1)
		b, bc := "button_normal", head
		if i == 1 || i == 2 {
			b, bc = "button_disabled", dis
		}
		c.btn(b, 1262, y+35, 245, 66, "Принять", bc, 26)
	}
	c.sprite("scroll_track", 1540, 320, 14, 520)
	c.sprite("scroll_handle_normal", 1540, 350, 14, 120)
	for i, p := range []struct {
		x, w float64
		v    string
	}{{483, 215, "1 240"}, {720, 185, "12"}, {925, 185, "3"}} {
		c.sprite("plate_currency", p.x, 873, p.w, 64)
		if i == 0 {
			c.glyph("glyph_coins", p.x+52, 905, 36, gold, 1)
		} else {
			c.icon(mock, p.x+25, 880, 52, 50)
		}
		c.text(p.v, sans, 26, body, p.x+p.w-40, 905, 2)
	}
	c.text("Обновится через 2 дня", sans, 20, dis, 1480, 905, 2)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func compareM7ModHud() (*image.NRGBA, *image.NRGBA) {
	mock := loadPNG(mockDir + "/m7-modhud-a.png")
	c := newCanvas(blurBG(mock), 1920, 1080)
	head, body := palette["text_header"], palette["text_body"]
	rare := rarity["legendary"]
	c.copyMock(mock, 820, 360, 300, 330)  // draugr (world)
	c.copyMock(mock, 35, 660, 630, 380)   // owned HUD cluster
	c.copyMock(mock, 1600, 20, 300, 310)  // owned minimap
	c.copyMock(mock, 1690, 400, 180, 300) // tombstone + raven (world)
	// Vaettir target panel (rare tier frame)
	c.sprite("frame_target", 612, 26, 700, 104)
	c.sprite("slot_cell_recess", 632, 40, 78, 76)
	c.icon(mock, 642, 50, 58, 56)
	c.text("Драугр-элита", serif, 30, rare, 958, 49, 1)
	ix, iy := 36/c.K, 14/c.K
	c.sprite("bar_trough", 725+ix, 66+iy, 390-2*ix, 40-2*iy)
	c.tint("bar_enemy_fill", 725+ix, 66+iy, (390-2*ix)*0.65, 40-2*iy, "#c0392b", 1)
	for k := 1; k < 10; k++ {
		x := 725 + ix + (390-2*ix)*float64(k)/10
		c.tint("separator_v", x-2, 66+iy, 4, 40-2*iy, "#000000", 0.5)
	}
	c.sprite("frame_enemy_normal", 725, 66, 390, 40)
	c.text("1 820 / 2 800", sans, 20, body, 920, 86, 1)
	for k, v := range []string{"3", "2", "1"} {
		x := 1132 + float64(k)*58
		c.sprite("socket_status", x, 46, 46, 46)
		c.icon(mock, x+6, 52, 34, 30)
		c.text(v, sans, 17, body, x+23, 98, 1)
	}
	// raid timer
	c.titleKnots(960, 156, "Нашествие драугров", 19)
	c.progressScaled(700, 168, 520, 32, 0.6, "#f08a3a")
	// Hamingja bleed-out
	c.popup(735, 868, 670, 145)
	c.text("ВЫ ПРИ СМЕРТИ", serif, 38, ember, 1073, 900, 1)
	c.progressScaled(795, 926, 550, 42, 0.42, "#8f989f")
	c.text("0:23", sans, 23, body, 1070, 947, 1)
	c.btn("button_normal", 960, 980, 225, 48, "[E] Сдаться", head, 22)
	// ally markers (compact housing from B)
	c.sprite("plate_marker", 1548, 527, 145, 64)
	c.glyph("glyph_death", 1578, 559, 30, body, 1)
	c.text("Бьорн", sans, 19, head, 1603, 547, 0)
	c.text("0:41", sans, 19, body, 1603, 572, 0)
	c.sprite("plate_marker", 62, 380, 150, 64)
	c.sprite("arrow_marker", 26, 386, 28, 52)
	c.text("Рагнар", sans, 19, head, 78, 400, 0)
	c.text("48 м", sans, 19, body, 78, 423, 0)
	c.sprite("bar_skill_trough", 140, 418, 60, 10)
	c.tint("bar_skill_fill", 141, 419, 40, 8, palette["rune_glow"], 1)
	c.flush()
	return mock, resize(c.img, 1920, 1080)
}

func cmdCompareM7() {
	loadSprites()
	palette = loadSpec().Palette
	em, eo := compareM7Ench()
	bm, bo := compareM7Board()
	hm, ho := compareM7ModHud()
	sideBySide(em, eo, "art/review/compare-ench-m7.png")
	sideBySide(bm, bo, "art/review/compare-board-m7.png")
	sideBySide(hm, ho, "art/review/compare-modhud-m7.png")
	R := func(x, y, w, h int) image.Rectangle { return image.Rect(x, y, x+w, y+h) }
	same := func(n string, a, b *image.NRGBA, r image.Rectangle) pair { return pair{n, a, b, r, r} }
	list := []pair{
		same("enchanting: header + tab column   [m7-ench-a | ours]", em, eo, R(310, 125, 340, 765)),
		same("enchanting: multi-select item list", em, eo, R(635, 205, 410, 665)),
		same("enchanting: detail (rarity, effects, cost, button)", em, eo, R(1040, 205, 570, 680)),
		same("adventure board: header + tabs   [m7-board-a | ours]", bm, bo, R(325, 130, 1270, 170)),
		same("adventure board: bounty rows", bm, bo, R(350, 295, 1240, 570)),
		same("adventure board: currencies footer", bm, bo, R(470, 860, 1030, 90)),
		same("Vaettir target panel + raid timer   [m7-modhud-a | ours]", hm, ho, R(600, 15, 725, 200)),
		same("Hamingja bleed-out", hm, ho, R(725, 855, 690, 175)),
		same("Hamingja ally markers (housing from B)", hm, ho, R(20, 370, 220, 90)),
		same("Hamingja ally marker over a tombstone", hm, ho, R(1535, 515, 175, 90)),
	}
	groupsM2(list, "art/review/compare-groups-m7.png")
	fmt.Println("compare-m7 done")
}
