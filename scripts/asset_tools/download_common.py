import shutil
import socket
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import urlopen


DOWNLOAD_TIMEOUT_SECONDS = 30
DOWNLOAD_MAX_RETRIES = 3
DOWNLOAD_RETRY_DELAY_SECONDS = 2


def download_file(url: str, destination: Path) -> None:
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
            if destination.exists():
                destination.unlink(missing_ok=True)
            if attempt < DOWNLOAD_MAX_RETRIES:
                time.sleep(DOWNLOAD_RETRY_DELAY_SECONDS)

    raise RuntimeError(
        f"Failed to download {url} after {DOWNLOAD_MAX_RETRIES} attempts "
        f"(timeout={DOWNLOAD_TIMEOUT_SECONDS}s): {last_error}"
    ) from last_error
