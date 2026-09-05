# Command-Line Interface

The `OcrAgent` executable supports interactive agent conversations and deterministic command-line
operations. All modes use the same processing engine, tools, workflows, storage configuration, and
run-recording infrastructure.

## Setup

From the repository root:

```bash
cp .env.example .env
dotnet build OcrAgent.slnx
```

For local file processing, set these values in `.env`:

```dotenv
OCR_Storage__Provider=local
OCR_Runs__Provider=none
```

Set `OPENAI_API_KEY` only when using interactive chat or another agent-driven operation. Direct
tool and workflow commands do not require a language model.

## Interactive agent

Run without a command, or pass `chat` explicitly:

```bash
dotnet run --project OcrAgent
dotnet run --project OcrAgent -- chat
```

The agent interprets a natural-language request and chooses from the registered preprocessing,
OCR, workflow, and run-history tools. When it chains operations, each tool's generated image
reference becomes the input to the following tool.

## Deterministic commands

```text
workflow <id> <image> [--overrides <json>]
wf       <id> <image> [--overrides <json>]
tool     <name> <image> [key=value ...]
register <filePath> [referenceId]
runs list [--top N] [--mode tool|workflow]
runs get <runId>
list-tools
list-workflows
chat
help
```

Register a local document and assign a stable reference:

```bash
dotnet run --project OcrAgent -- register ./document.jpg DOC001
```

Inspect and invoke individual tools:

```bash
dotnet run --project OcrAgent -- list-tools
dotnet run --project OcrAgent -- tool AssessQuality DOC001
dotnet run --project OcrAgent -- tool CorrectPerspective DOC001 sensitivity=0.8
dotnet run --project OcrAgent -- tool RunOcr DOC001 variant=auto pageSegMode=multi
```

List and execute workflows:

```bash
dotnet run --project OcrAgent -- list-workflows
dotnet run --project OcrAgent -- workflow receipt_extract_v3 DOC001
```

Workflow overrides are supplied as one JSON object:

```bash
dotnet run --project OcrAgent -- workflow receipt_extract_v3 DOC001 \
  --overrides '{"ocr":{"variant":"auto"}}'
```

When Azure Table run records are enabled, inspect recent executions with:

```bash
dotnet run --project OcrAgent -- runs list --top 10
dotnet run --project OcrAgent -- runs list --top 10 --mode workflow
dotnet run --project OcrAgent -- runs get <run-id>
```

## Container CLI

The production image contains the same command-line application:

```bash
docker run --rm --env-file .env agentic-document-ocr cli list-tools
docker run --rm --env-file .env agentic-document-ocr cli workflow receipt_extract_v3 DOC001
```

For storage, model, OCR, and run-record settings, see [CONFIGURATION.md](CONFIGURATION.md).
