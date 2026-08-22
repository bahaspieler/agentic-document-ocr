#!/usr/bin/env bash
#
# Creates the Phase 7 Azure SQL server and Basic database.
# The resource group is never created or deleted by this script.
#
# Usage:
#   scripts/azure-sql.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

load_env
apply_azure_defaults
require_az

if [ -z "${AZURE_SQL_ADMIN_PASSWORD:-}" ]; then
    # The generated value is never printed. It and the resulting connection string are written only
    # to the gitignored .env used by the deployment scripts.
    AZURE_SQL_ADMIN_PASSWORD="$(openssl rand -hex 24)Aa1!"
fi

if ! az group show --name "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[ERROR] Resource group '$AZURE_RESOURCE_GROUP' was not found." >&2
    exit 1
fi

LOCATION="$(deployment_location)"

echo "[INFO] Group       : $AZURE_RESOURCE_GROUP"
echo "[INFO] Location    : $LOCATION"
echo "[INFO] SQL server  : $AZURE_SQL_SERVER_NAME"
echo "[INFO] Database    : $AZURE_SQL_DATABASE_NAME (Basic)"

if ! az sql server show \
        --name "$AZURE_SQL_SERVER_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[INFO] Creating Azure SQL logical server"
    az sql server create \
        --name "$AZURE_SQL_SERVER_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --location "$LOCATION" \
        --admin-user "$AZURE_SQL_ADMIN_USER" \
        --admin-password "$AZURE_SQL_ADMIN_PASSWORD" \
        --output none
else
    echo "[INFO] SQL server already exists"
fi

# The Container App reaches SQL through Azure's public service endpoint. This rule does not expose
# SQL to arbitrary internet addresses; it permits connections originating from Azure resources.
az sql server firewall-rule create \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --server "$AZURE_SQL_SERVER_NAME" \
    --name AllowAzureServices \
    --start-ip-address 0.0.0.0 \
    --end-ip-address 0.0.0.0 \
    --output none

if ! az sql db show \
        --name "$AZURE_SQL_DATABASE_NAME" \
        --server "$AZURE_SQL_SERVER_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" >/dev/null 2>&1; then
    echo "[INFO] Creating Basic database"
    az sql db create \
        --name "$AZURE_SQL_DATABASE_NAME" \
        --server "$AZURE_SQL_SERVER_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --service-objective Basic \
        --backup-storage-redundancy Local \
        --output none
else
    echo "[INFO] Database already exists"
fi

CONNECTION_STRING="Server=tcp:${AZURE_SQL_SERVER_NAME}.database.windows.net,1433;Initial Catalog=${AZURE_SQL_DATABASE_NAME};Persist Security Info=False;User ID=${AZURE_SQL_ADMIN_USER};Password=${AZURE_SQL_ADMIN_PASSWORD};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

upsert_env_secret() {
    python3 - "$ENV_FILE" "$1" "$2" <<'PY'
import pathlib
import sys

path, key, value = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
lines = path.read_text().splitlines() if path.exists() else []
replacement = f"{key}={value}"
for index, line in enumerate(lines):
    if line.startswith(f"{key}="):
        lines[index] = replacement
        break
else:
    lines.append(replacement)
path.write_text("\n".join(lines) + "\n")
PY
}

upsert_env_secret AZURE_SQL_ADMIN_PASSWORD "$AZURE_SQL_ADMIN_PASSWORD"
upsert_env_secret OCR_Sql__Enabled true
upsert_env_secret OCR_Sql__ConnectionString "$CONNECTION_STRING"

echo "[OK] Azure SQL is ready."
echo "     Credentials and OCR_Sql__ConnectionString were saved to the uncommitted .env file."
