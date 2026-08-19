#!/usr/bin/env python3
"""Drives a running OcrAgent.Mcp server over MCP to verify the image and run stores end to end.

Checks, in order: an input image, a single plugin call, a full workflow run, two concurrent
workflow runs, and the run records left behind. The concurrency case matters because every run must
keep its own write scope; overlapping runs writing to the same scope would corrupt each other's
output. The run record checks are skipped when the server was started without a run store.

The input image is either a local file the server can read (--image, which goes through
RegisterImage) or a blob-native reference already in the container (--image-ref), which is how a
deployed instance is tested without a shared filesystem. Repeat --image-ref: the first drives steps
2 to 4, the next two drive the concurrent runs. Giving the pair a second reference of their own is
worth the extra upload, because the first one is in the server's cache by the time they start and a
shared download only races while it is cold.

Usage:
    python3 scripts/verify-blob-store.py --image /path/to/receipt.jpg
    python3 scripts/verify-blob-store.py --image-ref DEPLOY01 --url https://app.azurecontainerapps.io/
    python3 scripts/verify-blob-store.py --url http://localhost:5290/ --workflow receipt_extract_v3
"""

import argparse
import json
import re
import sys
import threading
import urllib.request

# RunWorkflow reports the workflow context at the end of its summary, one key per line, each value
# cut off at 200 characters. The OCR step leaves its result there as JSON, so the closing quote is
# usually gone by the time it is printed.
OCR_TEXT_PATTERN = re.compile(r'"text"\s*:\s*"((?:[^"\\]|\\.)*)')


class McpClient:
    """Minimal MCP Streamable HTTP client. One instance is one session."""

    def __init__(self, url, name):
        self.url = url
        self.name = name
        self.session_id = None
        self.request_id = 0

    def _post(self, body, parse=True):
        request = urllib.request.Request(self.url, data=json.dumps(body).encode(), method="POST")
        request.add_header("Content-Type", "application/json")
        request.add_header("Accept", "application/json, text/event-stream")
        if self.session_id:
            request.add_header("mcp-session-id", self.session_id)

        with urllib.request.urlopen(request, timeout=300) as response:
            session_id = response.headers.get("mcp-session-id")
            if session_id:
                self.session_id = session_id
            raw = response.read().decode(errors="replace")

        if not parse:
            return None

        # Responses arrive either as plain JSON or as server-sent events.
        for line in raw.splitlines():
            line = line.strip()
            if line.startswith("data:"):
                line = line[5:].strip()
            if not line:
                continue
            try:
                message = json.loads(line)
            except ValueError:
                continue
            if isinstance(message, dict) and "id" in message:
                return message
        return None

    def initialize(self):
        response = self._post({
            "jsonrpc": "2.0", "id": 1, "method": "initialize",
            "params": {"protocolVersion": "2025-06-18", "capabilities": {},
                       "clientInfo": {"name": self.name, "version": "1.0"}},
        })
        self._post({"jsonrpc": "2.0", "method": "notifications/initialized"}, parse=False)
        return response["result"]["serverInfo"]

    def tool_names(self):
        self.request_id += 1
        response = self._post({
            "jsonrpc": "2.0", "id": 100 + self.request_id, "method": "tools/list", "params": {},
        })
        tools = (response or {}).get("result", {}).get("tools", [])
        return {tool.get("name") for tool in tools}

    def call_tool(self, tool, arguments):
        self.request_id += 1
        response = self._post({
            "jsonrpc": "2.0", "id": 100 + self.request_id, "method": "tools/call",
            "params": {"name": tool, "arguments": arguments},
        })
        result = (response or {}).get("result", {})
        text = "\n".join(
            block.get("text", "") for block in result.get("content", [])
            if block.get("type") == "text"
        ).strip()
        # Tools returning a bare string arrive JSON quoted.
        if text.startswith('"'):
            try:
                text = json.loads(text)
            except ValueError:
                pass
        return result.get("isError", False), text


def run_id_of(text):
    for line in text.splitlines():
        if "Run ID:" in line:
            return line.split("Run ID:")[1].strip()
    return None


def ocr_text_of(output):
    """Recognised text from a workflow summary, or None when the workflow left none."""
    for line in output.splitlines():
        stripped = line.strip()
        if not stripped.startswith("ocrResult:"):
            continue
        match = OCR_TEXT_PATTERN.search(stripped)
        if match:
            return re.sub(r"\\[nrt]|\s+", " ", match.group(1)).strip()
    return None


def ocr_failure(output, label):
    """Reports a workflow that ran OCR and came back with nothing to show for it.

    A workflow whose image never decoded still reports success: the OCR step is handed a blank
    image and returns an empty string. Reading the text back is what separates the two.
    """
    if "RunOcr" not in output:
        return None, None

    text = ocr_text_of(output)
    if text is None:
        return "%s ran OCR but reported no result" % label, None
    if len(re.findall(r"[A-Za-z0-9]", text)) < 20:
        return "%s recognised no text (%r)" % (label, text), text
    return None, text


