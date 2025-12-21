using System.CommandLine;
using System.CommandLine.Invocation;
using NSwag;
using Spectre.Console;

namespace Contract2Markdown4AI.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var outputDefault = Path.Combine(Directory.GetCurrentDirectory(), "output_md");

        var root = new RootCommand("C2M4AI - convert OpenAPI to Markdown files");

        var inputArg = new Argument<string>("input") { Description = "Path to the OpenAPI JSON/YAML file" };
        var outputOpt = new Option<string>("--output", "-o" ) { Description = "Output folder for generated markdown", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = (ar) => outputDefault };
        var metaOpt = new Option<string[]>("--meta", "-m" ) { Description = "Metadata key:value entries to pass to generator", Arity = ArgumentArity.ZeroOrMore };

        root.Add(inputArg);
        root.Add(outputOpt);
        root.Add(metaOpt);

        root.SetAction(async parseResult =>
        {
            var input = parseResult.GetRequiredValue(inputArg);
            var output = parseResult.GetValue(outputOpt) ?? outputDefault;
            var metas = parseResult.GetValue(metaOpt) ?? Array.Empty<string>();

            var spinner = AnsiConsole.Status();

            var outputFolder = output;
            Directory.CreateDirectory(outputFolder);

            var metadata = ParseMeta(metas);

            OpenApiDocument? document = null;
            var loadFailed = false;

            await spinner.StartAsync("Processing...", async statusCtx =>
            {
                statusCtx.Status("Validating input");
                if (!File.Exists(input))
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Input file not found:[/] {input}");
                    Environment.ExitCode = 2;
                    loadFailed = true;
                    return;
                }

                statusCtx.Status("Loading OpenAPI document");
                try
                {
                    string content = await File.ReadAllTextAsync(input).ConfigureAwait(false);
                    string extension = Path.GetExtension(input).ToLowerInvariant();
                    if (extension == ".yaml" || extension == ".yml")
                        document = await OpenApiYamlDocument.FromYamlAsync(content).ConfigureAwait(false);
                    else
                        document = await OpenApiDocument.FromJsonAsync(content).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Failed to load document:[/] {Spectre.Console.Markup.Escape(ex.ToString())}");
                    Environment.ExitCode = 3;
                    loadFailed = true;
                    return;
                }
            });

            if (loadFailed || document == null)
            {
                return;
            }

            // Run the progress display for generation outside the status spinner
            int written = 0;
            try
            {
                // compute total operations so we can show a determinate progress bar
                int totalOps = 0;
                var rootJson = document.ToJson();
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(rootJson);
                var rootEl = jsonDoc.RootElement;
                if (rootEl.TryGetProperty("paths", out var paths) && paths.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var pathProp in paths.EnumerateObject())
                    {
                        var methods = pathProp.Value;
                        if (methods.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                        foreach (var methodProp in methods.EnumerateObject())
                        {
                            var methodNameLower = methodProp.Name.ToLowerInvariant();
                            var allowed = new[] { "get", "post", "put", "delete", "patch", "head", "options", "trace" };
                            if (Array.IndexOf(allowed, methodNameLower) < 0) continue;
                            totalOps++;
                        }
                    }
                }

                await AnsiConsole.Progress()
                    .AutoClear(true)
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask("Generating files", maxValue: Math.Max(1, totalOps));
                        var progAdapter = new Progress<int>(v => task.Value = v);
                        var fileProg = new Progress<string?>(name => task.Description = string.IsNullOrEmpty(name) ? "Generating files" : $"{name}");
                        written = await OpenApiToMarkdown.GenerateFilesAsync(document, outputFolder, metadata, progAdapter, fileProg).ConfigureAwait(false);
                        task.Value = task.MaxValue;
                    }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Failed to generate markdown:[/] {ex.Message}");
                Environment.ExitCode = 4;
                return;
            }

            AnsiConsole.MarkupLineInterpolated($"[green]Done[/]. Wrote {written} files to [u]{outputFolder}[/]");
        });

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    static Dictionary<string, string> ParseMeta(string[] metas)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metas ?? Array.Empty<string>())
        {
            var idx = pair.IndexOf(':');
            if (idx > 0)
            {
                var key = pair.Substring(0, idx).Trim();
                var value = pair.Substring(idx + 1).Trim();
                if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                    value = value.Substring(1, value.Length - 2);
                if (!string.IsNullOrEmpty(key))
                    metadata[key] = value;
            }
        }
        return metadata;
    }
}
