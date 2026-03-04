#!/bin/bash
# Test SFTP connection

echo "Testing SFTP connection to localhost:2222..."
echo "Username: ocragent"
echo "Password: password"
echo ""

sftp -P 2222 -o StrictHostKeyChecking=no ocragent@localhost <<EOF
pwd
ls
exit
EOF

if [ $? -eq 0 ]; then
    echo ""
    echo "✓ Connection successful!"
else
    echo ""
    echo "✗ Connection failed"
    exit 1
fi
