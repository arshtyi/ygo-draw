import argparse
import hashlib
import json
import logging
import shutil
import sys
import tarfile
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

import psycopg
from psycopg.types.json import Jsonb

from download_common import download_file


LOGGER = logging.getLogger("download_assets")

ASSETS_ARCHIVE_URL = "https://github.com/arshtyi/ygo-assets/releases/download/latest/assets.tar.xz"
OT_CARDS_JSON_URL = "https://github.com/arshtyi/ygo-cards/releases/download/latest/ot.json"
RD_CARDS_JSON_URL = "https://github.com/arshtyi/ygo-cards/releases/download/latest/rd.json"
TYPST_YGO_ARCHIVE_URL = "https://github.com/arshtyi/typst-ygo/archive/refs/heads/main.zip"
SUPPORTED_SERIES = ("ot", "rd")


@dataclass(frozen=True)
class PathConfig:
    project_root: Path
    cache_dir: Path
    assets_root: Path
    assets_archive: Path
    assets_extract_marker: Path
    ot_cards_json: Path
    rd_cards_json: Path
    ot_cards_target_path: Path
    rd_cards_target_path: Path
    typst_ygo_archive: Path
    typst_lib_target_dir: Path
    required_asset_files: tuple[Path, ...]
    required_typst_files: tuple[Path, ...]


@dataclass(frozen=True)
class DbConfig:
    host: str
    port: int
    user: str
    password: str
    dbname: str
    sslmode: str


@dataclass(frozen=True)
class AppConfigOutput:
    output_path: Path | None


@dataclass(frozen=True)
class CardRecord:
    series: str
    card_key: str
    payload: dict[str, Any]


def parse_cli_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Download Yu-Gi-Oh assets, extract typst-ygo layout, and import OT/RD cards into PostgreSQL."
    )
    parser.add_argument("--db-host", default="localhost", help="PostgreSQL host")
    parser.add_argument("--db-port", type=int, default=5432, help="PostgreSQL port")
    parser.add_argument("--db-user", default="ygo_draw", help="PostgreSQL user")
    parser.add_argument("--db-password", default="", help="PostgreSQL password")
    parser.add_argument(
        "--db-name", default="ygo_draw_db", help="PostgreSQL database name"
    )
    parser.add_argument("--db-sslmode", default="prefer", help="PostgreSQL sslmode")
    parser.add_argument(
        "--appsettings-output",
        default=None,
        help="Optional output path for generated appsettings.json",
    )
    return parser.parse_args()


def build_path_config() -> PathConfig:
    project_root = Path(__file__).resolve().parents[2]
    cache_dir = project_root / ".cache" / "downloads"
    assets_root = project_root / "assets"
    typst_lib_target_dir = project_root / "lib"
    return PathConfig(
        project_root=project_root,
        cache_dir=cache_dir,
        assets_root=assets_root,
        assets_archive=cache_dir / "assets.tar.xz",
        assets_extract_marker=assets_root / ".assets_archive.sha256",
        ot_cards_json=cache_dir / "ot.json",
        rd_cards_json=cache_dir / "rd.json",
        ot_cards_target_path=assets_root / "ot" / "card" / "ot.json",
        rd_cards_target_path=assets_root / "rd" / "card" / "rd.json",
        typst_ygo_archive=cache_dir / "typst-ygo-main.zip",
        typst_lib_target_dir=typst_lib_target_dir,
        required_asset_files=(
            assets_root / "ot" / "frame" / "effect.png",
            assets_root / "ot" / "attribute" / "light.png",
            assets_root / "ot" / "font" / "YGO_Card_JP.ttf",
            assets_root / "rd" / "frame" / "effect.png",
            assets_root / "rd" / "attribute" / "light.png",
            assets_root / "rd" / "font" / "YGO_Card_JP.ttf",
        ),
        required_typst_files=(
            typst_lib_target_dir / "mod.typ",
            typst_lib_target_dir / "ot" / "data.typ",
            typst_lib_target_dir / "rd" / "data.typ",
        ),
    )


def build_db_config(args: argparse.Namespace) -> DbConfig:
    return DbConfig(
        host=args.db_host,
        port=args.db_port,
        user=args.db_user,
        password=args.db_password,
        dbname=args.db_name,
        sslmode=args.db_sslmode,
    )


def build_app_output_config(args: argparse.Namespace) -> AppConfigOutput:
    output_arg = args.appsettings_output
    if output_arg is None or str(output_arg).strip() == "":
        return AppConfigOutput(output_path=None)
    return AppConfigOutput(output_path=Path(output_arg))


def download(url: str, destination: Path) -> None:
    LOGGER.info("Downloading %s -> %s", url, destination)
    download_file(url, destination)


