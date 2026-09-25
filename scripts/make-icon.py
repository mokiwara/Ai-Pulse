"""Rebuild the geometric Windows app icon. Requires Pillow only for this script."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
target = root / "assets"
target.mkdir(exist_ok=True)
size = 256
im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
d.rounded_rectangle((10, 10, 246, 246), radius=58, fill="#172238")
# AI Pulse: the same three rounded activity bars as the native UI mark.
for x, height, color in [(62, 83, "#889BEE"), (110, 128, "#59CDC7"), (158, 64, "#AF8ACA")]:
    d.rounded_rectangle((x, 192 - height, x + 27, 192), radius=13, fill=color)
im.save(target / "icon.ico", format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
