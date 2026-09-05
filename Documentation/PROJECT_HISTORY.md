# Project History

Agentic Document OCR developed in two technical generations that now form one continuous repository
history.

## Terminal agent

The first generation established the core architecture:

- a natural-language AI agent backed by a catalog of callable image-processing tools;
- analysis, geometric correction, enhancement, segmentation, OCR, and structured extraction;
- local and SFTP storage providers;
- deterministic JSON workflows alongside agent-selected tool chains;
- a command-line interface and interactive terminal conversation;
- Tesseract OCR and U2NETP-assisted document masking;
- receipt extraction as a specialized workflow built on the general document engine.

## Cloud-native system

The second generation retained the engine and added several execution and persistence boundaries:

- a reusable agent and execution service;
- stateless MCP and REST interfaces;
- a React Web UI with streamed agent progress;
- Blob-backed images and artifacts, Table-backed run records, and SQL-backed conversations;
- authentication through scoped OAuth tokens or named API keys;
- a Linux container with selectable MCP, CLI, and worker roles;
- asynchronous Azure Queue processing with retry, poison-message, bulk-planning, and idempotency
  behavior.

The current repository presents these stages as one product lineage. Historical academic reports,
planning files, duplicated datasets, and presentation media are intentionally excluded; functional
code, tests, workflows, and technical decisions remain in Git history.
