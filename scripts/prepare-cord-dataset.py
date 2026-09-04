#!/usr/bin/env python3
"""Download and deterministically package CORD v2 receipt images.

The official Hugging Face release is public and does not require Kaggle or a
Hugging Face token. Images are copied without re-encoding and receive
split-aware names so train/validation/test identifiers cannot collide.

Examples:
    python3 scripts/prepare-cord-dataset.py --mode smoke
    python3 scripts/prepare-cord-dataset.py --mode smoke --smoke-count 20
    python3 scripts/prepare-cord-dataset.py --mode full

Install the one optional download dependency with:
    python3 -m pip install "datasets>=2.14"
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import shutil
import sys
from collections.abc import Iterator
from typing import Any


ROOT = pathlib.Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "SampleFiles" / "datasets" / "cord"
SOURCE_DATASET = "naver-clova-ix/cord-v2"
# Pin the public release rather than silently accepting later corpus changes.
SOURCE_REVISION = "447d3d5adc8ac4b2f0782088847f18c7bd2c5a76"
SPLIT_COUNTS = {"train": 800, "validation": 100, "test": 100}
SPLITS = tuple(SPLIT_COUNTS)


class SourceChangedError(RuntimeError):
    """Raised when the upstream dataset no longer matches its documented shape."""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Package the public 1,000-image CORD v2 corpus, or a deterministic "
            "smoke subset, under SampleFiles/datasets/cord."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--mode", choices=("smoke", "full"), default="smoke")
    parser.add_argument(
        "--smoke-count",
        type=int,
        default=10,
        help="total images in smoke mode (allocated 80/10/10 across splits)",
    )
    parser.add_argument(
        "--output-dir",
        type=pathlib.Path,
        default=DEFAULT_OUTPUT,
        help="CORD package root; mode output is written below this directory",
    )
    parser.add_argument("--source", default=SOURCE_DATASET, help="Hugging Face dataset ID")
    parser.add_argument(
        "--revision",
        default=SOURCE_REVISION,
        help="immutable Hugging Face dataset revision",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="replace an existing package for the selected mode",
    )
    return parser.parse_args()


def smoke_quotas(total: int) -> dict[str, int]:
    if not 1 <= total <= sum(SPLIT_COUNTS.values()):
        raise ValueError("--smoke-count must be between 1 and 1000.")
    exact = {split_name: total * count / 1000 for split_name, count in SPLIT_COUNTS.items()}
    quotas = {split_name: int(value) for split_name, value in exact.items()}
    remainder = total - sum(quotas.values())
    order = sorted(SPLITS, key=lambda name: (exact[name] - quotas[name], -SPLITS.index(name)), reverse=True)
    for split_name in order[:remainder]:
        quotas[split_name] += 1
    return quotas


def import_huggingface() -> tuple[Any, Any]:
    try:
        from datasets import Image, load_dataset
    except ImportError as error:
        raise SystemExit(
            "The CORD downloader requires the Hugging Face datasets package.\n"
            'Install it with: python3 -m pip install "datasets>=2.14"\n'
            "No Kaggle account or dataset token is required."
        ) from error
    return Image, load_dataset


def rows_for_split(
    load_dataset: Any,
    image_feature: Any,
    source: str,
    revision: str,
    split_name: str,
) -> Iterator[dict[str, Any]]:
    try:
        rows = load_dataset(
            source,
            split=split_name,
            revision=revision,
            streaming=True,
        )
        rows = rows.cast_column("image", image_feature(decode=False))
        yield from rows
    except Exception as error:
        raise SourceChangedError(
            f"Could not stream split '{split_name}' from {source}@{revision}: {error}\n"
            "Check https://huggingface.co/datasets/naver-clova-ix/cord-v2 and "
            "update SOURCE_REVISION/SPLIT_COUNTS only after reviewing upstream changes. "
            "If a proxy blocks range requests, download the dataset with the Hugging "
            "Face CLI first and rerun with its normal cache enabled."
        ) from error


def image_bytes(value: Any) -> bytes:
    if not isinstance(value, dict):
        raise SourceChangedError("The upstream 'image' column is no longer an encoded image mapping.")
    data = value.get("bytes")
    if isinstance(data, bytes):
        return data
    path = value.get("path")
    if path and pathlib.Path(path).is_file():
        return pathlib.Path(path).read_bytes()
    raise SourceChangedError("An upstream image row contains neither bytes nor a readable path.")


def image_extension(data: bytes) -> str:
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return ".png"
    if data.startswith(b"\xff\xd8\xff"):
        return ".jpg"
    if data.startswith((b"II*\x00", b"MM\x00*")):
        return ".tiff"
    if data.startswith(b"BM"):
        return ".bmp"
    if data.startswith(b"RIFF") and data[8:12] == b"WEBP":
        return ".webp"
    raise SourceChangedError("CORD returned an image in an unsupported or unrecognized format.")


def canonical_json(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")


def package(args: argparse.Namespace) -> pathlib.Path:
    image_feature, load_dataset = import_huggingface()
    expected = SPLIT_COUNTS if args.mode == "full" else smoke_quotas(args.smoke_count)
    package_dir = args.output_dir.resolve() / args.mode
    staging_dir = args.output_dir.resolve() / f".{args.mode}.tmp"
    if package_dir.exists() and not args.force:
        raise SystemExit(f"Package already exists: {package_dir}\nRerun with --force to replace it.")
    shutil.rmtree(staging_dir, ignore_errors=True)
    images_dir = staging_dir / "images"
    images_dir.mkdir(parents=True)

    entries: list[dict[str, object]] = []
    try:
        for split_name in SPLITS:
            wanted = expected[split_name]
            if wanted == 0:
                continue
            rows = rows_for_split(
                load_dataset,
                image_feature,
                args.source,
                args.revision,
                split_name,
            )
            received = 0
            for source_index, row in enumerate(rows):
                if received >= wanted:
                    if args.mode == "full":
                        raise SourceChangedError(
                            f"Expected exactly {wanted} {split_name} images but the split has more."
                        )
                    break
                if "image" not in row:
                    raise SourceChangedError("The upstream dataset no longer has an 'image' column.")
                data = image_bytes(row["image"])
                extension = image_extension(data)
                filename = f"cord-v2_{split_name}_{source_index:04d}{extension}"
                destination = images_dir / filename
                destination.write_bytes(data)
                entries.append(
                    {
                        "bytes": len(data),
                        "filename": filename,
                        "package_path": f"images/{filename}",
                        "sha256": hashlib.sha256(data).hexdigest(),
                        "source_index": source_index,
                        "split": split_name,
                    }
                )
                received += 1
            if received != wanted:
                raise SourceChangedError(
                    f"Expected {wanted} {split_name} images but received {received}."
                )
    except Exception:
        shutil.rmtree(staging_dir, ignore_errors=True)
        raise

    entries.sort(key=lambda item: (str(item["split"]), int(item["source_index"])))
    digest_input = "".join(f"{item['sha256']}  {item['package_path']}\n" for item in entries)
    manifest = {
        "dataset": "CORD",
        "dataset_version": "v2",
        "license": "CC-BY-4.0",
        "mode": args.mode,
        "package_sha256": hashlib.sha256(digest_input.encode("utf-8")).hexdigest(),
        "source": args.source,
        "source_revision": args.revision,
        "split_counts": {
            split_name: sum(item["split"] == split_name for item in entries)
            for split_name in SPLITS
        },
        "total_images": len(entries),
        "files": entries,
    }
    (staging_dir / "manifest.json").write_bytes(canonical_json(manifest))
    (staging_dir / "SHA256SUMS").write_text(digest_input, encoding="utf-8", newline="\n")
    if package_dir.exists():
        shutil.rmtree(package_dir)
    staging_dir.rename(package_dir)
    return package_dir


def main() -> None:
    args = parse_args()
    try:
        output = package(args)
    except (SourceChangedError, ValueError) as error:
        raise SystemExit(f"[ERROR] {error}") from error
    print(f"[OK] Wrote {args.mode} CORD package to {output}")
    print(f"     Images   : {output / 'images'}")
    print(f"     Manifest : {output / 'manifest.json'}")


if __name__ == "__main__":
    main()
