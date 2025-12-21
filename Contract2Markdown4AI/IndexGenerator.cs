using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Contract2Markdown4AI;

public class IndexGenerator
{
    private readonly string _outputFolder;
    private readonly string _apiTitle;
    private readonly IDictionary<string, string>? _metadata;
    private readonly IProgress<int>? _progress;
    private readonly IProgress<string?>? _filenameProgress;

    public IndexGenerator(
        string outputFolder,
        string apiTitle,
        IDictionary<string, string>? metadata,
        IProgress<int>? progress,
        IProgress<string?>? filenameProgress)
    {
        _outputFolder = outputFolder;
        _apiTitle = apiTitle;
        _metadata = metadata;
        _progress = progress;
        _filenameProgress = filenameProgress;
    }

    public async Task<(int WrittenCount, List<string> WrittenPaths)> GenerateIndexAsync(List<(string File, string Method, string Path, string OperationId, string FriendlyName)> indexEntries)
    {
        int written = 0;
        var writtenPaths = new List<string>();

        var (fileName, content) = GetIndex(indexEntries);
        var indexPath = Path.Combine(_outputFolder, fileName);
        await File.WriteAllTextAsync(indexPath, content).ConfigureAwait(false);
        written++;
        writtenPaths.Add(indexPath);
        try { _progress?.Report(written); } catch { }
        try { _filenameProgress?.Report(fileName); } catch { }

        return (written, writtenPaths);
    }

    public (string FileName, string Content) GetIndex(List<(string File, string Method, string Path, string OperationId, string FriendlyName)> indexEntries)
    {
        var idx = new StringBuilder();

        // Prepend YAML front-matter metadata if provided
        if (_metadata != null && _metadata.Count > 0)
        {
            idx.AppendLine("---");
            foreach (var kv in _metadata)
            {
                var v = kv.Value?.Replace("\"", "\\\"") ?? string.Empty;
                idx.AppendLine($"{kv.Key}: \"{v}\"");
            }
            idx.AppendLine("---");
            idx.AppendLine();
        }

        idx.AppendLine($"# {_apiTitle} - Operations Index");
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

        return ("Index.md", idx.ToString());
    }
}
