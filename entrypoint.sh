#!/bin/sh
#
# Selects which entry point runs. The image carries both, because they share the engine, the native
# OCR stack, and the assets, and only differ by a few hundred kilobytes of entry assemblies.
#
#   docker run -p 5290:8080 --env-file .env ocr-agent            # mcp, the default
#   docker run --rm --env-file .env ocr-agent cli runs list      # any CLI command
#
set -eu

role="${1:-mcp}"
[ "$#" -gt 0 ] && shift

case "$role" in
    mcp)
        exec dotnet /app/mcp/OcrAgent.Mcp.dll "$@"
        ;;
    cli)
        exec dotnet /app/cli/OcrAgent.dll "$@"
        ;;
    worker)
        exec dotnet /app/worker/MyCloudProject.dll "$@"
        ;;
    *)
        echo "Unknown role '$role'. Use mcp, cli, or worker." >&2
        exit 64
        ;;
esac
