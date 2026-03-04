# SFTP Server for OcrAgent Development

This directory contains a Docker-based SFTP server for local development and testing of the OcrAgent SFTP storage backend.

## Quick Start

### Prerequisites
- Docker and Docker Compose installed
- Port 2222 available on your machine

### Starting the Server

```bash
cd docker/sftp
./start.sh
```

The SFTP server will be available at:
- **Host**: localhost
- **Port**: 2222
- **Username**: ocragent
- **Password**: password
- **Upload directory**: `/ocr`

### Stopping the Server

```bash
./stop.sh
```

To also remove all data volumes:
```bash
docker-compose down -v
```

### Viewing Logs

```bash
./logs.sh
```

### Testing Connection

```bash
./test-connection.sh
```

Or manually:
```bash
sftp -P 2222 ocragent@localhost
# Password: password
```

## Configuration

### Default Settings

The SFTP server is configured with:
- **User**: ocragent (UID: 1001, GID: 100)
- **Password**: password
- **Upload directory**: `/home/ocragent/ocr`
- **Port mapping**: 2222:22 (local:container)

### Using SSH Keys (Optional)

1. Generate SSH keys (if you don't have them):
```bash
mkdir -p ssh-keys
ssh-keygen -t rsa -b 4096 -f ssh-keys/id_rsa -N ""
```

2. Uncomment the volume mount in `docker-compose.yml`:
```yaml
volumes:
  - ./ssh-keys:/home/ocragent/.ssh/keys:ro
```

3. Update the command to disable password authentication:
```yaml
command: ocragent::1001:100:upload
```

4. Restart the server:
```bash
./stop.sh
./start.sh
```

## Connecting from OcrAgent

### Update appsettings.json

```json
{
  "Storage": {
    "Provider": "sftp",
    "Sftp": {
      "Host": "localhost",
      "Port": 2222,
      "Username": "ocragent",
      "AuthenticationMethod": "password",
      "Password": "password",
      "RemoteBasePath": "/upload/ocr"
    }
  }
}
```

### Or use environment variables:

```bash
export OCR_Storage__Provider=sftp
export OCR_Storage__Sftp__Host=localhost
export OCR_Storage__Sftp__Port=2222
export OCR_Storage__Sftp__Username=ocragent
export OCR_Storage__Sftp__Password=password
export OCR_Storage__Sftp__RemoteBasePath=/upload/ocr
```

## Cleanup

Remove everything (container, volume, network):
```bash
docker-compose down -v
docker network rm docker_ocragent-network 2>/dev/null
```

## Additional Resources

- [atmoz/sftp Docker Image](https://github.com/atmoz/sftp)
