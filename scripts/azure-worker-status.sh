#!/usr/bin/env bash
#
# Shows the queue worker image, ingress, scale, and active revisions without
# changing any Azure resource.
#
# Usage:
#   scripts/azure-worker-status.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

load_env
apply_azure_defaults
require_az

if ! az containerapp show --name "$AZURE_WORKER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[INFO] Worker '$AZURE_WORKER_APP_NAME' is not deployed."
    exit 0
fi

IMAGE="$(az containerapp show \
    --name "$AZURE_WORKER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "properties.template.containers[0].image" -o tsv)"
INGRESS="$(az containerapp show \
    --name "$AZURE_WORKER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query properties.configuration.ingress.fqdn -o tsv 2>/dev/null || true)"
RANGE="$(worker_replica_range | tr '\t' '/')"
ACTIVE="$(worker_active_revisions)"

echo "Worker:           $AZURE_WORKER_APP_NAME"
echo "Image:            $IMAGE"
echo "Ingress:          ${INGRESS:-none}"
echo "Replicas min/max: ${RANGE:-unknown}"
if [ -n "$ACTIVE" ]; then
    echo "Active revisions:"
    printf '%s\n' "$ACTIVE" | sed 's/^/  /'
else
    echo "Active revisions: none (compute stopped)"
fi
