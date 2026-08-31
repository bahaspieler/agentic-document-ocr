#!/usr/bin/env bash
#
# Uploads one image to the course training container and enqueues one or more
# plain-language commands for the queue worker. This is a producer only: it does
# not deploy the worker or wait for result rows.
#
# Usage:
#   scripts/azure-queue-produce.sh --image receipt.png \
#     --message "Remove the background from receipt."
#   scripts/azure-queue-produce.sh --image SampleFiles/images/RCPT001.png \
#     --message-file SampleFiles/queue/remove-background.txt \
#     --message-file SampleFiles/queue/extract-receipt.txt
#
# Optional overrides:
#   --blob-name images/RCPT001.png
#   --training-container training-files
#   --queue trigger-queue
#
# An existing Blob with the selected name is overwritten. Existing queue
# messages, result rows, and result artifacts are never deleted.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/_azure_common.sh
source "$SCRIPT_DIR/_azure_common.sh"

usage() {
    sed -n '2,21p' "$0" | sed 's/^# \?//'
}

IMAGE_PATH=""
BLOB_NAME=""
TRAINING_CONTAINER=""
QUEUE_NAME=""
MESSAGES=()
MESSAGE_FILES=()

while [ "$#" -gt 0 ]; do
    case "$1" in
        --image)
            [ "$#" -ge 2 ] || { echo "[ERROR] --image requires a path." >&2; exit 2; }
            IMAGE_PATH="$2"
            shift 2
            ;;
        --blob-name)
            [ "$#" -ge 2 ] || { echo "[ERROR] --blob-name requires a Blob path." >&2; exit 2; }
            BLOB_NAME="$2"
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
        --message)
            [ "$#" -ge 2 ] || { echo "[ERROR] --message requires text." >&2; exit 2; }
            MESSAGES+=("$2")
            shift 2
            ;;
        --message-file)
            [ "$#" -ge 2 ] || { echo "[ERROR] --message-file requires a path." >&2; exit 2; }
            MESSAGE_FILES+=("$2")
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

load_env
apply_azure_defaults

STORAGE_CONNECTION_STRING="${COURSE_STORAGE_CONNECTION_STRING:-${AZURE_STORAGE_CONNECTION_STRING:-}}"
TRAINING_CONTAINER="${TRAINING_CONTAINER:-$MyConfig__TrainingContainer}"
QUEUE_NAME="${QUEUE_NAME:-$MyConfig__Queue}"

if [ -z "$STORAGE_CONNECTION_STRING" ]; then
    echo "[ERROR] COURSE_STORAGE_CONNECTION_STRING and AZURE_STORAGE_CONNECTION_STRING are empty." >&2
    exit 1
fi
if [ -z "$IMAGE_PATH" ] || [ ! -f "$IMAGE_PATH" ] || [ ! -r "$IMAGE_PATH" ]; then
    echo "[ERROR] --image must name a readable file: ${IMAGE_PATH:-<empty>}" >&2
    exit 1
fi

IMAGE_FILE="$(basename "$IMAGE_PATH")"
IMAGE_EXTENSION="${IMAGE_FILE##*.}"
IMAGE_EXTENSION="$(printf '%s' "$IMAGE_EXTENSION" | tr '[:upper:]' '[:lower:]')"
case "$IMAGE_EXTENSION" in
    jpg|jpeg|png|bmp|tif|tiff|webp) ;;
    *)
        echo "[ERROR] Unsupported image extension '.$IMAGE_EXTENSION'." >&2
        exit 1
        ;;
esac

if [ -z "$BLOB_NAME" ]; then
    BLOB_NAME="images/$IMAGE_FILE"
fi
case "$BLOB_NAME" in
    /*|*..*|*/|"")
        echo "[ERROR] Unsafe Blob name: '$BLOB_NAME'." >&2
        exit 1
        ;;
esac

# The worker resolves image references from the Blob filename without its extension.
BLOB_FILE="$(basename "$BLOB_NAME")"
REFERENCE="${BLOB_FILE%.*}"
BLOB_EXTENSION="${BLOB_FILE##*.}"
BLOB_EXTENSION="$(printf '%s' "$BLOB_EXTENSION" | tr '[:upper:]' '[:lower:]')"
if [ -z "$REFERENCE" ] || [ "$REFERENCE" = "$BLOB_FILE" ]; then
    echo "[ERROR] Blob name must end with a supported image extension: '$BLOB_NAME'." >&2
    exit 1
fi
case "$BLOB_EXTENSION" in
    jpg|jpeg|png|bmp|tif|tiff|webp) ;;
    *)
        echo "[ERROR] Unsupported Blob image extension '.$BLOB_EXTENSION'." >&2
        exit 1
        ;;
esac
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

for message_file in "${MESSAGE_FILES[@]}"; do
    if [ ! -f "$message_file" ] || [ ! -r "$message_file" ]; then
        echo "[ERROR] Message file is not readable: $message_file" >&2
        exit 1
    fi
    while IFS= read -r message || [ -n "$message" ]; do
        message="${message%$'\r'}"
        [ -n "$message" ] && MESSAGES+=("$message")
    done < "$message_file"
done

if [ "${#MESSAGES[@]}" -eq 0 ]; then
    echo "[ERROR] Supply at least one --message or --message-file." >&2
    exit 1
fi

# Reject commands that the worker cannot associate with the uploaded image.
for message in "${MESSAGES[@]}"; do
    if ! printf '%s' "$message" | grep -Fqi -- "$REFERENCE"; then
        echo "[ERROR] Command does not contain image reference '$REFERENCE': $message" >&2
        exit 1
    fi
done

require_az

# Azure CLI reads this environment variable without exposing the connection string
# in the process arguments or command output.
export AZURE_STORAGE_CONNECTION_STRING="$STORAGE_CONNECTION_STRING"

echo "[INFO] Storage account    : $(storage_account_name)"
echo "[INFO] Training container : $TRAINING_CONTAINER"
echo "[INFO] Queue              : $QUEUE_NAME"
echo "[INFO] Blob               : $BLOB_NAME"
echo "[INFO] Image reference    : $REFERENCE"

az storage container create \
    --name "$TRAINING_CONTAINER" \
    --only-show-errors \
    --output none
az storage queue create \
    --name "$QUEUE_NAME" \
    --only-show-errors \
    --output none

# Upload and verify before publishing commands so the worker cannot receive a
# message whose source image is not yet durable.
az storage blob upload \
    --container-name "$TRAINING_CONTAINER" \
    --name "$BLOB_NAME" \
    --file "$IMAGE_PATH" \
    --overwrite true \
    --only-show-errors \
    --output none

UPLOAD_EXISTS="$(az storage blob exists \
    --container-name "$TRAINING_CONTAINER" \
    --name "$BLOB_NAME" \
    --query exists \
    --only-show-errors \
    --output tsv)"
if [ "$UPLOAD_EXISTS" != "true" ]; then
    echo "[ERROR] Azure did not confirm the uploaded Blob." >&2
    exit 1
fi

for message in "${MESSAGES[@]}"; do
    az storage message put \
        --queue-name "$QUEUE_NAME" \
        --content "$message" \
        --only-show-errors \
        --output none
    echo "[OK] Enqueued: $message"
done

echo "[OK] Uploaded one image and enqueued ${#MESSAGES[@]} message(s)."
echo "     Worker status: scripts/azure-worker-status.sh"
echo "     Result check : python3 scripts/verify-queue-worker.py --image \"$IMAGE_PATH\""
