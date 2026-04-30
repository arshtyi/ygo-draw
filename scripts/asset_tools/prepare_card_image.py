import argparse
import sys
from pathlib import Path
from urllib.request import urlopen

from asset_paths import card_image_dir, project_root
from download_common import DOWNLOAD_TIMEOUT_SECONDS

try:
    from PIL import Image, ImageOps
except ImportError as exc:
    raise SystemExit(
        "Pillow is required for card image conversion. "
        "Install it with: python -m pip install Pillow"
    ) from exc


IMAGE_URL_TEMPLATE = "https://images.ygoprodeck.com/images/cards_cropped/{card_image}.jpg"


def build_project_root() -> Path:
    return project_root()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Download a cropped Yu-Gi-Oh card image and convert it to PNG for typst-ygo."
    )
    parser.add_argument("card_image", help="YGOPRODeck cropped card image id")
    parser.add_argument(
        "--project-root",
        default=None,
        help="Project root. Defaults to this script's repository root.",
    )
    return parser.parse_args()


def download_bytes(url: str) -> bytes:
    with urlopen(url, timeout=DOWNLOAD_TIMEOUT_SECONDS) as response:
        content_type = response.headers.get("Content-Type", "")
        if "image" not in content_type.lower():
            raise RuntimeError(f"Expected image response, got Content-Type={content_type!r}")
        return response.read()


def prepare_card_image(card_image: str, project_root: Path) -> Path:
    image_dir = card_image_dir(project_root)
    image_dir.mkdir(parents=True, exist_ok=True)

    png_path = image_dir / f"{card_image}.png"
    if png_path.exists():
        print(f"Using cached PNG: {png_path}")
        return png_path

    jpg_path = image_dir / f"{card_image}.jpg"
    if not jpg_path.exists():
        url = IMAGE_URL_TEMPLATE.format(card_image=card_image)
        print(f"Downloading {url} -> {jpg_path}")
        jpg_path.write_bytes(download_bytes(url))

    print(f"Converting {jpg_path} -> {png_path}")
    with Image.open(jpg_path) as image:
        image = ImageOps.exif_transpose(image)
        if image.mode not in ("RGB", "RGBA"):
            image = image.convert("RGBA" if "A" in image.getbands() else "RGB")
        image.save(png_path, format="PNG", optimize=True)

    jpg_path.unlink(missing_ok=True)
    return png_path


def main() -> int:
    args = parse_args()
    project_root = Path(args.project_root).resolve() if args.project_root else build_project_root()
    try:
        prepare_card_image(str(args.card_image), project_root)
    except Exception as exc:
        print(f"Failed to prepare card image {args.card_image}: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
