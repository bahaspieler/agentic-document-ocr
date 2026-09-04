#!/usr/bin/env bash
#
# Creates or updates the no-ingress queue worker in the existing Container Apps
# environment. It reuses the exact image assigned to the serving app and never
# creates or deletes the registry, environment, resource group, or storage.
#
# Usage:
#   scripts/azure-worker-deploy.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

for arg in "$@"; do
    case "$arg" in
        -h|--help)
            sed -n '2,9p' "$0" | sed 's/^# \?//'
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

WORKER_STORAGE_CONNECTION_STRING="${COURSE_STORAGE_CONNECTION_STRING:-${AZURE_STORAGE_CONNECTION_STRING:-}}"
if [ -z "$WORKER_STORAGE_CONNECTION_STRING" ]; then
    echo "[ERROR] COURSE_STORAGE_CONNECTION_STRING and AZURE_STORAGE_CONNECTION_STRING are empty." >&2
    exit 1
fi

if ! az acr show --name "$AZURE_ACR_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Existing registry '$AZURE_ACR_NAME' was not found." >&2
    echo "        Deploy the serving app first with scripts/azure-deploy.sh." >&2
    exit 1
fi
if ! az containerapp env show --name "$AZURE_CONTAINER_APP_ENV" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Existing environment '$AZURE_CONTAINER_APP_ENV' was not found." >&2
    echo "        Deploy the serving app first with scripts/azure-deploy.sh." >&2
    exit 1
fi
if ! az containerapp show --name "$AZURE_CONTAINER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Serving app '$AZURE_CONTAINER_APP_NAME' was not found." >&2
    echo "        It supplies the shared image; deploy it first." >&2
    exit 1
fi

ACR_LOGIN_SERVER="$(az acr show \
    --name "$AZURE_ACR_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query loginServer -o tsv)"
ACR_USERNAME="$(az acr credential show --name "$AZURE_ACR_NAME" --query username -o tsv)"
ACR_PASSWORD="$(az acr credential show --name "$AZURE_ACR_NAME" --query passwords[0].value -o tsv)"
IMAGE="$(az containerapp show \
    --name "$AZURE_CONTAINER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "properties.template.containers[0].image" -o tsv)"

if [ -z "$IMAGE" ] || [[ "$IMAGE" != "$ACR_LOGIN_SERVER/"* ]]; then
    echo "[ERROR] Serving app image '$IMAGE' is not from $ACR_LOGIN_SERVER." >&2
    exit 1
fi

SECRET_ARGS=(
    course-storage-cs="$WORKER_STORAGE_CONNECTION_STRING"
)
if [ -n "${OPENAI_API_KEY:-}" ]; then
    SECRET_ARGS+=(openai-key="$OPENAI_API_KEY")
else
    echo "[WARN] OPENAI_API_KEY is empty; agent jobs will write handled failure rows."
fi
ENV_VARS=()
while IFS= read -r line; do
    ENV_VARS+=("$line")
done <<EOF
$(worker_env_vars)
EOF

echo "[INFO] Worker       : $AZURE_WORKER_APP_NAME"
echo "[INFO] Environment  : $AZURE_CONTAINER_APP_ENV (existing)"
echo "[INFO] Image        : $IMAGE (same as $AZURE_CONTAINER_APP_NAME)"

if ! az containerapp show --name "$AZURE_WORKER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[INFO] Creating no-ingress worker"
    az containerapp create \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --environment "$AZURE_CONTAINER_APP_ENV" \
        --image "$IMAGE" \
        --command /app/entrypoint.sh \
        --args worker \
        --cpu 1.0 \
        --memory 2.0Gi \
        --min-replicas 1 \
        --max-replicas 1 \
        --registry-server "$ACR_LOGIN_SERVER" \
        --registry-username "$ACR_USERNAME" \
        --registry-password "$ACR_PASSWORD" \
        --secrets "${SECRET_ARGS[@]}" \
        --env-vars "${ENV_VARS[@]}" \
        --output none
else
    echo "[INFO] Updating worker"
    az containerapp secret set \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --secrets "${SECRET_ARGS[@]}" \
        --output none
    az containerapp registry set \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --server "$ACR_LOGIN_SERVER" \
        --username "$ACR_USERNAME" \
        --password "$ACR_PASSWORD" \
        --output none
    az containerapp update \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --image "$IMAGE" \
        --command /app/entrypoint.sh \
        --args worker \
        --cpu 1.0 \
        --memory 2.0Gi \
        --min-replicas 1 \
        --max-replicas 1 \
        --set-env-vars "${ENV_VARS[@]}" \
        --output none

    if [ "$(az containerapp show \
            --name "$AZURE_WORKER_APP_NAME" \
            --resource-group "$AZURE_RESOURCE_GROUP" \
            --query properties.configuration.ingress.external -o tsv)" != "" ]; then
        echo "[INFO] Disabling pre-existing ingress"
        az containerapp ingress disable \
            --name "$AZURE_WORKER_APP_NAME" \
            --resource-group "$AZURE_RESOURCE_GROUP" \
            --output none
    fi
fi

echo "[INFO] Enforcing single-revision mode for exactly one queue consumer"
az containerapp revision set-mode \
    --name "$AZURE_WORKER_APP_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --mode single \
    --output none

echo "[OK] Worker deployed with min/max replicas 1/1 and no ingress."
echo "     Status: scripts/azure-worker-status.sh"
echo "     Stop:   scripts/azure-worker-stop.sh"
