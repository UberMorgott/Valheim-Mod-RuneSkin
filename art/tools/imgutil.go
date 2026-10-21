package main

import (
	"fmt"
	"image"
	"image/color"
	"image/png"
	"math"
	"os"
	"path/filepath"

	"golang.org/x/image/draw"
)

func loadPNG(path string) *image.NRGBA {
	f, err := os.Open(path)
	must(err)
	defer func() { _ = f.Close() }()
	src, err := png.Decode(f)
	must(err)
	b := src.Bounds()
	dst := image.NewNRGBA(image.Rect(0, 0, b.Dx(), b.Dy()))
	draw.Draw(dst, dst.Bounds(), src, b.Min, draw.Src)
	return dst
}

func savePNG(path string, img image.Image) {
	must(os.MkdirAll(filepath.Dir(path), 0o755))
	f, err := os.Create(path)
	must(err)
	enc := png.Encoder{CompressionLevel: png.BestCompression}
	must(enc.Encode(f, img))
	must(f.Close())
}

func must(err error) {
	if err != nil {
		fmt.Fprintln(os.Stderr, "error:", err)
		os.Exit(1)
	}
}

func crop(img *image.NRGBA, r image.Rectangle) *image.NRGBA {
	r = r.Intersect(img.Bounds())
	dst := image.NewNRGBA(image.Rect(0, 0, r.Dx(), r.Dy()))
	draw.Draw(dst, dst.Bounds(), img, r.Min, draw.Src)
	return dst
}

// resize with premultiplied alpha (CatmullRom), so keyed edges do not pick up background colour.
func resize(img *image.NRGBA, w, h int) *image.NRGBA {
	if img.Bounds().Dx() == w && img.Bounds().Dy() == h {
		return img
	}
	pm := image.NewRGBA(img.Bounds())
	draw.Draw(pm, pm.Bounds(), img, image.Point{}, draw.Src)
	out := image.NewRGBA(image.Rect(0, 0, w, h))
	draw.CatmullRom.Scale(out, out.Bounds(), pm, pm.Bounds(), draw.Src, nil)
	dst := image.NewNRGBA(out.Bounds())
	draw.Draw(dst, dst.Bounds(), out, image.Point{}, draw.Src)
	return dst
}

func clamp01(v float64) float64 { return math.Max(0, math.Min(1, v)) }

// keyMagenta removes a flat #FF00FF background: alpha from the distance to magenta,
// colour de-spilled (magenta component removed from semi-transparent edge pixels).
func keyMagenta(img *image.NRGBA) {
	b := img.Bounds()
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			i := img.PixOffset(x, y)
			r, g, bl := float64(img.Pix[i])/255, float64(img.Pix[i+1])/255, float64(img.Pix[i+2])/255
			// magenta-ness: r and b high, g low
			m := math.Min(r, bl) - g
			a := clamp01(1 - (m-0.25)/0.45)
			if a <= 0 {
				img.Pix[i+3] = 0
				img.Pix[i], img.Pix[i+1], img.Pix[i+2] = 0, 0, 0
				continue
			}
			if a < 1 {
				// observed = a*fg + (1-a)*magenta -> fg
				fr := (r - (1 - a)) / a
				fg := g / a
				fb := (bl - (1 - a)) / a
				img.Pix[i] = uint8(clamp01(fr) * 255)
				img.Pix[i+1] = uint8(clamp01(fg) * 255)
				img.Pix[i+2] = uint8(clamp01(fb) * 255)
			} else if m > 0.05 {
				// opaque but magenta-tinted spill: pull r/b down toward g-based neutral
				s := math.Min(r, bl) - m*0.8
				img.Pix[i] = uint8(clamp01(math.Min(r, s+0.02)) * 255)
				img.Pix[i+2] = uint8(clamp01(math.Min(bl, s+0.04)) * 255)
			}
			img.Pix[i+3] = uint8(a * 255)
		}
	}
}

// lumaMask turns a white-on-black mask into white RGB with alpha = luminance (with black/white points).
func lumaMask(img *image.NRGBA, black, white float64) {
	b := img.Bounds()
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			i := img.PixOffset(x, y)
			l := (0.2126*float64(img.Pix[i]) + 0.7152*float64(img.Pix[i+1]) + 0.0722*float64(img.Pix[i+2])) / 255
			a := clamp01((l - black) / (white - black))
			img.Pix[i], img.Pix[i+1], img.Pix[i+2], img.Pix[i+3] = 255, 255, 255, uint8(a*255+0.5)
		}
	}
}

// alphaBBox returns the bounding box of pixels with alpha > minAlpha.
func alphaBBox(img *image.NRGBA, minAlpha uint8) image.Rectangle {
	b := img.Bounds()
	r := image.Rectangle{}
	first := true
	for y := b.Min.Y; y < b.Max.Y; y++ {
		for x := b.Min.X; x < b.Max.X; x++ {
			if img.Pix[img.PixOffset(x, y)+3] > minAlpha {
				p := image.Rect(x, y, x+1, y+1)
				if first {
					r, first = p, false
				} else {
					r = r.Union(p)
				}
			}
		}
	}
	return r
}

func over(dst *image.NRGBA, src image.Image, at image.Point) {
	draw.Draw(dst, src.Bounds().Sub(src.Bounds().Min).Add(at), src, src.Bounds().Min, draw.Over)
}

func fill(dst *image.NRGBA, r image.Rectangle, c color.NRGBA) {
	draw.Draw(dst, r, &image.Uniform{c}, image.Point{}, draw.Over)
}
