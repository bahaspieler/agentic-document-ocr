# Testing with Cursor's MCP client

Connects Cursor to the OCR engine's tools, either the deployed Container App or a server on your own
machine. Deployment itself is in [DEPLOYMENT.md](DEPLOYMENT.md). Every command below runs from the
project root, the folder holding `.env`.

## 1. Get the endpoint and wake it up

```bash
scripts/azure-start.sh
```

The start script activates the latest revision and sets the serving replica range to min 1 and max
`AZURE_MAX_REPLICAS`. The URL is the plain hostname with a trailing slash, with no `/mcp` path:

```
https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/
```

Run `scripts/azure-stop.sh` when you are done, or the replica bills all night.

## 2. Choose an authentication path

The preferred provider-neutral path is automatic OAuth. The server publishes OAuth Protected
Resource Metadata pointing to Auth0. Auth0 has strict open Dynamic Client Registration (DCR), a
default `access:ocr` third-party user grant, and the Resource Parameter Compatibility Profile.
Cursor, Claude, and Copilot-compatible MCP clients can therefore register and open Auth0 Universal
Login without a repository-specific client ID. The API audience equals the canonical
`ResourceUri`, which lets clients use the RFC 8707 `resource` parameter.

Universal Login offers the Auth0 database, Google, and GitHub connections after they are promoted
to the tenant domain.

For unattended or explicitly provisioned clients, generate a named static credential:

```bash
python3 scripts/api-key-create.py CursorDemo
export OCR_AUTH_API_KEY="<raw-key-printed-once>"
```

The script stores only the SHA-256 hash in `.env` as
`OCR_Auth__ApiKeys__CursorDemo`. The raw value is revealed once and sent as `X-API-Key`; it must
never be saved in `.env`, committed, logged, or shown in screenshots.

For manual bearer-token verification, the implemented first-party native client supports device
flow:

```bash
export OCR_AUTH_ACCESS_TOKEN="$(python3 scripts/auth0-token.py)"
```

The helper prints sign-in instructions to stderr and only the token to stdout. Auth0 is configured
to issue this user access token for 24 hours. Do not persist it. Fully restart a desktop client
after setting or rotating an environment variable so the process inherits the new value.

## 3. Register it in Cursor

Create `.cursor/mcp.json` in whichever project you want the tools in, or `~/.cursor/mcp.json` for all
of them. For automatic OAuth DCR, configure only the remote URL:

```json
{
  "mcpServers": {
    "ocr-agent": {
      "url": "https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/"
    }
  }
}
```

The client discovers the public protected-resource metadata, dynamically registers with Auth0, and
opens Universal Login. For the named API-key path, Cursor expands `${env:...}` placeholders inside
headers:

```json
{
  "mcpServers": {
    "ocr-agent": {
      "url": "https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/",
      "headers": {
        "X-API-Key": "${env:OCR_AUTH_API_KEY}"
      }
    }
  }
}
```

Save the file, fully restart Cursor, then open **Customize → MCPs**: `ocr-agent` appears with a
toggle, and its tools show up under **Available Tools** in the chat panel. The second file contains
only an environment-variable reference, not the raw key itself. A manual device-flow token can
likewise be supplied with `"Authorization": "Bearer ${env:OCR_AUTH_ACCESS_TOKEN}"`.

Alongside the tools the server sends two other things a client may or may not use. Its `instructions`
carry the rules the tool schemas cannot express, the same ones the console agent has always had: call
`RunWorkflow` once instead of stepping a pipeline by hand, feed each tool's returned reference into the
next call, and assess quality first when working manually. Its prompts, `extract_receipt`,
`improve_image_for_ocr`, and `explain_run`, spell those flows out on request. Support for both varies
by client, so if a model starts calling tools one at a time, assume the instructions did not arrive and
say what you want in the request itself.

## 4. Put an image where the server can reach it

The deployed server has no filesystem in common with you, so `RegisterImage` is useless against it.
Upload to the blob container instead. A blob named `RCPT001.png` is the reference `RCPT001`.

The connection string holds semicolons, so do not execute `.env` as shell code. Use the shared loader,
which preserves existing exported values:

