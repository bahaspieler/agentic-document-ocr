#!/usr/bin/env python3
"""Drives a running OcrAgent.Mcp server over MCP to verify the image and run stores end to end.

Checks, in order: the guidance the server hands a client, an input image, a single plugin call, a
full workflow run, a second call on what that workflow produced, two concurrent workflow runs, and
the run records left behind. The
concurrency case matters because every run must keep its own write scope; overlapping runs writing to
the same scope would corrupt each other's output. The follow-up call matters because a workflow
writes under a scope it releases when it finishes, so its output is only reachable afterwards if the
reference it reports carries that scope. The workflow run also asks for progress, so its step
notifications are counted against the number of steps the summary says it ran. The run record checks
are skipped when the server was started without a run store.

The input image is either a local file the server can read (--image, which goes through
RegisterImage) or a blob-native reference already in the container (--image-ref), which is how a
deployed instance is tested without a shared filesystem. Repeat --image-ref: the first drives steps
2 to 4, the next two drive the concurrent runs. Giving the pair a second reference of their own is
worth the extra upload, because the first one is in the server's cache by the time they start and a
shared download only races while it is cold.

Usage:
    python3 scripts/verify-blob-store.py --image /path/to/receipt.jpg
    python3 scripts/verify-blob-store.py --image-ref DEPLOY01 --url https://app.azurecontainerapps.io/mcp
    python3 scripts/verify-blob-store.py --url http://localhost:5290/mcp --workflow receipt_extract_v3
"""

import argparse
import json
import os
import re
import sys
import threading
import urllib.request

# RunWorkflow reports the workflow context at the end of its summary, one key per line, each value
# cut off at 200 characters. The OCR step leaves its result there as JSON, so the closing quote is
# usually gone by the time it is printed.
OCR_TEXT_PATTERN = re.compile(r'"text"\s*:\s*"((?:[^"\\]|\\.)*)')

# Token the workflow call asks its progress notifications to be tagged with. Any value the client
# picks will do; a fixed one keeps the check readable.
PROGRESS_TOKEN = "verify-workflow-progress"


