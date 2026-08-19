#!/usr/bin/env bash
#
# Deletes only the Azure resources this phase created: the container app, its
# environment, and the container registry. Never deletes the resource group and
# never touches the storage account.
#
# Usage:
#   scripts/azure-teardown.sh
#   scripts/azure-teardown.sh --yes              # skip the confirmation prompt
#   scripts/azure-teardown.sh --include-logs     # also delete the Log Analytics workspace the environment created
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

CONFIRM=1
INCLUDE_LOGS=0
for arg in "$@"; do
    case "$arg" in
        --yes|-y) CONFIRM=0 ;;
        --include-logs) INCLUDE_LOGS=1 ;;
        -h|--help)
            sed -n '2,14p' "$0" | sed 's/^# \?//'
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

if ! az group show --name "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Resource group '$AZURE_RESOURCE_GROUP' was not found." >&2
    exit 1
fi

APP_EXISTS=0
ENV_EXISTS=0
ACR_EXISTS=0
az containerapp show --name "$AZURE_CONTAINER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1 && APP_EXISTS=1
az containerapp env show --name "$AZURE_CONTAINER_APP_ENV" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1 && ENV_EXISTS=1
az acr show --name "$AZURE_ACR_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1 && ACR_EXISTS=1

WORKSPACE_NAME=""
WORKSPACE_CUSTOMER_ID=""
if [ "$ENV_EXISTS" -eq 1 ]; then
    WORKSPACE_CUSTOMER_ID="$(az containerapp env show \
        --name "$AZURE_CONTAINER_APP_ENV" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --query properties.appLogsConfiguration.logAnalyticsConfiguration.customerId \
        -o tsv 2>/dev/null || true)"
    if [ -n "$WORKSPACE_CUSTOMER_ID" ]; then
        WORKSPACE_NAME="$(az monitor log-analytics workspace list \
            --resource-group "$AZURE_RESOURCE_GROUP" \
            --query "[?customerId=='$WORKSPACE_CUSTOMER_ID'].name | [0]" \
            -o tsv 2>/dev/null || true)"
    fi
fi

echo "Will delete these resources from $AZURE_RESOURCE_GROUP:"
if [ "$APP_EXISTS" -eq 1 ]; then
    echo "  container app : $AZURE_CONTAINER_APP_NAME"
else
    echo "  container app : $AZURE_CONTAINER_APP_NAME (not present)"
fi
if [ "$ENV_EXISTS" -eq 1 ]; then
    echo "  environment   : $AZURE_CONTAINER_APP_ENV"
else
    echo "  environment   : $AZURE_CONTAINER_APP_ENV (not present)"
fi
if [ "$ACR_EXISTS" -eq 1 ]; then
    echo "  registry      : $AZURE_ACR_NAME"
else
    echo "  registry      : $AZURE_ACR_NAME (not present)"
fi
if [ "$INCLUDE_LOGS" -eq 1 ]; then
    if [ -n "$WORKSPACE_NAME" ]; then
        echo "  log workspace : $WORKSPACE_NAME"
    else
        # The workspace is only reachable through the environment that created it, so once the
        # environment is gone there is nothing left to match it against by name.
        echo "  log workspace : none found. If a previous run deleted the environment on its own,"
        echo "                  its workspace is still in the group and has to go by hand:"
        echo "                  az monitor log-analytics workspace list -g $AZURE_RESOURCE_GROUP -o table"
    fi
elif [ -n "$WORKSPACE_NAME" ]; then
    echo "  log workspace : $WORKSPACE_NAME (kept; pass --include-logs to delete)"
fi
echo
echo "Will NOT delete:"
echo "  resource group : $AZURE_RESOURCE_GROUP"
echo "  storage account (or any blob/table/queue inside it)"
echo

if [ "$CONFIRM" -eq 1 ]; then
    printf "Type yes to continue: "
    read -r answer
    if [ "$answer" != "yes" ]; then
        echo "Aborted."
        exit 1
    fi
fi

if [ "$APP_EXISTS" -eq 1 ]; then
    echo "[INFO] Deleting container app $AZURE_CONTAINER_APP_NAME"
    az containerapp delete \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --yes \
        --output none
fi

if [ "$ENV_EXISTS" -eq 1 ]; then
    # Deleting an environment takes several minutes, and the CLI spends them holding one HTTP
    # request open. A connection reset there kills this script with a Python traceback while the
    # deletion carries on regardless, which leaves the registry standing and the group half torn
    # down. Ask for the delete, then poll for the result over short requests instead.
    echo "[INFO] Deleting environment $AZURE_CONTAINER_APP_ENV (several minutes)"
    az containerapp env delete \
        --name "$AZURE_CONTAINER_APP_ENV" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --yes \
        --no-wait \
        --output none

    printf '      waiting'
    for _ in $(seq 1 80); do
        if ! az containerapp env show --name "$AZURE_CONTAINER_APP_ENV" \
                --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
            break
        fi
        printf '.'
        sleep 15
    done
    printf '\n'

    if az containerapp env show --name "$AZURE_CONTAINER_APP_ENV" \
            --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
        echo "[ERROR] Environment is still there after 20 minutes. Re-run this script once it clears;" >&2
        echo "        everything it does is safe to repeat." >&2
        exit 1
    fi
fi

if [ "$ACR_EXISTS" -eq 1 ]; then
    echo "[INFO] Deleting registry $AZURE_ACR_NAME"
    az acr delete \
        --name "$AZURE_ACR_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --yes \
        --output none
fi

if [ "$INCLUDE_LOGS" -eq 1 ] && [ -n "$WORKSPACE_NAME" ]; then
    echo "[INFO] Deleting Log Analytics workspace $WORKSPACE_NAME"
    az monitor log-analytics workspace delete \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --workspace-name "$WORKSPACE_NAME" \
        --yes \
        --force true \
        --output none
fi

echo "[OK] Named resources removed. Resource group $AZURE_RESOURCE_GROUP is untouched."
