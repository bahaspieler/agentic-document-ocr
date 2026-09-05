#!/usr/bin/env python3
"""Poll Azure Table Storage for one bulk queue batch.

The worker has no parent subset/retry message contract. With
--requeue-failed, this verifier instead derives and previews one ordinary
natural-language command per failed source image. Add --yes to enqueue them.

Examples:
    python3 scripts/verify-bulk-queue.py
    python3 scripts/verify-bulk-queue.py --batch-id batch-abc --timeout 1800
    python3 scripts/verify-bulk-queue.py --batch-id batch-abc --requeue-failed
    python3 scripts/verify-bulk-queue.py --batch-id batch-abc --requeue-failed --yes
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import shutil
import subprocess
import time
from typing import Any


ROOT = pathlib.Path(__file__).resolve().parents[1]


def load_env_file() -> None:
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


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Report dispatched/total/succeeded/failed/pending counts and artifact "
            "URLs for a bulk queue batch."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--batch-id", help="batch RowKey; defaults to the most recent batch")
    parser.add_argument(
        "--table",
        default=os.getenv("WorkerOptions__ResultTable", os.getenv("WORKER_RESULT_TABLE", "results")),
    )
    parser.add_argument(
        "--group",
        default=os.getenv("WorkerOptions__GroupId", os.getenv("WORKER_GROUP_ID", "AgenticDocumentOcr")),
    )
    parser.add_argument(
        "--training-container",
        default=os.getenv(
            "WorkerOptions__TrainingContainer",
            os.getenv("WORKER_TRAINING_CONTAINER", "training-files"),
        ),
    )
    parser.add_argument(
        "--queue",
        default=os.getenv("WorkerOptions__Queue", os.getenv("WORKER_QUEUE_NAME", "trigger-queue")),
    )
    parser.add_argument("--timeout", type=int, default=900, help="seconds to poll (0 queries once)")
    parser.add_argument("--interval", type=float, default=10, help="poll interval in seconds")
    parser.add_argument(
        "--requeue-failed",
        action="store_true",
        help="preview one single-image command per failed item",
    )
    parser.add_argument(
        "--yes",
        action="store_true",
        help="actually enqueue commands selected by --requeue-failed",
    )
    return parser.parse_args()


def az(*args: str) -> Any:
    connection = os.getenv("WORKER_STORAGE_CONNECTION_STRING") or os.getenv(
        "AZURE_STORAGE_CONNECTION_STRING"
    )
    if not connection:
        raise SystemExit(
            "Set WORKER_STORAGE_CONNECTION_STRING or AZURE_STORAGE_CONNECTION_STRING "
            "(normally in the gitignored .env file)."
        )
    env = os.environ.copy()
    env["AZURE_STORAGE_CONNECTION_STRING"] = connection
    completed = subprocess.run(
        ["az", *args, "--only-show-errors", "--output", "json"],
        check=True,
        capture_output=True,
        text=True,
        env=env,
    )
    return json.loads(completed.stdout) if completed.stdout.strip() else None


def query_rows(table: str, group: str) -> list[dict[str, Any]]:
    escaped_group = group.replace("'", "''")
    response = az(
        "storage",
        "entity",
        "query",
        "--table-name",
        table,
        "--filter",
        f"PartitionKey eq '{escaped_group}'",
    )
    return list((response or {}).get("items", []))


def newest_parent(rows: list[dict[str, Any]]) -> dict[str, Any] | None:
    parents = [row for row in rows if str(row.get("ResultKind", "")).lower() == "batch"]
    if not parents:
        return None

    def time_key(row: dict[str, Any]) -> str:
        return str(
            row.get("StartTimeUtc")
            or row.get("Timestamp")
            or row.get("timestamp")
            or ""
        )

    return max(parents, key=time_key)


def select_batch(
    rows: list[dict[str, Any]], batch_id: str | None
) -> tuple[dict[str, Any] | None, list[dict[str, Any]]]:
    if batch_id:
        parent = next(
            (
                row
                for row in rows
                if row.get("RowKey") == batch_id
                and str(row.get("ResultKind", "")).lower() == "batch"
            ),
            None,
        )
    else:
        parent = newest_parent(rows)
    if parent is None:
        return None, []
    selected_id = str(parent["RowKey"])
    items = [
        row
        for row in rows
        if str(row.get("ResultKind", "")).lower() == "batch-item"
        and row.get("BatchId") == selected_id
    ]
    return parent, items


def item_state(row: dict[str, Any]) -> str:
    status = str(row.get("Status", "")).lower()
    if status in {"succeeded", "failed"}:
        return status
    if row.get("EndTimeUtc"):
        return "succeeded" if row.get("Success") else "failed"
    return "pending"


def summary(parent: dict[str, Any], items: list[dict[str, Any]]) -> dict[str, int]:
    total = int(parent.get("ItemCount") or 0)
    succeeded = sum(item_state(row) == "succeeded" for row in items)
    failed = sum(item_state(row) == "failed" for row in items)
    parent_status = str(parent.get("Status", "")).lower()
    dispatched = total if parent_status == "dispatched" else min(len(items), total)
    return {
        "dispatched": dispatched,
        "total": total,
        "succeeded": succeeded,
        "failed": failed,
        "pending": max(0, total - succeeded - failed),
    }


def artifact_urls(row: dict[str, Any]) -> list[str]:
    urls: list[str] = []
    raw = row.get("OutputFilesJson")
    if raw:
        try:
            decoded = json.loads(raw)
            if isinstance(decoded, list):
                urls.extend(str(url) for url in decoded if url)
        except (TypeError, json.JSONDecodeError):
            pass
    primary = row.get("PrimaryOutputFileUrl")
    if primary and primary not in urls:
        urls.append(str(primary))
    return urls


def print_report(parent: dict[str, Any], items: list[dict[str, Any]]) -> dict[str, int]:
    counts = summary(parent, items)
    print(
        "[STATUS] "
        f"batch={parent['RowKey']} dataset={parent.get('DatasetName') or '<unknown>'} "
        f"dispatched={counts['dispatched']}/{counts['total']} "
        f"succeeded={counts['succeeded']} failed={counts['failed']} "
        f"pending={counts['pending']}",
        flush=True,
    )
    for row in sorted(items, key=lambda item: str(item.get("SourceBlobName", ""))):
        urls = artifact_urls(row)
        if not urls:
            continue
        print(f"[ARTIFACT] {row.get('SourceBlobName')}", flush=True)
        for url in urls:
            print(f"  {url}", flush=True)
    return counts


def single_image_command(parent_command: str, dataset: str, reference: str) -> str:
    command = re.sub(
        r"\b(?:all|every)\s+(?:images?|files?|pictures?|receipts?)\b",
        reference,
        parent_command,
        count=1,
        flags=re.IGNORECASE,
    )
    command = re.sub(
        r"\b(?:images?|files?|pictures?|receipts?)\s+(?:all|every)\b",
        reference,
        command,
        count=1,
        flags=re.IGNORECASE,
    )
    if dataset:
        command = re.sub(
            rf"\s+\b(?:in|from|of)\s+{re.escape(dataset)}\b",
            "",
            command,
            count=1,
            flags=re.IGNORECASE,
        )
    command = re.sub(r"\s+", " ", command).strip()
    if reference.lower() not in command.lower():
        command = f"{command.rstrip('.')} for {reference}."
    return command


def requeue_failed(
    parent: dict[str, Any],
    items: list[dict[str, Any]],
    training_container: str,
    queue: str,
    confirmed: bool,
) -> None:
    failed = [row for row in items if item_state(row) == "failed"]
    if not failed:
        print("[OK] No failed items to requeue.")
        return
    commands: list[tuple[str, str]] = []
    for row in sorted(failed, key=lambda item: str(item.get("SourceBlobName", ""))):
        blob_name = str(row.get("SourceBlobName") or "")
        if not blob_name:
            print(f"[SKIP] {row.get('RowKey')}: missing SourceBlobName")
            continue
        exists = az(
            "storage",
            "blob",
            "exists",
            "--container-name",
            training_container,
            "--name",
            blob_name,
        )
        if not (exists or {}).get("exists"):
            print(f"[SKIP] Missing source Blob: {blob_name}")
            continue
        reference = pathlib.PurePosixPath(blob_name).stem
        command = single_image_command(
            str(parent.get("Name") or "Process image"),
            str(parent.get("DatasetName") or ""),
            reference,
        )
        commands.append((blob_name, command))
        print(f"[REQUEUE] {blob_name}: {command}")

    if not confirmed:
        print(f"[DRY-RUN] {len(commands)} command(s) selected; add --yes to enqueue.")
        return
    for _, command in commands:
        az("storage", "message", "put", "--queue-name", queue, "--content", command)
    print(f"[OK] Enqueued {len(commands)} single-image command(s).")


def main() -> None:
    load_env_file()
    args = parse_args()
    if args.timeout < 0 or args.interval <= 0:
        raise SystemExit("--timeout must be non-negative and --interval must be positive.")
    if args.yes and not args.requeue_failed:
        raise SystemExit("--yes is only valid with --requeue-failed.")
    if shutil.which("az") is None:
        raise SystemExit("Azure CLI (az) is not installed.")
    subprocess.run(
        ["az", "account", "show", "--only-show-errors", "--output", "none"],
        check=True,
    )

    deadline = time.monotonic() + args.timeout
    parent: dict[str, Any] | None = None
    items: list[dict[str, Any]] = []
    while True:
        rows = query_rows(args.table, args.group)
        parent, items = select_batch(rows, args.batch_id)
        if parent is None:
            if args.timeout == 0 or time.monotonic() >= deadline:
                target = args.batch_id or "a recent batch"
                raise SystemExit(f"No result row found for {target}.")
            print("[WAIT] No matching batch row yet.", flush=True)
        else:
            counts = print_report(parent, items)
            if counts["pending"] == 0:
                break
            if args.timeout == 0 or time.monotonic() >= deadline:
                raise SystemExit("Timed out while batch items were still pending.")
        time.sleep(min(args.interval, max(0, deadline - time.monotonic())))

    if args.requeue_failed:
        requeue_failed(
            parent,
            items,
            args.training_container,
            args.queue,
            args.yes,
        )


if __name__ == "__main__":
    main()
