"""Build multi-size uninstall.ico from uninstall.png (Pillow)."""
from __future__ import annotations

import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError as e:
    print("Pillow is required: pip install Pillow", file=sys.stderr)
    raise SystemExit(1) from e

SIZES = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def main() -> int:
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <input.png> <output.ico>", file=sys.stderr)
        return 2
    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    if not src.is_file():
        print(f"Missing input: {src}", file=sys.stderr)
        return 1
    dst.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(src) as im:
        im.convert("RGBA").save(dst, format="ICO", sizes=SIZES)
    print(f"Wrote {dst}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
