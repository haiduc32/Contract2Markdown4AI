# Contract2Markdown4AI

A .NET library and CLI tool to convert OpenAPI/Swagger contracts into Markdown files optimized for AI consumption (e.g., for use with LLMs, RAG, or AI agents).

## Features

- Converts OpenAPI 3.0 and Swagger 2.0 (JSON/YAML) to Markdown.
- Generates separate files for each operation for better granularity.
- Includes component schemas and security definitions.
- Optimized for AI context windows.

## Installation

```bash
dotnet add package Contract2Markdown4AI
```

## Usage

### Library

```csharp
using Contract2Markdown4AI;
using NSwag;

// Load JSON contract
var document = await OpenApiDocument.FromFileAsync("petstore.json");

// Or load YAML contract
// var yaml = await File.ReadAllTextAsync("petstore.yaml");
// var document = await OpenApiYamlDocument.FromYamlAsync(yaml);

var outputFolder = "./output";
var metadata = new Dictionary<string, string> { { "Project", "PetStore" } };

int filesWritten = await OpenApiToMarkdown.GenerateFilesAsync(document, outputFolder, metadata);
```

### CLI

If you want to use the CLI tool:

```bash
dotnet tool install -g C2M4AI
```

Then run:

```bash
c2m4ai petstore.json -o ./output
```

## License

MIT
