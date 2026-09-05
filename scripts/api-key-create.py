#!/usr/bin/env python3
"""Generate a named API key and store only its SHA-256 hash."""

from __future__ import annotations

import argparse
import hashlib
import re
import secrets
from pathlib import Path


def parse_args() -> argparse.Namespace:
    """Read the key name and destination environment file."""
    parser = argparse.ArgumentParser()
    parser.add_argument("name", help="Key name, for example AutomationClient or ClaudeDemo")
    parser.add_argument("--env-file", default=".env")
    return parser.parse_args()


def update_env(path: Path, variable: str, value: str) -> None:
    """Replace one environment value or append it when absent."""
    lines = path.read_text().splitlines() if path.exists() else []
    replacement = f"{variable}={value}"
    updated = False
    for index, line in enumerate(lines):
        if line.startswith(f"{variable}="):
            lines[index] = replacement
            updated = True
    if not updated:
        if lines:
            lines.append("")
        lines.append(replacement)
    path.write_text("\n".join(lines).rstrip() + "\n")


def main() -> int:
    """Generate the key, save its hash, and reveal the raw value once."""
    args = parse_args()
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", args.name):
        raise SystemExit("Key names must start with a letter and contain only letters, digits, or _.")

    raw_key = f"hlocr_{secrets.token_urlsafe(32)}"
    digest = hashlib.sha256(raw_key.encode()).hexdigest()
    variable = f"OCR_Auth__ApiKeys__{args.name}"
    update_env(Path(args.env_file), variable, digest)

    print(f"[OK] Stored only the SHA-256 hash in {args.env_file} as {variable}.")
    print("[IMPORTANT] Copy this key now; it cannot be recovered:")
    print(raw_key)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
