#!/usr/bin/env python3
"""Verify the natural-language queue worker against real Azure Storage.

This integration verifier creates the required storage entities, uploads one
sample image, sends two commands, waits for their Table rows, and confirms every
reported output Blob. Use azure-queue-produce.sh for manual producer-only use.

Usage:
    python3 scripts/verify-queue-worker.py
    python3 scripts/verify-queue-worker.py --image /path/to/receipt.png
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import pathlib
import subprocess
import time
import urllib.parse


ROOT = pathlib.Path(__file__).resolve().parents[1]
DEFAULT_IMAGE = ROOT / "SampleFiles" / "images" / "RCPT001.png"


def load_env_file() -> None:
    """Load missing values from the project .env without overriding the shell."""
    env_file = ROOT / ".env"
    if not env_file.is_file():
        return
    for raw_line in env_file.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        os.environ.setdefault(key.strip(), value)


def commands_for(reference: str) -> list[str]:
    """Return the two deterministic verification commands for an image reference."""
    return [
        f"Remove the background from {reference}.",
        f"Extract receipt data using workflow receipt_extract_v3 from {reference}.",
    ]


def az(*args: str) -> object:
    env = os.environ.copy()
    connection = env.get("WORKER_STORAGE_CONNECTION_STRING") or env.get(
        "AZURE_STORAGE_CONNECTION_STRING"
    )
    if not connection:
        raise SystemExit("Set WORKER_STORAGE_CONNECTION_STRING or AZURE_STORAGE_CONNECTION_STRING.")
    env["AZURE_STORAGE_CONNECTION_STRING"] = connection
    completed = subprocess.run(
        ["az", *args, "--only-show-errors", "-o", "json"],
        check=True,
        capture_output=True,
        text=True,
        env=env,
    )
    return json.loads(completed.stdout) if completed.stdout.strip() else None


def prepare(
    training: str,
    results: str,
    queue: str,
    table: str,
    image: pathlib.Path,
    blob_name: str,
) -> None:
    """Ensure storage entities exist and upload the source before enqueueing."""
    az("storage", "container", "create", "--name", training)
    az("storage", "container", "create", "--name", results)
    az("storage", "queue", "create", "--name", queue)
    az("storage", "table", "create", "--name", table)
    az(
        "storage", "blob", "upload",
        "--container-name", training,
        "--name", blob_name,
        "--file", str(image),
        "--overwrite", "true",
    )


def query_results(
    table: str,
    group: str,
    started: dt.datetime,
    commands: list[str],
) -> dict[str, dict]:
    """Return rows for this verification run, keyed by their command text."""
    response = az(
        "storage", "entity", "query",
        "--table-name", table,
        "--filter", f"PartitionKey eq '{group}'",
    )
    rows: dict[str, dict] = {}
    for row in response.get("items", []):
        command = row.get("Name")
        if command not in commands or not row.get("EndTimeUtc"):
            continue
        ended = dt.datetime.fromisoformat(row["EndTimeUtc"].replace("Z", "+00:00"))
        if ended >= started:
            rows[command] = row
    return rows


def verify_outputs(rows: dict[str, dict], result_container: str) -> None:
    """Require successful rows whose output URLs resolve to existing Blobs."""
    for command, row in rows.items():
        if not row.get("Success"):
            raise AssertionError(f"{command} failed: {row.get('Error')}")
        outputs = json.loads(row.get("OutputFilesJson") or "[]")
        if not outputs:
            raise AssertionError(f"{command} produced no output files.")
        for output in outputs:
            parsed = urllib.parse.urlparse(output)
            container, blob = parsed.path.lstrip("/").split("/", 1)
            if container != result_container:
                raise AssertionError(f"Unexpected result URL: {output}")
            exists = az(
                "storage", "blob", "exists",
                "--container-name", container,
                "--name", urllib.parse.unquote(blob),
            )
            if not exists.get("exists"):
                raise AssertionError(f"Missing result blob: {output}")
        print(f"[OK] {row['RowKey']}: {len(outputs)} artifact(s) — {command}", flush=True)


def main() -> None:
    load_env_file()
    parser = argparse.ArgumentParser(
        description="Exercise the deployed Queue → worker → Blob/Table path."
    )
    parser.add_argument(
        "--training",
        default=os.getenv(
            "WorkerOptions__TrainingContainer",
            os.getenv("WORKER_TRAINING_CONTAINER", "training-files"),
        ),
    )
    parser.add_argument(
        "--results",
        default=os.getenv(
            "WorkerOptions__ResultContainer",
            os.getenv("WORKER_RESULT_CONTAINER", "result-files"),
        ),
    )
    parser.add_argument(
        "--queue",
        default=os.getenv("WorkerOptions__Queue", os.getenv("WORKER_QUEUE_NAME", "trigger-queue")),
    )
    parser.add_argument(
        "--table",
        default=os.getenv("WorkerOptions__ResultTable", os.getenv("WORKER_RESULT_TABLE", "results")),
    )
    parser.add_argument(
        "--group",
        default=os.getenv("WorkerOptions__GroupId", os.getenv("WORKER_GROUP_ID", "AgenticDocumentOcr")),
    )
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--image", type=pathlib.Path, default=DEFAULT_IMAGE)
    args = parser.parse_args()

    if not args.image.is_file():
        raise SystemExit(f"Queue verification image not found: {args.image}")

    reference = args.image.stem
    commands = commands_for(reference)
    blob_name = f"images/{args.image.name}"
    prepare(args.training, args.results, args.queue, args.table, args.image, blob_name)
    started = dt.datetime.now(dt.timezone.utc) - dt.timedelta(seconds=5)
    for command in commands:
        az("storage", "message", "put", "--queue-name", args.queue, "--content", command)
        print(f"[OK] Enqueued: {command}", flush=True)

    deadline = time.time() + args.timeout
    rows: dict[str, dict] = {}
    while time.time() < deadline:
        rows = query_results(args.table, args.group, started, commands)
        if len(rows) == len(commands):
            break
        print(f"[WAIT] Completed {len(rows)}/{len(commands)} commands", flush=True)
        time.sleep(10)
    if len(rows) != len(commands):
        raise TimeoutError("The worker did not complete both queue commands.")

    verify_outputs(rows, args.results)
    print("[OK] Natural-language Queue → AgentService → Blob/Table path passed.")


if __name__ == "__main__":
    main()
