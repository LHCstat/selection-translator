from PIL import Image, ImageDraw, ImageFont, ImageFilter

WIDTH, HEIGHT = 1280, 640
OUT = "docs/images/banner.png"

FONT_CN_BOLD = "C:/Windows/Fonts/msyhbd.ttc"
FONT_CN = "C:/Windows/Fonts/msyh.ttc"
FONT_EN = "C:/Windows/Fonts/segoeui.ttf"
FONT_EN_BOLD = "C:/Windows/Fonts/segoeuib.ttf"


def font(path, size):
    return ImageFont.truetype(path, size)


# Background: warm graphite, slightly lighter at the top.
image = Image.new("RGB", (WIDTH, HEIGHT), "#111318")
draw = ImageDraw.Draw(image)
top = (21, 23, 29)
bottom = (13, 15, 19)
for y in range(HEIGHT):
    t = y / (HEIGHT - 1)
    color = tuple(int(top[i] * (1 - t) + bottom[i] * t) for i in range(3))
    draw.line([(0, y), (WIDTH, y)], fill=color)

# Soft cobalt glow behind the card.
glow = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse((760, 40, 1420, 620), fill=(47, 91, 255, 70))
glow = glow.filter(ImageFilter.GaussianBlur(170))
image = Image.alpha_composite(image.convert("RGBA"), glow).convert("RGB")

draw = ImageDraw.Draw(image)

# Card shadow.
shadow = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle((690, 176, 1200, 484), radius=18, fill=(0, 0, 0, 150))
shadow = shadow.filter(ImageFilter.GaussianBlur(28))
image = Image.alpha_composite(image.convert("RGBA"), shadow).convert("RGB")
draw = ImageDraw.Draw(image)

# Translation card.
draw.rounded_rectangle((690, 170, 1200, 478), radius=18, fill="#1D2027", outline="#2A2E37", width=1)
draw.rounded_rectangle((706, 198, 711, 450), radius=2, fill="#7C9BFF")
draw.rounded_rectangle((918, 188, 972, 191), radius=2, fill="#3A404D")

draw.text((730, 220), "The quiet hum of a library", font=font(FONT_EN, 17), fill="#9AA3B2")
draw.text((730, 245), "is its own kind of language.", font=font(FONT_EN, 17), fill="#9AA3B2")

cn = font(FONT_CN, 24)
draw.text((730, 300), "图书馆的静谧嗡嗡声，", font=cn, fill="#ECEEF3")
draw.text((730, 340), "本身就是一种语言。", font=cn, fill="#ECEEF3")

small = font(FONT_CN, 13)
draw.text((730, 425), "866 ms", font=small, fill="#6E7788")
draw.text((1040, 425), "复制", font=small, fill="#98A1B2")
draw.text((1100, 425), "重试", font=small, fill="#98A1B2")
draw.text((1160, 425), "关闭", font=small, fill="#98A1B2")

# Brand mark.
draw.ellipse((80, 112, 128, 160), fill="#2F5BFF", outline="#5A83FF", width=1)
mark = font(FONT_CN_BOLD, 22)
box = draw.textbbox((0, 0), "译", font=mark)
draw.text((104 - (box[2] - box[0]) / 2, 136 - (box[3] - box[1]) / 2 - 2), "译", font=mark, fill="#FFFFFF")

draw.text((80, 186), "划译", font=font(FONT_CN_BOLD, 82), fill="#F4F6FA")
draw.text((84, 292), "SelectionTranslator", font=font(FONT_EN_BOLD, 25), fill="#7C9BFF")
draw.text((84, 336), "选中即译，不打断阅读", font=font(FONT_CN, 21), fill="#9AA3B2")

# Platform / model pill.
pill = (84, 400, 462, 442)
draw.rounded_rectangle(pill, radius=21, fill="#1D2027", outline="#2A2E37", width=1)
pill_text = "Windows 系统级划词 / 免费 GLM-4-Flash"
pill_font = font(FONT_CN, 15)
box = draw.textbbox((0, 0), pill_text, font=pill_font)
draw.text(
    (pill[0] + (pill[2] - pill[0] - (box[2] - box[0])) / 2,
     pill[1] + (pill[3] - pill[1] - (box[3] - box[1])) / 2 - 2),
    pill_text, font=pill_font, fill="#9AA3B2")

draw.text((84, 520), "Alt+D 快捷翻译", font=font(FONT_CN, 14), fill="#6E7788")
draw.text((240, 520), "点击外部关闭", font=font(FONT_CN, 14), fill="#6E7788")
draw.text((380, 520), "剪贴板自动恢复", font=font(FONT_CN, 14), fill="#6E7788")

image.save(OUT, optimize=True)
print(f"saved {OUT} {image.size}")