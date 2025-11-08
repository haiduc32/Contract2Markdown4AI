using NSwag;

// Contract2Markdown4AI
// Usage: dotnet run -- <openapi-file> [-o|--output <folder>]
// Loads an OpenAPI (json/yaml) file with NSwag and writes one markdown file per operation.

namespace Contract2Markdown4AI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run -- <openapi-file> [-o|--output <folder>]");
            return 1;
        }

        string inputPath = args[0];
        string outputFolder = Path.Combine(Directory.GetCurrentDirectory(), "output_md");

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "-o" || a == "--output")
            {
                if (i + 1 < args.Length)
                {
                    outputFolder = args[i + 1];
                    i++;
                }
            }
            else if (a == "-m" || a == "--meta")
            {
                if (i + 1 < args.Length)
                {
                    var pair = args[i + 1];
                    i++;
                    var idx = pair.IndexOf(':');
                    if (idx > 0)
                    {
                        var key = pair.Substring(0, idx).Trim();
                        var value = pair.Substring(idx + 1).Trim();
                        // strip surrounding quotes if present
                        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                            value = value.Substring(1, value.Length - 2);
                        if (!string.IsNullOrEmpty(key))
                            metadata[key] = value;
                    }
                }
            }
        }

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Input file not found: {inputPath}");
            return 2;
        }

        Directory.CreateDirectory(outputFolder);

        Console.WriteLine($"Loading OpenAPI document: {inputPath}");
        OpenApiDocument document;
        try
        {
            document = await OpenApiDocument.FromFileAsync(inputPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load document: {ex.Message}");
            return 3;
        }

        // delegate conversion to OpenApiToMarkdown
        int written;
        try
        {
            written = await OpenApiToMarkdown.GenerateAsync(document, outputFolder, metadata).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to generate markdown: {ex.Message}");
            return 4;
        }

        Console.WriteLine($"Done. Wrote {written} files to {outputFolder}");
        return 0;
    }
}
