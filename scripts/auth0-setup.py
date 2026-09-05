#!/usr/bin/env python3
"""Create or update the Auth0 resources required by the OCR serving host."""

from __future__ import annotations

import argparse
import getpass
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

API_NAME = "Agentic Document OCR API"
SPA_NAME = "Agentic Document OCR Web"
DEVICE_NAME = "Agentic Document OCR Device Login"
SCOPE = "access:ocr"


class ManagementApi:
    """Small Auth0 Management API client that never persists its bearer token."""

    def __init__(self, domain: str, token: str) -> None:
        self.base_url = f"https://{domain}/api/v2"
        self.token = token

    def request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | list[dict[str, Any]] | None = None,
    ) -> Any:
        """Send a JSON request and return its decoded response."""
        data = json.dumps(payload).encode() if payload is not None else None
        request = urllib.request.Request(
            f"{self.base_url}/{path.lstrip('/')}",
            data=data,
            method=method,
            headers={
                "Authorization": f"Bearer {self.token}",
                "Content-Type": "application/json",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                body = response.read()
                return json.loads(body) if body else None
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")
            raise RuntimeError(
                f"Auth0 Management API {method} {path} failed ({error.code}): {detail}"
            ) from error


def parse_args() -> argparse.Namespace:
    """Parse command-line settings that are safe to retain in shell history."""
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--domain",
        default=os.environ.get("AUTH0_DOMAIN", ""),
        help="Auth0 tenant domain, for example tenant.eu.auth0.com",
    )
    parser.add_argument(
        "--app-url",
        required=True,
        help="Canonical public application URL; also used as the MCP API audience",
    )
    parser.add_argument("--env-file", default=".env")
    return parser.parse_args()


def normalize_domain(value: str) -> str:
    """Return an Auth0 domain without a URL scheme or trailing slash."""
    domain = value.strip().removeprefix("https://").removeprefix("http://").rstrip("/")
    if not domain or "." not in domain:
        raise ValueError("--domain must be a valid Auth0 tenant domain.")
    return domain


def normalize_url(value: str) -> str:
    """Return a canonical HTTPS resource URI with a trailing slash."""
    parsed = urllib.parse.urlparse(value)
    if parsed.scheme != "https" or not parsed.netloc:
        raise ValueError("--app-url must be an absolute HTTPS URL.")
    return value.rstrip("/") + "/"


def find_by_name(items: list[dict[str, Any]], name: str) -> dict[str, Any] | None:
    """Find one Auth0 object by its display name."""
    return next((item for item in items if item.get("name") == name), None)


def upsert_resource_server(api: ManagementApi, audience: str) -> None:
    """Create or update the resource server and its 24-hour token policy."""
    servers = api.request("GET", "resource-servers?per_page=100")
    current = find_by_name(servers, API_NAME)
    payload = {
        "name": API_NAME,
        "identifier": audience,
        "signing_alg": "RS256",
        "token_lifetime": 86_400,
        "token_lifetime_for_web": 86_400,
        "allow_offline_access": True,
        "skip_consent_for_verifiable_first_party_clients": True,
        "scopes": [{"value": SCOPE, "description": "Use OCR REST and MCP operations"}],
    }
    if current is None:
        api.request("POST", "resource-servers", payload)
        return
    if current["identifier"] != audience:
        raise RuntimeError(
            f"{API_NAME!r} already uses immutable identifier {current['identifier']!r}; "
            "delete it in Auth0 or use that URL."
        )
    update = dict(payload)
    update.pop("identifier")
    api.request(
        "PATCH",
        f"resource-servers/{urllib.parse.quote(audience, safe='')}",
        update,
    )


def upsert_client(
    api: ManagementApi,
    clients: list[dict[str, Any]],
    name: str,
    payload: dict[str, Any],
) -> str:
    """Create or update a client and return its public client identifier."""
    current = find_by_name(clients, name)
    if current is None:
        created = api.request("POST", "clients", {"name": name, **payload})
        return str(created["client_id"])
    client_id = str(current["client_id"])
    api.request("PATCH", f"clients/{urllib.parse.quote(client_id, safe='')}", payload)
    return client_id


def ensure_default_dcr_grant(api: ManagementApi, audience: str) -> None:
    """Grant the OCR user scope to every strict-mode DCR client."""
    grants = api.request(
        "GET",
        "client-grants?per_page=100&audience="
        + urllib.parse.quote(audience, safe=""),
    )
    current = next(
        (
            grant
            for grant in grants
            if grant.get("default_for") == "third_party_clients"
            and grant.get("subject_type") == "user"
        ),
        None,
    )
    payload = {
        "audience": audience,
        "scope": [SCOPE],
        "subject_type": "user",
        "default_for": "third_party_clients",
    }
    if current is None:
        api.request("POST", "client-grants", payload)
    else:
        api.request("PATCH", f"client-grants/{current['id']}", {"scope": [SCOPE]})


def promote_login_connections(api: ManagementApi, client_ids: list[str]) -> list[str]:
    """Promote configured database, Google, and GitHub connections for DCR clients."""
    connections = api.request("GET", "connections?per_page=100")
    desired_strategies = {"auth0", "google-oauth2", "github"}
    promoted: list[str] = []
    for connection in connections:
        if connection.get("strategy") not in desired_strategies:
            continue
        api.request(
            "PATCH",
            f"connections/{urllib.parse.quote(connection['id'], safe='')}",
            {"is_domain_connection": True},
        )
        api.request(
            "PATCH",
            f"connections/{urllib.parse.quote(connection['id'], safe='')}/clients",
            [{"client_id": client_id, "status": True} for client_id in client_ids],
        )
        promoted.append(str(connection["strategy"]))
    return promoted


def update_env(path: Path, values: dict[str, str]) -> None:
    """Update public Auth0 configuration without disturbing unrelated settings."""
    lines = path.read_text().splitlines() if path.exists() else []
    remaining = dict(values)
    updated: list[str] = []
    for line in lines:
        key = line.split("=", 1)[0] if "=" in line and not line.lstrip().startswith("#") else ""
        if key in remaining:
            updated.append(f"{key}={remaining.pop(key)}")
        else:
            updated.append(line)
    if updated and remaining:
        updated.append("")
    updated.extend(f"{key}={value}" for key, value in remaining.items())
    path.write_text("\n".join(updated).rstrip() + "\n")


def main() -> int:
    """Provision Auth0 and persist only non-secret runtime identifiers."""
    args = parse_args()
    token = os.environ.get("AUTH0_MANAGEMENT_API_TOKEN", "")
    if not token and sys.stdin.isatty():
        token = getpass.getpass("Paste the short-lived Auth0 Management API token: ").strip()
    if not token:
        print(
            "[ERROR] A short-lived Auth0 Management API token is required.",
            file=sys.stderr,
        )
        return 2

    domain = normalize_domain(args.domain)
    audience = normalize_url(args.app_url)
    browser_origin = audience.rstrip("/")
    api = ManagementApi(domain, token)
    upsert_resource_server(api, audience)

    clients = api.request("GET", "clients?per_page=100&fields=client_id,name&include_fields=true")
    spa_id = upsert_client(
        api,
        clients,
        SPA_NAME,
        {
            "app_type": "spa",
            "is_first_party": True,
            "oidc_conformant": True,
            "token_endpoint_auth_method": "none",
            "callbacks": [browser_origin],
            "allowed_logout_urls": [browser_origin],
            "web_origins": [browser_origin],
            "grant_types": ["authorization_code", "refresh_token"],
            "refresh_token": {
                "rotation_type": "rotating",
                "expiration_type": "expiring",
                "token_lifetime": 2_592_000,
                "idle_token_lifetime": 604_800,
            },
        },
    )
    device_id = upsert_client(
        api,
        clients,
        DEVICE_NAME,
        {
            "app_type": "native",
            "is_first_party": True,
            "oidc_conformant": True,
            "token_endpoint_auth_method": "none",
            "grant_types": [
                "authorization_code",
                "refresh_token",
                "urn:ietf:params:oauth:grant-type:device_code",
            ],
        },
    )

    ensure_default_dcr_grant(api, audience)
    promoted = promote_login_connections(api, [spa_id, device_id])
    api.request(
        "PATCH",
        "tenants/settings",
        {
            "flags": {"enable_dynamic_client_registration": True},
            "resource_parameter_profile": "compatibility",
            "dynamic_client_registration_security_mode": "strict",
            "authorization_response_iss_parameter_supported": True,
        },
    )

    update_env(
        Path(args.env_file),
        {
            "OCR_Auth__Enabled": "true",
            "OCR_Auth__Domain": domain,
            "OCR_Auth__ClientId": spa_id,
            "OCR_Auth__Audience": audience,
            "OCR_Auth__ScopeName": SCOPE,
            "OCR_Auth__ResourceUri": audience,
            "AUTH0_DEVICE_CLIENT_ID": device_id,
        },
    )
    print("[OK] Auth0 API, SPA, device client, strict DCR, and MCP resource profile configured.")
    print(f"[OK] Public identifiers written to {args.env_file}.")
    print(f"[OK] Promoted available login connections: {', '.join(promoted) or 'none'}.")
    if {"google-oauth2", "github"} - set(promoted):
        print("[NEXT] Configure missing Google/GitHub connections, then rerun this command.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
