# MyCloudProjectSample

A .NET 8 cloud-based experiment framework designed for running distributed experiments using Azure Storage services. This solution provides a complete infrastructure for queue-based experiment processing with input/output management through Azure Blob Storage and result tracking via Azure Table Storage.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Solution Structure](#solution-structure)
- [Projects](#projects)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Usage](#usage)
- [Development](#development)

## Overview

This solution implements a cloud-native experiment execution framework that:
- Receives experiment requests from an Azure Storage Queue
- Downloads input data from Azure Blob Storage
- Executes custom experiments with configurable parameters
- Uploads results to Azure Blob Storage and Azure Table Storage
- Provides a clean separation of concerns through interface-based design

The framework is designed for educational purposes as part of the **Software Engineering in the Cloud** course (SE Cloud 2025-2026) at Frankfurt University of Applied Sciences.

## Architecture

The solution follows a message-driven architecture with the following workflow:

1. **Request Reception** (Step 3): Application polls Azure Storage Queue for experiment requests
2. **Input Download** (Step 4): Downloads experiment input files from Azure Blob Storage
3. **Experiment Execution**: Runs the custom experiment logic with the downloaded input
4. **Result Upload** (Step 5): Uploads experiment results to Azure Blob Storage and Table Storage
5. **Commit**: Removes the processed message from the queue

```
┌─────────────┐      ┌──────────────┐      ┌─────────────┐
│   Queue     │─────→│  Main App    │─────→│ Blob Storage│
│  (Request)  │      │  (Processor) │      │   (I/O)     │
└─────────────┘      └──────────────┘      └─────────────┘
							│
							↓
					 ┌──────────────┐
					 │Table Storage │
					 │  (Results)   │
					 └──────────────┘
```

## Solution Structure

The solution consists of three projects organized in a layered architecture:

```
MyCloudProjectSample/
├── MyCloudProject/              # Main application (console host)
├── MyCloudProject.Common/       # Shared contracts and utilities
└── MyExperiment/                # Experiment implementation and Azure providers
```

## Projects

### 1. MyCloudProject (Console Application)

**Purpose**: Main entry point and orchestration layer

**Key Files**:
- `Program.cs` - Application entry point with message processing loop
- `appsettings.json` - Configuration for logging and Azure services
- `appsettings.dev.json` - Development-specific configuration

**Responsibilities**:
- Application lifecycle management
- Configuration initialization
- Logging setup
- Main processing loop that:
  - Receives experiment requests from queue
  - Downloads input files
  - Executes experiments
  - Uploads results
  - Commits processed messages

**Dependencies**:
- MyCloudProject.Common
- MyExperiment
- Microsoft.VisualStudio.Azure.Containers.Tools.Targets (v1.20.1)

### 2. MyCloudProject.Common (Class Library)

**Purpose**: Shared contracts, interfaces, and common utilities

**Key Files**:
- `IExperiment.cs` - Defines the experiment execution contract
- `IExperimentResult.cs` - Interface for experiment results
- `IExerimentRequest.cs` - Interface for experiment requests (note: typo in original)
- `IStorageProvider.cs` - Contract for all storage operations
- `InitHelpers.cs` - Helper methods for configuration and logging initialization

**Responsibilities**:
- Define core abstractions for the experiment framework
- Provide configuration and logging utilities
- Establish contracts for storage operations
- Enable dependency injection through interface-based design

**Dependencies**:
- Microsoft.Extensions.Configuration.* (v8.0.0) - Configuration abstraction
- Microsoft.Extensions.Logging.* (v8.0.0-8.0.1) - Logging infrastructure

### 3. MyExperiment (Class Library)

**Purpose**: Concrete implementation of experiment logic and Azure storage providers

**Key Files**:
- `Experiment.cs` - Main experiment implementation
- `ExperimentResult.cs` - Result data model with Azure Table Storage support
- `ExerimentRequestMessage.cs` - Request message implementation
- `AzureStorageProvider.cs` - Azure-specific storage operations (placeholder implementation)
- `MyConfig.cs` - Configuration model for Azure services

**Responsibilities**:
- Implement `IExperiment` interface with custom experiment logic
- Provide Azure Storage integration (Queue, Blob, Table)
- Define experiment-specific configuration
- Handle serialization/deserialization of experiment data

**Dependencies**:
- MyCloudProject.Common
- Azure.Storage.Queues (v12.18.0) - Azure Queue Storage client
- Azure.Storage.Blobs (v12.20.0) - Azure Blob Storage client
- Azure.Data.Tables (v12.8.3) - Azure Table Storage client

**Key Classes**:

#### `Experiment`
Implements the ML/data processing experiment that runs in the cloud. Currently contains placeholder logic that needs to be replaced with specific experiment implementation.

#### `ExperimentResult`
Implements `ITableEntity` for direct Azure Table Storage persistence. Tracks:
- Experiment metadata (ID, name, description)
- Timing information (start, end, duration)
- Input/output file references
- Custom metrics (accuracy, duration)

#### `AzureStorageProvider`
Provides Azure Storage operations (currently with placeholder implementations):
- `ReceiveExperimentRequestAsync()` - Poll queue for new requests
- `DownloadInputAsync()` - Download input files from blob storage
- `UploadResultAsync()` - Upload results to blob storage and table storage
- `CommitRequestAsync()` - Remove processed message from queue

## Getting Started

### Prerequisites

- .NET 8 SDK
- Azure Storage Account (or Azure Storage Emulator/Azurite for local development)
- Visual Studio 2022+ or Visual Studio Code

### Installation

1. Clone the repository:
```bash
git clone https://github.com/UniversityOfAppliedSciencesFrankfurt/se-cloud-2025-2026.git
cd se-cloud-2025-2026/Source/MyCloudProjectSample
```

2. Restore NuGet packages:
```bash
dotnet restore
```

3. Build the solution:
```bash
dotnet build
```

## Configuration

### appsettings.json

Configure the application through `MyCloudProject/appsettings.json`:

```json
{
  "Logging": {
	"IncludeScopes": false,
	"LogLevel": {
	  "Default": "Error",
	  "MyExperiment": "Debug"
	}
  },
  "MyConfig": {
	"GroupId": "your-group-id",
	"StorageConnectionString": "UseDevelopmentStorage=true;",
	"TrainingContainer": "training-files",
	"ResultContainer": "result-files",
	"ResultTable": "results",
	"Queue": "trigger-queue"
  }
}
```

### Configuration Properties

| Property | Description |
|----------|-------------|
| `GroupId` | Your project/group identifier |
| `StorageConnectionString` | Azure Storage connection string (use `UseDevelopmentStorage=true;` for local emulator) |
| `TrainingContainer` | Blob container name for input/training files |
| `ResultContainer` | Blob container name for output/result files |
| `ResultTable` | Table Storage table name for experiment results |
| `Queue` | Queue name for experiment triggers |

### Environment-Specific Configuration

Set the `MYCLOUDPROJECT_ENVIRONMENT` environment variable to load environment-specific configuration:

```bash
# Windows PowerShell
$env:MYCLOUDPROJECT_ENVIRONMENT = "dev"

# Linux/Mac
export MYCLOUDPROJECT_ENVIRONMENT=dev
```

This will load `appsettings.dev.json` instead of the default configuration.

## Usage

### Running the Application

1. Start Azure Storage Emulator or Azurite (for local development):
```bash
# Using Azurite
azurite --silent --location c:\azurite --debug c:\azurite\debug.log
```

2. Run the application:
```bash
cd MyCloudProject
dotnet run
```

3. The application will:
   - Initialize configuration and logging
   - Poll the configured queue every 500ms for new experiment requests
   - Process any received requests automatically
   - Log all operations to the console

### Stopping the Application

Press `Ctrl+C` to gracefully shut down the application. The cancellation token will be triggered, allowing in-progress operations to complete.

## Development

### Implementing Your Experiment

To implement your custom experiment logic:

1. **Update `Experiment.cs`** in the `MyExperiment` project:

```csharp
public Task<IExperimentResult> RunAsync(string inputData)
{
	// Read and parse input file
	var data = File.ReadAllText(inputData);

	// Implement your experiment logic here
	var result = YourExperimentLogic(data);

	// Create result object
	ExperimentResult res = new ExperimentResult(this.config.GroupId, Guid.NewGuid().ToString())
	{
		StartTimeUtc = DateTime.UtcNow,
		Name = "Your Experiment Name",
		Description = "Experiment description",
		Accuracy = result.Accuracy,
		// Add your custom properties
	};

	res.EndTimeUtc = DateTime.UtcNow;
	res.DurationSec = (long)(res.EndTimeUtc.Value - res.StartTimeUtc.Value).TotalSeconds;

	return Task.FromResult<IExperimentResult>(res);
}
```

2. **Implement `AzureStorageProvider.cs`** methods:

The following methods currently throw `NotImplementedException` and need to be implemented:

- `ReceiveExperimentRequestAsync()` - Receive and deserialize queue messages
- `DownloadInputAsync()` - Download blob files and return local path
- `UploadResultAsync()` - Upload results to blob and table storage
- `CommitRequestAsync()` - Delete processed queue message

### Adding Custom Experiment Properties

1. Add properties to `ExperimentResult.cs`:
```csharp
public string CustomMetric { get; set; }
public double Score { get; set; }
```

2. Add properties to `IExperimentResult.cs` interface in `MyCloudProject.Common`

### Logging

The framework uses Microsoft.Extensions.Logging. Add logging statements:

```csharp
logger?.LogInformation("Processing experiment {ExperimentId}", request.ExperimentId);
logger?.LogError(ex, "Error processing experiment {ExperimentId}", request.ExperimentId);
```

### Testing

1. Create test experiment requests in Azure Storage Queue
2. Place input files in the configured blob container
3. Run the application and monitor logs
4. Verify results in Azure Table Storage and result blob container

## Project Information

- **Course**: Software Engineering in the Cloud 2025-2026
- **Institution**: Frankfurt University of Applied Sciences
- **Framework**: .NET 8
- **Cloud Platform**: Microsoft Azure

## Notes

- The project contains a typo in several interface names (`IExerimentRequest` instead of `IExperimentRequest`)
- Several methods in `AzureStorageProvider` are placeholder implementations
- Update the `_projectName` variable in `Program.cs` with your actual project identifier
- For production use, replace `UseDevelopmentStorage=true` with actual Azure Storage connection strings
- Consider adding proper error handling and retry logic for production deployments

## License

This project is part of the academic curriculum at Frankfurt University of Applied Sciences.
