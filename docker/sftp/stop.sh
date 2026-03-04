#!/bin/bash
# Stop SFTP server

echo "Stopping SFTP server..."
docker-compose down

echo "✓ SFTP server stopped"
echo ""
echo "To remove all data volumes: docker-compose down -v"
