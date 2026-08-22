#!/usr/bin/env python3
"""Verify the deployed Phase 7 SPA, REST API, SQL stores, and durable SSE chat."""

from __future__ import annotations

import argparse
import json
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor


def request(base: str, path: str, method: str = "GET", body: object | None = None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(
        f"{base.rstrip('/')}{path}",
        data=data,
        method=method,
        headers={"Content-Type": "application/json"} if data else {},
    )
    with urllib.request.urlopen(req, timeout=180) as response:
        raw = response.read().decode()
        return json.loads(raw) if raw else None


def stream_turn(base: str, conversation_id: str, message: str, key: str):
    req = urllib.request.Request(
        f"{base.rstrip('/')}/api/conversations/{conversation_id}/messages",
        data=json.dumps({"message": message, "idempotencyKey": key}).encode(),
        method="POST",
        headers={"Content-Type": "application/json"},
    )
    events: list[tuple[str, dict]] = []
    with urllib.request.urlopen(req, timeout=180) as response:
        event_name = "message"
        data_lines: list[str] = []
        for raw_line in response:
            line = raw_line.decode().rstrip("\r\n")
            if not line:
                if data_lines:
                    events.append((event_name, json.loads("".join(data_lines))))
                event_name, data_lines = "message", []
            elif line.startswith("event:"):
                event_name = line[6:].strip()
            elif line.startswith("data:"):
                data_lines.append(line[5:].strip())
    return events


def event(events, name):
    return next((payload for event_name, payload in events if event_name == name), None)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("base_url")
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    with urllib.request.urlopen(f"{base}/", timeout=30) as response:
        html = response.read().decode()
    assert "Hidden Layer OCR Studio" in html
    print("[OK] React SPA")

    config = request(base, "/api/config")
    assert config["sqlEnabled"] and config["agentEnabled"], config
    assert request(base, "/api/workflows")
    assert isinstance(request(base, "/api/runs?top=5"), list)
    assert request(base, "/api/receipts?top=5")
    print("[OK] REST, run history, and SQL receipt query")

    conversation = request(
        base, "/api/conversations", "POST", {"title": "Automated Phase 7 verification"}
    )
    conversation_id = conversation["id"]
    try:
        first = stream_turn(
            base,
            conversation_id,
            "List the available workflows, then end your answer with the exact text COUNT=4.",
            "verify-web-turn-1",
        )
        first_done = event(first, "done")
        assert first_done and "COUNT=4" in first_done["answer"], first
        assert event(first, "tool"), first

        second = stream_turn(
            base,
            conversation_id,
            "What count did you state? Reply with only the number.",
            "verify-web-turn-2",
        )
        second_done = event(second, "done")
        assert second_done and second_done["answer"].strip() == "4", second

        replay = stream_turn(
            base,
            conversation_id,
            "This changed text must not be appended.",
            "verify-web-turn-2",
        )
        replay_done = event(replay, "done")
        assert replay_done and replay_done["replayed"] is True, replay

        detail = request(base, f"/api/conversations/{conversation_id}")
        assert detail["conversation"]["turnCount"] == 2, detail
        assert len(detail["turns"]) == 2, detail
        assert "payloadJson" not in detail["conversation"], detail
        print("[OK] SSE, multi-turn restore, history, and idempotent retry")
    finally:
        request(base, f"/api/conversations/{conversation_id}", "DELETE")

    concurrent = request(
        base, "/api/conversations", "POST", {"title": "Concurrency verification"}
    )
    concurrent_id = concurrent["id"]
    try:
        with ThreadPoolExecutor(max_workers=2) as pool:
            futures = [
                pool.submit(
                    stream_turn, base, concurrent_id,
                    f"Reply with only {number}.", f"verify-concurrent-{number}"
                )
                for number in (1, 2)
            ]
            results = [future.result() for future in futures]
        completed = sum(event(result, "done") is not None for result in results)
        conflicts = sum(
            bool(event(result, "error"))
            and event(result, "error").get("code") == "conversation_conflict"
            for result in results
        )
        detail = request(base, f"/api/conversations/{concurrent_id}")
        assert completed == 1 and conflicts == 1, results
        assert detail["conversation"]["turnCount"] == 1 and len(detail["turns"]) == 1, detail
        print("[OK] Optimistic concurrency leaves one consistent turn")
    finally:
        request(base, f"/api/conversations/{concurrent_id}", "DELETE")

    print("All Phase 7 web checks passed.")


if __name__ == "__main__":
    main()
