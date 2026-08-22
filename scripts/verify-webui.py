#!/usr/bin/env python3
"""Verify the deployed Phase 7 SPA, REST API, SQL stores, and durable SSE chat."""

from __future__ import annotations

import argparse
import json
import mimetypes
import pathlib
import uuid
import urllib.error
import urllib.parse
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


def stream_turn(
    base: str,
    conversation_id: str,
    message: str,
    key: str,
    attachments: list[dict] | None = None,
):
    return stream_request(
        base,
        f"/api/conversations/{conversation_id}/messages",
        {
            "message": message,
            "idempotencyKey": key,
            "attachments": attachments or [],
        },
    )


def stream_request(base: str, path: str, body: dict):
    req = urllib.request.Request(
        f"{base.rstrip('/')}{path}",
        data=json.dumps(body).encode(),
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


def upload_image(base: str, path: pathlib.Path):
    boundary = f"----ocr-{uuid.uuid4().hex}"
    content_type = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{path.name}"\r\n'
        f"Content-Type: {content_type}\r\n\r\n"
    ).encode() + path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(
        f"{base.rstrip('/')}/api/images",
        data=body,
        method="POST",
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    with urllib.request.urlopen(req, timeout=180) as response:
        return json.loads(response.read().decode()), content_type


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("base_url")
    parser.add_argument("--image", type=pathlib.Path)
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
    assert isinstance(request(base, "/api/receipts?top=5"), list)
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
        assert detail["turns"][0]["events"], detail
        print("[OK] SSE, multi-turn restore, history, and idempotent retry")

        if args.image:
            uploaded, content_type = upload_image(base, args.image)
            attachment = {
                "reference": uploaded["reference"],
                "name": args.image.name,
                "contentType": content_type,
            }
            attached = stream_turn(
                base,
                conversation_id,
                "Acknowledge the attached receipt image reference without processing it.",
                "verify-web-attachment",
                [attachment],
            )
            assert event(attached, "done"), attached
            detail = request(base, f"/api/conversations/{conversation_id}")
            assert detail["turns"][-1]["attachments"][0]["reference"] == uploaded["reference"]
            print("[OK] Chat attachment survives conversation reload")

            tool_events = stream_turn(
                base,
                conversation_id,
                "Use RemoveDocumentBackground exactly once on the attached image. "
                "Do not run a workflow.",
                "verify-single-image-tool",
                [attachment],
            )
            tool_runs = [
                payload["run"]
                for event_name, payload in tool_events
                if event_name == "run" and payload.get("run", {}).get("mode") == "tool"
            ]
            assert tool_runs, tool_events
            tool_run = tool_runs[-1]
            tool_artifacts = request(
                base,
                f"/api/runs/{tool_run['runId']}/artifacts"
                f"?conversationId={conversation_id}",
            )
            assert any(
                row["contentType"].startswith("image/") for row in tool_artifacts
            ), tool_artifacts
            print("[OK] Single image tool exposes its output as a run artifact")

            workflows = request(base, "/api/workflows")
            workflow = max(
                (row for row in workflows if "receipt_extract" in row["id"]),
                key=lambda row: row["id"],
            )
            job_events = stream_request(
                base,
                "/api/jobs/stream",
                {
                    "mode": "workflow",
                    "name": workflow["id"],
                    "imageReference": uploaded["reference"],
                    "conversationId": conversation_id,
                    "idempotencyKey": "verify-guided-workflow",
                    "userMessage": f"Run guided workflow {workflow['id']} on this image.",
                    "attachmentName": args.image.name,
                    "attachmentContentType": content_type,
                },
            )
            assert event(job_events, "step"), job_events
            completed_job = event(job_events, "done")
            assert completed_job, job_events
            job = completed_job
            result = job["result"]
            assert result["success"] and result["runId"], job
            assert result.get("receiptId"), job
            artifacts = request(
                base,
                f"/api/runs/{result['runId']}/artifacts?conversationId={conversation_id}",
            )
            assert artifacts, job
            assert any(row["name"].lower().endswith(".json") for row in artifacts), artifacts
            assert any(row["name"].lower().endswith(".csv") for row in artifacts), artifacts
            detail = request(base, f"/api/conversations/{conversation_id}")
            guided_turn = detail["turns"][-1]
            assert guided_turn["runIds"] == [result["runId"]], guided_turn
            assert guided_turn["attachments"][0]["reference"] == uploaded["reference"], guided_turn
            assert any(
                row.get("type") == "run" and row.get("run", {}).get("runId") == result["runId"]
                for row in guided_turn["events"]
            ), guided_turn
            assert any(row.get("type") == "artifact" for row in guided_turn["events"]), guided_turn
            print("[OK] Guided workflow persists a complete conversation turn")
            artifact = artifacts[0]
            encoded_name = urllib.parse.quote(artifact["name"])
            with urllib.request.urlopen(
                f"{base}/api/runs/{result['runId']}/artifacts/{encoded_name}"
                f"?conversationId={conversation_id}&download=true",
                timeout=30,
            ) as response:
                assert response.read()
                assert "attachment" in response.headers.get("Content-Disposition", "")
            print("[OK] Structured workflow and artifact preview/download")

            if result.get("receiptId"):
                receipt_id = urllib.parse.quote(result["receiptId"], safe="")
                receipt = request(
                    base,
                    f"/api/receipts/{receipt_id}?conversationId={conversation_id}",
                )
                corrected = json.loads(receipt["currentJson"])
                corrected["merchant"]["name"] = (
                    corrected["merchant"].get("name") or "Unknown"
                ) + " (verified)"
                updated = request(
                    base,
                    f"/api/receipts/{receipt_id}",
                    "PATCH",
                    {
                        "receipt": corrected,
                        "rowVersion": receipt["rowVersion"],
                        "conversationId": conversation_id,
                        "changeSummary": "Deployment verifier correction",
                    },
                )
                assert updated["currentRevision"] == receipt["currentRevision"] + 1
                revisions = request(
                    base,
                    f"/api/receipts/{receipt_id}/revisions"
                    f"?conversationId={conversation_id}",
                )
                assert revisions and revisions[0]["revisionNumber"] == updated["currentRevision"]
                for suffix in ("corrected.json", "corrected.csv"):
                    with urllib.request.urlopen(
                        f"{base}/api/receipts/{receipt_id}/{suffix}"
                        f"?conversationId={conversation_id}",
                        timeout=30,
                    ) as response:
                        assert response.read()
                print("[OK] Audited receipt correction and corrected exports")
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
