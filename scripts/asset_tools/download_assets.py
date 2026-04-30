import argparse
import hashlib
import json
import logging
import socket
import shutil
import sys
import tarfile
import time
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import urlopen
import psycopg
from psycopg import sql
from psycopg.types.json import Jsonb

LOGGER = logging.getLogger("download_assets")

TEMPLATE_ARCHIVE_URL = (
    "https://github.com/arshtyi/Card-Templates-Of-YuGiOh/releases/download/1-11/"
    "yugioh-card-template.tar.xz"
)
TEMPLATE_SHA256_URL = f"{TEMPLATE_ARCHIVE_URL}.sha256"
CARDS_JSON_URL = (
    "https://github.com/arshtyi/YuGiOh-Cards-Asset/releases/download/latest/cards.json"
)
CARDS_SHA256_URL = f"{CARDS_JSON_URL}.sha256"
TYPST_YGO_ARCHIVE_URL = "https://github.com/arshtyi/typst-ygo/archive/refs/heads/main.zip"
DOWNLOAD_TIMEOUT_SECONDS = 30
DOWNLOAD_MAX_RETRIES = 3
DOWNLOAD_RETRY_DELAY_SECONDS = 2


@dataclass(frozen=True)
class PathConfig:
    project_root: Path
    cache_dir: Path
    template_archive: Path
    template_sha256: Path
    cards_json: Path
    cards_sha256: Path
    template_target_dir: Path
    cards_target_path: Path
    typst_ygo_archive: Path
    typst_ygo_target_dir: Path
    template_extract_marker: Path
    required_template_files: tuple[Path, ...]


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
    card_key: str
    payload: dict[str, Any]


