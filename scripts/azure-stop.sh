#!/usr/bin/env bash
#
# Stops compute on the serving instance without deleting it. Container Apps
# rejects max-replicas 0, so this deactivates the active revision instead:
# the app, its URL, and its configuration stay; no replica is billed.
#
# Usage:
#   scripts/azure-stop.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

load_env
apply_azure_defaults
require_az

if ! az containerapp show --name "$AZURE_CONTAINER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Container app '$AZURE_CONTAINER_APP_NAME' was not found in $AZURE_RESOURCE_GROUP." >&2
    echo "        Deploy first with scripts/azure-deploy.sh" >&2
    exit 1
fi

ACTIVE="$(az containerapp revision list \
    --name "$AZURE_CONTAINER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[?properties.active].name" \
    -o tsv)"

if [ -z "$ACTIVE" ]; then
    echo "[OK] No active revision; compute is already stopped."
else
    echo "[INFO] Deactivating revision(s):"
    echo "$ACTIVE" | while IFS= read -r revision; do
        [ -z "$revision" ] && continue
        echo "      $revision"
        az containerapp revision deactivate \
            --name "$AZURE_CONTAINER_APP_NAME" \
            --resource-group "$AZURE_RESOURCE_GROUP" \
            --revision "$revision" \
            --output none
    done
    echo "[OK] Compute stopped."
fi

FQDN="$(app_fqdn)"
[ -n "$FQDN" ] && echo "     URL still reserved: https://$FQDN/"
echo "     Start again with: scripts/azure-start.sh"
