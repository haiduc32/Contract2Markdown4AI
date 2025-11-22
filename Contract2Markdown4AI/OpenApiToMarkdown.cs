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

public static class OpenApiToMarkdown
{
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
    var indexEntries = new List<(string File, string Method, string Path, string OperationId, string FriendlyName)>();
    var writtenPaths = new List<string>();
    // shared per-run expansion cache for component expansions
    var expansionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pathProp in paths.EnumerateObject())
        {
            var path = pathProp.Name; // e.g. /pets/{id}
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
                // collect component models referenced directly or indirectly by this operation
                var modelsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Prepend YAML front-matter metadata if provided
                if (metadata != null && metadata.Count > 0)
                {
                    sb.AppendLine("---");
                    foreach (var kv in metadata)
                    {
                        // naive escaping of quotes in value
                        var v = kv.Value?.Replace("\"", "\\\"") ?? string.Empty;
                        sb.AppendLine($"{kv.Key}: \"{v}\"");
                    }
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
                sb.AppendLine($"# {apiTitle} {friendlyName}");
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
                                    paramElement = Helpers.ResolveReference(root, refPath);
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
                                var parentElement = Helpers.ResolveReference(root, parentRef);
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
                            typeShort = Helpers.GetSchemaShortName(ps, root);
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
                                // collect component refs referenced by this schema; we'll append model sections once (after responses)
                                CollectComponentRefs(schema, modelsToInclude);

                                // If top-level schema is a direct component $ref, prefer not to inline it here; we'll include model sections below.
                                if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var srefTop) && srefTop.ValueKind == JsonValueKind.String)
                                {
                                    var refTop = srefTop.GetString() ?? string.Empty;
                                    if (refTop.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) ||
                                        refTop.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var parts = refTop.Split('/');
                                        var modelName = parts.Length > 0 ? parts[^1] : refTop;
                                        // record model for inclusion at the end of this operation file
                                        modelsToInclude.Add(refTop);
                                        sb.AppendLine();
                                        sb.AppendLine($"- Schema: {modelName} (see model section below)");
                                        continue;
                                    }
                                }

                                var schemaSummary = Helpers.SummarizeSchema(schema, root, expansionCache, false);
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

                        // Check for OpenAPI 3.0 style (content property)
                        if (r.TryGetProperty("content", out var rcontent) && rcontent.ValueKind == JsonValueKind.Object)
                        {
                            if (rcontent.EnumerateObject().Count() > 0)
                                sb.AppendLine();
                            foreach (var media in rcontent.EnumerateObject())
                            {
                                sb.AppendLine($"- Content: {media.Name}");
                                if (media.Value.ValueKind == JsonValueKind.Object && media.Value.TryGetProperty("schema", out var schema))
                                {
                                    ProcessResponseSchema(schema, root, modelsToInclude, expansionCache, sb);
                                }
                            }
                        }
                        // Check for Swagger 2.0 style (schema directly under response)
                        else if (r.TryGetProperty("schema", out var schema2))
                        {
                            sb.AppendLine();
                            ProcessResponseSchema(schema2, root, modelsToInclude, expansionCache, sb);
                        }
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("No responses defined.");
                }

                // Expand the collected models to include transitive component references (avoid infinite recursion)
                if (modelsToInclude != null && modelsToInclude.Count > 0)
                {
                    CollectTransitiveComponentRefs(root, modelsToInclude);
                }

                // After processing request and responses, append any collected component model sections once
                if (modelsToInclude != null && modelsToInclude.Count > 0)
                {
                    foreach (var mref in modelsToInclude)
                    {
                        try
                        {
                            var resolved = Helpers.ResolveReference(root, mref);
                            var parts = mref.Split('/');
                            var modelName = parts.Length > 0 ? parts[^1] : mref;
                            sb.AppendLine();
                            sb.AppendLine($"## Model: {modelName}");
                            sb.AppendLine($"<a id=\"{modelName}\"></a>");
                            sb.AppendLine();
                            // Summarize the model but do not expand component $refs here — keep $ref links to other models
                            var modelExpanded = Helpers.SummarizeSchema(resolved, root, expansionCache, false);
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
                var outPath = Path.Combine(outputFolder, safe);
                await File.WriteAllTextAsync(outPath, sb.ToString()).ConfigureAwait(false);
                written++;
                writtenPaths.Add(outPath);
                try { progress?.Report(written); } catch { }
                try { filenameProgress?.Report(Path.GetFileName(outPath)); } catch { }

                // record index entry (use filename only for linking)
                indexEntries.Add((Path.GetFileName(outPath), httpMethod, path, operationId ?? string.Empty, friendlyName));
            }
        }

        // Generate components.md with one section per component schema
        // Handle both OpenAPI 3.0 (components/schemas) and Swagger 2.0 (definitions)
        try
        {
            var schemasToDocument = new List<(string name, JsonElement schema)>();
            
            // Check for OpenAPI 3.0 components/schemas
            if (root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Object &&
                components.TryGetProperty("schemas", out var schemas) && schemas.ValueKind == JsonValueKind.Object)
            {
                foreach (var schema in schemas.EnumerateObject())
                {
                    schemasToDocument.Add((schema.Name, schema.Value));
                }
            }
            
            // Check for Swagger 2.0 definitions
            if (root.TryGetProperty("definitions", out var definitions) && definitions.ValueKind == JsonValueKind.Object)
            {
                foreach (var def in definitions.EnumerateObject())
                {
                    schemasToDocument.Add((def.Name, def.Value));
                }
            }

            if (schemasToDocument.Count > 0)
            {
                var comp = new StringBuilder();
                if (metadata != null && metadata.Count > 0)
                {
                    comp.AppendLine("---");
                    foreach (var kv in metadata)
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
                        var expanded = Helpers.SummarizeSchema(schemaValue, root, expansionCache);
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

                var compPath = Path.Combine(outputFolder, "components.md");
                await File.WriteAllTextAsync(compPath, comp.ToString()).ConfigureAwait(false);
                written++;
                writtenPaths.Add(compPath);
                try { progress?.Report(written); } catch { }
                try { filenameProgress?.Report(Path.GetFileName(compPath)); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write components.md: {ex.Message}");
        }

        // Generate Index.md
        try
        {
            var idx = new StringBuilder();

            // Prepend YAML front-matter metadata if provided
            if (metadata != null && metadata.Count > 0)
            {
                idx.AppendLine("---");
                foreach (var kv in metadata)
                {
                    var v = kv.Value?.Replace("\"", "\\\"") ?? string.Empty;
                    idx.AppendLine($"{kv.Key}: \"{v}\"");
                }
                idx.AppendLine("---");
                idx.AppendLine();
            }

            idx.AppendLine($"# {apiTitle} - Operations Index");
            idx.AppendLine();
            idx.AppendLine("| Operation | OperationId | Method | Path |");
            idx.AppendLine("|---|---|---|---|");
            foreach (var e in indexEntries)
            {
                var fileLink = e.File;
                var opText = string.IsNullOrWhiteSpace(e.FriendlyName) ? (string.IsNullOrWhiteSpace(e.OperationId) ? fileLink : e.OperationId) : e.FriendlyName;
                var opIdText = string.IsNullOrWhiteSpace(e.OperationId) ? "" : e.OperationId;
                // make path monospace and escape vertical bars
                var pathEscaped = e.Path.Replace("|", "\\|");
                idx.AppendLine($"| [{opText}](./{fileLink}) | {opIdText} | {e.Method} | `{pathEscaped}` |");
            }

            var indexPath = Path.Combine(outputFolder, "Index.md");
            await File.WriteAllTextAsync(indexPath, idx.ToString()).ConfigureAwait(false);
            written++;
            writtenPaths.Add(indexPath);
            try { progress?.Report(written); } catch { }
            try { filenameProgress?.Report(Path.GetFileName(indexPath)); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write Index.md: {ex.Message}");
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

    // Helpers

    static void ProcessResponseSchema(JsonElement schema, JsonElement root, HashSet<string> modelsToInclude, Dictionary<string, string> expansionCache, StringBuilder sb)
    {
        // collect any component refs referenced by this response schema
        CollectComponentRefs(schema, modelsToInclude);
        
        // Check if the schema is a direct reference to a component
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var sref) && sref.ValueKind == JsonValueKind.String)
        {
            var refStr = sref.GetString() ?? string.Empty;
            // Handle both OpenAPI 3.0 (#/components/schemas/) and Swagger 2.0 (#/definitions/) references
            if (refStr.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) || 
                refStr.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = refStr.Split('/');
                var modelName = parts.Length > 0 ? parts[^1] : refStr;
                // record model for inclusion at the end of this operation file
                modelsToInclude.Add(refStr);
                sb.AppendLine($"- Schema: {modelName} (see model section below)");
                return;
            }
        }

        // Otherwise, inline the schema
        var schemaSummary = Helpers.SummarizeSchema(schema, root, expansionCache, false);
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

    static void CollectComponentRefs(JsonElement node, HashSet<string> set)
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
                CollectComponentRefs(prop.Value, set);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in node.EnumerateArray())
                CollectComponentRefs(el, set);
        }
    }

    // Collect transitive component refs: starting from the given set, resolve each component schema
    // and collect any component $refs it references, repeating until no new refs are discovered.
    static void CollectTransitiveComponentRefs(JsonElement root, HashSet<string> set)
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
                // Handle both OpenAPI 3.0 (#/components/schemas/) and Swagger 2.0 (#/definitions/) references
                if (!r.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) && 
                    !r.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase)) 
                    continue;
                JsonElement resolved;
                try { resolved = Helpers.ResolveReference(root, r); }
                catch { continue; }

                // Collect direct component refs referenced by this resolved schema
                var beforeCount = set.Count;
                CollectComponentRefs(resolved, set);
                // For any newly added refs, enqueue them for processing
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
                // ignore and continue
            }
        }
    }

}