def parse_cli_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Download Yu-Gi-Oh assets, verify hash, extract, and import cards into PostgreSQL."
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
    template_target_dir = project_root / "assets" / "card_templates"
    return PathConfig(
        project_root=project_root,
        cache_dir=cache_dir,
        template_archive=cache_dir / "yugioh-card-template.tar.xz",
        template_sha256=cache_dir / "yugioh-card-template.tar.xz.sha256",
        cards_json=cache_dir / "cards.json",
        cards_sha256=cache_dir / "cards.json.sha256",
        typst_ygo_archive=cache_dir / "typst-ygo-main.zip",
        template_target_dir=template_target_dir,
        cards_target_path=project_root / "assets" / "cards" / "cards.json",
        typst_ygo_target_dir=project_root / "assets" / "typst-ygo",
        template_extract_marker=template_target_dir / ".template_archive.sha256",
        required_template_files=(
            template_target_dir / "figure" / "cards" / "card-effect.png",
            template_target_dir / "figure" / "attributes" / "attribute-light.png",
            template_target_dir / "font" / "sc" / "Yu-Gi-Oh! DFKaiW5-A（简体中文）.ttf",
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


def download_file(url: str, destination: Path) -> None:
    LOGGER.info("Downloading %s -> %s", url, destination)
    destination.parent.mkdir(parents=True, exist_ok=True)

    last_error: Exception | None = None
    for attempt in range(1, DOWNLOAD_MAX_RETRIES + 1):
        try:
            with (
                urlopen(url, timeout=DOWNLOAD_TIMEOUT_SECONDS) as response,
                destination.open("wb") as file_handle,
            ):
                shutil.copyfileobj(response, file_handle)
            return
        except (URLError, TimeoutError, socket.timeout, HTTPError, OSError) as exc:
            last_error = exc
            LOGGER.warning(
                "Download failed (attempt %s/%s) for %s: %s",
                attempt,
                DOWNLOAD_MAX_RETRIES,
                url,
                exc,
            )
            if destination.exists():
                destination.unlink(missing_ok=True)
            if attempt < DOWNLOAD_MAX_RETRIES:
                time.sleep(DOWNLOAD_RETRY_DELAY_SECONDS)

    raise RuntimeError(
        f"Failed to download {url} after {DOWNLOAD_MAX_RETRIES} attempts "
        f"(timeout={DOWNLOAD_TIMEOUT_SECONDS}s): {last_error}"
    ) from last_error


def sha256_of_file(file_path: Path) -> str:
    hasher = hashlib.sha256()
    with file_path.open("rb") as file_handle:
        while True:
            chunk = file_handle.read(1024 * 1024)
            if not chunk:
                break
            hasher.update(chunk)
    return hasher.hexdigest()


def expected_sha256_from_file(file_path: Path) -> str:
    raw_text = file_path.read_text(encoding="utf-8").strip()
    if not raw_text:
        raise ValueError(f"SHA256 file is empty: {file_path}")
    token = raw_text.split()[0].lower()
    if len(token) != 64 or any(ch not in "0123456789abcdef" for ch in token):
        raise ValueError(f"SHA256 value is invalid in {file_path}: {token}")
    return token


def verify_sha256(content_file: Path, sha256_file: Path) -> None:
    expected = expected_sha256_from_file(sha256_file)
    actual = sha256_of_file(content_file)
    if actual != expected:
        raise ValueError(
            f"SHA256 mismatch for {content_file.name}: expected={expected}, actual={actual}"
        )
    LOGGER.info("SHA256 verified for %s", content_file.name)


def should_extract_templates(path_config: PathConfig) -> bool:
    if not path_config.template_target_dir.exists():
        return True
    if any(
        not required_file.exists()
        for required_file in path_config.required_template_files
    ):
        return True

    if not path_config.template_extract_marker.exists():
        return True

    expected = expected_sha256_from_file(path_config.template_sha256)
    current = (
        path_config.template_extract_marker.read_text(encoding="utf-8").strip().lower()
    )
    return expected != current


def safe_extract_tar_xz(archive_path: Path, destination_dir: Path) -> None:
    LOGGER.info("Extracting %s -> %s", archive_path, destination_dir)
    if destination_dir.exists():
        shutil.rmtree(destination_dir)
    destination_dir.mkdir(parents=True, exist_ok=True)
    destination_root = destination_dir.resolve()
    with tarfile.open(archive_path, mode="r:xz") as archive:
        for member in archive.getmembers():
            member_path = destination_root / member.name
            member_path_resolved = member_path.resolve()
            if (
                destination_root not in member_path_resolved.parents
                and member_path_resolved != destination_root
            ):
                raise ValueError(
                    f"Blocked path traversal while extracting tar: {member.name}"
                )
        archive.extractall(destination_root)


def safe_extract_zip_strip_root(archive_path: Path, destination_dir: Path) -> None:
    LOGGER.info("Extracting %s -> %s", archive_path, destination_dir)
    if destination_dir.exists():
        shutil.rmtree(destination_dir)
    destination_dir.mkdir(parents=True, exist_ok=True)
    destination_root = destination_dir.resolve()

    with zipfile.ZipFile(archive_path) as archive:
        members = archive.infolist()
        root_prefix = ""
        first_name = next((member.filename for member in members if member.filename), "")
        if "/" in first_name:
            root_prefix = first_name.split("/", 1)[0] + "/"

        for member in members:
            member_name = member.filename
            if not member_name or member_name.endswith("/"):
                continue
            relative_name = (
                member_name.removeprefix(root_prefix)
                if root_prefix and member_name.startswith(root_prefix)
                else member_name
            )
            if not relative_name:
                continue
            target_path = (destination_root / relative_name).resolve()
            if destination_root not in target_path.parents and target_path != destination_root:
                raise ValueError(
                    f"Blocked path traversal while extracting zip: {member_name}"
                )
            target_path.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, target_path.open("wb") as target:
                shutil.copyfileobj(source, target)


def ensure_typst_ygo_package(path_config: PathConfig) -> None:
    package_entrypoint = path_config.typst_ygo_target_dir / "lib" / "mod.typ"
    package_manifest = path_config.typst_ygo_target_dir / "typst.toml"
    if package_entrypoint.exists() and package_manifest.exists():
        LOGGER.info("typst-ygo package is already available: %s", path_config.typst_ygo_target_dir)
        return

    download_file(TYPST_YGO_ARCHIVE_URL, path_config.typst_ygo_archive)
    safe_extract_zip_strip_root(path_config.typst_ygo_archive, path_config.typst_ygo_target_dir)


def write_template_extract_marker(path_config: PathConfig) -> None:
    digest = expected_sha256_from_file(path_config.template_sha256)
    path_config.template_extract_marker.write_text(f"{digest}\n", encoding="utf-8")
    LOGGER.info(
        "Updated template extraction marker: %s", path_config.template_extract_marker
    )


def ensure_downloaded_and_verified(
    content_url: str,
    sha256_url: str,
    content_path: Path,
    sha256_path: Path,
) -> None:
    # Always refresh .sha256 to reflect upstream latest resources.
    download_file(sha256_url, sha256_path)

    if content_path.exists():
        try:
            verify_sha256(content_path, sha256_path)
            LOGGER.info("Using cached verified file: %s", content_path)
            return
        except ValueError:
            LOGGER.warning(
                "Cached file is outdated or corrupted, re-downloading: %s", content_path
            )

    download_file(content_url, content_path)
    verify_sha256(content_path, sha256_path)


def load_card_records(cards_json_path: Path) -> list[CardRecord]:
    LOGGER.info("Loading and normalizing card data from %s", cards_json_path)
    payload = json.loads(cards_json_path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError(
            'cards.json must be a JSON object in form {"<card_key>": { ...card fields... }}'
        )
    if not payload:
        return []
    if not all(isinstance(value, dict) for value in payload.values()):
        raise ValueError(
            "cards.json must use outer key as card primary key, and each value must be a JSON object"
        )
    return [
        CardRecord(card_key=str(card_key), payload=value)
        for card_key, value in payload.items()
    ]


def sanitize_column_name(name: str) -> str:
    cleaned = []
    for ch in name:
        if ch.isalnum() or ch == "_":
            cleaned.append(ch.lower())
        else:
            cleaned.append("_")
    result = "".join(cleaned).strip("_")
    if not result:
        result = "field"
    if result[0].isdigit():
        result = f"f_{result}"
    return result


def choose_sql_type(values: list[Any]) -> str:
    non_null_values = [value for value in values if value is not None]
    if not non_null_values:
        return "TEXT"
    if any(isinstance(value, (dict, list)) for value in non_null_values):
        return "JSONB"
    if all(isinstance(value, bool) for value in non_null_values):
        return "BOOLEAN"
    if all(
        isinstance(value, int) and not isinstance(value, bool)
        for value in non_null_values
    ):
        return "BIGINT"
    if all(
        isinstance(value, (int, float)) and not isinstance(value, bool)
        for value in non_null_values
    ):
        return "DOUBLE PRECISION"
    return "TEXT"


def build_column_mapping(records: list[CardRecord]) -> dict[str, str]:
    mapping: dict[str, str] = {}
    used_names: set[str] = {"card_key"}
    for record in records:
        for raw_key in record.payload.keys():
            if raw_key in mapping:
                continue
            base_name = sanitize_column_name(raw_key)
            candidate = base_name
            suffix = 2
            while candidate in used_names:
                candidate = f"{base_name}_{suffix}"
                suffix += 1
            mapping[raw_key] = candidate
            used_names.add(candidate)
    return mapping


def build_column_types(
    records: list[CardRecord], column_mapping: dict[str, str]
) -> dict[str, str]:
    values_by_raw_key: dict[str, list[Any]] = {
        raw_key: [] for raw_key in column_mapping
    }
    for record in records:
        for raw_key in column_mapping:
            values_by_raw_key[raw_key].append(record.payload.get(raw_key))

    return {
        raw_key: choose_sql_type(values)
        for raw_key, values in values_by_raw_key.items()
    }


def adapt_value(value: Any, sql_type: str) -> Any:
    if value is None:
        return None
    if sql_type == "JSONB":
        return Jsonb(value)
    if sql_type == "TEXT":
        return str(value)
    return value


def rebuild_cards_table(
    cursor: psycopg.Cursor[Any],
    column_mapping: dict[str, str],
    column_types: dict[str, str],
) -> None:
    cursor.execute("DROP TABLE IF EXISTS ygo_cards")
    cursor.execute(
        """
        CREATE TABLE ygo_cards (
            card_key TEXT PRIMARY KEY,
            id BIGINT NOT NULL,
            name TEXT NOT NULL,
            card_image BIGINT NOT NULL,
            card_type TEXT NOT NULL,
            frame_type TEXT NOT NULL,
            payload JSONB NOT NULL
        )
        """
    )


def insert_cards(
    cursor: psycopg.Cursor[Any],
    records: list[CardRecord],
    column_mapping: dict[str, str],
    column_types: dict[str, str],
) -> None:
    insert_stmt = """
        INSERT INTO ygo_cards (
            card_key,
            id,
            name,
            card_image,
            card_type,
            frame_type,
            payload
        )
        VALUES (%s, %s, %s, %s, %s, %s, %s)
    """
    rows = [
        (
            record.card_key,
            int(record.payload["id"]),
            str(record.payload["name"]),
            int(record.payload.get("cardImage", record.payload["id"])),
            str(record.payload.get("cardType", "")),
            str(record.payload.get("frameType", "")),
            Jsonb(record.payload),
        )
        for record in records
    ]

    if rows:
        cursor.executemany(insert_stmt, rows)


def upsert_cards_to_postgres(records: list[CardRecord], db_config: DbConfig) -> None:
    LOGGER.info(
        "Importing %s normalized card records into PostgreSQL database %s",
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
                raise ValueError("No card records found in cards.json")
            rebuild_cards_table(cursor, {}, {})
            insert_cards(cursor, records, {}, {})
        connection.commit()


def sync_cards_json_to_target(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    LOGGER.info("Copied cards.json to %s", destination)


def ensure_cards_json_target(path_config: PathConfig) -> None:
    if not path_config.cards_target_path.exists():
        sync_cards_json_to_target(path_config.cards_json, path_config.cards_target_path)
        return
    try:
        verify_sha256(path_config.cards_target_path, path_config.cards_sha256)
        LOGGER.info(
            "cards target is already up to date: %s", path_config.cards_target_path
        )
    except ValueError:
        LOGGER.info(
            "cards target is outdated/corrupted, replacing: %s",
            path_config.cards_target_path,
        )
        sync_cards_json_to_target(path_config.cards_json, path_config.cards_target_path)


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

    ensure_downloaded_and_verified(
        TEMPLATE_ARCHIVE_URL,
        TEMPLATE_SHA256_URL,
        path_config.template_archive,
        path_config.template_sha256,
    )
    if should_extract_templates(path_config):
        safe_extract_tar_xz(
            path_config.template_archive, path_config.template_target_dir
        )
        write_template_extract_marker(path_config)
    else:
        LOGGER.info("Template resources are already complete, skipping extraction.")

    ensure_downloaded_and_verified(
        CARDS_JSON_URL,
        CARDS_SHA256_URL,
        path_config.cards_json,
        path_config.cards_sha256,
    )
    ensure_cards_json_target(path_config)
    ensure_typst_ygo_package(path_config)

    records = load_card_records(path_config.cards_json)
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
