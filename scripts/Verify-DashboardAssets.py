"""Optional development-only audit; requires Pillow, never starts the application or HID.
Original PNG bytes are compared, not changed. Composites are asset diagnostics, not Windows screenshots.
"""
from pathlib import Path
import hashlib
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent
sources = {
    "g915.png": "RGB Lightspeed Gaming Keyboard Cutout.png",
    "g305.png": "Matte Black Gaming Mouse with Blue Glow (1).png",
    "hyperx-cloud-iii.png": "HyperX-Style Black and Red Gaming Headset (1).png",
}
rows = []
for name, original in sources.items():
    path = root / "assets/devices" / name
    assert path.read_bytes() == (path.parent / original).read_bytes(), "Canonical asset differs from owner source"
    with Image.open(path) as image:
        image.load()
        assert image.mode == "RGBA", f"{name}: missing alpha"
        alpha = image.getchannel("A")
        counts = alpha.histogram()
        assert counts[0] > 0 and sum(counts[1:]) > 0, f"{name}: missing transparency/visible content"
        bounds = alpha.getbbox()
        print(f"{name}: {image.size}; alpha=0:{counts[0]}, partial:{sum(counts[1:255])}, 255:{counts[255]}; bounds={bounds}; SHA256={hashlib.sha256(path.read_bytes()).hexdigest()}")
        scaled = image.crop(bounds)
        scaled.thumbnail((430, 210))
        row = Image.new("RGB", (900, 240), "white")
        ImageDraw.Draw(row).text((5, 2), name, fill="black")
        for x, color in [(5, (9, 18, 30, 255)), (455, (240, 244, 249, 255))]:
            background = Image.new("RGBA", (440, 210), color)
            background.alpha_composite(scaled, ((440 - scaled.width) // 2, (210 - scaled.height) // 2))
            row.paste(background.convert("RGB"), (x, 25))
        rows.append(row)
assert (root / "assets/references/dashboard-concept.png").read_bytes() == (
    root / "assets/references/Neon Gaming Device Battery Dashboard-1.png").read_bytes()
preview = Image.new("RGB", (900, 720), "white")
for i, row in enumerate(rows):
    preview.paste(row, (0, i * 240))
output = root / "artifacts/device-alpha-validation.png"
output.parent.mkdir(exist_ok=True)
preview.save(output)
print(f"PASS: byte-identical canonical PNGs. Inspect dark/light composites: {output}")