```bash
source scripts/_azure_common.sh
load_env

az storage blob upload \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  --container-name ocr-images \
  --name RCPT001.png \
  --file SampleFiles/images/RCPT001.png \
  --overwrite
```

Any receipt image works. Accepted extensions are `png`, `jpg`, `jpeg`, `tif`, `tiff`, and `bmp`.

Uploading different bytes under a name the server has already read is the one thing to avoid: each
replica caches images by reference and will keep serving the first copy it downloaded. Use a new name,
or stop and restart the serving app to start with empty replica caches.

## 5. Drive it from chat

Agent mode, plain language. Cursor picks the tool from the descriptions.

```
list the OCR workflows available
binarize RCPT001 with otsu
assess the quality of RCPT001
run the receipt_extract_v3 workflow on RCPT001
show me the last 10 runs
```

`RunWorkflow` on a receipt takes a minute or two and returns the run id, the step trace, and the
extracted fields. It also reports each step as it finishes, so a client that asked for progress can show
which of the twelve steps is running rather than only that a call is outstanding. Every call is
recorded, so `ListRuns` and `GetRun` are the quickest way to confirm a tool call actually reached the
server rather than being answered from context.

A reference a workflow produces names the run directory it was written to, so it contains `/`:

```
wf_receipt_extract_v3/01a02781-4ca2-7000-b27f-c884ceb32daa/RCPT001_deskew_resize_945x1260
```

Pass it back exactly as given. It is what makes the output of a finished run usable in the next call, and
shortening it to the last segment is the one way to make that fail.

Outputs land in the same blob container under `wf_<workflowId>/<runId>/`:

```bash
az storage blob list --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  --container-name ocr-images --prefix wf_receipt_extract_v3/<runId>/ -o table
```

## 6. Against a local server instead

Two transports, same tools. HTTP is closest to the deployed setup:

```bash
scripts/run-mcp-blob.sh --background   # http://localhost:5290/
```

```json
{
  "mcpServers": {
    "ocr-agent-local": { "url": "http://localhost:5290/" }
  }
}
```

Stdio needs no server running beforehand, and Cursor starts and stops it for you. This is the one
case where `RegisterImage` with an absolute path works, because the server is on your machine:

```json
{
  "mcpServers": {
    "ocr-agent-local": {
      "command": "dotnet",
      "args": [
        "/absolute/path/to/OcrAgent.Mcp/bin/Debug/net10.0/OcrAgent.Mcp.dll",
        "--stdio"
      ]
    }
  }
}
```

The engine reads `.env` from its own directory upwards, so a stdio server picks up the same storage
settings without anything being passed in.

## Troubleshooting

| Symptom | Cause |
|---------|-------|
| Server never connects, or shows no tools | Start the serving app; for automatic OAuth, remove and re-add it to restart discovery; for environment credentials, fully restart Cursor. |
| Automatic OAuth never opens login | Confirm strict DCR, the default third-party user grant, and Resource Parameter Compatibility Profile are enabled, and audience equals the canonical resource URI. |
| `401 Unauthorized` after it previously worked | Reauthorize automatic OAuth, refresh the device-flow token, or check that the named API key has not been rotated; then restart Cursor when using environment credentials. |
| Tools listed but every call fails | Check **Output → MCP Logs** (`Cmd+Shift+U`). |
| `No stored image found for reference` | The blob is not in the container the app is configured with, its extension is not one of the accepted ones, or a workflow output reference was shortened to its last segment. |
| A workflow completes but the text is empty | A stale cached copy of the reference. See step 4. |
| Edits to `mcp.json` ignored | Restart Cursor, or remove and re-add the server in Customize → MCPs. |

An end-to-end check that does not involve Cursor at all:

```bash
export OCR_AUTH_ACCESS_TOKEN="$(python3 scripts/auth0-token.py)"
scripts/verify-deployment.sh
```

Or verify the static credential path:

```bash
export OCR_AUTH_API_KEY="<raw-generated-key>"
unset OCR_AUTH_ACCESS_TOKEN
scripts/verify-deployment.sh
```

The Auth0 tenant and protected deployment were verified. OAuth discovery advertised DCR and PKCE,
anonymous MCP access was rejected, and the named API-key path completed live MCP workflows and
persistence assertions. The abandoned Entra approach is not a fallback.
