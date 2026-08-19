#!/usr/bin/env bash
#
# Runs OcrAgent.Mcp with Azure Blob Storage as the image store.
#
# Settings come from the .env file at the project root (copy .env.example to .env and fill it in).
# The server loads that file itself, so nothing has to be exported first. Anything already exported
# still wins, and the variables below override the file when passed to this script.
#
# Optional environment variables:
#   OCR_CONTAINER   blob container name          (default: from .env)
#   OCR_PORT        HTTP port for the MCP server (default: 5290)
#   OCR_BLOB_PREFIX prefix inside the container  (default: from .env)
#
# Usage:
#   scripts/run-mcp-blob.sh              # run in the foreground
#   scripts/run-mcp-blob.sh --background # run detached, log to /tmp/mcp_blob.log
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/../OcrAgent.Mcp/OcrAgent.Mcp.csproj"
APP_DIR="$SCRIPT_DIR/../OcrAgent.Mcp/bin/Debug/net8.0"
ENV_FILE="$SCRIPT_DIR/../.env"

if [ ! -f "$ENV_FILE" ]; then
    echo "[ERROR] No .env file at $ENV_FILE"
    echo "        cp .env.example .env, then fill in AZURE_STORAGE_CONNECTION_STRING."
    exit 1
fi

PORT="${OCR_PORT:-5290}"

dotnet build "$PROJECT" -c Debug --nologo --verbosity quiet

# Free the port so repeated runs do not fail on a stale listener.
PIDS=$(lsof -ti "tcp:$PORT" 2>/dev/null || true)
if [ -n "$PIDS" ]; then
    echo "[INFO] Stopping process already bound to port $PORT"
    kill -9 $PIDS 2>/dev/null || true
    sleep 1
fi

export OCR_Storage__Provider=azureblob
export ASPNETCORE_URLS="http://localhost:$PORT"

[ -n "${OCR_CONTAINER:-}" ] && export OCR_Storage__AzureBlob__ContainerName="$OCR_CONTAINER"
[ -n "${OCR_BLOB_PREFIX:-}" ] && export OCR_Storage__AzureBlob__BlobPrefix="$OCR_BLOB_PREFIX"

echo "[INFO] Settings : $ENV_FILE"
echo "[INFO] Endpoint : http://localhost:$PORT"

cd "$APP_DIR"

if [ "${1:-}" = "--background" ]; then
    nohup dotnet OcrAgent.Mcp.dll > /tmp/mcp_blob.log 2>&1 &
    echo "[OK] Started in background (pid $!). Log: /tmp/mcp_blob.log"
else
    exec dotnet OcrAgent.Mcp.dll
fi
