# OCR Agent Web UI

This React, Vite, and TypeScript application is the browser client for the OCR preprocessing
service. Production assets are compiled into `OcrAgent.Mcp/wwwroot` and served from the same origin
as the protected REST API and MCP endpoint.

For user features, REST/SSE contracts, Auth0 behavior, receipt corrections, and verification, see
[Web UI and REST API](../Documentation/WEBUI.md). For the complete deployed-system quick start, see
the repository [README](../README.md).

## Local frontend development

```bash
npm ci
npm run dev
```

The current Vite configuration has no `/api` proxy, so `npm run dev` is for frontend-only work.
Use `docker compose up --build` for same-origin API integration. Validate frontend changes with:

```bash
npm run test
npm run build
```