def sha256_of_file(file_path: Path) -> str:
    hasher = hashlib.sha256()
    with file_path.open("rb") as file_handle:
        while True:
            chunk = file_handle.read(1024 * 1024)
            if not chunk:
                break
            hasher.update(chunk)
    return hasher.hexdigest()


def safe_clean_directory(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


def normalized_parts(member_name: str) -> tuple[str, ...]:
    parts = tuple(part for part in PurePosixPath(member_name).parts if part not in ("", "."))
    if any(part == ".." for part in parts):
        raise ValueError(f"Blocked path traversal: {member_name}")
    return parts


def asset_relative_path(member_name: str) -> Path | None:
    parts = normalized_parts(member_name)
    if not parts:
        return None
    while parts and parts[0] not in ("assets", *SUPPORTED_SERIES):
        parts = parts[1:]
    if not parts:
        return None
    if parts[0] == "assets":
        parts = parts[1:]
    if not parts or parts[0] not in SUPPORTED_SERIES:
        return None
    return Path(*parts)


def typst_lib_relative_path(member_name: str) -> Path | None:
    parts = normalized_parts(member_name)
    if not parts:
        return None
    if parts[0] != "lib" and len(parts) > 1:
        parts = parts[1:]
    if not parts or parts[0] != "lib":
        return None
    return Path(*parts[1:])


def ensure_within(target: Path, root: Path, source_name: str) -> None:
    resolved_target = target.resolve()
    resolved_root = root.resolve()
    if resolved_target != resolved_root and resolved_root not in resolved_target.parents:
        raise ValueError(f"Blocked path traversal while extracting: {source_name}")


def extract_assets_archive(path_config: PathConfig) -> None:
    LOGGER.info("Extracting static assets: %s -> %s", path_config.assets_archive, path_config.assets_root)
    safe_clean_directory(path_config.assets_root)
    with tarfile.open(path_config.assets_archive, mode="r:xz") as archive:
        for member in archive.getmembers():
            if member.issym() or member.islnk():
                raise ValueError(f"Blocked link in tar archive: {member.name}")

            relative_path = asset_relative_path(member.name)
            if relative_path is None:
                continue

            target_path = path_config.assets_root / relative_path
            ensure_within(target_path, path_config.assets_root, member.name)

            if member.isdir():
                target_path.mkdir(parents=True, exist_ok=True)
                continue
            if not member.isfile():
                raise ValueError(f"Unsupported tar member type: {member.name}")

            source = archive.extractfile(member)
            if source is None:
                raise ValueError(f"Unable to read tar member: {member.name}")
            target_path.parent.mkdir(parents=True, exist_ok=True)
            with source, target_path.open("wb") as target:
                shutil.copyfileobj(source, target)

    digest = sha256_of_file(path_config.assets_archive)
    path_config.assets_extract_marker.write_text(f"{digest}\n", encoding="utf-8")


def extract_typst_lib(path_config: PathConfig) -> None:
    LOGGER.info("Extracting typst-ygo lib: %s -> %s", path_config.typst_ygo_archive, path_config.typst_lib_target_dir)
    safe_clean_directory(path_config.typst_lib_target_dir)
    with zipfile.ZipFile(path_config.typst_ygo_archive) as archive:
        for member in archive.infolist():
            if member.is_dir():
                continue
            relative_path = typst_lib_relative_path(member.filename)
            if relative_path is None:
                continue

            target_path = path_config.typst_lib_target_dir / relative_path
            ensure_within(target_path, path_config.typst_lib_target_dir, member.filename)
            target_path.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, target_path.open("wb") as target:
                shutil.copyfileobj(source, target)


def sync_cards_json(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    LOGGER.info("Copied %s -> %s", source, destination)


def validate_required_files(path_config: PathConfig) -> None:
    missing = [
        path
        for path in (
            *path_config.required_asset_files,
            *path_config.required_typst_files,
            path_config.ot_cards_target_path,
            path_config.rd_cards_target_path,
        )
        if not path.exists()
    ]
    if missing:
        formatted = "\n".join(str(path.relative_to(path_config.project_root)) for path in missing)
        raise FileNotFoundError(f"Downloaded resources are incomplete:\n{formatted}")


def ensure_downloaded_resources(path_config: PathConfig) -> None:
    path_config.cache_dir.mkdir(parents=True, exist_ok=True)
    download(ASSETS_ARCHIVE_URL, path_config.assets_archive)
    download(OT_CARDS_JSON_URL, path_config.ot_cards_json)
    download(RD_CARDS_JSON_URL, path_config.rd_cards_json)
    download(TYPST_YGO_ARCHIVE_URL, path_config.typst_ygo_archive)

    extract_assets_archive(path_config)
    sync_cards_json(path_config.ot_cards_json, path_config.ot_cards_target_path)
    sync_cards_json(path_config.rd_cards_json, path_config.rd_cards_target_path)
    extract_typst_lib(path_config)
    validate_required_files(path_config)


def load_card_records(cards_json_path: Path, series: str) -> list[CardRecord]:
    LOGGER.info("Loading %s cards from %s", series, cards_json_path)
    payload = json.loads(cards_json_path.read_text(encoding="utf-8"))
    if not isinstance(payload, list):
        raise ValueError(f"{cards_json_path.name} must be a JSON array")

    records: list[CardRecord] = []
    for index, item in enumerate(payload):
        if not isinstance(item, dict):
            raise ValueError(f"{cards_json_path.name}[{index}] must be a JSON object")
        if "id" not in item or "name" not in item:
            raise ValueError(f"{cards_json_path.name}[{index}] must contain id and name")
        card_id = int(item["id"])
        records.append(CardRecord(series=series, card_key=f"{series}:{card_id}", payload=item))
    return records


def rebuild_cards_table(cursor: psycopg.Cursor[Any]) -> None:
    cursor.execute("DROP TABLE IF EXISTS ygo_cards")
    cursor.execute(
        """
        CREATE TABLE ygo_cards (
            card_key TEXT PRIMARY KEY,
            series TEXT NOT NULL CHECK (series IN ('ot', 'rd')),
            id BIGINT NOT NULL,
            name TEXT NOT NULL,
            image BIGINT NOT NULL,
            payload JSONB NOT NULL
        )
        """
    )
    cursor.execute("CREATE UNIQUE INDEX ygo_cards_series_id_uidx ON ygo_cards (series, id)")
    cursor.execute("CREATE INDEX ygo_cards_name_idx ON ygo_cards (name)")


def insert_cards(cursor: psycopg.Cursor[Any], records: list[CardRecord]) -> None:
    insert_stmt = """
        INSERT INTO ygo_cards (
            card_key,
            series,
            id,
            name,
            image,
            payload
        )
        VALUES (%s, %s, %s, %s, %s, %s)
    """
    rows = [
        (
            record.card_key,
            record.series,
            int(record.payload["id"]),
            str(record.payload["name"]),
            int(record.payload.get("image", record.payload["id"])),
            Jsonb(record.payload),
        )
        for record in records
    ]

    if rows:
        cursor.executemany(insert_stmt, rows)


def upsert_cards_to_postgres(records: list[CardRecord], db_config: DbConfig) -> None:
    LOGGER.info(
        "Importing %s card records into PostgreSQL database %s",
        len(records),
        db_config.dbname,
    )
    conninfo = (
        f"host={db_config.host} "
        f"port={db_config.port} "
        f"user={db_config.user} "
        f"password={db_config.password} "
        f"dbname={db_config.dbname} "
        f"sslmode={db_config.sslmode}"
    )

    with psycopg.connect(conninfo) as connection:
        with connection.cursor() as cursor:
            if not records:
                raise ValueError("No card records found")
            rebuild_cards_table(cursor)
            insert_cards(cursor, records)
        connection.commit()


def build_connection_string(db_config: DbConfig) -> str:
    parts = [
        f"Host={db_config.host}",
        f"Port={db_config.port}",
        f"Username={db_config.user}",
        f"Database={db_config.dbname}",
        f"SslMode={db_config.sslmode}",
    ]
    if db_config.password:
        parts.append(f"Password={db_config.password}")
    return ";".join(parts)


def write_appsettings(app_output: AppConfigOutput, db_config: DbConfig) -> None:
    if app_output.output_path is None:
        return
    app_output.output_path.parent.mkdir(parents=True, exist_ok=True)
    content = {
        "database": {
            "connection_string": build_connection_string(db_config),
        }
    }
    app_output.output_path.write_text(
        json.dumps(content, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    LOGGER.info("Generated appsettings.json at %s", app_output.output_path)


def run() -> None:
    path_config = build_path_config()
    cli_args = parse_cli_args()
    db_config = build_db_config(cli_args)
    app_output = build_app_output_config(cli_args)

    ensure_downloaded_resources(path_config)
    records = [
        *load_card_records(path_config.ot_cards_target_path, "ot"),
        *load_card_records(path_config.rd_cards_target_path, "rd"),
    ]
    upsert_cards_to_postgres(records, db_config)
    write_appsettings(app_output, db_config)
    LOGGER.info("All assets are ready.")


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )
    try:
        run()
    except Exception as exc:
        LOGGER.exception("Asset preparation failed: %s", exc)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
