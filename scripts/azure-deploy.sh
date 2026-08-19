#!/usr/bin/env bash
#
# Creates or updates the Azure Container Apps serving instance.
#
# Idempotent: re-running after a code change builds a new uniquely tagged image and
# rolls a new revision. Never creates a worker instance; that role is a placeholder
# until the queue path lands.
#
# Usage:
#   scripts/azure-deploy.sh
#   scripts/azure-deploy.sh --skip-build   # reuse the image already in the registry
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

SKIP_BUILD=0
for arg in "$@"; do
    case "$arg" in
        --skip-build) SKIP_BUILD=1 ;;
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

if [ -z "${AZURE_STORAGE_CONNECTION_STRING:-}" ]; then
    echo "[ERROR] AZURE_STORAGE_CONNECTION_STRING is empty." >&2
    exit 1
fi

if ! az group show --name "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Resource group '$AZURE_RESOURCE_GROUP' was not found." >&2
    echo "        The scripts never create or delete the group; it is bound to the team." >&2
    exit 1
fi

LOCATION="$(deployment_location)"
echo "[INFO] Subscription : $(az account show --query name -o tsv)"
echo "[INFO] Group        : $AZURE_RESOURCE_GROUP ($(resource_group_location))"
echo "[INFO] Location     : $LOCATION (where the storage account is)"
echo "[INFO] Registry     : $AZURE_ACR_NAME"
echo "[INFO] Environment  : $AZURE_CONTAINER_APP_ENV"
echo "[INFO] App          : $AZURE_CONTAINER_APP_NAME"

register_provider() {
    local ns="$1"
    local state
    state="$(az provider show --namespace "$ns" --query registrationState -o tsv 2>/dev/null || echo NotRegistered)"
    if [ "$state" = "Registered" ]; then
        return 0
    fi
    echo "[INFO] Registering provider $ns (currently $state)"
    az provider register --namespace "$ns" --wait >/dev/null
    state="$(az provider show --namespace "$ns" --query registrationState -o tsv)"
    if [ "$state" != "Registered" ]; then
        echo "[ERROR] Provider $ns did not reach Registered (state: $state)" >&2
        exit 1
    fi
}

echo "[INFO] Checking Azure providers and the containerapp extension"
register_provider Microsoft.App
register_provider Microsoft.OperationalInsights
register_provider Microsoft.ContainerRegistry
az extension add --name containerapp --upgrade --yes >/dev/null

if ! az acr show --name "$AZURE_ACR_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[INFO] Creating container registry $AZURE_ACR_NAME"
    az acr create \
        --name "$AZURE_ACR_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --location "$LOCATION" \
        --sku Basic \
        --admin-enabled true \
        --output none
else
    echo "[INFO] Registry already exists"
    az acr update --name "$AZURE_ACR_NAME" --resource-group "$AZURE_RESOURCE_GROUP" --admin-enabled true --output none
fi

if ! az containerapp env show --name "$AZURE_CONTAINER_APP_ENV" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[INFO] Creating Container Apps environment $AZURE_CONTAINER_APP_ENV"
    az containerapp env create \
        --name "$AZURE_CONTAINER_APP_ENV" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --location "$LOCATION" \
        --output none
else
    ENV_LOCATION="$(az containerapp env show --name "$AZURE_CONTAINER_APP_ENV" --resource-group "$AZURE_RESOURCE_GROUP" --query location -o tsv)"
    echo "[INFO] Environment already exists ($ENV_LOCATION)"
    if ! same_region "$ENV_LOCATION" "$LOCATION"; then
        echo "[WARN] The environment is in $ENV_LOCATION and the storage account is in $LOCATION."
        echo "[WARN] Every image the workflow reads and writes crosses regions. An environment"
        echo "[WARN] cannot be moved: scripts/azure-teardown.sh, then deploy again, to co-locate."
        echo "[WARN] The hostname changes when you do."
    fi
fi

