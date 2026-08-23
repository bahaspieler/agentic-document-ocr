#!/usr/bin/env bash
#
# Starts only ca-ocr-worker after azure-worker-stop.sh. Shared Azure resources
# and the serving app are not changed.
#
# Usage:
#   scripts/azure-worker-start.sh
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

LATEST="$(az containerapp show \
    --name "$AZURE_WORKER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query properties.latestRevisionName -o tsv)"
if [ -z "$LATEST" ]; then
    echo "[ERROR] Worker has no revision to start." >&2
    exit 1
fi

if worker_active_revisions | grep -Fxq "$LATEST"; then
    echo "[INFO] Worker revision $LATEST is already active"
else
    echo "[INFO] Activating worker revision $LATEST"
    az containerapp revision activate \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --revision "$LATEST" \
        --output none
fi

az containerapp update \
    --name "$AZURE_WORKER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --min-replicas 1 \
    --max-replicas 1 \
    --output none

echo "[OK] Worker revision $LATEST is active with min/max replicas 1/1."
