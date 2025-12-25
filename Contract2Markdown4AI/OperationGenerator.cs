using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Contract2Markdown4AI;

public class OperationGenerator
{
    private readonly JsonElement _root;
    private readonly string _outputFolder;
    private readonly IDictionary<string, string>? _metadata;
    private readonly Dictionary<string, string> _expansionCache;
    private readonly IProgress<int>? _progress;
    private readonly IProgress<string?>? _filenameProgress;
    private readonly string _apiTitle;

    public OperationGenerator(
        JsonElement root,
        string outputFolder,
        string apiTitle,
        IDictionary<string, string>? metadata,
        Dictionary<string, string> expansionCache,
        IProgress<int>? progress,
        IProgress<string?>? filenameProgress)
    {
        _root = root;
        _outputFolder = outputFolder;
        _apiTitle = apiTitle;
        _metadata = metadata;
        _expansionCache = expansionCache;
        _progress = progress;
        _filenameProgress = filenameProgress;
    }

    public async Task<(int WrittenCount, List<(string File, string Method, string Path, string OperationId, string FriendlyName)> IndexEntries, List<string> WrittenPaths)> GenerateOperationsAsync(JsonElement paths)
    {
        int written = 0;
        var indexEntries = new List<(string File, string Method, string Path, string OperationId, string FriendlyName)>();
        var writtenPaths = new List<string>();

        await foreach (var op in GetOperationsAsync(paths).ConfigureAwait(false))
        {
            var outPath = Path.Combine(_outputFolder, op.FileName);
            await File.WriteAllTextAsync(outPath, op.Content).ConfigureAwait(false);
            written++;
            writtenPaths.Add(outPath);
            try { _progress?.Report(written); } catch { }
            try { _filenameProgress?.Report(op.FileName); } catch { }

            indexEntries.Add((op.FileName, op.Method, op.Path, op.OperationId, op.FriendlyName));
        }

        return (written, indexEntries, writtenPaths);
    }