def resolve_inputs(args):
    """Returns (primary_ref, concurrent_a, concurrent_b, register_path_or_none)."""
    if bool(args.image) == bool(args.image_refs):
        raise SystemExit("Provide exactly one of --image or --image-ref.")

    if args.image:
        return None, None, None, args.image

    refs = list(args.image_refs)
    primary = refs[0]
    concurrent_a = refs[1] if len(refs) > 1 else primary
    concurrent_b = refs[2] if len(refs) > 2 else primary
    return primary, concurrent_a, concurrent_b, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://localhost:5290/")
    parser.add_argument("--image", help="Absolute path to an image the server can read")
    parser.add_argument(
        "--image-ref", action="append", dest="image_refs", metavar="REF",
        help="Blob-native image reference. Repeatable: first drives steps 2-4, next two drive concurrent runs.")
    parser.add_argument("--workflow", default="receipt_extract_v3")
    args = parser.parse_args()

    primary, concurrent_a, concurrent_b, register_path = resolve_inputs(args)
    failures = []

    client = McpClient(args.url, "verify-blob-store")
    print("server:", client.initialize())

    if register_path is not None:
        is_error, reference = client.call_tool("RegisterImage", {"filePath": register_path})
        print("\n[1] RegisterImage  error=%s reference=%s" % (is_error, reference))
        if is_error:
            sys.exit("RegisterImage failed: %s" % reference)
        concurrent_refs = {"A": None, "B": None}
    else:
        reference = primary
        print("\n[1] Image reference %s (blob-native, RegisterImage skipped)" % reference)
        concurrent_refs = {"A": concurrent_a, "B": concurrent_b}
        if concurrent_a == concurrent_b:
            print("    concurrent runs both read %s%s" % (
                concurrent_a, "" if concurrent_a == reference else " (untouched so far, so cold)"))

    is_error, output = client.call_tool("AssessQuality", {"imageReference": reference})
    print("[2] AssessQuality  error=%s" % is_error)
    print("    %s" % output[:140])
    if is_error:
        failures.append("AssessQuality")

    is_error, output = client.call_tool("BinarizeImage", {"imageReference": reference, "method": "otsu"})
    print("[3] BinarizeImage  error=%s output=%s" % (is_error, output))
    if is_error:
        failures.append("BinarizeImage")

    is_error, workflow_output = client.call_tool(
        "RunWorkflow", {"workflowId": args.workflow, "imageReference": reference})
    workflow_run_id = run_id_of(workflow_output)
    print("[4] RunWorkflow    error=%s runId=%s" % (is_error, workflow_run_id))
    if is_error or "completed" not in workflow_output:
        failures.append("RunWorkflow")
        print("    %s" % workflow_output[:300])
    else:
        problem, text = ocr_failure(workflow_output, "RunWorkflow")
        if problem:
            failures.append(problem)
            print("    NO OCR TEXT")
        elif text:
            print("    ocr text: %s" % text[:110])

    print("\n[5] Two concurrent workflow runs")
    results = {}

    def run_workflow(tag, image_ref):
        worker = McpClient(args.url, "verify-concurrent-" + tag)
        worker.initialize()
        if image_ref is None:
            failed, image = worker.call_tool("RegisterImage", {"filePath": register_path})
            if failed:
                results[tag] = (True, "RegisterImage failed: %s" % image)
                return
            image_ref = image
        results[tag] = worker.call_tool(
            "RunWorkflow", {"workflowId": args.workflow, "imageReference": image_ref})

    threads = [
        threading.Thread(target=run_workflow, args=(tag, concurrent_refs[tag]))
        for tag in ("A", "B")
    ]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()

    for tag in ("A", "B"):
        failed, output = results[tag]
        print("    run %s: error=%s completed=%s runId=%s"
              % (tag, failed, "completed" in output, run_id_of(output)))
        if failed or "completed" not in output:
            failures.append("concurrent run %s" % tag)
            continue

        # When both runs share an input reference, this is what catches one of them reading a
        # half-downloaded copy: the run still completes, and the text is what goes missing.
        problem, text = ocr_failure(output, "concurrent run %s" % tag)
        if problem:
            failures.append(problem)
            print("      NO OCR TEXT")
        elif text:
            print("      ocr text: %s" % text[:100])

    ids = [run_id_of(results[tag][1]) for tag in ("A", "B")]
    if ids[0] is None or ids[0] == ids[1]:
        failures.append("concurrent runs shared a run id")

    failures.extend(check_run_records(client, workflow_run_id, ids))

    print("\n%s" % ("FAILED: " + ", ".join(failures) if failures else "All checks passed."))
    return 1 if failures else 0


def check_run_records(client, workflow_run_id, concurrent_ids):
    """Confirms each job left a record and that concurrent runs were recorded separately."""
    failures = []

    if not {"GetRun", "ListRuns"} <= client.tool_names():
        print("\n[6] Run records    skipped (server started without a run store)")
        return failures

    is_error, listing = client.call_tool("ListRuns", {"top": 25})
    print("\n[6] ListRuns       error=%s" % is_error)
    if is_error:
        return ["ListRuns"]

    # The single plugin call from step 3 has no id of its own to look up, so the listing is the
    # evidence that standalone calls are recorded and not only workflows.
    if "tool:BinarizeImage" not in listing:
        failures.append("no record for the standalone BinarizeImage call")

    for label, run_id in [("workflow", workflow_run_id)] + list(zip(["concurrent A", "concurrent B"], concurrent_ids)):
        if run_id is None:
            continue

        is_error, record = client.call_tool("GetRun", {"runId": run_id})
        recorded = not is_error and run_id in record
        print("    GetRun %s: %s" % (label, "found" if recorded else "MISSING"))
        if not recorded:
            failures.append("no record for %s run %s" % (label, run_id))

    workflow_rows = [line for line in listing.splitlines() if "workflow:" in line]
    print("    workflow rows in history: %d" % len(workflow_rows))
    if len(workflow_rows) < 3:
        failures.append("expected at least 3 workflow records, found %d" % len(workflow_rows))

    return failures


if __name__ == "__main__":
    sys.exit(main())
