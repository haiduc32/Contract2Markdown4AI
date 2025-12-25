using System;

namespace Contract2Markdown4AI;

using System.Text;
using System.Text.Json;

/// <summary>
/// Provides helper methods for processing JSON schemas and OpenAPI documents.
/// </summary>
class Helpers
{
    /// <summary>
    /// Resolves a local JSON reference within a root element.
    /// </summary>
    /// <param name="rootElement">The root JSON element to search within.</param>
    /// <param name="reference">The JSON reference string (e.g., "#/components/schemas/MyModel").</param>
    /// <returns>The resolved JSON element.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the reference is not local or cannot be found.</exception>
    public static JsonElement ResolveReference(JsonElement rootElement, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("#/"))
            throw new InvalidOperationException($"Only local references supported: {reference}");

        var parts = reference.Substring(2).Split('/');
        var current = rootElement;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out var next))
                throw new InvalidOperationException($"Reference path not found: {reference}");
            current = next;
        }
        return current;
    }

    /// <summary>
    /// Normalizes a block of text by removing common leading indentation and prefixing each line.
    /// </summary>
    /// <param name="block">The text block to normalize.</param>
    /// <param name="prefix">The prefix to add to each line.</param>
    /// <returns>A list of normalized and indented lines.</returns>
    public static List<string> NormalizeAndIndent(string block, string prefix)
    {
        var lines = block.Split(new[] { '\n' }, StringSplitOptions.None).Select(l => l.Replace("\r", "")).ToList();
        // remove empty leading/trailing lines
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        int minLead = int.MaxValue;
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;
            int lead = 0;
            while (lead < l.Length && l[lead] == ' ') lead++;
            if (lead < minLead) minLead = lead;
        }
        if (minLead == int.MaxValue) minLead = 0;
        var outLines = new List<string>();
        foreach (var l in lines)
        {
            var trimmed = l.Length > minLead ? l.Substring(minLead) : l.TrimStart();
            outLines.Add(prefix + trimmed.TrimEnd());
        }
        return outLines;
    }

    /// <summary>
    /// Gets a short name for a schema, either from its reference or its type.
    /// </summary>
    /// <param name="schema">The JSON schema element.</param>
    /// <param name="root">The root JSON element for reference resolution.</param>
    /// <returns>A short name for the schema.</returns>
    public static string GetSchemaShortName(JsonElement schema, JsonElement root)
    {
        try
        {
            if (schema.ValueKind != JsonValueKind.Object) return "";
            if (schema.TryGetProperty("$ref", out var sref) && sref.ValueKind == JsonValueKind.String)
            {
                var r = sref.GetString() ?? "";
                var parts = r.Split('/');
                return parts.Length > 0 ? parts[parts.Length - 1] : r;
            }
            if (schema.TryGetProperty("type", out var st) && st.ValueKind == JsonValueKind.String)
            {
                return st.GetString() ?? "";
            }
        }
        catch { }
        return "";
    }

    /// <summary>
    /// Summarizes a JSON schema into a human-readable string.
    /// </summary>
    /// <param name="schema">The JSON schema element to summarize.</param>
    /// <param name="root">The root JSON element for reference resolution.</param>
    /// <returns>A summarized string representation of the schema.</returns>
    public static string SummarizeSchema(JsonElement schema, JsonElement root)
    {
        // Expand schemas to arbitrary depth, following $ref and arrays.
        // Avoid infinite recursion by tracking seen $ref paths per expansion branch.
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return ExpandSchema(schema, root, seen, 0, cache, true).TrimEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Summarizes a JSON schema into a human-readable string using a shared cache.
    /// </summary>
    /// <param name="schema">The JSON schema element to summarize.</param>
    /// <param name="root">The root JSON element for reference resolution.</param>
    /// <param name="cache">A shared cache for schema expansions.</param>
    /// <returns>A summarized string representation of the schema.</returns>
    public static string SummarizeSchema(JsonElement schema, JsonElement root, Dictionary<string, string> cache)
    {
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return ExpandSchema(schema, root, seen, 0, cache, true).TrimEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Summarizes a JSON schema into a human-readable string with control over schema reference expansion.
    /// </summary>
    /// <param name="schema">The JSON schema element to summarize.</param>
    /// <param name="root">The root JSON element for reference resolution.</param>
    /// <param name="cache">A shared cache for schema expansions.</param>
    /// <param name="expandSchemaRefs">If true, expands schema references inline; otherwise, leaves them as references.</param>
    /// <returns>A summarized string representation of the schema.</returns>
    public static string SummarizeSchema(JsonElement schema, JsonElement root, Dictionary<string, string> cache, bool expandSchemaRefs)
    {
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return ExpandSchema(schema, root, seen, 0, cache, expandSchemaRefs).TrimEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Recursively expands a JSON schema into a human-readable string.
    /// </summary>
    /// <param name="schema">The JSON schema element to expand.</param>
    /// <param name="root">The root JSON element for reference resolution.</param>
    /// <param name="seenRefs">A set of already seen references to prevent infinite recursion.</param>
    /// <param name="indent">The current indentation level.</param>
    /// <param name="cache">A shared cache for schema expansions.</param>
    /// <param name="expandSchemaRefs">If true, expands schema references inline; otherwise, leaves them as references.</param>
    /// <returns>An expanded string representation of the schema.</returns>
    public static string ExpandSchema(JsonElement schema, JsonElement root, HashSet<string> seenRefs, int indent, Dictionary<string, string>? cache, bool expandSchemaRefs)
    {
        var sb = new StringBuilder();
        string ind = new string(' ', indent * 2);

        if (schema.ValueKind != JsonValueKind.Object)
        {
            // primitive or unexpected
            if (schema.ValueKind == JsonValueKind.String)
                sb.AppendLine(ind + schema.GetString());
            return sb.ToString();
        }

        // $ref handling
        if (schema.TryGetProperty("$ref", out var sref) && sref.ValueKind == JsonValueKind.String)
        {
            var r = sref.GetString() ?? string.Empty;
            if (seenRefs.Contains(r))
            {
                // recursion detected: show the reference and stop expanding
                sb.AppendLine(ind + $"$ref: {r}  (recursive)");
                return sb.ToString();
            }

            // If this is a schema reference and caller requested not to expand schema refs inline,
            // just print the $ref and don't resolve it here.
            // Handle both OpenAPI 3.0 (#/components/schemas/) and Swagger 2.0 (#/definitions/)
            if (!expandSchemaRefs && 
                (r.StartsWith("#/components/schemas/", StringComparison.OrdinalIgnoreCase) ||
                 r.StartsWith("#/definitions/", StringComparison.OrdinalIgnoreCase)))
            {
                sb.AppendLine(ind + $"$ref: {r}");
                return sb.ToString();
            }

            // If cached, reuse canonical expansion (normalize to current indent)
            if (cache != null && cache.TryGetValue(r, out var cached))
            {
                sb.AppendLine(ind + $"$ref: {r}");
                var norm = NormalizeAndIndent(cached, ind + "  ");
                foreach (var l in norm)
                    sb.AppendLine(l);
                return sb.ToString();
            }

            // Mark this ref as seen for this branch to detect recursion
            seenRefs.Add(r);
            try
            {
                var resolved = ResolveReference(root, r);
                // compute canonical expansion for caching at base indent 0
                var canonical = ExpandSchema(resolved, root, seenRefs, 0, cache, expandSchemaRefs);
                if (cache != null)
                {
                    try { cache[r] = canonical; } catch { }
                }

                sb.AppendLine(ind + $"$ref: {r}");
                var norm = NormalizeAndIndent(canonical, ind + "  ");
                foreach (var l in norm)
                    sb.AppendLine(l);
                return sb.ToString();
            }
            catch
            {
                sb.AppendLine(ind + $"$ref: {r}");
                return sb.ToString();
            }
            finally
            {
                // allow siblings to expand the same ref independently
                seenRefs.Remove(r);
            }
        }

        // Combine type + format if present
        if (schema.TryGetProperty("type", out var st) && st.ValueKind == JsonValueKind.String)
        {
            var t = st.GetString() ?? "";
            if (t == "object")
            {
                sb.AppendLine(ind + "type: object");
                if (schema.TryGetProperty("description", out var sd) && sd.ValueKind == JsonValueKind.String)
                    sb.AppendLine(ind + "  description: " + sd.GetString());

                if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                {
                    sb.AppendLine(ind + "properties:");
                    foreach (var prop in props.EnumerateObject())
                    {
                        sb.Append(ind + "  - " + prop.Name + ": ");
                        // expand property schema inline and normalize lines
                        var propExpansion = ExpandSchema(prop.Value, root, seenRefs, indent + 2, cache, expandSchemaRefs);
                        if (string.IsNullOrWhiteSpace(propExpansion))
                        {
                            sb.AppendLine("(unknown)");
                        }
                        else
                        {
                            var normalized = NormalizeAndIndent(propExpansion, ind + "    ");
                            if (normalized.Count == 0)
                            {
                                sb.AppendLine("(unknown)");
                            }
                            else
                            {
                                // first line goes after colon
                                sb.AppendLine(normalized[0].TrimStart());
                                for (int i = 1; i < normalized.Count; i++)
                                    sb.AppendLine(normalized[i]);
                            }
                        }
                    }
                }
                else
                {
                    // no properties listed
                }
                return sb.ToString();
            }
            else if (t == "array")
            {
                sb.AppendLine(ind + "type: array");
                if (schema.TryGetProperty("items", out var items))
                {
                    sb.AppendLine(ind + "items:");
                    var itemsExpansion = ExpandSchema(items, root, seenRefs, indent + 1, cache, expandSchemaRefs);
                    var normalized = NormalizeAndIndent(itemsExpansion, ind + "  ");
                    foreach (var line in normalized)
                        sb.AppendLine(line);
                }
                return sb.ToString();
            }
            else
            {
                // primitive type
                var line = ind + $"type: {t}";
                if (schema.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String)
                    line += $" (format: {fmt.GetString()})";
                if (schema.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array)
                {
                    var enums = new List<string>();
                    foreach (var e in en.EnumerateArray())
                        enums.Add(e.ToString());
                    line += $" enum: [{string.Join(", ", enums)}]";
                }
                sb.AppendLine(line);
                return sb.ToString();
            }
        }

        // If there is oneOf/anyOf/allOf, expand each
        if (schema.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine(ind + "oneOf:");
            int idx = 0;
            foreach (var item in oneOf.EnumerateArray())
            {
                sb.AppendLine(ind + $"  - option{idx}:");
                var opt = ExpandSchema(item, root, seenRefs, indent + 2, cache, expandSchemaRefs);
                var norm = NormalizeAndIndent(opt, ind + "    ");
                foreach (var l in norm)
                    sb.AppendLine(l);
                idx++;
            }
            return sb.ToString();
        }
        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine(ind + "anyOf:");
            int idx = 0;
            foreach (var item in anyOf.EnumerateArray())
            {
                sb.AppendLine(ind + $"  - option{idx}:");
                var opt = ExpandSchema(item, root, seenRefs, indent + 2, cache, expandSchemaRefs);
                var norm = NormalizeAndIndent(opt, ind + "    ");
                foreach (var l in norm)
                    sb.AppendLine(l);
                idx++;
            }
            return sb.ToString();
        }
        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine(ind + "allOf:");
            int idx = 0;
            foreach (var item in allOf.EnumerateArray())
            {
                sb.AppendLine(ind + $"  - part{idx}:");
                var opt = ExpandSchema(item, root, seenRefs, indent + 2, cache, expandSchemaRefs);
                var norm = NormalizeAndIndent(opt, ind + "    ");
                foreach (var l in norm)
                    sb.AppendLine(l);
                idx++;
            }
            return sb.ToString();
        }

        // Fallback: print available keys
        sb.AppendLine(ind + "<schema>");
        foreach (var prop in schema.EnumerateObject())
        {
            if (prop.NameEquals("description") || prop.NameEquals("title"))
                sb.AppendLine(ind + "  " + prop.Name + ": " + (prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString()));
        }
        return sb.ToString();
    }
}
