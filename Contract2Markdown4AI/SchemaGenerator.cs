using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Contract2Markdown4AI;

public class SchemaGenerator
{
    private readonly JsonElement _root;
    private readonly string _outputFolder;
    private readonly IDictionary<string, string>? _metadata;
    private readonly Dictionary<string, string> _expansionCache;
    private readonly IProgress<int>? _progress;
    private readonly IProgress<string?>? _filenameProgress;

    public SchemaGenerator(
        JsonElement root,
        string outputFolder,
        IDictionary<string, string>? metadata,
        Dictionary<string, string> expansionCache,
        IProgress<int>? progress,
        IProgress<string?>? filenameProgress)
    {
        _root = root;
        _outputFolder = outputFolder;
        _metadata = metadata;
        _expansionCache = expansionCache;
        _progress = progress;
        _filenameProgress = filenameProgress;
    }

    public async Task<(int WrittenCount, List<string> WrittenPaths)> GenerateSchemasAsync()
    {
        int written = 0;
        var writtenPaths = new List<string>();

        await foreach (var schema in GetSchemasAsync().ConfigureAwait(false))
        {
            var schemaPath = Path.Combine(_outputFolder, schema.FileName);
            await Helpers.WriteAllTextAsync(schemaPath, schema.Content).ConfigureAwait(false);
            written++;
            writtenPaths.Add(schemaPath);
            try { _progress?.Report(written); } catch { }
            try { _filenameProgress?.Report(schema.FileName); } catch { }
        }

        return (written, writtenPaths);
    }

    public async IAsyncEnumerable<(string FileName, string Content)> GetSchemasAsync(bool independent = false)
    {
        var schemasToDocument = new List<(string name, JsonElement schema)>();
        
        // Check for OpenAPI 3.0 schemas
        if (_root.TryGetProperty("components", out var components) && components.TryGetProperty("schemas", out var schemas) && schemas.ValueKind == JsonValueKind.Object)
        {
            foreach (var schema in schemas.EnumerateObject())
            {
                schemasToDocument.Add((schema.Name, schema.Value));
            }
        }
        
        // Check for Swagger 2.0 definitions
        if (_root.TryGetProperty("definitions", out var definitions) && definitions.ValueKind == JsonValueKind.Object)
        {
            foreach (var def in definitions.EnumerateObject())
            {
                schemasToDocument.Add((def.Name, def.Value));
            }
        }

        if (schemasToDocument.Count > 0)
        {
            if (independent)
            {
                foreach (var (name, schemaValue) in schemasToDocument)
                {
                    var comp = new StringBuilder();
                    if (_metadata != null && _metadata.Count > 0)
                    {
                        comp.AppendLine("---");
                        foreach (var kv in _metadata)
                        {
                            var v = kv.Value?.Replace("\"", "\\\"") ?? string.Empty;
                            comp.AppendLine($"{kv.Key}: \"{v}\"");
                        }
                        comp.AppendLine("---");
                        comp.AppendLine();
                    }
                    comp.AppendLine($"# {name}");
                    comp.AppendLine();
                    try
                    {
                        var expanded = Helpers.SummarizeSchema(schemaValue, _root, _expansionCache);
                        if (!string.IsNullOrWhiteSpace(expanded))
                        {
                            comp.AppendLine("```");
                            comp.AppendLine(expanded.TrimEnd());
                            comp.AppendLine("```");
                        }
                        else
                        {
                            comp.AppendLine("```json");
                            comp.AppendLine(JsonSerializer.Serialize(schemaValue, new JsonSerializerOptions { WriteIndented = true }));
                            comp.AppendLine("```");
                        }
                    }
                    catch (Exception ex)
                    {
                        comp.AppendLine($"Failed to expand {name}: {ex.Message}");
                    }
                    comp.AppendLine();

                    yield return (Path.Combine("schemas", $"{name}.md"), comp.ToString());
                }
            }
            else
            {
                var comp = new StringBuilder();
                if (_metadata != null && _metadata.Count > 0)
                {
                    comp.AppendLine("---");
                    foreach (var kv in _metadata)
                    {
                        var v = kv.Value?.Replace("\"", "\\\"") ?? string.Empty;
                        comp.AppendLine($"{kv.Key}: \"{v}\"");
                    }
                    comp.AppendLine("---");
                    comp.AppendLine();
                }
                comp.AppendLine("# Schemas");
                comp.AppendLine();
                
                foreach (var (name, schemaValue) in schemasToDocument)
                {
                    comp.AppendLine($"## {name}");
                    comp.AppendLine();
                    try
                    {
                        var expanded = Helpers.SummarizeSchema(schemaValue, _root, _expansionCache);
                        if (!string.IsNullOrWhiteSpace(expanded))
                        {
                            comp.AppendLine("```");
                            comp.AppendLine(expanded.TrimEnd());
                            comp.AppendLine("```");
                        }
                        else
                        {
                            comp.AppendLine("```json");
                            comp.AppendLine(JsonSerializer.Serialize(schemaValue, new JsonSerializerOptions { WriteIndented = true }));
                            comp.AppendLine("```");
                        }
                    }
                    catch (Exception ex)
                    {
                        comp.AppendLine($"Failed to expand {name}: {ex.Message}");
                    }
                    comp.AppendLine();
                }

                yield return ("Schemas.md", comp.ToString());
            }
        }
    }
}