ACR_LOGIN_SERVER="$(az acr show --name "$AZURE_ACR_NAME" --resource-group "$AZURE_RESOURCE_GROUP" --query loginServer -o tsv)"
ACR_USERNAME="$(az acr credential show --name "$AZURE_ACR_NAME" --query username -o tsv)"
ACR_PASSWORD="$(az acr credential show --name "$AZURE_ACR_NAME" --query passwords[0].value -o tsv)"

if [ "$SKIP_BUILD" -eq 1 ]; then
    # Every build also moves the 'latest' tag, so it is usually the most recent one and would be
    # picked here. Deploying by that name defeats the point of the unique tag: the image string
    # would not change between revisions, and Container Apps would have nothing to roll.
    TAG="$(az acr repository show-tags --name "$AZURE_ACR_NAME" --repository "$AZURE_IMAGE_NAME" --orderby time_desc -o tsv 2>/dev/null \
        | grep -vx 'latest' \
        | head -n 1 || true)"
    if [ -z "$TAG" ]; then
        echo "[ERROR] --skip-build was set but $AZURE_ACR_NAME has no $AZURE_IMAGE_NAME tags yet." >&2
        exit 1
    fi
    echo "[INFO] Reusing image tag $TAG"
else
    TAG="$(date -u +%Y%m%d%H%M%S)"
    if git -C "$ROOT" rev-parse --short HEAD >/dev/null 2>&1; then
        TAG="$(git -C "$ROOT" rev-parse --short HEAD)-$TAG"
    fi
    echo "[INFO] Building $AZURE_IMAGE_NAME:$TAG on linux/amd64 in ACR"
    az acr build \
        --registry "$AZURE_ACR_NAME" \
        --image "${AZURE_IMAGE_NAME}:${TAG}" \
        --image "${AZURE_IMAGE_NAME}:latest" \
        --platform linux/amd64 \
        --file Dockerfile \
        "$ROOT"
fi

IMAGE="${ACR_LOGIN_SERVER}/${AZURE_IMAGE_NAME}:${TAG}"
echo "[INFO] Image        : $IMAGE"

SECRET_ARGS=(storage-cs="$AZURE_STORAGE_CONNECTION_STRING")
if [ -n "${OPENAI_API_KEY:-}" ]; then
    SECRET_ARGS+=(openai-key="$OPENAI_API_KEY")
fi

ENV_VARS=()
while IFS= read -r line; do
    ENV_VARS+=("$line")
done <<EOF
$(container_env_vars)
EOF

if ! az containerapp show --name "$AZURE_CONTAINER_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[INFO] Creating container app $AZURE_CONTAINER_APP_NAME"
    az containerapp create \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --environment "$AZURE_CONTAINER_APP_ENV" \
        --image "$IMAGE" \
        --target-port 8080 \
        --ingress external \
        --cpu 1.0 \
        --memory 2.0Gi \
        --min-replicas 0 \
        --max-replicas 1 \
        --registry-server "$ACR_LOGIN_SERVER" \
        --registry-username "$ACR_USERNAME" \
        --registry-password "$ACR_PASSWORD" \
        --secrets "${SECRET_ARGS[@]}" \
        --env-vars "${ENV_VARS[@]}" \
        --output none
else
    echo "[INFO] Updating container app $AZURE_CONTAINER_APP_NAME"
    az containerapp secret set \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --secrets "${SECRET_ARGS[@]}" \
        --output none
    az containerapp registry set \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --server "$ACR_LOGIN_SERVER" \
        --username "$ACR_USERNAME" \
        --password "$ACR_PASSWORD" \
        --output none
    az containerapp update \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --image "$IMAGE" \
        --cpu 1.0 \
        --memory 2.0Gi \
        --min-replicas 0 \
        --max-replicas 1 \
        --set-env-vars "${ENV_VARS[@]}" \
        --output none
fi

FQDN="$(app_fqdn)"
if [ -z "$FQDN" ]; then
    echo "[ERROR] Deployed, but no FQDN was returned." >&2
    exit 1
fi

echo
echo "[OK] Serving instance is at https://$FQDN/"
echo "     MCP clients:  https://$FQDN/"
echo "     Verify:       scripts/verify-deployment.sh"
echo "     Stop compute: scripts/azure-stop.sh"
