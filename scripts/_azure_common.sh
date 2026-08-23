#!/usr/bin/env bash
#
# Shared helpers for the Azure scripts. Sourced, not executed.
# Reads the project-root .env the same way the applications do: existing environment
# variables win, so a caller can override a single key without editing the file.
#

_AZURE_COMMON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$_AZURE_COMMON_DIR/.." && pwd)"
ENV_FILE="$ROOT/.env"

load_env() {
    if [ ! -f "$ENV_FILE" ]; then
        echo "[ERROR] No .env file at $ENV_FILE" >&2
        echo "        cp .env.example .env, then fill in AZURE_STORAGE_CONNECTION_STRING." >&2
        exit 1
    fi

    while IFS= read -r line || [ -n "$line" ]; do
        local trimmed="${line#"${line%%[![:space:]]*}"}"
        trimmed="${trimmed%"${trimmed##*[![:space:]]}"}"
        [ -z "$trimmed" ] && continue
        case "$trimmed" in
            \#*) continue ;;
        esac

        local key="${trimmed%%=*}"
        local value="${trimmed#*=}"
        key="${key%"${key##*[![:space:]]}"}"
        key="${key#"${key%%[![:space:]]*}"}"
        value="${value#"${value%%[![:space:]]*}"}"
        value="${value%"${value##*[![:space:]]}"}"

        if [ "${#value}" -ge 2 ]; then
            local first="${value:0:1}"
            local last="${value:$((${#value} - 1)):1}"
            if { [ "$first" = '"' ] && [ "$last" = '"' ]; } || { [ "$first" = "'" ] && [ "$last" = "'" ]; }; then
                value="${value:1:$((${#value} - 2))}"
            fi
        fi

        [ -z "$key" ] && continue
        if [ -z "$(printenv "$key" 2>/dev/null || true)" ]; then
            export "$key=$value"
        fi
    done < "$ENV_FILE"
}

require_az() {
    if ! command -v az >/dev/null 2>&1; then
        echo "[ERROR] Azure CLI (az) is not installed." >&2
        exit 1
    fi
    if ! az account show >/dev/null 2>&1; then
        echo "[ERROR] Not logged in to Azure. Run: az login" >&2
        exit 1
    fi
}

# Resource names. Overridable from .env; defaults match the team's existing group.
apply_azure_defaults() {
    # The MCP server serves every request statelessly, while REST conversation state lives in SQL,
    # so any replica can answer any request.
    : "${AZURE_MAX_REPLICAS:=3}"
    : "${AZURE_RESOURCE_GROUP:=RG_Hidden_Layer}"
    : "${AZURE_ACR_NAME:=acrhiddenlayerocr}"
    : "${AZURE_CONTAINER_APP_NAME:=ca-ocr-agent}"
    : "${AZURE_WORKER_APP_NAME:=ca-ocr-worker}"
    : "${AZURE_CONTAINER_APP_ENV:=cae-ocr-agent}"
    : "${AZURE_IMAGE_NAME:=ocr-agent}"
    : "${AZURE_SQL_SERVER_NAME:=sql-hidden-layer-ocr}"
    : "${AZURE_SQL_DATABASE_NAME:=ocr-agent}"
    : "${AZURE_SQL_ADMIN_USER:=ocradmin}"
    : "${OCR_Storage__AzureBlob__ContainerName:=ocr-images}"
    # Keep the deployed run store independent from a developer's local OCR_Runs__Provider=none.
    : "${AZURE_OCR_RUNS_PROVIDER:=azuretable}"
    : "${OCR_Runs__TableName:=OcrRuns}"
    : "${OCR_Runs__GroupId:=HiddenLayer}"
    : "${MyConfig__TrainingContainer:=training-files}"
    : "${MyConfig__ResultContainer:=result-files}"
    : "${MyConfig__ResultTable:=results}"
    : "${MyConfig__Queue:=trigger-queue}"
    : "${MyConfig__GroupId:=HiddenLayer}"
    : "${MyConfig__MessageVisibilityTimeoutSeconds:=900}"
}

resource_group_location() {
    az group show --name "$AZURE_RESOURCE_GROUP" --query location -o tsv
}

# Account name out of the connection string, so .env stays the only copy of the fact.
storage_account_name() {
    printf '%s' "${AZURE_STORAGE_CONNECTION_STRING:-}" \
        | tr ';' '\n' \
        | sed -n 's/^AccountName=//p' \
        | head -n 1
}

# Compute belongs beside the data. Every workflow step reads an image and writes another, so a
# container in a different region pays a round trip on each one and egress on top. A resource
# group's own location is only the default for new resources and says nothing about where the
# storage account actually is; ours are not the same region.
deployment_location() {
    local account location
    account="$(storage_account_name)"
    if [ -n "$account" ]; then
        location="$(az storage account show --name "$account" --query location -o tsv 2>/dev/null || true)"
        if [ -n "$location" ]; then
            printf '%s\n' "$location"
            return
        fi
    fi
    resource_group_location
}

# Regions come back as 'eastus' from one command and 'East US' from another.
same_region() {
    local a b
    a="$(printf '%s' "$1" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
    b="$(printf '%s' "$2" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
    [ "$a" = "$b" ]
}

blob_name() {
    local file_name="$1"
    local prefix="${OCR_Storage__AzureBlob__BlobPrefix:-}"
    prefix="${prefix#/}"
    prefix="${prefix%/}"
    if [ -n "$prefix" ]; then
        printf '%s/%s\n' "$prefix" "$file_name"
    else
        printf '%s\n' "$file_name"
    fi
}

app_fqdn() {
    az containerapp show \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --query properties.configuration.ingress.fqdn \
        -o tsv 2>/dev/null || true
}

app_url() {
    local fqdn
    fqdn="$(app_fqdn)"
    [ -n "$fqdn" ] && printf 'https://%s/\n' "$fqdn"
}

replica_range() {
    az containerapp show \
        --name "$AZURE_CONTAINER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --query "{min:properties.template.scale.minReplicas, max:properties.template.scale.maxReplicas}" \
        -o tsv 2>/dev/null || true
}

worker_replica_range() {
    az containerapp show \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --query "{min:properties.template.scale.minReplicas, max:properties.template.scale.maxReplicas}" \
        -o tsv 2>/dev/null || true
}

worker_active_revisions() {
    az containerapp revision list \
        --name "$AZURE_WORKER_APP_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --query "[?properties.active].name" \
        -o tsv
}

# Environment variables applied to the container. The connection string (and the
# model key, when present) are secret references rather than plaintext values.
container_env_vars() {
    local vars=(
        "AZURE_STORAGE_CONNECTION_STRING=secretref:storage-cs"
        "OCR_Storage__Provider=azureblob"
        "OCR_Storage__AzureBlob__ContainerName=${OCR_Storage__AzureBlob__ContainerName}"
        "OCR_Runs__Provider=${AZURE_OCR_RUNS_PROVIDER}"
        "OCR_Runs__TableName=${OCR_Runs__TableName}"
        "OCR_Runs__GroupId=${OCR_Runs__GroupId}"
    )
    if [ -n "${OCR_Sql__ConnectionString:-}" ]; then
        vars+=(
            "OCR_Sql__Enabled=true"
            "OCR_Sql__ConnectionString=secretref:sql-cs"
        )
    fi
    if [ -n "${OCR_Auth__ClientId:-}" ]; then
        vars+=(
            "OCR_Auth__Enabled=true"
            "OCR_Auth__ClientId=${OCR_Auth__ClientId}"
            "OCR_Auth__TenantId=${OCR_Auth__TenantId:-common}"
            "OCR_Auth__Instance=${OCR_Auth__Instance:-https://login.microsoftonline.com/}"
        )
    fi
    if [ -n "${OCR_Storage__AzureBlob__BlobPrefix:-}" ]; then
        vars+=("OCR_Storage__AzureBlob__BlobPrefix=${OCR_Storage__AzureBlob__BlobPrefix}")
    fi
    if [ -n "${OPENAI_API_KEY:-}" ]; then
        vars+=("OPENAI_API_KEY=secretref:openai-key")
    fi
    printf '%s\n' "${vars[@]}"
}

# The worker intentionally receives no serving-only SQL or authentication settings.
# Both consumers use the same storage and model secrets, while MyCloudProject binds
# its course-template settings from the unprefixed MyConfig section.
worker_env_vars() {
    local vars=(
        "MyConfig__StorageConnectionString=secretref:course-storage-cs"
        "MyConfig__TrainingContainer=${MyConfig__TrainingContainer}"
        "MyConfig__ResultContainer=${MyConfig__ResultContainer}"
        "MyConfig__ResultTable=${MyConfig__ResultTable}"
        "MyConfig__Queue=${MyConfig__Queue}"
        "MyConfig__GroupId=${MyConfig__GroupId}"
        "MyConfig__WorkRoot=/var/ocr/worker"
        "MyConfig__MessageVisibilityTimeoutSeconds=${MyConfig__MessageVisibilityTimeoutSeconds:-900}"
        "MyConfig__EnsureStorageResources=true"
        "OCR_Storage__Provider=local"
        "OCR_Runs__Provider=none"
    )
    if [ -n "${OPENAI_API_KEY:-}" ]; then
        vars+=("OPENAI_API_KEY=secretref:openai-key")
    fi
    if [ -n "${OPENAI_CHATCOMPLETION_DEPLOYMENT:-}" ]; then
        vars+=("OPENAI_CHATCOMPLETION_DEPLOYMENT=${OPENAI_CHATCOMPLETION_DEPLOYMENT}")
    fi
    printf '%s\n' "${vars[@]}"
}
