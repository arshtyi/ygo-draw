import argparse
import sys
from pathlib import Path
from urllib.request import urlopen

from asset_paths import card_image_dir, project_root
from download_common import DOWNLOAD_TIMEOUT_SECONDS


IMAGE_URL_TEMPLATE = "https://images.ygoprodeck.com/images/cards_cropped/{card_image}.jpg"
SUPPORTED_SERIES = ("ot", "rd")


def build_project_root() -> Path:
    return project_root()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Download a cropped Yu-Gi-Oh card image for typst-ygo."
    )
    parser.add_argument("series", choices=SUPPORTED_SERIES, help="Card series: ot or rd")
    parser.add_argument("card_image", help="Card image id")
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


def prepare_card_image(series: str, card_image: str, root: Path) -> Path:
    image_dir = card_image_dir(series, root)
    image_dir.mkdir(parents=True, exist_ok=True)

    jpg_path = image_dir / f"{card_image}.jpg"
    if jpg_path.exists():
        print(f"Using cached image: {jpg_path}")
        return jpg_path

    url = IMAGE_URL_TEMPLATE.format(card_image=card_image)
    print(f"Downloading {url} -> {jpg_path}")
    jpg_path.write_bytes(download_bytes(url))
    return jpg_path


def main() -> int:
    args = parse_args()
    root = Path(args.project_root).resolve() if args.project_root else build_project_root()
    try:
        prepare_card_image(args.series, str(args.card_image), root)
    except Exception as exc:
        print(
            f"Failed to prepare card image {args.series}/{args.card_image}: {exc}",
            file=sys.stderr,
        )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
