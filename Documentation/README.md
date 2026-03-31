# OcrAgent - AI-Powered Image Preprocessing System

## Overview

OcrAgent is an AI-driven preprocessing system designed to enhance document images before OCR extraction. The system uses natural language processing to interpret user commands and applies appropriate image transformations through a plugin-based architecture.

The agent accepts an image reference and a natural language prompt, then generates multiple enhanced variants optimized for OCR performance. Each output is stored with metadata tracking the complete processing lineage.

## Architecture

### System Components

The system is built with a modular architecture consisting of the following layers:

#### 1. Storage Layer

**IImageStore Interface**
- Abstracts storage operations for cross-backend compatibility
- Supports save, load, exists, metadata operations
- Tracks image lineage through parent references

**LocalFileImageStore**
- Default filesystem-based storage
- Saves images with JSON sidecar metadata
- Configurable root directory

**SftpImageStore**
- Enterprise SFTP storage backend
- Connection pooling with configurable min/max connections
- Automatic retry with exponential backoff
- Health monitoring and metrics tracking
- Support for password, private key, and SSH agent authentication
- Async operations with timeout handling
- Path validation for security

#### 2. Plugin System

All image processing operations are implemented as plugins following the `IImageProcessingTool` interface.

**Plugin Categories:**

**Geometric Plugins** (`OcrAgent/Plugins/Geometric/`)
- `ZoomInPlugin` - Zoom into specified regions with start/end/step ranges
- `RotatePlugin` - Rotate images by angle ranges
- `PerspectivePlugin` - Correct perspective distortion in mobile captures
- `DeskewPlugin` - Auto-correct skew and tilt
- `ResizePlugin` - Resize images to target dimensions or DPI

**Enhancement Plugins** (`OcrAgent/Plugins/Enhancement/`)
- `ContrastPlugin` - Improve contrast using auto, histogram, or CLAHE methods
- `BrightnessPlugin` - Adjust brightness in parameter ranges
- `DenoisePlugin` - Remove noise using bilateral, median, or NLM filters
- `SharpenPlugin` - Enhance edge definition and reduce blur
- `ColorCorrectionPlugin` - Fix color casts from mobile captures
- `InvertPlugin` - Invert colors for dark-background documents

**Segmentation Plugins** (`OcrAgent/Plugins/Segmentation/`)
- `BinarizePlugin` - Convert to black and white using Otsu or adaptive thresholding
- `MorphologyPlugin` - Apply morphological operations for post-binarization cleanup
- `DocumentBackgroundRemovalPlugin` - Remove backgrounds using U2NETP ML model with multi-strategy fallback

**Plugin Architecture:**
- Each plugin implements `IImageProcessingTool` interface
- Plugins use `PluginHelpers` for common operations
- All plugins registered in `PluginAggregator`
- Plugins exposed as AI tools to the agent

**Analysis Plugins** (`OcrAgent/Plugins/Analysis/`)
- `QualityAssessmentPlugin` - Assess blur, noise, brightness, contrast, skew, and resolution
- `ContourExtractionPlugin` - Detect document boundaries and extract quad corners

**OCR Plugins** (`OcrAgent/Plugins/Ocr/`)
- `OcrPlugin` - Tesseract OCR with multi-variant binarization (natural, Otsu, adaptive, Sauvola) and multi-PSM selection
- `OcrScoringPlugin` - Scores OCR candidates on word count, line structure, and character quality

**Extraction Plugins** (`OcrAgent/Plugins/Extraction/`)
- `ParseReceiptPlugin` - Rule-based receipt parsing with zone classification and field extraction
- `ExportReceiptPlugin` - Export structured receipt data to JSON and CSV

#### 3. User Interface Layer

**AgentRunner (Chat Mode)**
- Manages interactive conversation loop with the user
- Interfaces with OpenAI GPT models via `Microsoft.Extensions.AI`
- Streams responses for real-time feedback
- The LLM selects and invokes tools from the plugin registry based on user intent

**CliRunner (CLI Mode)**
- Non-interactive command mode for workflows, single-tool calls, and image registration
- Invokes tools directly through the `ToolBindingRegistry` without LLM involvement
- Supports workflow execution, tool invocation, image registration, and listing commands

#### 4. Orchestration Layer

**PluginAggregator (Tool Registry)**
- Central registry that instantiates all plugins and binds them as AI-callable tools
- Exposes `RegisterImage`, all plugin tools, and workflow tools to both AgentRunner and CliRunner
- Wires the `WorkflowTool` to the workflow engine at startup
- Workflow executors invoke plugins back through this registry

#### 5. Workflow Engine

**WorkflowCatalog**
- Discovers and loads workflow JSON definitions from `Workflows/Definitions/`
- Provides listing and lookup by workflow ID

