"""Builds the Nexus / README images in docs/pics/nexus from the raw screenshots in docs/pics.

Needs Pillow and numpy (pip install pillow numpy). Usage: python make_nexus_images.py
Raw screenshots (git-ignored) are 2878 px wide with black bars around a 16:9 game frame; the frame top is detected.
Boxes below are in raw screenshot pixels (full file, bars included): left, top, right, bottom.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "nexus")
FONT = "C:/Windows/Fonts/segoeuib.ttf"
FONT_LIGHT = "C:/Windows/Fonts/segoeui.ttf"
GOLD = (216, 201, 163)
W, H = 1920, 1080
RAW_W = 2878
S = W / RAW_W  # raw -> 1920 scale

# The Enchantments block of each tooltip, and single lines worth a zoom.
ENCHANTS = {
    "ranges": (1366, 950, 2026, 1224),
    "details": (1720, 718, 2382, 1092),
    "exalted": (1366, 1004, 2026, 1226),
}
EXECUTE_LINE = (1716, 808, 2382, 880)          # "Execute Infected Low Health Enemies ... (<20% HP)" (execute.png)
DRAIN_LINE = (1366, 1180, 2026, 1224)          # "Drain Health in Combat (1/s)" on the ranges shot
FACET_LINE = {"facet_off": (1000, 684, 1678, 728), "facet_on": (1000, 654, 1678, 698), "exalted": (1366, 856, 2026, 898),
              "character": (1000, 658, 1678, 702)}
FACET_POPUP = (486, 778, 982, 946)             # the game's "Fireproof / Item Facet" pop-up (facet_off)
FACET_TOOLTIP = {"facet_off": (996, 410, 1680, 1000), "facet_on": (996, 380, 1680, 970)}

# Settings screenshot: our three rows, and each row for the captions.
SETTINGS_ROWS = (282, 1290, 1420, 1522)
SETTINGS_ROW_Y = {"ranges": 1320, "facets": 1404, "details": 1488}


def font(size, light=False):
    return ImageFont.truetype(FONT_LIGHT if light else FONT, size)


_cache = {}


def raw(name):
    if name not in _cache:
        _cache[name] = Image.open(os.path.join(HERE, name + ".png")).convert("RGB")
    return _cache[name]


def frame_top(name):
    rows = np.asarray(raw(name).convert("L")).astype(float).mean(axis=1)
    return int(np.where(rows > 6)[0][0])


def frame(name):
    """The 16:9 game frame, scaled to 1920x1080."""
    top = frame_top(name)
    return raw(name).crop((0, top, RAW_W, top + round(RAW_W * 9 / 16))).resize((W, H), Image.LANCZOS)


def to_frame(name, box, pad=0):
    top = frame_top(name)
    l, t, r, b = box
    return (round(l * S) - pad, round((t - top) * S) - pad, round(r * S) + pad, round((b - top) * S) + pad)


def spotlight(img, boxes, dim=0.38, radius=12):
    """Darken everything outside the boxes and outline them in gold."""
    dark = Image.eval(img, lambda v: int(v * dim))
    mask = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(mask)
    for b in boxes:
        d.rounded_rectangle(b, radius, fill=255)
    out = Image.composite(img, dark, mask.filter(ImageFilter.GaussianBlur(3)))
    d = ImageDraw.Draw(out)
    for b in boxes:
        d.rounded_rectangle(b, radius, outline=GOLD, width=3)
    return out


def shadowed_paste(dst, src, xy, border=GOLD):
    x, y = xy
    sh = Image.new("RGBA", (src.width + 60, src.height + 60), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rectangle((30, 30, 30 + src.width, 30 + src.height), fill=(0, 0, 0, 200))
    sh = sh.filter(ImageFilter.GaussianBlur(14))
    dst.paste(sh, (x - 30 + 6, y - 30 + 8), sh)
    dst.paste(src, (x, y))
    ImageDraw.Draw(dst).rectangle((x - 1, y - 1, x + src.width, y + src.height), outline=border, width=2)


def zoom(name, box, factor):
    """A raw-resolution crop, scaled by factor relative to the 1920 frame."""
    im = raw(name).crop(box)
    return im.resize((round(im.width * S * factor), round(im.height * S * factor)), Image.LANCZOS)


def connector(img, a, b):
    ImageDraw.Draw(img).line((a, b), fill=GOLD, width=2)


def settings_row(key, width):
    y = SETTINGS_ROW_Y[key]
    im = raw("settings").crop((282, y - 36, 1420, y + 36))
    return im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)


def caption(img, key, cx, y, width=640):
    """The real settings row, centered at (cx, y) on a dark strip, with 'Options > Gameplay' above it."""
    row = settings_row(key, width)
    x = cx - row.width // 2
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle((x - 24, y - 48, x + row.width + 24, y + row.height + 14), 10, fill=(0, 0, 0, 200))
    d.text((x, y - 42), "Options > Gameplay", font=font(24, light=True), fill=(170, 170, 170))
    img.paste(row, (x, y))


def label(img, text, xy, size=34, anchor="la", color="white"):
    d = ImageDraw.Draw(img, "RGBA")
    f = font(size)
    l, t, r, b = d.textbbox(xy, text, font=f, anchor=anchor)
    d.rounded_rectangle((l - 16, t - 10, r + 16, b + 10), 10, fill=(0, 0, 0, 200))
    d.text(xy, text, font=f, fill=color, anchor=anchor)


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name), quality=92)
    print(f"{name:20} {os.path.getsize(os.path.join(OUT, name)) // 1024:5} KB")


# ---- Tooltip screens: spotlight + zoom inset + the matching settings row -----------------------------------------------

def magnify(name, src, title, caption_key=None, extra_boxes=(), out=None):
    """A magnifier, not a comparison: the whole frame is dimmed (the source a little less, with a thin outline), and the
    source is enlarged as far as the free space to its left allows, joined to it by a translucent cone."""
    img = frame(name)
    box = to_frame(name, src, 6)
    boxes = [box] + [to_frame(name, b, 6) for b in extra_boxes]
    dark = Image.eval(img, lambda v: int(v * 0.30))
    mid = Image.eval(img, lambda v: int(v * 0.62))
    mask = Image.new("L", img.size, 0)
    md = ImageDraw.Draw(mask)
    for b in boxes:
        md.rounded_rectangle(b, 10, fill=255)
    img = Image.composite(mid, dark, mask.filter(ImageFilter.GaussianBlur(2)))

    bw, bh = box[2] - box[0], box[3] - box[1]
    margin, gap = 40, 70
    factor = min((box[0] - margin - gap) / bw, (H - 360) / bh, 2.6)
    z = raw(name).crop(src)
    z = z.resize((round(bw * factor), round(bh * factor)), Image.LANCZOS)
    x = margin
    y = max(150, min(H - z.height - 210, (box[1] + box[3]) // 2 - z.height // 2))

    cone = Image.new("RGBA", img.size, (0, 0, 0, 0))
    cd = ImageDraw.Draw(cone)
    cd.polygon([(box[0], box[1]), (box[0], box[3]), (x + z.width, y + z.height), (x + z.width, y)], fill=GOLD + (28,))
    cd.line([(box[0], box[1]), (x + z.width, y)], fill=GOLD + (200,), width=2)
    cd.line([(box[0], box[3]), (x + z.width, y + z.height)], fill=GOLD + (200,), width=2)
    img = Image.alpha_composite(img.convert("RGBA"), cone).convert("RGB")
    d = ImageDraw.Draw(img)
    for b in boxes:
        d.rounded_rectangle(b, 10, outline=GOLD, width=2)
    shadowed_paste(img, z, (x, y))
    label(img, title, (x, y - 30), size=36, anchor="lb")
    if caption_key:
        caption(img, caption_key, x + z.width // 2, y + z.height + 80)
    save(img, out or name + ".jpg")


def ranges():
    magnify("ranges", ENCHANTS["ranges"], "Every roll with its worst–best range", "ranges")


def details():
    magnify("details", ENCHANTS["details"], "The numbers the game leaves out", "details")


def exalted():
    magnify("exalted", ENCHANTS["exalted"], "Exalted lines: the range grows too", extra_boxes=[FACET_LINE["exalted"]])


# ---- Facets: the game's pop-up vs the mod's numbers ---------------------------------------------------------------------

def facets():
    img = Image.new("RGB", (W, H), (12, 12, 14))
    d = ImageDraw.Draw(img)
    d.text((W // 2, 90), "Facets: what they really change", font=font(60), fill="white", anchor="mm")
    colx = (W // 4 + 10, 3 * W // 4 - 10)
    d.text((colx[0], 190), "Without the mod", font=font(40, light=True), fill=GOLD, anchor="mm")
    d.text((colx[1], 190), "With the mod", font=font(40, light=True), fill=GOLD, anchor="mm")
    top = 280

    # Left: the bare facet line and the game's pop-up.
    off_line = zoom("facet_off", FACET_LINE["facet_off"], 1.35)
    popup = zoom("facet_off", FACET_POPUP, 1.35)
    y = top
    shadowed_paste(img, off_line, (colx[0] - off_line.width // 2, y), border=(90, 85, 70))
    y += off_line.height + 50
    shadowed_paste(img, popup, (colx[0] - popup.width // 2, y), border=(90, 85, 70))
    y += popup.height + 45
    d.text((colx[0], y), "a pop-up without numbers", font=font(30, light=True), fill=(160, 160, 160), anchor="mm")

    # Right: the same facet with numbers, then two more.
    y = top
    for n in ("facet_on", "exalted", "character"):
        z = zoom(n, FACET_LINE[n], 1.35)
        shadowed_paste(img, z, (colx[1] - z.width // 2, y))
        y += z.height + 40
    d.text((colx[1], y + 20), "upside and downside, in numbers", font=font(30, light=True), fill=(160, 160, 160),
           anchor="mm")
    caption(img, "facets", W // 2, 760, width=560)
    save(img, "facets.jpg")


# ---- Settings: the Gameplay tab with our rows lit ---------------------------------------------------------------------

def settings():
    top = frame_top("settings")
    im = raw("settings").crop((0, top + 300, 1500, top + 1500))
    scale = min(W / im.width, (H - 150) / im.height)
    im = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
    l, t, r, b = SETTINGS_ROWS
    box = (round(l * scale) - 8, round((t - top - 300) * scale) - 8, round(r * scale) + 8, round((b - top - 300) * scale) + 8)
    im = spotlight(im, [box], dim=0.45)
    img = Image.new("RGB", (W, H), (0, 0, 0))
    img.paste(im, ((W - im.width) // 2, 130))
    ImageDraw.Draw(img).text((W // 2, 62), "Options > Gameplay", font=font(56), fill="white", anchor="mm")
    save(img, "settings.jpg")


# ---- Cover, Nexus header, README banner --------------------------------------------------------------------------------

def cards(scale):
    return [
        ("Roll ranges", zoom("ranges", ENCHANTS["ranges"], 1.0 * scale)),
        ("Hidden numbers", zoom("execute", EXECUTE_LINE, 1.25 * scale)),
        ("Facets", zoom("facet_on", FACET_LINE["facet_on"], 1.0 * scale)),
    ]


def cover():
    img = frame("character").filter(ImageFilter.GaussianBlur(6))
    img = Image.eval(img, lambda v: int(v * 0.4))
    d = ImageDraw.Draw(img)
    d.text((W // 2, 150), "Enchantment Details", font=font(104), fill="white", anchor="mm")
    d.text((W // 2, 250), "Roll ranges and hidden numbers for enchantments", font=font(42, light=True),
           fill=GOLD, anchor="mm")
    cs = cards(1.0)
    y = 360
    for i, (lbl, c) in enumerate(cs):
        x = (W - c.width) // 2
        shadowed_paste(img, c, (x, y))
        d.text((x - 24, y + c.height // 2), lbl, font=font(30, light=True), fill=(200, 200, 200), anchor="rm")
        y += c.height + 60
    d.text((W // 2, H - 70), "Enchantments  ·  Gems  ·  Facets", font=font(34, light=True), fill=(170, 170, 170),
           anchor="mm")
    save(img, "cover.jpg")


def header():
    """Nexus page header, 1300x372: the character shot with the title on the right."""
    # A 1300x372 window (1:1 in frame pixels) with the tooltip on the left and the character behind the title.
    img = frame("character").crop((620, 205, 620 + 1300, 205 + 372))
    grad = Image.linear_gradient("L").rotate(90).resize((1300, 372))
    img = Image.composite(Image.new("RGB", img.size, (0, 0, 0)), img,
                          grad.point(lambda v: 0 if v < 110 else min(235, int((v - 110) * 5))))
    d = ImageDraw.Draw(img)
    d.text((1260, 150), "Enchantment Details", font=font(60), fill="white", anchor="rm")
    d.text((1262, 225), "Roll ranges and hidden numbers", font=font(28, light=True), fill=GOLD, anchor="rm")
    save(img, "header.jpg")


def banner():
    """GitHub README banner, 1600x320: title plus one zoomed line per feature."""
    bw, bh = 1600, 320
    bg = frame("character").filter(ImageFilter.GaussianBlur(6))
    img = Image.eval(bg.crop((0, (H - round(W * bh / bw)) // 2, W, (H + round(W * bh / bw)) // 2)).resize((bw, bh), Image.LANCZOS),
                     lambda v: int(v * 0.42))
    d = ImageDraw.Draw(img)
    d.text((bw // 2, 50), "Enchantment Details", font=font(58), fill="white", anchor="mm")
    d.text((bw // 2, 102), "Roll ranges and hidden numbers for enchantments", font=font(24, light=True),
           fill=GOLD, anchor="mm")
    lines = [zoom("ranges", DRAIN_LINE, 0.95), zoom("execute", EXECUTE_LINE, 0.95), zoom("facet_on", FACET_LINE["facet_on"], 0.95)]
    gap = 30
    total = sum(c.width for c in lines) + gap * (len(lines) - 1)
    scale = min(1.0, (bw - 80) / total)
    lines = [c.resize((round(c.width * scale), round(c.height * scale)), Image.LANCZOS) for c in lines]
    total = sum(c.width for c in lines) + gap * (len(lines) - 1)
    x = (bw - total) // 2
    top = 170
    for c in lines:
        shadowed_paste(img, c, (x, top))
        x += c.width + gap
    img.save(os.path.join(HERE, "banner.jpg"), quality=92)
    print(f"{'banner.jpg (docs/pics)':20} {os.path.getsize(os.path.join(HERE, 'banner.jpg')) // 1024:5} KB")


if __name__ == "__main__":
    for f in (cover, ranges, details, exalted, facets, settings, header, banner):
        f()
