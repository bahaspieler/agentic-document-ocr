#!/usr/bin/env python3
"""Obtain a delegated Auth0 access token with the OAuth device flow."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser
from typing import Any


ROOT = Path(__file__).resolve().parents[1]


def load_env_file() -> None:
    """Load missing Auth0 settings from the project-local .env file."""
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


def post_form(url: str, fields: dict[str, str]) -> dict[str, Any]:
    """Post form-encoded OAuth fields and decode the JSON response."""
    request = urllib.request.Request(
        url,
        data=urllib.parse.urlencode(fields).encode(),
        headers={"Content-Type": "application/x-www-form-urlencoded"},
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        payload = json.loads(error.read().decode())
        if error.code == 403 and payload.get("error") in {"authorization_pending", "slow_down"}:
            return payload
        raise RuntimeError(
            f"Auth0 token request failed ({error.code}): "
            f"{payload.get('error_description', payload.get('error', 'unknown error'))}"
        ) from error


def parse_args() -> argparse.Namespace:
    """Read public Auth0 identifiers from arguments or environment variables."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--domain", default=os.environ.get("OCR_Auth__Domain", ""))
    parser.add_argument("--client-id", default=os.environ.get("AUTH0_DEVICE_CLIENT_ID", ""))
    parser.add_argument("--audience", default=os.environ.get("OCR_Auth__Audience", ""))
    parser.add_argument("--scope", default=os.environ.get("OCR_Auth__ScopeName", "access:ocr"))
    parser.add_argument("--no-browser", action="store_true")
    return parser.parse_args()


def main() -> int:
    """Run device authorization and print only the final bearer token to stdout."""
    load_env_file()
    args = parse_args()
    if not all((args.domain, args.client_id, args.audience, args.scope)):
        print("[ERROR] Auth0 domain, device client ID, audience, and scope are required.", file=sys.stderr)
        return 2

    issuer = f"https://{args.domain.strip().removeprefix('https://').rstrip('/')}"
    device = post_form(
        f"{issuer}/oauth/device/code",
        {
            "client_id": args.client_id,
            "audience": args.audience,
            "scope": f"openid profile email offline_access {args.scope}",
        },
    )
    verification_url = device.get("verification_uri_complete") or device["verification_uri"]
    print(
        f"Open {verification_url} and enter code {device['user_code']} if requested.",
        file=sys.stderr,
    )
    if not args.no_browser:
        webbrowser.open(verification_url)

    interval = int(device.get("interval", 5))
    deadline = time.monotonic() + int(device["expires_in"])
    while time.monotonic() < deadline:
        time.sleep(interval)
        token = post_form(
            f"{issuer}/oauth/token",
            {
                "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
                "device_code": device["device_code"],
                "client_id": args.client_id,
            },
        )
        error = token.get("error")
        if error == "authorization_pending":
            continue
        if error == "slow_down":
            interval += 5
            continue
        if error:
            raise RuntimeError(token.get("error_description", error))
        print(token["access_token"])
        return 0

    print("[ERROR] Device authorization expired before sign-in completed.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
