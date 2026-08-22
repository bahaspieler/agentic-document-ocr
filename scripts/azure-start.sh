#!/usr/bin/env bash
#
# Restores the serving instance after azure-stop.sh by activating the latest
# revision and setting the replica range.
#
# Usage:
#   scripts/azure-start.sh
#
# The maximum comes from AZURE_MAX_REPLICAS and is above one: the server holds no per-client state
# between requests, so scaling out needs no session affinity.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

for arg in "$@"; do
    case "$arg" in
        -h|--help)
            sed -n '2,12p' "$0" | sed 's/^# \?//'
            exit 0
            ;;
        *)
            echo "Unknown argument: $arg" >&2
            exit 2
            ;;
    esac
done

load_env
apply_azure_defaults
require_az

if ! az containerapp show --name "$AZURE_CONTAINER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Container app '$AZURE_CONTAINER_APP_NAME' was not found in $AZURE_RESOURCE_GROUP." >&2
    echo "        Deploy first with scripts/azure-deploy.sh" >&2
    exit 1
fi

LATEST="$(az containerapp show \
    --name "$AZURE_CONTAINER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query properties.latestRevisionName \
    -o tsv)"

if [ -z "$LATEST" ]; then
    echo "[ERROR] No revisions found. Deploy first with scripts/azure-deploy.sh" >&2
    exit 1
fi

ACTIVE="$(az containerapp revision list \
    --name "$AZURE_CONTAINER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[?properties.active].name" \
    -o tsv)"

if echo "$ACTIVE" | grep -Fxq "$LATEST"; then
    echo "[INFO] Revision $LATEST is already active"
else
    echo "[INFO] Activating revision $LATEST"
    az containerapp revision activate \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --revision "$LATEST" \
        --output none
fi

MIN=1
MAX="$AZURE_MAX_REPLICAS"

echo "[INFO] Scaling $AZURE_CONTAINER_APP_NAME to min=$MIN max=$MAX"
az containerapp update \
    --name "$AZURE_CONTAINER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --min-replicas "$MIN" \
    --max-replicas "$MAX" \
    --output none

RANGE="$(replica_range | tr '\t' '/')"
echo "[OK] Revision $LATEST active. Replicas min/max: ${RANGE:-$MIN/$MAX}"
FQDN="$(app_fqdn)"
if [ -n "$FQDN" ]; then
    echo "     Endpoint: https://$FQDN/"
fi
echo "     A replica will stay running until you call scripts/azure-stop.sh"
