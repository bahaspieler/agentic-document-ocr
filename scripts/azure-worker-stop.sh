#!/usr/bin/env bash
#
# Stops compute only for ca-ocr-worker by deactivating its revisions. The worker
# configuration and every shared Azure resource remain in place.
#
# Usage:
#   scripts/azure-worker-stop.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

load_env
apply_azure_defaults
require_az

if ! az containerapp show --name "$AZURE_WORKER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Worker '$AZURE_WORKER_APP_NAME' was not found." >&2
    echo "        Deploy it with scripts/azure-worker-deploy.sh." >&2
    exit 1
fi

ACTIVE="$(worker_active_revisions)"
if [ -z "$ACTIVE" ]; then
    echo "[OK] Worker compute is already stopped."
    exit 0
fi

echo "[INFO] Deactivating worker revision(s):"
while IFS= read -r revision; do
    [ -z "$revision" ] && continue
    echo "      $revision"
    az containerapp revision deactivate \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --revision "$revision" \
        --output none
done <<< "$ACTIVE"

echo "[OK] Worker compute stopped. Shared resources and serving app are untouched."
echo "     Start again with: scripts/azure-worker-start.sh"
