# OcrAgent CLI Usage Guide

## Command Structure

```bash
dotnet run -- <command> [arguments]
```

If no command is given, the agent starts in interactive chat mode.

## Commands

### workflow / wf

Run a preprocessing and extraction pipeline.

```bash
dotnet run -- workflow <workflowId> <imageReference> [--overrides <json>]
dotnet run -- wf <workflowId> <imageReference> [--overrides <json>]
```

**Arguments:**
- `workflowId` — workflow to run (e.g. `receipt_extract_v3`, `receipt_preprocess_v1`)
- `imageReference` — a registered reference ID or a file path (auto-registers as uppercase filename)

**Options:**
- `--overrides <json>` — JSON to customize step parameters or disable steps

**Examples:**

```bash
dotnet run -- workflow receipt_extract_v3 RECEIPT001
dotnet run -- wf receipt_extract_v3 ./samples/receipt-1.jpg
dotnet run -- workflow receipt_extract_v3 RECEIPT001 --overrides '{"steps":{"denoise":{"enabled":false}}}'
```

**Output directory:** `local-storage/wf_{workflowId}/{runId}/`

### tool

Invoke a single plugin tool on an image.

```bash
dotnet run -- tool <toolName> <imageReference> [key=value ...]
```

**Examples:**

```bash
dotnet run -- tool AssessQuality RECEIPT001
dotnet run -- tool EnhanceContrast RECEIPT001 method=auto_lab
dotnet run -- tool DenoiseImage RECEIPT001 method=bilateral strength=15
dotnet run -- tool CorrectPerspective RECEIPT001
dotnet run -- tool RunOcr RECEIPT001 language=eng pageSegMode=SingleBlock
dotnet run -- tool RemoveDocumentBackground RECEIPT001 mode=on_white
dotnet run -- tool AutoOrient RECEIPT001
dotnet run -- tool AutoCrop RECEIPT001 marginPercent=2
dotnet run -- tool AddBorderPadding RECEIPT001 pixels=10
```

### register

Register an image file and assign a reference ID.

```bash
dotnet run -- register <filePath> [referenceId]
```

```bash
dotnet run -- register ./samples/receipt-1.jpg RECEIPT001
dotnet run -- register ./samples/receipt-2.jpg
```

### list-tools

```bash
dotnet run -- list-tools
```

### list-workflows

```bash
dotnet run -- list-workflows
```

### help

```bash
dotnet run -- help
```

## Available Tools

**Analysis:**
- `AssessQuality` — blur, noise, brightness, contrast, skew, resolution
- `ExtractDocumentContours` — document boundary detection

**Geometric:**
- `AutoOrient` — fix 90/180/270 rotations
- `CorrectPerspective` — fix perspective distortion
- `DeskewImage` — correct minor skew/tilt
- `ResizeImage` — resize to OCR-optimal dimensions
- `AutoCrop` — crop to document content
- `AddBorderPadding` — add white/black border
- `ZoomInImage` — zoom into regions
- `RotateImage` — rotate by angle

**Enhancement:**
- `EnhanceContrast` — improve contrast (auto, histogram, CLAHE)
- `AutoAdjustBrightness` — normalize brightness
- `DenoiseImage` — remove noise (bilateral, median, NLM)
- `NormalizeIllumination` — remove shadows/uneven lighting
- `SharpenImage` — sharpen text edges
- `CorrectColor` — fix color casts
- `InvertImage` — invert colors

**Segmentation:**
- `BinarizeImage` — convert to black and white (Otsu, adaptive)
- `ApplyMorphology` — morphological cleanup
- `RemoveDocumentBackground` — isolate document via U2-Net

**OCR:**
- `RunOcr` — Tesseract OCR with multi-variant and multi-PSM support
- `RunOcrMultiStrategy` — multi-variant OCR with automated scoring
- `ScoreOcrResult` — score and rank OCR outputs

**Extraction:**
- `ParseReceipt` — rule-based receipt parsing
- `ExportReceiptData` — export to JSON and CSV

## Workflow Overrides

**Disable a step:**

```bash
dotnet run -- workflow receipt_extract_v3 RECEIPT001 --overrides '{"steps":{"denoise":{"enabled":false}}}'
```

**Override step arguments:**

```bash
dotnet run -- workflow receipt_extract_v3 RECEIPT001 --overrides '{"steps":{"resize":{"args":{"targetDPI":600}}}}'
```

**Set budget limits:**

```bash
dotnet run -- workflow receipt_extract_v3 RECEIPT001 --overrides '{"budget":{"maxVariants":3}}'
```

## Chaining Tools Manually

Each tool returns a new image reference. Use the output as input to the next step.

```bash
dotnet run -- register ./samples/receipt-1.jpg RECEIPT001
dotnet run -- tool AssessQuality RECEIPT001
dotnet run -- tool AutoOrient RECEIPT001
dotnet run -- tool CorrectPerspective RECEIPT001
dotnet run -- tool DenoiseImage RECEIPT001 method=auto
dotnet run -- tool RunOcr RECEIPT001 language=eng pageSegMode=multi variant=auto
```

## Quick Reference

| Task | Command |
|---|---|
| Run v3 workflow | `dotnet run -- wf receipt_extract_v3 RECEIPT001` |
| Assess quality | `dotnet run -- tool AssessQuality RECEIPT001` |
| Denoise | `dotnet run -- tool DenoiseImage RECEIPT001 method=auto` |
| OCR | `dotnet run -- tool RunOcr RECEIPT001 language=eng` |
| Register image | `dotnet run -- register ./scan.jpg SCAN001` |
| List tools | `dotnet run -- list-tools` |
| List workflows | `dotnet run -- list-workflows` |
| Chat mode | `dotnet run` |
| Help | `dotnet run -- help` |

## Troubleshooting

**"Tool not found"** — run `dotnet run -- list-tools` to see available tools.

**"Workflow not found"** — run `dotnet run -- list-workflows` to see available workflows.

**"Image reference not found"** — register the image first with `dotnet run -- register <path> <ID>`.
