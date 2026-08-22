# Configuration

How `OcrAgent` (console) and `OcrAgent.Mcp` (MCP server) are configured. The sample template's
`MyCloudProject` keeps its own `MyConfig` section and is not covered here.

## Sources, in precedence order

1. Environment variables
2. `.env` file
3. `appsettings.{DOTNET_ENVIRONMENT}.json`, when that variable is set and the file exists
4. `appsettings.json`

Both applications call `EnvFile.Load()` at start-up. It searches for `.env` from the working
directory upwards, then from the application directory upwards, so one file at the project root
serves the console app, the MCP server, and the helper scripts. A variable already exported in the
shell is never overwritten, which keeps a container's real settings ahead of a stray file.

The JSON files are read from the working directory when it holds an `appsettings.json`, and from the
directory of the assembly otherwise. Starting a process in a folder with its own settings is a
deliberate choice, so it wins; the copy beside the assembly is the one that ships, which is what the
container uses, where the working directory is the filesystem root. Relative storage paths resolve
against whichever of the two was used, not against the working directory.

Every entry point loads configuration through one `EngineHost.Create()`, so the console, the MCP
server, and later the queue worker cannot disagree about any of the above.

Copy `.env.example` to `.env` and fill it in. `.env` is gitignored.

## Keys

Keys prefixed `OCR_` override `appsettings.json`, with a double underscore per nesting level:
`OCR_Runs__TableName` sets `Runs:TableName`.

| Key | Purpose |
|-----|---------|
| `AZURE_STORAGE_CONNECTION_STRING` | Storage account, used by both the image store and the run store |
| `OPENAI_API_KEY` | Chat model credentials, needed only for the agent |
| `OPENAI_CHATCOMPLETION_DEPLOYMENT` | Chat model to use (default `gpt-4o-mini`) |
| `OCR_Storage__Provider` | Image store: `local`, `sftp`, or `azureblob` |
| `OCR_Storage__AzureBlob__ContainerName` | Container holding images, sidecars, and run artifacts |
| `OCR_Storage__AzureBlob__BlobPrefix` | Optional prefix, so several projects can share a container |
| `OCR_Runs__Provider` | Run records: `none` to disable, or `azuretable` |
| `OCR_Runs__TableName` | Table holding run records (default `OcrRuns`) |
| `OCR_Runs__GroupId` | Partition key for this deployment's records |

Neither store needs its connection string set explicitly. When `Storage:AzureBlob:ConnectionString`
or `Runs:ConnectionString` is empty, the factory falls back to `AZURE_STORAGE_CONNECTION_STRING`.

## Sections

`Storage` selects and configures the image store; see the provider classes in `OcrAgent/Storage/`.
`Runs` selects and configures the run store and is documented in [RUN_RECORDS.md](RUN_RECORDS.md).
Both ship with safe defaults: `Storage:Provider` is `local` and `Runs:Provider` is `none`, so a
checkout with no `.env` still runs.

## Scripts

`scripts/run-mcp-blob.sh` builds and starts the MCP server with the blob image store, frees the port
first, and relies on `.env` for everything else. `OCR_PORT`, `OCR_CONTAINER`, and `OCR_BLOB_PREFIX`
override per invocation.

Azure hosting is documented in [DEPLOYMENT.md](DEPLOYMENT.md). The four `scripts/azure-*.sh` helpers
read the same `.env` file. Pointing an MCP client at either server is covered in
[MCP_CLIENT_TESTING.md](MCP_CLIENT_TESTING.md).
