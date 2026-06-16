from pathlib import Path


def project_root() -> Path:
    return Path(__file__).resolve().parents[2]


def card_template_dir(root: Path | None = None) -> Path:
    root = root or project_root()
    return root / "assets"


def card_image_dir(series: str, root: Path | None = None) -> Path:
    root = root or project_root()
    return root / "assets" / series / "images"


def download_cache_dir(root: Path | None = None) -> Path:
    root = root or project_root()
    return root / ".cache" / "downloads"