**WorkflowCompiler**
- Compiles a JSON definition into an executable step graph
- Resolves tool names to registered functions via `ToolBindingRegistry`
- Applies optional overrides to enable, disable, or reconfigure steps

**WorkflowRunner**
- Executes compiled workflows with per-run scoped storage directories
- Manages a `WorkflowContext` that carries data between steps (quality profile, contour data, OCR results)
- Collects step trace, timing, and output into `WorkflowRunResult`

**Executors**
- `ToolStepExecutor` - Invokes a single plugin tool, resolving arguments from context variables
- `BranchDispatcherExecutor` - Reads a condition variable from context and dispatches to the matching branch (e.g., Tesseract-specific steps)

**Workflow Definitions**
- `receipt_preprocess_v1` - Preprocessing-only pipeline
- `receipt_extract_v1` / `v2` / `v3` - Full extraction workflows with branching and multi-variant OCR

#### 6. OCR Engine

**TesseractOcrEngine**
- In-process Tesseract 5 wrapper using the `Tesseract` NuGet package
- Configurable `tessdata` path via `appsettings.json` with auto-discovery fallback
- Supports DPI and `ConfigVars` for engine tuning
- Automatic macOS arm64 native library resolution

#### 7. Inference Layer

**U2NetpOnnxPredictor**
- Loads and runs U2NETP ONNX model for foreground segmentation
- Produces probability masks used by Perspective, Background Removal, and Contour Extraction plugins
- Loaded once at startup and shared across plugins

**DocumentContourExtractor**
- Post-processes probability masks into binary document masks
- Extracts 4-corner quadrilaterals with MinAreaRect fallback
- Returns confidence score for detection quality

#### 8. Extraction Pipeline

**RuleBasedReceiptExtractor**
- Normalizes OCR tokens, classifies lines into zones (header, body, totals, footer)
- Field extractors for merchant, date/time, line items, and totals
- Validates extracted data for consistency

**Exporters**
- `ReceiptJsonExporter` - Structured JSON output
- `ReceiptCsvExporter` - Tabular CSV output

### Data Flow

**Chat Mode:**
1. User sends a natural language prompt
2. AgentRunner forwards it to OpenAI LLM
3. LLM selects and calls one or more tools from the plugin registry
4. Plugin loads input image from storage, processes it, saves output
5. Result returned to LLM, which formats a response for the user

**CLI Mode:**
1. User runs a command with tool name or workflow ID and image reference
2. CliRunner invokes the tool directly or triggers the workflow engine
3. Plugin or workflow executes, results printed to console

**Workflow Execution:**
1. WorkflowRunner loads definition from catalog and compiles it
2. Each step's executor invokes the corresponding plugin through the tool registry
3. Step output reference becomes the next step's input
4. WorkflowContext carries inter-step data (quality profile, contour data, OCR text)
5. Branch steps check a context variable and dispatch to the matching branch
6. Final result with trace, output references, and context returned to caller

## Installation and Setup

### Prerequisites

- .NET 9.0 SDK
- OpenAI API key
- Docker (optional, for local SFTP testing)

### Environment Variables

Create a `.env` file in the project root:

```bash
# Required
OPENAI_API_KEY=your_openai_api_key_here

# Optional (defaults shown)
OPENAI_CHATCOMPLETION_DEPLOYMENT=gpt-4o-mini

# SFTP password (if using SFTP storage)
SFTP_PASSWORD=your_sftp_password
```

### Configuration

Edit `OcrAgent/appsettings.json` to configure storage:

**Local Storage (Default):**
```json
{
  "Storage": {
    "Provider": "local",
    "Local": {
      "RootPath": "./Storage"
    }
  }
}
```

**SFTP Storage:**
```json
{
  "Storage": {
    "Provider": "sftp",
    "Sftp": {
      "Host": "localhost",
      "Port": 2222,
      "Username": "ocragent",
      "AuthenticationMethod": "password",
      "Password": null,
      "RemoteBasePath": "/ocr",
      "ConnectionPool": {
        "MinConnections": 1,
        "MaxConnections": 10,
        "ConnectionIdleTimeout": 300,
        "KeepAliveInterval": 60
      },
      "Retry": {
        "MaxRetries": 3,
        "InitialDelayMs": 100,
        "MaxDelayMs": 5000,
        "BackoffMultiplier": 2.0
      }
    }
  }
}
```

See `Documentation/SFTP_STORAGE.md` for complete SFTP configuration details.

### Build and Run

```bash
# Clone repository
cd Source/ML-25-26-01-AI-Preprocessing-Agent-Plugin-for-Improving-OCR-Performance

# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run application
cd OcrAgent
dotnet run
```

