#!/bin/bash
# Start SFTP server for OcrAgent development

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ENV_FILE="$SCRIPT_DIR/../../.env"

if [ -f "$ENV_FILE" ]; then
    export $(grep -v '^#' "$ENV_FILE" | xargs)
else
    echo "✗ .env file not found at $ENV_FILE"
    echo "  Create it with: echo 'SFTP_PASSWORD=your_password' > $ENV_FILE"
    exit 1
fi

echo "Starting SFTP server..."
docker-compose up -d

echo "Waiting for SFTP server to be ready..."
sleep 5

if docker-compose ps | grep -q "Up"; then
    echo "✓ SFTP server is running on port 2222"
    echo ""
    echo "Connection details:"
    echo "  Host: localhost"
    echo "  Port: 2222"
    echo "  Username: ocragent"
    echo "  Password: (from .env file)"
    echo "  Upload path: /ocr (chrooted, writable)"
    echo ""
    echo "To test connection: sftp -P 2222 ocragent@localhost"
    echo "To view logs: ./logs.sh"
    echo "To stop: ./stop.sh"
else
    echo "✗ Failed to start SFTP server"
    docker-compose logs
    exit 1
fi
