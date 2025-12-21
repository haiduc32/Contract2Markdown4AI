using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Contract2Markdown4AI;

public class ComponentGenerator
{
    private readonly JsonElement _root;
    private readonly string _outputFolder;
    private readonly IDictionary<string, string>? _metadata;
    private readonly Dictionary<string, string> _expansionCache;
    private readonly IProgress<int>? _progress;
    private readonly IProgress<string?>? _filenameProgress;

    public ComponentGenerator(
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

    public async Task<(int WrittenCount, List<string> WrittenPaths)> GenerateComponentsAsync()
    {
        int written = 0;
        var writtenPaths = new List<string>();

        try
        {
            var schemasToDocument = new List<(string name, JsonElement schema)>();
            
            // Check for OpenAPI 3.0 components/schemas
            if (_root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Object &&
                components.TryGetProperty("schemas", out var schemas) && schemas.ValueKind == JsonValueKind.Object)
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
                comp.AppendLine("# Components");
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

                var compPath = Path.Combine(_outputFolder, "components.md");
                await File.WriteAllTextAsync(compPath, comp.ToString()).ConfigureAwait(false);
                written++;
                writtenPaths.Add(compPath);
                try { _progress?.Report(written); } catch { }
                try { _filenameProgress?.Report(Path.GetFileName(compPath)); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write components.md: {ex.Message}");
        }

        return (written, writtenPaths);
    }
}