### Docker SFTP Setup (Optional)

For local SFTP testing:

```bash
cd docker/sftp
./start.sh

# View logs
./logs.sh

# Stop server
./stop.sh
```

## Usage Guide

### Starting the Agent

```bash
cd OcrAgent
dotnet run
```

The agent will start in interactive mode with a prompt:

```
OcrAgent - AI-Powered Image Preprocessing for OCR
Type 'exit' to quit.

>
```

### Command Examples

#### Register an Image

```
> Register the image at /path/to/receipt.jpg
```

The agent will store the image and return a reference ID like `RECEIPT001`.

#### Basic Preprocessing

```
> Enhance contrast of RECEIPT001
> Denoise RECEIPT001 using bilateral filter
> Binarize RECEIPT001 using otsu method
> Rotate RECEIPT001 from 0 to 90 degrees in steps of 15
```

#### Perspective Correction (Mobile Captures)

```
> Fix perspective of RECEIPT001
> Correct perspective distortion in IMG001 with high sensitivity
```

#### Background Removal

```
> Remove background from RECEIPT001 on white
> Remove document background from IMG001 as transparent PNG
```

#### Multi-Step Processing

```
> For RECEIPT001: first correct perspective, then denoise with bilateral filter, then enhance contrast using clahe, finally binarize with adaptive gaussian
```

#### Parameter Ranges

```
> Zoom into RECEIPT001 from 10% to 50% in steps of 10%
> Adjust brightness of IMG001 from -30 to 30 in steps of 10
> Rotate DOC001 from -5 to 5 degrees in steps of 1 degree
```

#### Color and Invert

```
> Fix color cast in PHOTO001 using auto white balance
> Invert colors of SCAN001 automatically if dark background detected
```

#### Morphological Operations

```
> Apply morphological closing to BINARY001 with kernel size 3
> Clean up BINARY001 using morphological open operation
```

### Command Reference

#### Geometric Operations

| Command | Description | Parameters |
|---------|-------------|------------|
| `ZoomInImage` | Zoom into image regions | reference, start%, end%, step% |
| `RotateImage` | Rotate by angle ranges | reference, startAngle, endAngle, step |
| `CorrectPerspective` | Fix angled mobile photos | reference, sensitivity?, targetW?, targetH? |
| `DeskewImage` | Auto-correct skew/tilt | reference, maxAngle?, method?, preserveSize? |
| `ResizeImage` | Resize to dimensions | reference, width, height, maintainAspect? |

#### Enhancement Operations

| Command | Description | Parameters |
|---------|-------------|------------|
| `EnhanceContrast` | Improve contrast | reference, method (auto/histogram/clahe) |
| `AdjustBrightness` | Adjust brightness | reference, start, end, step |
| `DenoiseImage` | Remove noise | reference, method (bilateral/median/nlm), strength? |
| `SharpenImage` | Enhance sharpness | reference, amount?, radius?, threshold? |
| `CorrectColor` | Fix color casts | reference, method (auto/gray_world/white_patch) |
| `InvertImage` | Invert colors | reference, mode (auto/always/smart) |

#### Segmentation Operations

| Command | Description | Parameters |
|---------|-------------|------------|
| `BinarizeImage` | Convert to B&W | reference, method (otsu/adaptive_mean/adaptive_gaussian) |
| `ApplyMorphology` | Morphological operations | reference, operation (open/close/erode/dilate/gradient), kernelSize?, iterations? |
| `RemoveDocumentBackground` | Remove background | reference, mode (on_white/mask_only/transparent), sensitivity?, feather? |

### Output and Metadata

Each processed image is saved with:

1. **Image file** - In configured storage location
2. **Metadata file** - JSON sidecar with:
   - Original dimensions
   - Operation performed
   - Parameters used
   - Parent reference (lineage tracking)
   - Confidence scores (where applicable)
   - Processing timestamp

Example metadata:
```json
{
  "reference": "RECEIPT001_contrast_clahe",
  "parentReference": "RECEIPT001",
  "operation": "EnhanceContrast",
  "parameters": {
    "method": "clahe"
  },
  "originalWidth": 1920,
  "originalHeight": 1080,
  "timestamp": "2026-03-05T00:15:30Z"
}
```

## Project Structure

