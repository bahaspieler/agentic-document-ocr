#!/usr/bin/env bash
#
# Recursively uploads a local image directory as one worker dataset and then
# publishes exactly one natural-language parent command.
#
# Usage:
#   scripts/azure-queue-produce-bulk.sh \
#     --upload-dir SampleFiles/datasets/cord/smoke/images \
#     --dataset cord-smoke \
#     --command "Remove background from all images in cord-smoke"
#
# Optional overrides:
#   --training-container training-files
#   --queue trigger-queue
#
# Source images are stored as:
#   training-files/images/<dataset>/<relative-file>
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

usage() {
    sed -n '2,18p' "$0" | sed 's/^# \?//'
}

UPLOAD_DIR=""
DATASET=""
COMMAND=""
TRAINING_CONTAINER=""
QUEUE_NAME=""

while [ "$#" -gt 0 ]; do
    case "$1" in
        --upload-dir)
            [ "$#" -ge 2 ] || { echo "[ERROR] --upload-dir requires a path." >&2; exit 2; }
            UPLOAD_DIR="$2"
            shift 2
            ;;
        --dataset)
            [ "$#" -ge 2 ] || { echo "[ERROR] --dataset requires a name." >&2; exit 2; }
            DATASET="$2"
            shift 2
            ;;
        --command)
            [ "$#" -ge 2 ] || { echo "[ERROR] --command requires text." >&2; exit 2; }
            COMMAND="$2"
            shift 2
            ;;
        --training-container)
            [ "$#" -ge 2 ] || { echo "[ERROR] --training-container requires a name." >&2; exit 2; }
            TRAINING_CONTAINER="$2"
            shift 2
            ;;
        --queue)
            [ "$#" -ge 2 ] || { echo "[ERROR] --queue requires a name." >&2; exit 2; }
            QUEUE_NAME="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [ -z "$UPLOAD_DIR" ] || [ ! -d "$UPLOAD_DIR" ] || [ ! -r "$UPLOAD_DIR" ]; then
    echo "[ERROR] --upload-dir must name a readable directory: ${UPLOAD_DIR:-<empty>}" >&2
    exit 1
fi
UPLOAD_DIR="${UPLOAD_DIR%/}"
if [[ ! "$DATASET" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || [[ "$DATASET" == *..* ]]; then
    echo "[ERROR] --dataset must be a safe single Blob path segment." >&2
    exit 1
fi
if [ -z "$COMMAND" ]; then
    echo "[ERROR] --command is required." >&2
    exit 1
fi

# Mirror the worker's two parent-command requirements before uploading anything:
# explicit bulk intent and an unambiguous dataset token.
python3 - "$DATASET" "$COMMAND" <<'PY'
import re
import sys

dataset, command = sys.argv[1:]
token = re.compile(
    rf"(?<![\w-]){re.escape(dataset)}(?![\w-])",
    flags=re.IGNORECASE,
)
bulk = re.compile(
    r"\b(?:all|every)\b[\s\S]{0,40}\b(?:images?|files?|pictures?|receipts?)\b"
    r"|\b(?:images?|files?|pictures?|receipts?)\b[\s\S]{0,40}\b(?:all|every)\b",
    flags=re.IGNORECASE,
)
if not token.search(command):
    raise SystemExit(f"[ERROR] Parent command must name dataset '{dataset}'.")
if not bulk.search(command):
    raise SystemExit("[ERROR] Parent command must explicitly request all/every images or files.")
PY

IMAGE_FILES=()
while IFS= read -r -d '' file; do
    IMAGE_FILES+=("$file")
done < <(
    find "$UPLOAD_DIR" -type f \
        \( -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' -o \
           -iname '*.bmp' -o -iname '*.tif' -o -iname '*.tiff' -o \
           -iname '*.webp' \) -print0 | sort -z
)
if [ "${#IMAGE_FILES[@]}" -eq 0 ]; then
    echo "[ERROR] No supported images found below $UPLOAD_DIR." >&2
    exit 1
fi

load_env
apply_azure_defaults
STORAGE_CONNECTION_STRING="${COURSE_STORAGE_CONNECTION_STRING:-${AZURE_STORAGE_CONNECTION_STRING:-}}"
TRAINING_CONTAINER="${TRAINING_CONTAINER:-$MyConfig__TrainingContainer}"
QUEUE_NAME="${QUEUE_NAME:-$MyConfig__Queue}"

if [ -z "$STORAGE_CONNECTION_STRING" ]; then
    echo "[ERROR] COURSE_STORAGE_CONNECTION_STRING and AZURE_STORAGE_CONNECTION_STRING are empty." >&2
    exit 1
fi
if [[ ! "$TRAINING_CONTAINER" =~ ^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$ ]] ||
   [[ "$TRAINING_CONTAINER" == *--* ]]; then
    echo "[ERROR] Invalid Azure Blob container name: '$TRAINING_CONTAINER'." >&2
    exit 1
fi
if [[ ! "$QUEUE_NAME" =~ ^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$ ]] ||
   [[ "$QUEUE_NAME" == *--* ]]; then
    echo "[ERROR] Invalid Azure Queue name: '$QUEUE_NAME'." >&2
    exit 1
fi

require_az
export AZURE_STORAGE_CONNECTION_STRING="$STORAGE_CONNECTION_STRING"

echo "[INFO] Storage account    : $(storage_account_name)"
echo "[INFO] Training container : $TRAINING_CONTAINER"
echo "[INFO] Queue              : $QUEUE_NAME"
echo "[INFO] Dataset prefix     : images/$DATASET/"
echo "[INFO] Local images       : ${#IMAGE_FILES[@]}"

az storage container create \
    --name "$TRAINING_CONTAINER" \
    --only-show-errors \
    --output none
az storage queue create \
    --name "$QUEUE_NAME" \
    --only-show-errors \
    --output none

# Publish no command until every source image is durable. Relative directories
# are retained, while the package manifest and other non-image files stay local.
uploaded=0
for file in "${IMAGE_FILES[@]}"; do
    relative="${file#"$UPLOAD_DIR"/}"
    blob_name="images/$DATASET/$relative"
    case "$blob_name" in
        /*|*"/../"*|*"//"*)
            echo "[ERROR] Unsafe generated Blob name: $blob_name" >&2
            exit 1
            ;;
    esac
    az storage blob upload \
        --container-name "$TRAINING_CONTAINER" \
        --name "$blob_name" \
        --file "$file" \
        --overwrite true \
        --only-show-errors \
        --output none
    uploaded=$((uploaded + 1))
    printf '\r[UPLOAD] %d/%d' "$uploaded" "${#IMAGE_FILES[@]}"
done
printf '\n'

az storage message put \
    --queue-name "$QUEUE_NAME" \
    --content "$COMMAND" \
    --only-show-errors \
    --output none

echo "[OK] Uploaded $uploaded images and enqueued exactly one parent command."
echo "     $COMMAND"
