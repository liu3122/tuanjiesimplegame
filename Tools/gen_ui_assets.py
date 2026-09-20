# -*- coding: utf-8 -*-
"""生成「秘宝暗金」UI 素材（PNG，程序化绘制，可整体替换为美术成品）"""
import math, os
from PIL import Image, ImageDraw, ImageFilter

OUT = r"G:\SimpleProject\Assets\AssetRaw\UI"
os.makedirs(OUT, exist_ok=True)

# 色板
BG      = (18, 21, 29, 255)
PANEL   = (28, 34, 48, 255)
PANEL_L = (38, 46, 64, 255)
GOLD    = (232, 195, 90, 255)
GOLD_L  = (245, 208, 97, 255)
GOLD_D  = (168, 138, 58, 255)
GREEN   = (77, 208, 165, 255)
RED     = (224, 93, 93, 255)
BLUE    = (110, 198, 255, 255)
TXT     = (154, 163, 178, 255)
WHITE   = (245, 247, 250, 255)


def rounded(draw, box, r, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def save(img, name):
    img.save(os.path.join(OUT, name))
    print("  ", name)


# ---------------- 九宫格面板 ----------------
def panel(name, size, base, edge, radius=24, border=32, glow=False):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    m = 6
    if glow:
        g = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        gd = ImageDraw.Draw(g)
        rounded(gd, (m, m, size - m, size - m), radius, edge)
        g = g.filter(ImageFilter.GaussianBlur(6))
        img.alpha_composite(g)
        d = ImageDraw.Draw(img)
    rounded(d, (m, m, size - m, size - m), radius, base, outline=edge, width=3)
    # 内侧高光
    rounded(d, (m + 4, m + 4, size - m - 4, m + size // 5), radius - 6, (255, 255, 255, 14))
    save(img, name)


def button(name, w, h, base, top, edge, text_color=WHITE):
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    m = 3
    r = h // 3
    rounded(d, (m, m, w - m, h - m), r, base, outline=edge, width=2)
    # 顶部渐变高光
    hi = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi)
    hd.rounded_rectangle((m + 3, m + 3, w - m - 3, h // 2), r - 4, top)
    img.alpha_composite(hi)
    save(img, name)


print("panels / buttons")
panel("panel_gold.png", 256, PANEL, GOLD, glow=True)
panel("panel_dark.png", 256, PANEL, (70, 80, 100, 255))
button("btn_gold.png", 192, 64, GOLD_D, (255, 255, 255, 40), GOLD_L)
button("btn_green.png", 192, 64, (34, 110, 88, 255), (255, 255, 255, 36), GREEN)
button("btn_red.png", 192, 64, (122, 48, 48, 255), (255, 255, 255, 30), RED)
button("btn_blue.png", 192, 64, (38, 74, 110, 255), (255, 255, 255, 30), BLUE)

# ---------------- 开箱格子 ----------------
print("slots")
for name, base, edge in [("slot_locked", PANEL_L, GOLD_D), ("slot_open", (16, 19, 26, 235), (60, 68, 84, 255))]:
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rounded(d, (4, 4, 124, 124), 16, base, outline=edge, width=3)
    if name.endswith("locked"):
        d.ellipse((58, 30, 70, 46), fill=GOLD_L)  # 问号点
        d.rectangle((56, 50, 72, 88), fill=GOLD_L)
        d.ellipse((56, 94, 72, 106), fill=GOLD_L)
    save(img, name + ".png")


# ---------------- 金币 ----------------
print("coin")
img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.ellipse((10, 14, 118, 114), fill=GOLD_D, outline=GOLD, width=4)
d.ellipse((24, 26, 104, 96), fill=GOLD)
d.ellipse((36, 34, 92, 66), fill=GOLD_L)
d.rectangle((56, 44, 72, 86), fill=GOLD_D)
d.rectangle((48, 44, 80, 56), fill=GOLD_D)
save(img, "icon_coin.png")


# ---------------- 宝箱 ----------------
def chest(name, body, band):
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rounded(d, (16, 56, 112, 108), 10, body, outline=(0, 0, 0, 90), width=2)
    d.pieslice((16, 26, 112, 96), 180, 360, fill=tuple(min(255, c + 24) for c in body[:3]) + (255,))
    d.rectangle((14, 58, 114, 70), fill=band)
    d.rounded_rectangle((52, 54, 76, 84), 6, GOLD_L, outline=GOLD_D, width=2)
    d.ellipse((60, 64, 68, 74), fill=(30, 30, 34, 255))
    save(img, name)


chest("chest_wood.png", (146, 106, 74, 255), (108, 78, 52, 255))
chest("chest_silver.png", (176, 188, 200, 255), (128, 140, 154, 255))
chest("chest_gold.png", (232, 195, 90, 255), (188, 152, 60, 255))


# ---------------- 技能图标 ----------------
print("skills")
def skill_base(color):
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.regular_polygon((64, 64, 58), 6, rotation=30, fill=PANEL, outline=color, width=4)
    return img, d


img, d = skill_base(GREEN)
d.ellipse((38, 38, 90, 90), outline=GREEN, width=6)
d.ellipse((54, 54, 74, 74), fill=GREEN)
save(img, "skill_insight.png")

img, d = skill_base(GOLD_L)
d.line((64, 90, 64, 40), fill=GOLD_L, width=10)
d.polygon([(44, 54), (64, 30), (84, 54)], fill=GOLD_L)
d.line((48, 96, 80, 96), fill=GOLD_L, width=8)
save(img, "skill_price.png")

img, d = skill_base(BLUE)
d.polygon([(72, 22), (46, 68), (62, 68), (54, 106), (84, 56), (66, 56)], fill=BLUE)
save(img, "skill_free.png")

# ---------------- 主大厅四入口 ----------------
print("menu icons")
img, d = Image.new("RGBA", (128, 128), (0, 0, 0, 0)), None
d = ImageDraw.Draw(img)
d.rectangle((20, 34, 108, 104), fill=PANEL_L, outline=GOLD, width=3)
d.polygon([(20, 34), (64, 14), (108, 34)], fill=GOLD_D)
d.rectangle((52, 62, 76, 104), fill=GOLD_D)
save(img, "icon_shop.png")

img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
rounded(d, (22, 30, 106, 108), 12, (146, 106, 74, 255), outline=GOLD, width=3)
d.arc((22, 6, 106, 74), 180, 360, fill=GOLD, width=6)
d.rectangle((58, 60, 70, 84), fill=GOLD_L)
save(img, "icon_bag.png")

img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rectangle((24, 40, 104, 100), fill=PANEL_L, outline=GOLD, width=3)
d.polygon([(24, 40), (64, 20), (104, 40)], fill=GOLD_D, outline=GOLD)
d.ellipse((52, 60, 76, 86), outline=GOLD_L, width=4)
save(img, "icon_auction.png")

img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
for i, (x, h) in enumerate([(28, 40), (56, 70), (84, 52)]):
    top = 104 - h
    d.rectangle((x, top, x + 20, 104), fill=(GREEN if i == 1 else PANEL_L), outline=GOLD, width=2)
d.line((20, 108, 108, 108), fill=GOLD, width=4)
save(img, "icon_rank.png")

print("ALL UI ASSETS DONE ->", OUT)
