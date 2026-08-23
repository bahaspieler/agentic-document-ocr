#!/usr/bin/env python3
"""Verify the simple natural-language queue worker against real Azure Storage."""

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
COMMANDS = [
    "Remove the background from RCPT001.",
    "Extract receipt data using workflow receipt_extract_v3 from RCPT001.",
]


def az(*args: str) -> object:
    env = os.environ.copy()
    connection = env.get("COURSE_STORAGE_CONNECTION_STRING") or env.get(
        "AZURE_STORAGE_CONNECTION_STRING"
    )
    if not connection:
        raise SystemExit("Set COURSE_STORAGE_CONNECTION_STRING or AZURE_STORAGE_CONNECTION_STRING.")
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
) -> None:
    az("storage", "container", "create", "--name", training)
    az("storage", "container", "create", "--name", results)
    az("storage", "queue", "create", "--name", queue)
    az("storage", "table", "create", "--name", table)
    az(
        "storage", "blob", "upload",
        "--container-name", training,
        "--name", f"images/RCPT001{image.suffix.lower()}",
        "--file", str(image),
        "--overwrite", "true",
    )


def query_results(table: str, group: str, started: dt.datetime) -> dict[str, dict]:
    response = az(
        "storage", "entity", "query",
        "--table-name", table,
        "--filter", f"PartitionKey eq '{group}'",
    )
    rows: dict[str, dict] = {}
    for row in response.get("items", []):
        command = row.get("Name")
        if command not in COMMANDS or not row.get("EndTimeUtc"):
            continue
        ended = dt.datetime.fromisoformat(row["EndTimeUtc"].replace("Z", "+00:00"))
        if ended >= started:
            rows[command] = row
    return rows


def verify_outputs(rows: dict[str, dict], result_container: str) -> None:
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
    parser = argparse.ArgumentParser()
    parser.add_argument("--training", default=os.getenv("COURSE_TRAINING_CONTAINER", "training-files"))
    parser.add_argument("--results", default=os.getenv("COURSE_RESULT_CONTAINER", "result-files"))
    parser.add_argument("--queue", default=os.getenv("COURSE_QUEUE_NAME", "trigger-queue"))
    parser.add_argument("--table", default=os.getenv("COURSE_RESULT_TABLE", "results"))
    parser.add_argument("--group", default=os.getenv("COURSE_GROUP_ID", "HiddenLayer"))
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--image", type=pathlib.Path, default=DEFAULT_IMAGE)
    args = parser.parse_args()

    if not args.image.is_file():
        raise SystemExit(f"Queue verification image not found: {args.image}")

    prepare(args.training, args.results, args.queue, args.table, args.image)
    started = dt.datetime.now(dt.timezone.utc) - dt.timedelta(seconds=5)
    for command in COMMANDS:
        az("storage", "message", "put", "--queue-name", args.queue, "--content", command)
        print(f"[OK] Enqueued: {command}", flush=True)

    deadline = time.time() + args.timeout
    rows: dict[str, dict] = {}
    while time.time() < deadline:
        rows = query_results(args.table, args.group, started)
        if len(rows) == len(COMMANDS):
            break
        print(f"[WAIT] Completed {len(rows)}/{len(COMMANDS)} commands", flush=True)
        time.sleep(10)
    if len(rows) != len(COMMANDS):
        raise TimeoutError("The worker did not complete both queue commands.")

    verify_outputs(rows, args.results)
    print("[OK] Natural-language Queue → AgentService → Blob/Table path passed.")


if __name__ == "__main__":
    main()
