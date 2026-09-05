# Agentic Document OCR

**A cloud-native AI agent for document-image preprocessing, OCR, and structured extraction.**

Agentic Document OCR gives a language model a catalog of image-processing and OCR tools, then lets
the model choose and chain the operations needed for a user's document. The same processing engine
also runs deterministic JSON workflows when repeatability matters more than agentic planning.

The system accepts general document images. Receipt extraction is included as a specialized
workflow and reference implementation; it is not a restriction on the engine's input or purpose.

## What it does

- Assesses blur, noise, brightness, contrast, skew, resolution, and document boundaries.
- Corrects perspective and orientation, crops, resizes, denoises, sharpens, normalizes lighting,
  removes backgrounds, and binarizes document images.
- Runs Tesseract OCR with selectable preprocessing variants and page-segmentation strategies.
- Lets an AI agent select tools from a natural-language request and pass each generated artifact to
  the next operation.
- Runs versioned JSON workflows for deterministic processing pipelines.
- Extracts and exports structured receipt data as one domain-specific workflow.
- Exposes the engine through an interactive CLI, MCP, REST, a React Web UI, and an asynchronous
  Azure Queue worker.
- Persists images, artifacts, run records, conversations, and extracted data through local or cloud
  storage providers.

## Execution model

```text
User or client
    |
    +-- AI agent -------- selects and chains registered tools
    +-- JSON workflow --- executes a predefined deterministic pipeline
    +-- Direct API ------ invokes a specific tool or workflow
              |
              v
       Shared OcrAgent engine
              |
              +-- image analysis and preprocessing
              +-- Tesseract OCR
              +-- structured extraction
              +-- artifact and run persistence
```

Agentic execution is useful for open-ended requests such as “assess this scan and prepare it for
OCR.” Workflows are preferable for repeatable jobs such as a fixed extraction pipeline. Both paths
use the same tool implementations.

## Applications

| Project | Responsibility |
| --- | --- |
| `OcrAgent` | Processing engine, tool catalog, workflows, CLI, and interactive agent |
| `OcrAgent.Mcp` | MCP and REST host; also serves the production Web UI |
| `web` | React, TypeScript, and Vite frontend |
| `OcrAgent.Worker` | Queue-worker process and retry loop |
| `OcrAgent.Worker.Azure` | Azure Queue, Blob, and Table adapters |
| `OcrAgent.Worker.Contracts` | Transport-neutral worker contracts |
| `UnitTests` | Engine, workflow, storage, and worker tests |
| `OcrAgent.Mcp.Tests` | Authentication and server tests |

## Requirements

- .NET 10 SDK
- Node.js 20.19+, 22.13+, or 24+ for frontend development
- An OpenAI API key for agent-driven execution
- Docker for the Linux container path
- Optional Azure Blob, Table, Queue, SQL, Container Registry, and Container Apps resources

Tesseract English language data and the U2NETP model are bundled under `OcrAgent/Assets`. The
container installs the matching native OCR libraries. On Apple Silicon, run
`scripts/setup-native-libs.sh` if the local Tesseract wrapper cannot load its native dependencies.

## Quick start

Create a local configuration file:

```bash
cp .env.example .env
```

Set `OPENAI_API_KEY` in `.env`. For a fully local first run, also set:

```dotenv
OCR_Storage__Provider=local
OCR_Runs__Provider=none
OCR_Auth__Enabled=false
OCR_Sql__Enabled=false
```

Start the interactive agent:

```bash
dotnet run --project OcrAgent
```

Example requests:

```text
Assess this document and prepare it for OCR.
Correct the perspective, normalize the lighting, and extract the text.
Remove the background and return an OCR-ready image.
Run the receipt_extract_v3 workflow on RCPT001.
```

Inspect the deterministic interfaces:

```bash
dotnet run --project OcrAgent -- list-tools
dotnet run --project OcrAgent -- list-workflows
dotnet run --project OcrAgent -- help
```

## Web UI, REST, and MCP

Run the backend:

```bash
dotnet run --project OcrAgent.Mcp
```

For frontend development, start Vite in another terminal:

```bash
npm ci --prefix web
npm run dev --prefix web
```

The production container builds the Web UI and serves it from the ASP.NET host. MCP uses the `/mcp`
endpoint; REST endpoints use `/api/*`.

For a complete local container:

```bash
docker build --platform linux/amd64 -t agentic-document-ocr .
docker run --rm -p 5290:8080 --env-file .env agentic-document-ocr
```

The same image supports three roles:

```bash
docker run --rm --env-file .env agentic-document-ocr mcp
docker run --rm --env-file .env agentic-document-ocr cli list-tools
docker run --rm --env-file .env agentic-document-ocr worker
```

## Workflows and samples

Workflow definitions live in `OcrAgent/Workflows/Definitions`. A workflow can describe general
preprocessing and OCR, or add domain-specific extraction steps. `receipt_extract_v1` through
`receipt_extract_v3` demonstrate how the engine can evolve a specialized extraction pipeline
without turning receipt processing into a core limitation.

`SampleFiles` contains one small receipt example and queue/workflow request examples. The CORD
dataset is intentionally not committed; its manifest and preparation script provide a reproducible
way to obtain the evaluation subset.

## Documentation

- [System architecture](Documentation/SYSTEM_ARCHITECTURE.md)
- [Command-line interface](Documentation/CLI.md)
- [Configuration](Documentation/CONFIGURATION.md)
- [Container roles](Documentation/CONTAINER.md)
- [MCP client setup](Documentation/MCP_CLIENT_TESTING.md)
- [Web UI and REST API](Documentation/WEBUI.md)
- [Queue worker](Documentation/QUEUE_WORKER.md)
- [Run records](Documentation/RUN_RECORDS.md)
- [Azure deployment](Documentation/DEPLOYMENT.md)
- [Project history](Documentation/PROJECT_HISTORY.md)

## Security

Never commit `.env`, storage connection strings, API keys, bearer or refresh tokens, database
passwords, or Auth0 management credentials. `.env.example` contains names and safe defaults only.
Use named secret stores for deployed environments and rotate a credential immediately if it ever
enters Git history.

## License

This project is available under the [MIT License](LICENSE).

## Project evolution

The project began as a terminal-based, tool-using document preprocessing and OCR agent. It later
gained a reusable execution core, MCP and REST interfaces, a Web UI, durable persistence, container
support, and asynchronous cloud workers. The repository history preserves both generations as one
continuous product lineage.