```
.
├── OcrAgent/
│   ├── Agent/
│   │   └── AgentRunner.cs           # AI agent orchestration
│   ├── Inference/
│   │   ├── U2NetpOnnxPredictor.cs   # ML model inference
│   │   └── DocumentContourExtractor.cs
│   ├── Plugins/
│   │   ├── Geometric/               # Geometric transformations
│   │   ├── Enhancement/             # Image enhancement
│   │   ├── Segmentation/            # Segmentation operations
│   │   ├── IImageProcessingTool.cs
│   │   ├── PluginAggregator.cs
│   │   └── PluginHelpers.cs
│   ├── Storage/
│   │   ├── IImageStore.cs
│   │   ├── LocalFileImageStore.cs
│   │   ├── SftpImageStore.cs
│   │   ├── SftpConnectionPool.cs
│   │   ├── SftpHealthMonitor.cs
│   │   ├── StorageConfiguration.cs
│   │   └── StorageFactory.cs
│   ├── appsettings.json
│   └── Program.cs
├── UnitTests/
│   ├── Plugins/
│   ├── Storage/
│   └── IntegrationTests.cs
├── Documentation/
│   ├── README.md                    # This file
│   ├── PROJECT_PLAN.md             # Sprint planning
│   ├── SFTP_STORAGE.md             # SFTP configuration
│   └── PROGRESS_UPDATE_ISSUE.md
├── docker/
│   └── sftp/                       # Local SFTP server
├── .env.example
└── README.md
```

## Advanced Features

### U2NETP-Based Background Removal

The document background removal plugin uses a U2NETP deep learning model for accurate document segmentation:

1. Primary strategy uses U2NETP ONNX model for probability mask generation
2. Fallback to multi-strategy computer vision methods if model unavailable
3. Combines multiple techniques: Otsu thresholding, HSV filtering, edge detection, GrabCut
4. Weighted pixel voting for robust mask generation
5. Output modes: white background, mask only, or transparent PNG

### SFTP Connection Pooling

The SFTP storage backend implements enterprise-grade features:

- Thread-safe connection pool with configurable size
- Idle connection cleanup with configurable timeout
- Keep-alive mechanism to prevent connection drops
- Health checks for connection monitoring
- Automatic retry with exponential backoff
- Comprehensive error handling and recovery

### Perspective Correction Algorithm

Multi-strategy perspective detection:

1. Full-frame detection for documents filling the entire frame
2. GrabCut-based foreground segmentation
3. Edge-based contour detection with preprocessing
4. MinAreaRect regularization for bumpy contours
5. Adaptive margin calculation based on image characteristics
6. Corner snapping for border-touching documents

## Testing

### Run Unit Tests

```bash
dotnet test
```

### Run Specific Test Suite

```bash
# Plugin tests
dotnet test --filter "FullyQualifiedName~Plugins"

# Storage tests
dotnet test --filter "FullyQualifiedName~Storage"

# SFTP integration tests (requires Docker)
cd docker/sftp && ./start.sh
dotnet test --filter "FullyQualifiedName~SftpImageStore"
```

### Test Coverage

- Unit tests for all plugins
- Integration tests for storage backends
- SFTP connection pool tests
- Configuration validation tests
- End-to-end agent tests

## Troubleshooting

### Common Issues

**"Missing OPENAI_API_KEY environment variable"**
- Create `.env` file in project root
- Add `OPENAI_API_KEY=your_key`

**"Failed to initialize storage"**
- Check `appsettings.json` configuration
- Verify SFTP credentials if using remote storage
- Ensure storage directory is writable for local storage

**"SFTP Permission denied"**
- Verify remote path is writable
- Check user permissions on SFTP server
- See `Documentation/SFTP_STORAGE.md` for detailed troubleshooting

**"Model file not found" (U2NETP)**
- Download u2netp.onnx model
- Place in `OcrAgent/` directory
- Model is optional, fallback methods will be used

### Debug Mode

Enable debug output for plugins:

```
> Remove background from RECEIPT001 with debug enabled
> Correct perspective of IMG001 with debug mode
```

Debug images saved to storage under `_debug/` folder.

## Performance Considerations

### Storage Performance

- Local storage: Best for development and small-scale usage
- SFTP storage: Suitable for production with remote storage requirements
- Connection pooling reduces overhead for SFTP operations
- Adjust pool size based on concurrent operation needs

### Image Processing

- Large images take longer to process
- Perspective correction is computationally intensive
- U2NETP model requires significant memory
- Consider resizing very large images before processing

### Optimization Tips

- Use specific parameters instead of wide ranges
- Limit number of generated variants
- Use appropriate compression for storage
- Configure SFTP connection pool based on workload

## Limitations

- Maximum 20 variants per operation to prevent excessive output
- U2NETP model requires 320x320 minimum input size
- Perspective correction works best on rectangular documents
- SFTP operations require stable network connection
- Agent requires internet connection for OpenAI API

## Future Enhancements

- OCR integration with engine abstraction
- Best variant selection based on OCR quality metrics
- Document workflow templates (receipts, invoices, etc)
- Batch processing support
- Performance benchmarking tools
- Additional authentication methods for SFTP
- Caching layer for frequently accessed images
