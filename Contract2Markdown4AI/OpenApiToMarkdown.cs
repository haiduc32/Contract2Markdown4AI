using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contract2Markdown4AI;
using NSwag;

namespace Contract2Markdown4AI;

/// <summary>
/// Provides functionality to convert an OpenAPI document to Markdown files.
/// </summary>
public static class OpenApiToMarkdown
{
    /// <summary>
    /// Generates Markdown files from an OpenAPI document and writes them to disk.
    /// </summary>
    /// <param name="document">The OpenAPI document to convert.</param>
    /// <param name="outputFolder">The folder where the Markdown files will be generated.</param>
    /// <param name="metadata">Optional metadata to include in the YAML front-matter of the generated files.</param>
    /// <param name="progress">Optional progress reporter for the number of files written.</param>
    /// <param name="filenameProgress">Optional progress reporter for the name of the file currently being written.</param>
    /// <param name="generateIndependentSchemas">Whether to generate schemas as independent files.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of files written.</returns>
    public static async Task<int> GenerateFilesAsync(OpenApiDocument document, string outputFolder, IDictionary<string, string>? metadata = null, IProgress<int>? progress = null, IProgress<string?>? filenameProgress = null, bool generateIndependentSchemas = false)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentNullException(nameof(outputFolder));

        Directory.CreateDirectory(outputFolder);

        int written = 0;
        var writtenPaths = new List<string>();

        await foreach (var item in GenerateAsync(document, metadata, progress, filenameProgress, generateIndependentSchemas).ConfigureAwait(false))
        {
            var outPath = Path.Combine(outputFolder, item.FileName);
            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outPath, item.Content).ConfigureAwait(false);
            written++;
            writtenPaths.Add(outPath);
            try { progress?.Report(written); } catch { }
            try { filenameProgress?.Report(item.FileName); } catch { }
        }

        // Print generated files list once at the end
        try
        {
            foreach (var p in writtenPaths)
                Console.WriteLine($"Wrote {p}");
        }
        catch { }

        return written;
    }

    /// <summary>
    /// Generates Markdown content from an OpenAPI document and returns it as an asynchronous stream.
    /// </summary>
    /// <param name="document">The OpenAPI document to convert.</param>
    /// <param name="metadata">Optional metadata to include in the YAML front-matter of the generated files.</param>
    /// <param name="progress">Optional progress reporter for the number of files generated.</param>
    /// <param name="filenameProgress">Optional progress reporter for the name of the file currently being generated.</param>
    /// <param name="generateIndependentSchemas">Whether to generate schemas as independent files.</param>
    /// <returns>An asynchronous stream of generated files.</returns>
    public static async IAsyncEnumerable<GeneratedFile> GenerateAsync(OpenApiDocument document, IDictionary<string, string>? metadata = null, IProgress<int>? progress = null, IProgress<string?>? filenameProgress = null, bool generateIndependentSchemas = false)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var docJson = document.ToJson();
        using var jsonDoc = JsonDocument.Parse(docJson);
        var root = jsonDoc.RootElement;

        string apiTitle = "API";
        if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            if (info.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                apiTitle = t.GetString() ?? apiTitle;
        }

        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
        {
            Console.WriteLine("No paths found in the OpenAPI document.");
            yield break;
        }

        // shared per-run expansion cache for schema expansions
        var expansionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var indexEntries = new List<(string File, string Method, string Path, string OperationId, string FriendlyName)>();
        var schemaEntries = new List<(string Name, string File)>();

        // 1. Generate Operations
        var opGen = new OperationGenerator(root, string.Empty, apiTitle, metadata, expansionCache, progress, filenameProgress);
        await foreach (var op in opGen.GetOperationsAsync(paths).ConfigureAwait(false))
        {
            indexEntries.Add((op.FileName, op.Method, op.Path, op.OperationId, op.FriendlyName));
            
            var fileMetadata = new Dictionary<string, string>
            {
                { "Method", op.Method },
                { "Path", op.Path },
                { "OperationId", op.OperationId },
                { "FriendlyName", op.FriendlyName }
            };

            yield return new GeneratedFile
            {
                FileName = op.FileName,
                Content = op.Content,
                FileType = GeneratedFileType.Operation,
                Metadata = fileMetadata
            };
        }

        // 2. Generate Schemas
        bool schemasGenerated = false;
        var schemaGen = new SchemaGenerator(root, string.Empty, metadata, expansionCache, progress, filenameProgress);
        await foreach (var schema in schemaGen.GetSchemasAsync(generateIndependentSchemas).ConfigureAwait(false))
        {
            schemasGenerated = true;
            if (generateIndependentSchemas)
            {
                // Extract schema name from filename (schemas/Name.md)
                var name = Path.GetFileNameWithoutExtension(schema.FileName);
                schemaEntries.Add((name, schema.FileName));
            }

            yield return new GeneratedFile
            {
                FileName = schema.FileName,
                Content = schema.Content,
                FileType = GeneratedFileType.Schema
            };
        }

        // 3. Generate Index
        var idxGen = new IndexGenerator(string.Empty, apiTitle, metadata, progress, filenameProgress);
        var idx = idxGen.GetIndex(indexEntries, generateIndependentSchemas ? schemaEntries : (schemasGenerated ? new List<(string Name, string File)> { ("All Schemas", "schemas.md") } : null));
        yield return new GeneratedFile
        {
            FileName = idx.FileName,
            Content = idx.Content,
            FileType = GeneratedFileType.Index
        };
    }
}