    public async IAsyncEnumerable<(string FileName, string Content, string Method, string Path, string OperationId, string FriendlyName)> GetOperationsAsync(JsonElement paths)
    {
        foreach (var pathProp in paths.EnumerateObject())
        {
            var path = pathProp.Name;
            var methods = pathProp.Value;
            if (methods.ValueKind != JsonValueKind.Object) continue;

            foreach (var methodProp in methods.EnumerateObject())
            {
                var methodNameLower = methodProp.Name.ToLowerInvariant();
                var allowed = new[] { "get", "post", "put", "delete", "patch", "head", "options", "trace" };
                if (Array.IndexOf(allowed, methodNameLower) < 0)
                    continue;

                var httpMethod = methodProp.Name.ToUpperInvariant();
                var op = methodProp.Value;
                if (op.ValueKind != JsonValueKind.Object)
                    continue;

                string? operationId = op.TryGetProperty("operationId", out var opid) && opid.ValueKind == JsonValueKind.String ? opid.GetString() : null;
                string? summary = op.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                string? description = op.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

                var friendlyName = summary ?? operationId ?? ($"{httpMethod} {path}");

                var sb = new StringBuilder();
                var modelsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Metadata
                if (_metadata != null && _metadata.Count > 0)
                {
                    sb.AppendLine("---");
                    foreach (var kv in _metadata)
                    {
                        var v = kv.Value?.Replace("\"", "\\\"") ?? string.Empty;
                        sb.AppendLine($"{kv.Key}: \"{v}\"");
                    }
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                sb.AppendLine($"# {_apiTitle} {friendlyName}");
                sb.AppendLine();
                sb.AppendLine($"{friendlyName} {operationId} {httpMethod} {path}");
                sb.AppendLine();
                sb.AppendLine($"- Friendly name: {friendlyName}");
                sb.AppendLine($"- Operation ID: {operationId}");
                sb.AppendLine($"- HTTP Method: {httpMethod}");
                sb.AppendLine($"- Path: {path}");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(description))
                {
                    sb.AppendLine(description);
                    sb.AppendLine();
                }

                // Parameters
                sb.AppendLine("## Parameters").AppendLine();
                if (op.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
                {
                    foreach (var p in parameters.EnumerateArray())
                    {
                        JsonElement paramElement = p;
                        string? originalRef = null;
                        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("$ref", out var refEl) && refEl.ValueKind == JsonValueKind.String)
                        {
                            originalRef = refEl.GetString();
                            var refPath = originalRef;
                            try
                            {
                                if (!string.IsNullOrEmpty(refPath))
                                    paramElement = Helpers.ResolveReference(_root, refPath);
                            }
                            catch
                            {
                                paramElement = p;
                            }
                        }

                        if (string.IsNullOrEmpty(originalRef) == false && paramElement.ValueKind == JsonValueKind.Object &&
                            !paramElement.TryGetProperty("name", out _) && originalRef.EndsWith("/schema", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var parentRef = originalRef.Substring(0, originalRef.Length - "/schema".Length);
                                var parentElement = Helpers.ResolveReference(_root, parentRef);
                                if (parentElement.ValueKind == JsonValueKind.Object)
                                    paramElement = parentElement;
                            }
                            catch { }
                        }

                        var name = paramElement.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() ?? "" : "";
                        var @in = paramElement.TryGetProperty("in", out var pi) && pi.ValueKind == JsonValueKind.String ? pi.GetString() ?? "" : "";
                        var req = paramElement.TryGetProperty("required", out var pr) && pr.ValueKind == JsonValueKind.True ? "required" : "optional";
                        var pdesc = paramElement.TryGetProperty("description", out var pd) && pd.ValueKind == JsonValueKind.String ? pd.GetString() : null;

                        if (string.IsNullOrEmpty(name))
                        {
                            if (!string.IsNullOrEmpty(originalRef))
                            {
                                try
                                {
                                    var parts = originalRef.Split('/');
                                    name = parts.Length > 0 ? parts[parts.Length - 1] : originalRef;
                                }
                                catch
                                {
                                    name = originalRef;
                                }
                            }
                        }

                        string typeShort = "";
                        if (paramElement.TryGetProperty("schema", out var ps) && ps.ValueKind == JsonValueKind.Object)
                        {
                            typeShort = Helpers.GetSchemaShortName(ps, _root);
                        }

                        sb.AppendLine($"- **{name}** ({@in}{(string.IsNullOrEmpty(typeShort) ? "" : $", {typeShort}")}) - {req}{(string.IsNullOrEmpty(pdesc) ? "" : $": {pdesc}")}");
                    }
                }
                else
                {
                    sb.AppendLine("No parameters.");
                }
                sb.AppendLine();

                // Request body
                sb.AppendLine("## Request body").AppendLine();
                if (op.TryGetProperty("requestBody", out var reqBody) && reqBody.ValueKind == JsonValueKind.Object)
                {
                    if (reqBody.TryGetProperty("description", out var rbd) && rbd.ValueKind == JsonValueKind.String)
                        sb.AppendLine(rbd.GetString());

                    if (reqBody.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var media in content.EnumerateObject())
                        {
                            sb.AppendLine($"- Content: {media.Name}");
                            if (media.Value.ValueKind == JsonValueKind.Object && media.Value.TryGetProperty("schema", out var schema))
                            {
                                CollectSchemaRefs(schema, modelsToInclude);

                                if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var srefTop) && srefTop.ValueKind == JsonValueKind.String)
                                {
                                    var refTop = srefTop.GetString() ?? string.Empty;
                                    if (refTop.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) ||
                                        refTop.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var parts = refTop.Split('/');
                                        var modelName = parts.Length > 0 ? parts[^1] : refTop;
                                        modelsToInclude.Add(refTop);
                                        sb.AppendLine();
                                        sb.AppendLine($"- Schema: {modelName} (see model section below)");
                                        continue;
                                    }
                                }

                                var schemaSummary = Helpers.SummarizeSchema(schema, _root, _expansionCache, false);
                                if (!string.IsNullOrWhiteSpace(schemaSummary))
                                {
                                    sb.AppendLine();
                                    sb.AppendLine("```");
                                    sb.AppendLine(schemaSummary.TrimEnd());
                                    sb.AppendLine("```");
                                }
                                else
                                {
                                    sb.AppendLine();
                                    sb.AppendLine("```json");
                                    sb.AppendLine(JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true }));
                                    sb.AppendLine("```");
                                }
                            }
                        }
                    }
                }
                else
                {
                    sb.AppendLine("No request body.");
                }
                sb.AppendLine();

                // Responses
                sb.AppendLine("## Response").AppendLine();
                if (op.TryGetProperty("responses", out var responses) && responses.ValueKind == JsonValueKind.Object)
                {
                    foreach (var resp in responses.EnumerateObject())
                    {
                        sb.AppendLine($"### {resp.Name}").AppendLine();
                        var r = resp.Value;
                        if (r.TryGetProperty("description", out var rd) && rd.ValueKind == JsonValueKind.String)
                            sb.AppendLine(rd.GetString());

                        if (r.TryGetProperty("content", out var rcontent) && rcontent.ValueKind == JsonValueKind.Object)
                        {
                            if (rcontent.EnumerateObject().Count() > 0)
                                sb.AppendLine();
                            foreach (var media in rcontent.EnumerateObject())
                            {
                                sb.AppendLine($"- Content: {media.Name}");
                                if (media.Value.ValueKind == JsonValueKind.Object && media.Value.TryGetProperty("schema", out var schema))
                                {
                                    ProcessResponseSchema(schema, modelsToInclude, sb);
                                }
                            }
                        }
                        else if (r.TryGetProperty("schema", out var schema2))
                        {
                            sb.AppendLine();
                            ProcessResponseSchema(schema2, modelsToInclude, sb);
                        }
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("No responses defined.");
                }

                // Expand models
                if (modelsToInclude != null && modelsToInclude.Count > 0)
                {
                    CollectTransitiveSchemaRefs(modelsToInclude);
                }

                // Append models
                if (modelsToInclude != null && modelsToInclude.Count > 0)
                {
                    foreach (var mref in modelsToInclude)
                    {
                        try
                        {
                            var resolved = Helpers.ResolveReference(_root, mref);
                            var parts = mref.Split('/');
                            var modelName = parts.Length > 0 ? parts[^1] : mref;
                            sb.AppendLine();
                            sb.AppendLine($"## Model: {modelName}");
                            sb.AppendLine($"<a id=\"{modelName}\"></a>");
                            sb.AppendLine();
                            var modelExpanded = Helpers.SummarizeSchema(resolved, _root, _expansionCache, false);
                            if (!string.IsNullOrWhiteSpace(modelExpanded))
                            {
                                sb.AppendLine("```");
                                sb.AppendLine(modelExpanded.TrimEnd());
                                sb.AppendLine("```");
                            }
                            else
                            {
                                sb.AppendLine("```json");
                                sb.AppendLine(JsonSerializer.Serialize(resolved, new JsonSerializerOptions { WriteIndented = true }));
                                sb.AppendLine("```");
                            }
                        }
                        catch { }
                    }
                }

                var rawFileName = (string.IsNullOrWhiteSpace(operationId) ? (httpMethod + "_" + path) : operationId) + ".md";
                var safe = string.Join("_", rawFileName.Split(Path.GetInvalidFileNameChars()));
                
                yield return (safe, sb.ToString(), httpMethod, path, operationId ?? string.Empty, friendlyName);
            }
        }
    }

    private void ProcessResponseSchema(JsonElement schema, HashSet<string> modelsToInclude, StringBuilder sb)
    {
        CollectSchemaRefs(schema, modelsToInclude);
        
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var sref) && sref.ValueKind == JsonValueKind.String)
        {
            var refStr = sref.GetString() ?? string.Empty;
            if (refStr.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) || 
                refStr.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = refStr.Split('/');
                var modelName = parts.Length > 0 ? parts[^1] : refStr;
                modelsToInclude.Add(refStr);
                sb.AppendLine($"- Schema: {modelName} (see model section below)");
                return;
            }
        }

        var schemaSummary = Helpers.SummarizeSchema(schema, _root, _expansionCache, false);
        if (!string.IsNullOrWhiteSpace(schemaSummary))
        {
            sb.AppendLine("```");
            sb.AppendLine(schemaSummary.TrimEnd());
            sb.AppendLine("```");
        }
        else
        {
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("```");
        }
    }

    private void CollectSchemaRefs(JsonElement node, HashSet<string> set)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("$ref", out var r) && r.ValueKind == JsonValueKind.String)
            {
                var rr = r.GetString();
                if (!string.IsNullOrEmpty(rr) && 
                    (rr.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) ||
                     rr.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase)))
                {
                    set.Add(rr);
                    return;
                }
            }
            foreach (var prop in node.EnumerateObject())
            {
                CollectSchemaRefs(prop.Value, set);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in node.EnumerateArray())
                CollectSchemaRefs(el, set);
        }
    }

    private void CollectTransitiveSchemaRefs(HashSet<string> set)
    {
        if (set == null || set.Count == 0) return;
        var seen = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(set);

        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            try
            {
                if (string.IsNullOrWhiteSpace(r)) continue;
                if (!r.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) && 
                    !r.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase)) 
                    continue;
                JsonElement resolved;
                try { resolved = Helpers.ResolveReference(_root, r); }
                catch { continue; }

                var beforeCount = set.Count;
                CollectSchemaRefs(resolved, set);
                foreach (var added in set)
                {
                    if (!seen.Contains(added))
                    {
                        seen.Add(added);
                        queue.Enqueue(added);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
