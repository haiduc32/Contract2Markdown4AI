# Contract2Markdown4AI

A .NET library and CLI tool to convert OpenAPI/Swagger contracts into Markdown files optimized for AI consumption (e.g., for use with LLMs, RAG, or AI agents).

## Features

- Converts OpenAPI 3.0 and Swagger 2.0 (JSON/YAML) to Markdown.
- Generates separate files for each operation for better granularity.
- Includes schemas and security definitions.
- Optimized for AI context windows.

## Installation

```bash
dotnet add package Contract2Markdown4AI
```

## Usage

### Library

The library provides two main ways to generate Markdown: writing directly to disk or streaming in-memory.

#### 1. Generate and Save to Disk

Use `GenerateFilesAsync` to process an OpenAPI document and save the resulting Markdown files to a specified folder.

```csharp
using Contract2Markdown4AI;
using NSwag;

// Load document
var document = await OpenApiDocument.FromFileAsync("petstore.json");

var outputFolder = "./output";
var metadata = new Dictionary<string, string> { { "Project", "PetStore" } };

// Basic usage
int filesWritten = await OpenApiToMarkdown.GenerateFilesAsync(
    document, 
    outputFolder, 
    metadata);

// Advanced usage with all options
int totalFiles = await OpenApiToMarkdown.GenerateFilesAsync(
    document,
    outputFolder,
    metadata,
    progress: new Progress<int>(count => Console.WriteLine($"Files written: {count}")),
    filenameProgress: new Progress<string?>(name => Console.WriteLine($"Writing: {name}")),
    generateIndependentSchemas: true, // Schemas in separate files
    skipSchemas: false                // Set to true to skip schemas
);
```

#### 2. Generate In-Memory (Streaming)

Use `GenerateAsync` to get an `IAsyncEnumerable<GeneratedFile>` if you want to process the content in-memory without writing to disk immediately.

```csharp
using Contract2Markdown4AI;

await foreach (var file in OpenApiToMarkdown.GenerateAsync(document))
{
    Console.WriteLine($"Generated: {file.FileName} ({file.FileType})");
    // Access file.Content, file.Metadata, etc.
}
```

#### Data Models

**GeneratedFile**
- `FileName`: The suggested relative path/name for the file.
- `Content`: The generated Markdown content.
- `FileType`: A `GeneratedFileType` enum value.
- `Metadata`: Dictionary of metadata associated with the file.

**GeneratedFileType (Enum)**
- `Operation`: Represents an API endpoint.
- `Schema`: Represents a data model/schema.
- `Index`: Represents the main index/table of contents.
- `Other`: Any other generated content.

#### 3. Advanced: Fine-grained Generation

If you need more control, you can use the individual generator classes: `OperationGenerator`, `SchemaGenerator`, and `IndexGenerator`.

```csharp
using System.Text.Json;
using Contract2Markdown4AI;

// You need the raw JsonElement from the OpenAPI document
var docJson = document.ToJson();
using var jsonDoc = JsonDocument.Parse(docJson);
var root = jsonDoc.RootElement;

var expansionCache = new Dictionary<string, string>();

// Generate only operations
var opGen = new OperationGenerator(root, "./output", "My API", null, expansionCache, null, null);
var (opCount, indexEntries, opPaths) = await opGen.GenerateOperationsAsync(root.GetProperty("paths"));

// Generate only schemas
var schemaGen = new SchemaGenerator(root, "./output", null, expansionCache, null, null);
var (schemaCount, schemaPaths) = await schemaGen.GenerateSchemasAsync();

// Generate only the index
var idxGen = new IndexGenerator("./output", "My API", null, null, null);
await idxGen.GenerateIndexAsync(indexEntries);
```

### CLI

If you want to use the CLI tool:

```bash
dotnet tool install -g C2M4AI
```

#### Usage

```bash
c2m4ai <input-file> [options]
```

#### Options

- `input`: (Required) Path to the OpenAPI JSON or YAML file.
- `-o, --output <folder>`: Output folder for generated markdown files. (Default: `output_md`)
- `-m, --meta <key:value>`: Metadata entries to include in the generated files. Can be specified multiple times.
- `-s, --schemas-folder`: Generate schemas as independent files in a dedicated `schemas` folder.
- `--no-schema`: Do not generate any schema files.

> [!NOTE]
> `--schemas-folder` and `--no-schema` are mutually exclusive.

#### Examples

**Basic usage:**
```bash
c2m4ai petstore.json -o ./docs
```

**With metadata:**
```bash
c2m4ai petstore.yaml -m "Project:PetStore" -m "Version:1.0.0"
```

**Generating schemas in a separate folder:**
```bash
c2m4ai petstore.json -s
```

**Excluding schemas:**
```bash
c2m4ai petstore.json --no-schema
```

## License

MIT
