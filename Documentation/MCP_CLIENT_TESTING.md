# MCP client setup and testing

Connects Cursor, Claude, or another Streamable HTTP client to the OCR engine's tools, either through
the deployed Container App or a server on your own machine. Deployment itself is in
[DEPLOYMENT.md](DEPLOYMENT.md). Shell commands below run from the project root, the folder holding
`.env`.

## 1. Get the endpoint and wake it up

```bash
scripts/azure-start.sh
```

The start script activates the latest revision and sets the serving replica range to min 1 and max
`AZURE_MAX_REPLICAS`. The Streamable HTTP endpoint is:

```
https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/mcp
```

This is the current team hostname. Recreating the Container Apps environment changes it; confirm
the current value in Azure Portal or with `az containerapp show` before updating a client.

Run `scripts/azure-stop.sh` when you are done, or the replica bills all night.

## 2. Choose an authentication path

The preferred interactive path is OAuth. The server publishes OAuth Protected Resource Metadata
pointing to Auth0 and advertises `access:ocr`. Auth0 has strict open Dynamic Client Registration
(DCR), a default `access:ocr` third-party user grant, and the Resource Parameter Compatibility
Profile. Provider-neutral clients can therefore attempt registration and Auth0 Universal Login
without a repository-specific client ID.

OAuth clients do not all interpret discovery identically. The server therefore publishes compatible
authorization-server and OpenID metadata on the protected-resource origin. Its authorization
endpoint preserves the client's state, PKCE challenge, callback, and requested scopes, supplies the
configured Auth0 audience and RFC 8707 resource, then redirects to Auth0. The server never receives a
password, authorization code, access token, or refresh token in this step.

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
For Azure, run `scripts/azure-deploy.sh --skip-build` after generation so the new hash reaches the
Container App. Until that deployment update completes, production rejects the new raw key.

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
of them. The verified configuration for the current deployment uses a fixed public OAuth client:

```json
{
  "mcpServers": {
    "ocr-agent": {
      "url": "https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/mcp",
      "auth": {
        "CLIENT_ID": "tpc_9u6Q12BsiEyzBPdXZVyLEc",
        "scopes": ["access:ocr"]
      }
    }
  }
}
```

The client ID and scope are public identifiers, not credentials. This client was registered with
Cursor's callback URI and cannot be reused by a different MCP application. Open
**Customize → MCPs**, choose **Authenticate**, and complete Auth0 Universal Login.

URL-only DCR remains available for clients that implement the full discovery flow:

```json
{
  "mcpServers": {
    "ocr-agent-dcr": {
      "url": "https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/mcp"
    }
  }
}
```

Do not repeatedly remove and add a DCR entry from several open Cursor windows: each independent
registration can create another Auth0 client and eventually encounter registration throttling. The
fixed public client is the recommended Cursor demonstration path.

For the named API-key path, Cursor expands `${env:...}` placeholders inside headers:

```json
{
  "mcpServers": {
    "ocr-agent": {
      "url": "https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/mcp",
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

## 4. Register it in Claude

Remote Claude connectors are account-level and run from Anthropic's cloud, even when configured
through Claude Desktop. In Claude on the web or Desktop:

1. Open **Customize → Connectors**.
2. Select **+ → Add custom connector**.
3. Enter a name and the deployed `/mcp` URL.
4. Leave advanced OAuth credentials empty for DCR, add the connector, then choose **Connect**.
5. Enable it in a conversation through **+ → Connectors**.

Free Claude accounts support one custom connector; organization-managed accounts may require an
administrator to add it. Do not put the remote URL in `claude_desktop_config.json`; that mechanism
is for local/stdio servers. Do not enter Cursor's client ID because its registered callback belongs
to Cursor. If Claude requires a predefined client, create a separate public Auth0 client with
`https://claude.ai/api/mcp/auth_callback` as an allowed callback and enter that public ID in
advanced settings.

Claude Code supports the same remote endpoint:

```bash
claude mcp add --transport http hidden-layer-ocr --scope user \
  https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/mcp
claude mcp login hidden-layer-ocr
```

For a non-OAuth Claude Code entry, configure a named API key as a static header:

```bash
claude mcp add --transport http hidden-layer-ocr-key --scope user \
  https://ca-ocr-agent.blackglacier-c262b0d9.germanywestcentral.azurecontainerapps.io/mcp \
  --header "X-API-Key: <raw-key>"
```

Claude Code stores that header in its local MCP configuration; keep the file private. An invalid
configured `Authorization` header does not fall back to OAuth, so remove it before interactive
login. Claude Desktop exposes custom request headers only for supported accounts; do not put
credentials in URL query parameters.

If `claude mcp login` is unavailable, update Claude Code or authenticate from `/mcp` in an
interactive session. Remote connector instructions are maintained by
[Anthropic](https://claude.com/docs/connectors/custom/remote-mcp);
Claude Code syntax is in its
[MCP guide](https://docs.anthropic.com/en/docs/claude-code/mcp).

Cursor OAuth and API-key MCP access have been verified against this deployment. The Claude
configuration follows Anthropic's current connector contract but remains an interoperability path
to verify rather than a claimed completed deployment test.

Alongside the tools the server sends two other things a client may or may not use. Its `instructions`
carry the rules the tool schemas cannot express, the same ones the console agent has always had: call
`RunWorkflow` once instead of stepping a pipeline by hand, feed each tool's returned reference into the
next call, and assess quality first when working manually. Its prompts, `extract_receipt`,
`improve_image_for_ocr`, and `explain_run`, spell those flows out on request. Support for both varies
by client, so if a model starts calling tools one at a time, assume the instructions did not arrive and
say what you want in the request itself.

## 5. Put an image where the server can reach it

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

Any receipt image works. Accepted extensions are `png`, `jpg`, `jpeg`, `bmp`, `tif`, `tiff`, and
`webp`.

Uploading different bytes under a name the server has already read is the one thing to avoid: each
replica caches images by reference and will keep serving the first copy it downloaded. Use a new name,
or stop and restart the serving app to start with empty replica caches.

## 6. Drive it from chat

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

## 7. Against a local server instead

Two transports, same tools. HTTP is closest to the deployed setup. With the checked-in local
defaults, `OCR_Auth__Enabled=false`, so local HTTP needs no bearer token or API key unless you
explicitly enable and configure authentication in `.env`:

```bash
scripts/run-mcp-blob.sh --background   # MCP: http://localhost:5290/mcp
```

```json
{
  "mcpServers": {
    "ocr-agent-local": { "url": "http://localhost:5290/mcp" }
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
| Automatic OAuth never opens login | Confirm the configured URL ends in `/mcp`, then confirm strict DCR, the default third-party user grant, and Resource Parameter Compatibility Profile are enabled, and audience equals the canonical resource URI. |
| `Unexpected token '<'` or an HTML response | The client is pointed at the Web UI root. Change the MCP URL to the same origin with `/mcp` appended. |
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
anonymous MCP access was rejected, interactive Cursor OAuth completed through the public client,
and the named API-key path completed live MCP workflows and persistence assertions. The abandoned
Entra approach is not a fallback.
