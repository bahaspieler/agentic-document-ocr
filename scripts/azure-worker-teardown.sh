#!/usr/bin/env bash
#
# Deletes only ca-ocr-worker. It never deletes the serving app, Container Apps
# environment, registry, resource group, storage account, or storage contents.
#
# Usage:
#   scripts/azure-worker-teardown.sh
#   scripts/azure-worker-teardown.sh --yes
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

CONFIRM=1
for arg in "$@"; do
    case "$arg" in
        --yes|-y) CONFIRM=0 ;;
        -h|--help)
            sed -n '2,10p' "$0" | sed 's/^# \?//'
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

if ! az containerapp show --name "$AZURE_WORKER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[OK] Worker '$AZURE_WORKER_APP_NAME' is already absent."
    exit 0
fi

echo "Will delete only:"
echo "  worker app : $AZURE_WORKER_APP_NAME"
echo
echo "Will NOT delete or change:"
echo "  serving app  : $AZURE_CONTAINER_APP_NAME"
echo "  environment  : $AZURE_CONTAINER_APP_ENV"
echo "  registry     : $AZURE_ACR_NAME"
echo "  resource group, storage account, and all storage contents"
echo

if [ "$CONFIRM" -eq 1 ]; then
    printf "Type yes to continue: "
    read -r answer
    if [ "$answer" != "yes" ]; then
        echo "Aborted."
        exit 1
    fi
fi

az containerapp delete \
    --name "$AZURE_WORKER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --yes \
    --output none

echo "[OK] Worker removed. All shared resources and the serving app are untouched."