class McpClient:
    """Minimal MCP Streamable HTTP client. One instance is one session."""

    def __init__(self, url, name, access_token=None, api_key=None):
        self.url = url
        self.name = name
        self.access_token = access_token
        self.api_key = api_key
        self.session_id = None
        self.request_id = 0
        self.instructions = None
        # Notifications the server sent while answering the most recent request. In stateless mode
        # there is no standalone stream to receive them on, so they arrive on the response body of
        # the request that caused them, ahead of the result.
        self.notifications = []

    def _post(self, body, parse=True):
        request = urllib.request.Request(self.url, data=json.dumps(body).encode(), method="POST")
        request.add_header("Content-Type", "application/json")
        request.add_header("Accept", "application/json, text/event-stream")
        if self.access_token:
            request.add_header("Authorization", f"Bearer {self.access_token}")
        elif self.api_key:
            request.add_header("X-API-Key", self.api_key)
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
        self.notifications = []
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
            if not isinstance(message, dict):
                continue
            if "id" in message:
                return message
            if "method" in message:
                self.notifications.append(message)
        return None

    def initialize(self):
        response = self._post({
            "jsonrpc": "2.0", "id": 1, "method": "initialize",
            "params": {"protocolVersion": "2025-06-18", "capabilities": {},
                       "clientInfo": {"name": self.name, "version": "1.0"}},
        })
        self._post({"jsonrpc": "2.0", "method": "notifications/initialized"}, parse=False)
        self.instructions = response["result"].get("instructions")
        return response["result"]["serverInfo"]

    def prompts(self):
        self.request_id += 1
        response = self._post({
            "jsonrpc": "2.0", "id": 100 + self.request_id, "method": "prompts/list", "params": {},
        })
        return (response or {}).get("result", {}).get("prompts", [])

    def tool_names(self):
        self.request_id += 1
        response = self._post({
            "jsonrpc": "2.0", "id": 100 + self.request_id, "method": "tools/list", "params": {},
        })
        tools = (response or {}).get("result", {}).get("tools", [])
        return {tool.get("name") for tool in tools}

    def call_tool(self, tool, arguments, progress_token=None):
        self.request_id += 1
        params = {"name": tool, "arguments": arguments}
        # A server only reports progress for a call that asked for it, by naming the token its
        # notifications should carry.
        if progress_token is not None:
            params["_meta"] = {"progressToken": progress_token}

        response = self._post({
            "jsonrpc": "2.0", "id": 100 + self.request_id, "method": "tools/call",
            "params": params,
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


def steps_executed_by(text):
    """How many steps a workflow summary says it ran, or None when it does not say."""
    for line in text.splitlines():
        if line.startswith("Steps executed:"):
            try:
                return int(line.split("Steps executed:")[1].strip())
            except ValueError:
                return None
    return None


def output_image_of(text):
    """The image reference a workflow summary reports as its result, or None."""
    for line in text.splitlines():
        if line.startswith("Output image:"):
            return line.split("Output image:")[1].strip() or None
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
    parser.add_argument("--url", default="http://localhost:5290/mcp")
    parser.add_argument("--image", help="Absolute path to an image the server can read")
    parser.add_argument(
        "--image-ref", action="append", dest="image_refs", metavar="REF",
        help="Blob-native image reference. Repeatable: first drives steps 2-4, next two drive concurrent runs.")
    parser.add_argument("--workflow", default="receipt_extract_v3")
    parser.add_argument(
        "--access-token",
        default=os.environ.get("OCR_AUTH_ACCESS_TOKEN"),
        help="Delegated bearer token; defaults to OCR_AUTH_ACCESS_TOKEN.",
    )
    parser.add_argument(
        "--api-key",
        default=os.environ.get("OCR_AUTH_API_KEY"),
        help="Static API key; defaults to OCR_AUTH_API_KEY.",
    )
    args = parser.parse_args()

    primary, concurrent_a, concurrent_b, register_path = resolve_inputs(args)
    failures = []

    client = McpClient(args.url, "verify-blob-store", args.access_token, args.api_key)
    print("server:", client.initialize())
    failures.extend(check_server_guidance(client))

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
        "RunWorkflow", {"workflowId": args.workflow, "imageReference": reference},
        progress_token=PROGRESS_TOKEN)
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

    failures.extend(check_progress(client.notifications, workflow_output))

    failures.extend(check_followup_call(client, workflow_output))

    print("\n[6] Two concurrent workflow runs")
    results = {}

    def run_workflow(tag, image_ref):
        worker = McpClient(
            args.url,
            "verify-concurrent-" + tag,
            args.access_token,
            args.api_key,
        )
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


def check_server_guidance(client):
    """Confirms the server tells a connecting client how to use the tools together.

    Instructions carry the rules the tool schemas cannot express, chiefly that a workflow is one call
    and that references chain. They must not repeat the tool catalogue, which the client already has.
    Prompts name the flows worth offering by name.
    """
    failures = []
    instructions = client.instructions or ""

    print("\n[0] Server instructions: %d characters" % len(instructions))
    if not instructions:
        failures.append("server sent no instructions")
    else:
        for rule in ("RunWorkflow ONCE", "CHAINING RULE", "REFERENCE RULE"):
            if rule not in instructions:
                failures.append("instructions omit %r" % rule)
        if "AVAILABLE TOOLS" in instructions:
            failures.append("instructions repeat the tool catalogue")

    names = [prompt.get("name") for prompt in client.prompts()]
    print("    prompts: %s" % (", ".join(names) or "none"))
    for expected in ("extract_receipt", "improve_image_for_ocr"):
        if expected not in names:
            failures.append("prompt %r not offered" % expected)

    return failures


def check_progress(notifications, workflow_output):
    """Confirms the workflow reported its steps while it was still running.

    A run takes seconds across a dozen steps, so a client should be able to show where it is rather
    than only that a call is outstanding. The count is checked against the summary: one notification
    per step the run actually executed, no more and no fewer.
    """
    progress = [
        message["params"] for message in notifications
        if message.get("method") == "notifications/progress"
        and message.get("params", {}).get("progressToken") == PROGRESS_TOKEN
    ]

    print("    progress notifications: %d" % len(progress))
    if not progress:
        return ["RunWorkflow sent no progress notifications"]

    print("      first: %s" % json.dumps(progress[0].get("message") or progress[0]))
    print("      last:  %s" % json.dumps(progress[-1].get("message") or progress[-1]))

    steps = steps_executed_by(workflow_output)
    if steps is not None and len(progress) != steps:
        return ["progress notifications (%d) do not match steps executed (%d)"
                % (len(progress), steps)]

    # The last report should say the run is finished, which is how a client knows not to expect more.
    tail = progress[-1]
    if tail.get("total") is not None and tail.get("progress") != tail.get("total"):
        return ["last progress notification is %s of %s" % (tail.get("progress"), tail.get("total"))]

    return []


def check_followup_call(client, workflow_output):
    """Confirms the reference a workflow reports is usable in the next call.

    The run wrote it under a per-run scope and released that scope on the way out, so this only
    succeeds if the reference names the scope it lives in.
    """
    reference = output_image_of(workflow_output)
    if reference is None:
        print("\n[5] Follow-up call skipped (the workflow reported no output image)")
        return []

    is_error, output = client.call_tool(
        "BinarizeImage", {"imageReference": reference, "method": "otsu"})
    print("\n[5] BinarizeImage on the workflow output  error=%s" % is_error)
    print("    %s -> %s" % (reference, output[:120]))
    return ["follow-up call on the workflow output: %s" % output[:160]] if is_error else []


def check_run_records(client, workflow_run_id, concurrent_ids):
    """Confirms each job left a record and that concurrent runs were recorded separately."""
    failures = []

    if not {"GetRun", "ListRuns"} <= client.tool_names():
        print("\n[7] Run records    skipped (server started without a run store)")
        return failures

    is_error, listing = client.call_tool("ListRuns", {"top": 25})
    print("\n[7] ListRuns       error=%s" % is_error)
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
