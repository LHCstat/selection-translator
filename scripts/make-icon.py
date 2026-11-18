from PIL import Image, ImageDraw, ImageFont

SIZE = 256
OUT_ICO = "src/SelectionTranslator.App/Assets/app.ico"
OUT_PNG = "docs/images/icon.png"

FONT = "C:/Windows/Fonts/msyhbd.ttc"

# Diagonal cobalt gradient.
gradient = Image.new("RGB", (SIZE, SIZE))
pixels = gradient.load()
start = (90, 131, 255)
end = (30, 62, 210)
for y in range(SIZE):
    for x in range(SIZE):
        t = (x / SIZE * 0.45) + (y / SIZE * 0.55)
        pixels[x, y] = tuple(int(start[i] * (1 - t) + end[i] * t) for i in range(3))

# Rounded-square mask.
mask = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(mask).rounded_rectangle((8, 8, SIZE - 8, SIZE - 8), radius=52, fill=255)

icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
icon.paste(gradient, (0, 0), mask)

draw = ImageDraw.Draw(icon)

# Subtle inner rim.
draw.rounded_rectangle((8, 8, SIZE - 8, SIZE - 8), radius=52, outline=(255, 255, 255, 38), width=2)

# Glyph centered optically.
font = ImageFont.truetype(FONT, 148)
box = draw.textbbox((0, 0), "译", font=font)
width = box[2] - box[0]
height = box[3] - box[1]
x = (SIZE - width) / 2 - box[0]
y = (SIZE - height) / 2 - box[1] - 4
draw.text((x, y), "译", font=font, fill=(255, 255, 255, 255))

icon.save(OUT_PNG)
icon.save(
    OUT_ICO,
    sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print(f"saved {OUT_ICO} and {OUT_PNG}")