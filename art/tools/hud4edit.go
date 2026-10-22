package main

import (
	"encoding/json"
	"fmt"
	"image"
	"image/color"
	"os"
	"path/filepath"

	"golang.org/x/image/draw"
)

// hud4edit: generic masked edit of a HUD mock driven by a JSON spec.
// Moves copy exact pixels; masks are inpainted from a Codex output of one crop; protected pixels
// (medallion knots, explicit rects) never change. Everything else stays identical to the base.

type hud4Rect [4]int

func (r hud4Rect) R() image.Rectangle { return image.Rect(r[0], r[1], r[2], r[3]) }

type hud4Move struct {
	Src    hud4Rect `json:"src"`
	Dx     int      `json:"dx"`
	Dy     int      `json:"dy"`
	Medals bool     `json:"medals"` // move only medallion knot pixels inside Src (hud-4M positions)
}

type hud4Spec struct {
	Base    string     `json:"base"`
	Hud4    string     `json:"hud4"` // medallion knot source (hud-4)
	Ref     string     `json:"ref"`  // comparison image for the side-by-side
	RefName string     `json:"refName"`
	Name    string     `json:"name"`
	Crop    hud4Rect   `json:"crop"`
	Moves   []hud4Move `json:"moves"`
	Masks   []hud4Rect `json:"masks"`
	Protect []hud4Rect `json:"protect"`
	// Force masks apply after moves (repaint inside moved pixels, e.g. icon interiors, seams).
	Force []hud4Rect `json:"force"`
	// ProtectMedals keeps the medallion knot pixels (at their hud-4M place) out of the mask.
	ProtectMedals bool `json:"protectMedals"`
}

func cmdHud4Edit(args []string) {
	if len(args) < 3 {
		must(fmt.Errorf("usage: hud4edit prep|compose <spec.json> <dir>"))
	}
	raw, err := os.ReadFile(args[1])
	must(err)
	var sp hud4Spec
	must(json.Unmarshal(raw, &sp))
	dir := args[2]
	base := loadPNG(sp.Base)
	h4 := loadPNG(sp.Hud4)
	b := base.Bounds()
	knots := image.NewAlpha(b)
	hud4MedKnot(h4, knots, false)
	hud4MedKnot(h4, knots, true)

	comp := image.NewNRGBA(b)
	draw.Draw(comp, b, base, image.Point{}, draw.Src)
	dest := image.NewAlpha(b)
	movedKnots := image.NewAlpha(b)
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			movedKnots.SetAlpha(x, y, knots.AlphaAt(x, y))
		}
	}
	for _, mv := range sp.Moves {
		r := mv.Src.R()
		src := crop(comp, b) // snapshot so overlapping moves read the pre-move pixels
		for y := r.Min.Y; y < r.Max.Y; y++ {
			for x := r.Min.X; x < r.Max.X; x++ {
				if mv.Medals && knots.AlphaAt(x, y).A == 0 {
					continue
				}
				tx, ty := x+mv.Dx, y+mv.Dy
				if !image.Pt(tx, ty).In(b) {
					continue
				}
				comp.SetNRGBA(tx, ty, src.NRGBAAt(x, y))
				dest.SetAlpha(tx, ty, color.Alpha{255})
				if mv.Medals {
					movedKnots.SetAlpha(x, y, color.Alpha{})
					movedKnots.SetAlpha(tx, ty, color.Alpha{255})
				}
			}
		}
	}
	mask := image.NewAlpha(b)
	for _, r := range sp.Masks {
		draw.Draw(mask, r.R(), image.Opaque, image.Point{}, draw.Src)
	}
	for _, r := range sp.Protect {
		draw.Draw(mask, r.R(), image.Transparent, image.Point{}, draw.Src)
	}
	for _, r := range sp.Force {
		rr := r.R()
		for y := rr.Min.Y; y < rr.Max.Y; y++ {
			for x := rr.Min.X; x < rr.Max.X; x++ {
				dest.SetAlpha(x, y, color.Alpha{})
				mask.SetAlpha(x, y, color.Alpha{255})
			}
		}
	}
	for _, r := range sp.Protect {
		draw.Draw(mask, r.R(), image.Transparent, image.Point{}, draw.Src)
	}
	allowed := image.NewAlpha(b)
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			if dest.AlphaAt(x, y).A > 0 || (sp.ProtectMedals && movedKnots.AlphaAt(x, y).A > 0) {
				mask.SetAlpha(x, y, color.Alpha{})
			}
			if dest.AlphaAt(x, y).A > 0 || mask.AlphaAt(x, y).A > 0 {
				allowed.SetAlpha(x, y, color.Alpha{255})
			} else {
				comp.SetNRGBA(x, y, base.NRGBAAt(x, y))
			}
		}
	}
	c := sp.Crop.R()
	switch args[0] {
	case "prep":
		mk := crop(comp, c)
		for y := c.Min.Y; y < c.Max.Y; y++ {
			for x := c.Min.X; x < c.Max.X; x++ {
				if mask.AlphaAt(x, y).A > 0 {
					mk.SetNRGBA(x-c.Min.X, y-c.Min.Y, color.NRGBA{255, 0, 255, 255})
				}
			}
		}
		savePNG(filepath.Join(dir, sp.Name+"-crop-masked.png"), mk)
		savePNG(filepath.Join(dir, sp.Name+"-crop.png"), crop(comp, c))
		savePNG(filepath.Join(dir, sp.Name+"-comp.png"), comp)
		fmt.Println("prep ok")
	case "compose":
		out := comp
		gp := filepath.Join(dir, sp.Name+"-gen.png")
		if _, err := os.Stat(gp); err == nil {
			rawg := loadPNG(gp)
			gen := image.NewNRGBA(image.Rect(0, 0, c.Dx(), c.Dy()))
			draw.CatmullRom.Scale(gen, gen.Bounds(), rawg, rawg.Bounds(), draw.Src, nil)
			dx, dy, rms := hud4Align(gen, crop(comp, c), mask, c.Min)
			fmt.Printf("%s: align dx=%d dy=%d rms=%.1f\n", sp.Name, dx, dy, rms)
			for y := c.Min.Y; y < c.Max.Y; y++ {
				for x := c.Min.X; x < c.Max.X; x++ {
					if mask.AlphaAt(x, y).A == 0 {
						continue
					}
					// hard edge: pixels under the mask can be stale (moved away), never blend them in
					out.SetNRGBA(x, y, gen.NRGBAAt(x-c.Min.X+dx, y-c.Min.Y+dy))
				}
			}
		}
		savePNG(filepath.Join(dir, sp.Name+".png"), out)
		hud4MReport(base, loadPNG(sp.Ref), out, allowed, dir, sp.Name, sp.RefName)
	default:
		must(fmt.Errorf("unknown mode %q", args[0]))
	}
}
