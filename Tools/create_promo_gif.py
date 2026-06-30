from __future__ import annotations

import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
HERO = ROOT / "Docs" / "github-hero.png"
OUT = ROOT / "Docs" / "grokoengine3d-promo.gif"

W, H = 960, 540
FPS = 12
SECONDS = 16
FRAMES = FPS * SECONDS


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        r"C:\Windows\Fonts\seguisb.ttf" if bold else r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arialbd.ttf" if bold else r"C:\Windows\Fonts\arial.ttf",
    ]
    for c in candidates:
        try:
            return ImageFont.truetype(c, size)
        except Exception:
            pass
    return ImageFont.load_default()


FONT_HERO = font(58, True)
FONT_TITLE = font(40, True)
FONT_SUB = font(24)
FONT_SMALL = font(18)


def cover(img: Image.Image, w: int, h: int) -> Image.Image:
    img = img.convert("RGB")
    s = max(w / img.width, h / img.height)
    nw, nh = int(img.width * s), int(img.height * s)
    img = img.resize((nw, nh), Image.Resampling.LANCZOS)
    x = (nw - w) // 2
    y = (nh - h) // 2
    return img.crop((x, y, x + w, y + h))


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def ease(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def add_text(draw: ImageDraw.ImageDraw, xy: tuple[int, int], text: str, fnt, fill, shadow=True):
    x, y = xy
    if shadow:
        draw.text((x + 2, y + 2), text, font=fnt, fill=(0, 0, 0, 170))
    draw.text((x, y), text, font=fnt, fill=fill)


def rounded_panel(draw: ImageDraw.ImageDraw, box, fill, outline=None, radius=18, width=2):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def particles(draw: ImageDraw.ImageDraw, frame: int):
    for i in range(34):
        a = (frame * 0.035 + i * 0.74) % (math.pi * 2)
        r = 70 + (i % 9) * 22
        cx = int(W * 0.74 + math.cos(a) * r)
        cy = int(H * 0.46 + math.sin(a * 1.35) * r * 0.55)
        size = 2 + (i % 4)
        color = (80, 175, 255, 110) if i % 3 else (255, 145, 55, 120)
        draw.ellipse((cx - size, cy - size, cx + size, cy + size), fill=color)


def slide_text(t: float):
    if t < 4.2:
        return (
            "GrokoEngine3D",
            "Experimental C# 3D engine and editor",
            ["Created by Gerar Gonzalez", "In active development", "Unity-inspired workflow"],
        )
    if t < 8.4:
        return (
            "Editor Workflow",
            "Scene View, Game View, Inspector, Hierarchy and Project Browser",
            ["GameObjects + Components", "Materials, prefabs and assets", "Real-time viewport"],
        )
    if t < 12.5:
        return (
            "Engine Systems",
            "Physics, particles, animation, shader graph and rendering",
            ["Rigidbody + Character Controller", "Particle curves, gradients and presets", "Lighting, shadows and post FX"],
        )
    return (
        "Public Experimental Preview",
        "Follow the development on GitHub",
        ["github.com/gerargonzalez9401-afk/GrokoEngine3D", "MIT License", "Built from scratch in C#"],
    )


def main():
    if not HERO.exists():
        raise FileNotFoundError(HERO)

    base_raw = cover(Image.open(HERO), W, H)
    frames: list[Image.Image] = []

    for i in range(FRAMES):
        t = i / FPS
        zoom = 1.0 + 0.030 * (i / max(1, FRAMES - 1))
        zw, zh = int(W * zoom), int(H * zoom)
        bg = base_raw.resize((zw, zh), Image.Resampling.LANCZOS)
        bg = bg.crop(((zw - W) // 2, (zh - H) // 2, (zw - W) // 2 + W, (zh - H) // 2 + H))
        bg = bg.filter(ImageFilter.GaussianBlur(radius=0.25))

        overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        od = ImageDraw.Draw(overlay, "RGBA")
        od.rectangle((0, 0, W, H), fill=(4, 8, 14, 78))
        od.rectangle((0, 0, int(W * 0.62), H), fill=(4, 8, 14, 132))
        od.rectangle((0, 0, W, 82), fill=(5, 8, 14, 115))
        particles(od, i)

        title, subtitle, bullets = slide_text(t)
        local = t % 4.2
        fade = ease(min(local / 0.65, (4.2 - local) / 0.65 if local > 3.45 else 1.0))
        alpha = int(255 * fade)
        x = int(lerp(48, 70, fade))

        od.rounded_rectangle((42, 34, 266, 66), radius=16, fill=(19, 35, 55, 185), outline=(74, 143, 220, 140), width=1)
        add_text(od, (60, 39), "GROKOENGINE3D", FONT_SMALL, (166, 210, 255, 230))

        add_text(od, (x, 128), title, FONT_HERO if title == "GrokoEngine3D" else FONT_TITLE, (232, 242, 255, alpha))
        add_text(od, (x + 2, 198), subtitle, FONT_SUB, (158, 205, 255, alpha))

        py = 262
        for idx, b in enumerate(bullets):
            by = py + idx * 42
            od.rounded_rectangle((x, by, x + 510, by + 30), radius=11, fill=(13, 22, 34, int(160 * fade)), outline=(60, 120, 190, int(80 * fade)))
            od.ellipse((x + 13, by + 10, x + 23, by + 20), fill=(90, 190, 255, alpha))
            add_text(od, (x + 36, by + 4), b, FONT_SMALL, (222, 235, 250, alpha), shadow=False)

        od.rounded_rectangle((W - 235, H - 56, W - 38, H - 25), radius=15, fill=(8, 18, 30, 180), outline=(55, 135, 230, 115))
        add_text(od, (W - 218, H - 51), "Experimental Preview", FONT_SMALL, (173, 218, 255, 220), shadow=False)

        composed = Image.alpha_composite(bg.convert("RGBA"), overlay).convert("P", palette=Image.Palette.ADAPTIVE, colors=128)
        frames.append(composed)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(
        OUT,
        save_all=True,
        append_images=frames[1:],
        duration=int(1000 / FPS),
        loop=0,
        optimize=True,
    )
    print(OUT)


if __name__ == "__main__":
    main()
