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
    /// Generates Markdown files from an OpenAPI document.
    /// </summary>
    /// <param name="document">The OpenAPI document to convert.</param>
    /// <param name="outputFolder">The folder where the Markdown files will be generated.</param>
    /// <param name="metadata">Optional metadata to include in the YAML front-matter of the generated files.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of files written.</returns>
    public static async Task<int> GenerateAsync(OpenApiDocument document, string outputFolder, IDictionary<string, string>? metadata = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentNullException(nameof(outputFolder));

        Directory.CreateDirectory(outputFolder);

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
            return 0;
        }

        // Default: no progress
        return await GenerateAsync(document, outputFolder, metadata, progress: null).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates Markdown files from an OpenAPI document with progress reporting.
    /// </summary>
    /// <param name="document">The OpenAPI document to convert.</param>
    /// <param name="outputFolder">The folder where the Markdown files will be generated.</param>
    /// <param name="metadata">Optional metadata to include in the YAML front-matter of the generated files.</param>
    /// <param name="progress">Optional progress reporter for the number of files written.</param>
    /// <param name="filenameProgress">Optional progress reporter for the name of the file currently being written.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of files written.</returns>
    public static async Task<int> GenerateAsync(OpenApiDocument document, string outputFolder, IDictionary<string, string>? metadata = null, IProgress<int>? progress = null, IProgress<string?>? filenameProgress = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentNullException(nameof(outputFolder));

        Directory.CreateDirectory(outputFolder);

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
            return 0;
        }

        // Pre-calc how many operation files will be created so callers can show a determinate progress bar
        var operationCount = 0;
        foreach (var pathProp in paths.EnumerateObject())
        {
            var methods = pathProp.Value;
            if (methods.ValueKind != JsonValueKind.Object) continue;
            foreach (var methodProp in methods.EnumerateObject())
            {
                var methodNameLower = methodProp.Name.ToLowerInvariant();
                var allowed = new[] { "get", "post", "put", "delete", "patch", "head", "options", "trace" };
                if (Array.IndexOf(allowed, methodNameLower) < 0) continue;
                operationCount++;
            }
        }

        int written = 0;
        var writtenPaths = new List<string>();
        // shared per-run expansion cache for component expansions
        var expansionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Generate Operations
        var opGen = new OperationGenerator(root, outputFolder, apiTitle, metadata, expansionCache, progress, filenameProgress);
        var (opWritten, indexEntries, opPaths) = await opGen.GenerateOperationsAsync(paths).ConfigureAwait(false);
        written += opWritten;
        writtenPaths.AddRange(opPaths);

        // 2. Generate Components
        var compGen = new ComponentGenerator(root, outputFolder, metadata, expansionCache, progress, filenameProgress);
        var (compWritten, compPaths) = await compGen.GenerateComponentsAsync().ConfigureAwait(false);
        written += compWritten;
        writtenPaths.AddRange(compPaths);

        // 3. Generate Index
        var idxGen = new IndexGenerator(outputFolder, apiTitle, metadata, progress, filenameProgress);
        var (idxWritten, idxPaths) = await idxGen.GenerateIndexAsync(indexEntries).ConfigureAwait(false);
        written += idxWritten;
        writtenPaths.AddRange(idxPaths);

        // Print generated files list once at the end
        try
        {
            foreach (var p in writtenPaths)
                Console.WriteLine($"Wrote {p}");
        }
        catch { }

        return written;
    }
}

