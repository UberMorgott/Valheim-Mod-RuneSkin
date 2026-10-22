// Command runeskin-art: mechanical art pipeline for RuneSkin.
// All creative pixels come from Codex image-gen kit sheets (art/src/kit/*.png); this tool only
// crops, keys, resizes, assembles 9-slice parts, writes sprites.json and builds review composites.
//
//	go run ./art/tools build            kit sheets -> art/sprites/*.png + sprites.json
//	go run ./art/tools check            sprites.json <-> files consistency
//	go run ./art/tools compare          art/review/compare-*.png ([mockup | ours])
//	go run ./art/tools compare-m2 [OLD_WET.png]  art/review/compare-{inventory,dock,groups}-m2.png (layout A)
//	go run ./art/tools compare-m3       art/review/compare-{crafting,groups}-m3.png (crafting kit)
//	go run ./art/tools compare-m3s      art/review/compare-{skills,trophies}-m3.png + compare-groups-m3s.png
//	go run ./art/tools compare-m4       art/review/compare-{hud-m4a,hud-m4b,map-m4,groups-m4}.png
//	go run ./art/tools compare-m5       art/review/compare-{menu,char,world,load,settings,groups}-m5.png
//	go run ./art/tools compare-m6       art/review/compare-{build,trader,text,load,ship,dialogs,groups}-m6.png
//	go run ./art/tools compare-eb ME MB BLOOD OUT  enemy-b tiers + boss-c bar from the sprites next to the mockups
//	go run ./art/tools compare-m7       art/review/compare-{ench,board,modhud,groups}-m7.png
//	go run ./art/tools sheet           art/review/contact-sheet.png
//	go run ./art/tools zoom IN x,y,w,h SCALE OUT    inspection crop
//	go run ./art/tools sample IN x,y [x,y...]       print pixel hex
package main

import (
	"fmt"
	"image"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

func main() {
	chdirRepoRoot()
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, "usage: build | check | compare | zoom | sample")
		os.Exit(2)
	}
	switch os.Args[1] {
	case "build":
		cmdBuild()
	case "check":
		cmdCheck()
	case "compare-m2":
		old := ""
		if len(os.Args) > 2 {
			old = os.Args[2]
		}
		cmdCompareM2(old)
	case "compare-m3":
		cmdCompareM3()
	case "compare-m3s":
		cmdCompareM3S()
	case "compare-m4":
		cmdCompareM4()
	case "compare-m5":
		cmdCompareM5()
	case "compare-m6":
		cmdCompareM6()
	case "compare-m7":
		cmdCompareM7()
	case "compare-eb":
		cmdCompareEB()
	case "strip":
		cmdStrip()
	case "rowmatch":
		cmdRowMatch()
	case "metalmatch":
		cmdMetalMatch()
	case "pairdiff":
		cmdPairDiff()
	case "sheet":
		cmdSheet()
	case "compare":
		cmdCompare()
	case "zoom":
		img := loadPNG(os.Args[2])
		r := parseRect(os.Args[3])
		s, _ := strconv.ParseFloat(os.Args[4], 64)
		c := crop(img, r)
		savePNG(os.Args[5], resize(c, int(float64(c.Bounds().Dx())*s), int(float64(c.Bounds().Dy())*s)))
	case "blobs":
		n, _ := strconv.Atoi(os.Args[4])
		cmdBlobs(os.Args[2], os.Args[3], n)
	case "sample":
		img := loadPNG(os.Args[2])
		for _, a := range os.Args[3:] {
			p := parseInts(a)
			i := img.PixOffset(p[0], p[1])
			fmt.Printf("%d,%d #%02X%02X%02X a%d\n", p[0], p[1], img.Pix[i], img.Pix[i+1], img.Pix[i+2], img.Pix[i+3])
		}
	default:
		fmt.Fprintln(os.Stderr, "unknown command", os.Args[1])
		os.Exit(2)
	}
}

// chdirRepoRoot makes repo-relative paths work under `go -C art/tools run .`.
func chdirRepoRoot() {
	d, err := os.Getwd()
	must(err)
	for {
		if _, err := os.Stat(filepath.Join(d, ".git")); err == nil {
			must(os.Chdir(d))
			return
		}
		p := filepath.Dir(d)
		if p == d {
			return
		}
		d = p
	}
}

func parseInts(s string) []int {
	var out []int
	for _, p := range strings.Split(s, ",") {
		v, err := strconv.Atoi(strings.TrimSpace(p))
		must(err)
		out = append(out, v)
	}
	return out
}

func parseRect(s string) image.Rectangle {
	v := parseInts(s)
	return image.Rect(v[0], v[1], v[0]+v[2], v[1]+v[3])
}
