#!/usr/bin/env bash
#
# Uploads a receipt to Blob, drives the deployed MCP server over HTTPS, then
# asserts the artifacts and the run row exist in Azure Storage. Images never
# travel as local paths; the server only ever sees a blob reference.
#
# Usage:
#   scripts/verify-deployment.sh
#   scripts/verify-deployment.sh --image /path/to/receipt.jpg
#   scripts/verify-deployment.sh --url https://ca-ocr-agent....azurecontainerapps.io/
#   scripts/verify-deployment.sh --workflow receipt_extract_v3
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

IMAGE_FILE=""
URL_OVERRIDE=""
WORKFLOW="receipt_extract_v3"
REFERENCE="DEPLOY01"
SHARED_REFERENCE="DEPLOY02"

while [ $# -gt 0 ]; do
    case "$1" in
        --image)
            IMAGE_FILE="${2:-}"
            shift 2
            ;;
        --url)
            URL_OVERRIDE="${2:-}"
            shift 2
            ;;
        --workflow)
            WORKFLOW="${2:-receipt_extract_v3}"
            shift 2
            ;;
        -h|--help)
            sed -n '2,14p' "$0" | sed 's/^# \?//'
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 2
            ;;
    esac
done

load_env
apply_azure_defaults
require_az

if [ -z "${AZURE_STORAGE_CONNECTION_STRING:-}" ]; then
    echo "[ERROR] AZURE_STORAGE_CONNECTION_STRING is empty." >&2
    exit 1
fi

find_sample() {
    if [ -n "$IMAGE_FILE" ]; then
        printf '%s\n' "$IMAGE_FILE"
        return
    fi
    local candidates=(
        "$ROOT/SampleFiles/images/RCPT001.png"
        "$ROOT/samples/receipt-1.jpg"
    )
    local path
    for path in "${candidates[@]}"; do
        if [ -f "$path" ]; then
            printf '%s\n' "$path"
            return
        fi
    done
    echo "[ERROR] No sample receipt found. Pass --image /path/to/receipt.jpg" >&2
    exit 1
}

wait_for_mcp() {
    local url="$1"
    local attempt
    echo "[INFO] Waiting for MCP at $url"
    for attempt in $(seq 1 18); do
        if python3 - "$url" <<'PY'
import json, sys, urllib.request
url = sys.argv[1]
body = json.dumps({
    "jsonrpc": "2.0", "id": 1, "method": "initialize",
    "params": {"protocolVersion": "2025-06-18", "capabilities": {},
               "clientInfo": {"name": "wait", "version": "1.0"}},
}).encode()
req = urllib.request.Request(url, data=body, method="POST")
req.add_header("Content-Type", "application/json")
req.add_header("Accept", "application/json, text/event-stream")
try:
    with urllib.request.urlopen(req, timeout=30) as response:
        sys.exit(0 if response.status < 400 else 1)
except Exception:
    sys.exit(1)
PY
        then
            echo "[OK] MCP is answering"
            return 0
        fi
        echo "      not ready yet ($attempt/18), retrying in 10s"
        sleep 10
    done
    echo "[ERROR] MCP did not become ready." >&2
    return 1
}

IMAGE_FILE="$(find_sample)"
EXT="${IMAGE_FILE##*.}"
EXT="$(printf '%s' "$EXT" | tr '[:upper:]' '[:lower:]')"
BLOB="$(blob_name "${REFERENCE}.${EXT}")"
SHARED_BLOB="$(blob_name "${SHARED_REFERENCE}.${EXT}")"
CONTAINER="${OCR_Storage__AzureBlob__ContainerName}"

# The same file under two references. The first carries the single-tool checks and the first
# workflow, which leaves it in the replica's cache; the second is only ever touched by the two
# concurrent runs, so their shared download starts cold, which is the case that can race.
echo "[INFO] Uploading $IMAGE_FILE -> ${CONTAINER}/{${BLOB},${SHARED_BLOB}}"
for blob in "$BLOB" "$SHARED_BLOB"; do
    az storage blob upload \
        --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
        --container-name "$CONTAINER" \
        --name "$blob" \
        --file "$IMAGE_FILE" \
        --overwrite \
        --output none
done

URL="${URL_OVERRIDE:-$(app_url)}"
if [ -z "$URL" ]; then
    echo "[ERROR] No URL. Pass --url or deploy first with scripts/azure-deploy.sh" >&2
    exit 1
fi

wait_for_mcp "$URL"

VERIFY_LOG="$(mktemp)"
set +e
# The verifier asserts the OCR text itself, on the summary it already holds rather than on what
# reaches this log. A workflow that completes without recognising anything fails there.
python3 "$SCRIPT_DIR/verify-blob-store.py" \
    --url "$URL" \
    --workflow "$WORKFLOW" \
    --image-ref "$REFERENCE" \
    --image-ref "$SHARED_REFERENCE" \
    --image-ref "$SHARED_REFERENCE" \
    | tee "$VERIFY_LOG"
VERIFY_STATUS=${PIPESTATUS[0]}
set -e

if [ "$VERIFY_STATUS" -ne 0 ]; then
    echo "[ERROR] MCP checks failed." >&2
    rm -f "$VERIFY_LOG"
    exit 1
fi

RUN_IDS="$(grep -Eo 'runId=[0-9a-f-]+' "$VERIFY_LOG" | sed 's/runId=//' | awk '!seen[$0]++')"
if [ -z "$RUN_IDS" ]; then
    echo "[ERROR] No run ids in the verifier output." >&2
    rm -f "$VERIFY_LOG"
    exit 1
fi

echo
echo "[8] Azure-side assertions"
FAILURES=0
for run_id in $RUN_IDS; do
    prefix="$(blob_name "wf_${WORKFLOW}/${run_id}/")"
    count="$(az storage blob list \
        --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
        --container-name "$CONTAINER" \
        --prefix "$prefix" \
        --query "length(@)" \
        -o tsv)"
    echo "    blobs under $prefix : $count"
    if [ "${count:-0}" -lt 1 ]; then
        echo "    MISSING artifacts for $run_id"
        FAILURES=$((FAILURES + 1))
    fi

    rows="$(az storage entity query \
        --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
        --table-name "$OCR_Runs__TableName" \
        --filter "PartitionKey eq '${OCR_Runs__GroupId}' and RunId eq '${run_id}'" \
        --query "length(items)" \
        -o tsv)"
    echo "    table rows for $run_id : $rows"
    if [ "${rows:-0}" -lt 1 ]; then
        echo "    MISSING table row for $run_id"
        FAILURES=$((FAILURES + 1))
    fi
done

rm -f "$VERIFY_LOG"

if [ "$FAILURES" -ne 0 ]; then
    echo
    echo "FAILED: $FAILURES Azure-side assertion(s)"
    exit 1
fi

echo
echo "All checks passed."
echo "    MCP     : $URL"
echo "    images  : $REFERENCE ($BLOB), $SHARED_REFERENCE ($SHARED_BLOB)"
echo "    storage : $CONTAINER / $OCR_Runs__TableName"
