from __future__ import annotations

import math
from pathlib import Path

import imageio.v2 as imageio
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[1]
HERO = ROOT / "Docs" / "github-hero.png"
OUT = ROOT / "Docs" / "grokoengine3d-youtube-demo.mp4"

W, H = 1920, 1080
FPS = 24
DURATION = 28
FRAMES = FPS * DURATION


def font(size: int, bold: bool = False):
    candidates = [
        r"C:\Windows\Fonts\seguisb.ttf" if bold else r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arialbd.ttf" if bold else r"C:\Windows\Fonts\arial.ttf",
    ]
    for path in candidates:
        try:
            return ImageFont.truetype(path, size)
        except Exception:
            pass
    return ImageFont.load_default()


F_LOGO = font(72, True)
F_TITLE = font(56, True)
F_SUB = font(34)
F_UI = font(25, True)
F_SMALL = font(24)
F_TINY = font(18)


def cover(img: Image.Image, w: int, h: int) -> Image.Image:
    img = img.convert("RGB")
    scale = max(w / img.width, h / img.height)
    nw, nh = int(img.width * scale), int(img.height * scale)
    img = img.resize((nw, nh), Image.Resampling.LANCZOS)
    return img.crop(((nw - w) // 2, (nh - h) // 2, (nw - w) // 2 + w, (nh - h) // 2 + h))


def ease(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def fade_between(t: float, start: float, end: float, fade: float = 0.6) -> float:
    if t < start or t > end:
        return 0.0
    return min(ease((t - start) / fade), ease((end - t) / fade))


def add_text(d: ImageDraw.ImageDraw, xy, text: str, fnt, fill, shadow=True, anchor=None):
    x, y = xy
    if shadow:
        d.text((x + 3, y + 3), text, font=fnt, fill=(0, 0, 0, int(fill[3] * 0.60) if len(fill) > 3 else 160), anchor=anchor)
    d.text((x, y), text, font=fnt, fill=fill, anchor=anchor)


def rounded(d: ImageDraw.ImageDraw, box, fill, outline=None, radius=16, width=2):
    d.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def draw_project_editor(d: ImageDraw.ImageDraw, t: float):
    # Clean readable editor overlay on top of the generated hero so the project is visible.
    rounded(d, (90, 130, 1830, 950), (12, 15, 22, 145), (70, 125, 190, 90), 22, 2)
    d.rectangle((90, 130, 1830, 184), fill=(18, 22, 31, 205))
    add_text(d, (125, 145), "</> GrokoEngine3D", F_UI, (190, 225, 255, 235), shadow=False)
    for i, label in enumerate(["Scene", "Game", "Inspector", "Project", "Particles", "Shader Graph"]):
        x = 420 + i * 170
        col = (42, 95, 160, 210) if label in ("Game", "Particles") else (32, 38, 50, 190)
        rounded(d, (x, 143, x + 135, 172), col, (90, 150, 220, 100), 8, 1)
        add_text(d, (x + 15, 148), label, F_TINY, (225, 235, 245, 230), shadow=False)

    # Panels
    rounded(d, (115, 210, 410, 900), (10, 13, 20, 190), (50, 70, 95, 110), 14, 1)
    add_text(d, (140, 232), "Hierarchy", F_SMALL, (210, 230, 250, 230), shadow=False)
    for idx, name in enumerate(["Main Camera", "Directional Light", "Player_Controller", "Gun_Muzzle", "Particle_System", "Enemy_Target"]):
        y = 280 + idx * 50
        if name == "Player_Controller":
            rounded(d, (132, y - 8, 390, y + 28), (42, 95, 160, 170), None, 8, 1)
        add_text(d, (152, y), "▸ " + name, F_TINY, (185, 205, 225, 230), shadow=False)

    rounded(d, (1450, 210, 1805, 900), (10, 13, 20, 190), (50, 70, 95, 110), 14, 1)
    add_text(d, (1476, 232), "Inspector", F_SMALL, (210, 230, 250, 230), shadow=False)
    for idx, comp in enumerate(["Transform", "Character Controller", "Rigidbody (Kinematic)", "Shooter Controller", "Particle System"]):
        y = 286 + idx * 76
        rounded(d, (1472, y - 15, 1784, y + 36), (22, 28, 38, 170), (54, 92, 130, 80), 9, 1)
        add_text(d, (1492, y), comp, F_TINY, (210, 228, 242, 230), shadow=False)

    rounded(d, (115, 910, 1805, 940), (9, 12, 18, 200), None, 8, 1)
    add_text(d, (140, 916), "Project: Assets / Demo / Player / VFX / Materials", F_TINY, (155, 185, 215, 230), shadow=False)


def draw_viewport_world(d: ImageDraw.ImageDraw, t: float):
    # Viewport area
    vx0, vy0, vx1, vy1 = 455, 210, 1418, 900
    rounded(d, (vx0, vy0, vx1, vy1), (8, 11, 18, 80), (50, 100, 165, 120), 16, 2)
    # grid
    horizon = 610
    for i in range(19):
        x = vx0 + i * 55
        d.line((x, horizon, int((x - 940) * 1.65 + 940), vy1 - 10), fill=(55, 80, 110, 95), width=2)
    for j in range(8):
        y = horizon + j * 38
        d.line((vx0 + 18, y, vx1 - 18, y), fill=(55, 80, 110, 80), width=2)

    # light beams / scene props
    d.ellipse((1170, 285, 1240, 355), fill=(255, 190, 80, 120))
    for k in range(5):
        d.line((1205, 355, 760 + k * 110, 780), fill=(255, 160, 60, 30), width=6)
    rounded(d, (1050, 620, 1180, 735), (38, 48, 62, 220), (100, 130, 160, 150), 8, 2)
    rounded(d, (1220, 650, 1325, 740), (52, 38, 32, 220), (190, 110, 65, 140), 8, 2)


def draw_player(d: ImageDraw.ImageDraw, t: float):
    vx0, vy0, vx1, vy1 = 455, 210, 1418, 900
    run_start, run_end = 5.0, 17.5
    u = ease((t - run_start) / (run_end - run_start))
    px = int(620 + 460 * u)
    py = 705 + int(math.sin(t * 5.5) * 4)
    bob = math.sin(t * 10) * 10

    # shadow
    d.ellipse((px - 70, py + 94, px + 75, py + 118), fill=(0, 0, 0, 130))
    # body capsule / character
    d.rounded_rectangle((px - 34, py - 92, px + 34, py + 70), radius=28, fill=(54, 115, 190, 245), outline=(160, 215, 255, 210), width=3)
    d.ellipse((px - 30, py - 148, px + 30, py - 88), fill=(210, 225, 238, 245), outline=(80, 120, 160, 190), width=2)
    # visor
    d.rounded_rectangle((px - 22, py - 126, px + 24, py - 108), radius=8, fill=(35, 55, 78, 240))
    d.rectangle((px - 16, py - 121, px + 20, py - 116), fill=(75, 205, 255, 220))
    # legs animated
    leg1 = int(math.sin(t * 8) * 22)
    leg2 = int(math.sin(t * 8 + math.pi) * 22)
    d.line((px - 16, py + 64, px - 34 + leg1, py + 120), fill=(32, 74, 130, 245), width=12)
    d.line((px + 16, py + 64, px + 34 + leg2, py + 120), fill=(32, 74, 130, 245), width=12)
    # arms + gun
    d.line((px + 28, py - 46, px + 86, py - 28 + bob * 0.25), fill=(210, 225, 238, 245), width=12)
    d.rounded_rectangle((px + 76, py - 42, px + 145, py - 16), radius=8, fill=(24, 30, 38, 250), outline=(125, 155, 185, 180), width=2)
    d.rectangle((px + 132, py - 35, px + 172, py - 24), fill=(42, 48, 58, 255))
    return px + 172, py - 30


def draw_shots(d: ImageDraw.ImageDraw, t: float, muzzle):
    # repeating shots between 8s and 20s
    mx, my = muzzle
    if 7.0 < t < 21.0:
        for n in range(8):
            shot_t = 7.6 + n * 1.55
            age = t - shot_t
            if 0 <= age <= 1.15:
                sx = mx + int(age * 610)
                sy = my + int(math.sin(age * 10) * 5)
                alpha = int(255 * (1 - age / 1.15))
                d.line((mx, my, sx, sy), fill=(80, 200, 255, int(alpha * 0.35)), width=6)
                d.ellipse((sx - 14, sy - 5, sx + 24, sy + 5), fill=(85, 220, 255, alpha))
                d.ellipse((mx + 4, my - 28, mx + 72, my + 28), fill=(255, 190, 60, int(alpha * 0.35)))
            # impact burst
            impact_age = t - (shot_t + 1.1)
            if 0 <= impact_age <= 0.85:
                cx, cy = 1240, 665
                burst = impact_age / 0.85
                for p in range(26):
                    a = p * 0.62 + n
                    r = 18 + burst * (80 + (p % 5) * 12)
                    x = cx + math.cos(a) * r
                    y = cy + math.sin(a) * r * 0.65
                    size = 6 * (1 - burst) + 2
                    col = (255, 160, 55, int(220 * (1 - burst))) if p % 3 else (80, 220, 255, int(220 * (1 - burst)))
                    d.ellipse((x - size, y - size, x + size, y + size), fill=col)


def scene_caption(t: float):
    if t < 4.5:
        return "GrokoEngine3D", "Experimental C# 3D Engine / Editor"
    if t < 10.0:
        return "Player Controller", "Walking inside the editor scene"
    if t < 16.5:
        return "Shooting + Particles", "Projectile trail, muzzle flash and impact VFX"
    if t < 22.5:
        return "Unity-style Workflow", "Hierarchy, Inspector, Project panel and real-time viewport"
    return "Created by Gerar Gonzalez", "Public experimental preview on GitHub"


def draw_caption(d: ImageDraw.ImageDraw, t: float):
    title, sub = scene_caption(t)
    pulse = 0.5 + 0.5 * math.sin(t * 2.0)
    rounded(d, (95, 820, 780, 980), (4, 8, 14, 178), (70, 150, 240, 120), 26, 2)
    d.rectangle((118, 844, 128, 958), fill=(58, 155, 255, int(190 + 45 * pulse)))
    add_text(d, (152, 846), title, F_TITLE, (238, 246, 255, 245))
    add_text(d, (154, 914), sub, F_SUB, (166, 210, 255, 235))


def draw_outro(d: ImageDraw.ImageDraw, t: float):
    a = fade_between(t, 24.0, 28.0, 1.0)
    if a <= 0:
        return
    alpha = int(230 * a)
    d.rectangle((0, 0, W, H), fill=(2, 5, 10, int(130 * a)))
    add_text(d, (W // 2, 380), "GrokoEngine3D", F_LOGO, (238, 246, 255, alpha), anchor="mm")
    add_text(d, (W // 2, 470), "Experimental engine in active development", F_SUB, (170, 215, 255, alpha), anchor="mm")
    add_text(d, (W // 2, 560), "github.com/gerargonzalez9401-afk/GrokoEngine3D", F_SMALL, (235, 238, 245, alpha), anchor="mm")


def main():
    if not HERO.exists():
        raise FileNotFoundError(HERO)

    hero = cover(Image.open(HERO), W, H)
    hero = ImageEnhance.Color(hero).enhance(1.12)
    hero = ImageEnhance.Contrast(hero).enhance(1.06)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    writer = imageio.get_writer(
        OUT,
        fps=FPS,
        codec="libx264",
        quality=8,
        macro_block_size=1,
        ffmpeg_params=["-pix_fmt", "yuv420p", "-movflags", "+faststart"],
    )

    try:
        for i in range(FRAMES):
            t = i / FPS
            zoom = 1.0 + 0.025 * (i / max(1, FRAMES - 1))
            zw, zh = int(W * zoom), int(H * zoom)
            bg = hero.resize((zw, zh), Image.Resampling.LANCZOS)
            bg = bg.crop(((zw - W) // 2, (zh - H) // 2, (zw - W) // 2 + W, (zh - H) // 2 + H))
            bg = bg.filter(ImageFilter.GaussianBlur(0.35))

            frame = bg.convert("RGBA")
            overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            d = ImageDraw.Draw(overlay, "RGBA")
            d.rectangle((0, 0, W, H), fill=(2, 5, 10, 65))

            draw_project_editor(d, t)
            draw_viewport_world(d, t)
            muzzle = draw_player(d, t)
            draw_shots(d, t, muzzle)
            draw_caption(d, t)
            draw_outro(d, t)

            final = Image.alpha_composite(frame, overlay).convert("RGB")
            writer.append_data(np.asarray(final))
    finally:
        writer.close()

    print(OUT)


if __name__ == "__main__":
    main()
