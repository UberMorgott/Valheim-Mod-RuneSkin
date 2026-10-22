package main

import (
	"fmt"
	"image"
)

// mock-3c: hud-3c rendered from our sprites (everything cut from mockups hud-3 / hud-2; icons = mockup content) at the
// mockup's own panel height (190 px at 1080) so it compares 1:1 with hud-3: wings on the bottom edge, status niches above
// them on a double-rail ledge, canonical knotwork + triquetra medallions on the bar block ends, shield as a thin line above
// the HP well, live slot counts spread evenly over the fixed wings. Variants: A 3 food / 3 potions / 6 buffs / 3 debuffs,
// B 4 food / 5 potions, C crowded 14 buffs / 6 debuffs (rows grow upward, max 3, then "+N").
func cmdMockHud3c() {
	dir := hud2Out
	h3 := loadPNG(hud3Mock)
	loadSprites()
	palette = loadSpec().Palette
	_, a := compareHud3(190, hudOpts{food: 3, pot: 3, buffs: 6, debuffs: 3, niches: true})
	_, b := compareHud3(190, hudOpts{food: 4, pot: 5, buffs: 6, debuffs: 3, niches: true})
	_, cr := compareHud3(190, hudOpts{food: 3, pot: 3, buffs: 14, debuffs: 6, niches: true})
	savePNG(dir+"hud-3c.png", a)
	savePNG(dir+"hud-3c-4food-5potions.png", b)
	savePNG(dir+"hud-3c-crowded.png", cr)
	sideBySide(h3, a, dir+"hud-3-vs-3c.png")
	sideBySide(a, b, dir+"hud-3c-3-vs-4food5potions.png")
	sideBySide(a, cr, dir+"hud-3c-vs-crowded.png")
	strip := image.Rect(0, 700, 1920, 1080)
	sideBySideV(crop(h3, strip), crop(a, strip), dir+"hud-3-vs-3c-strip.png")
	ref := loadPNG("C:/Users/Morgott/.claude/uploads/d62659f5-98fd-415e-8c61-9c596d73bdf9/2221df3c-FA3EDF2C-AC81-422C-821C-E0F788C7B60D.png")
	cut := sprites["ornament_medallion"].img
	sideBySide(resize(ref, 240, 244), resize(cut, 240, 244), dir+"hud-3c-ornament.png")
	fmt.Println("mock-3c done")
}
