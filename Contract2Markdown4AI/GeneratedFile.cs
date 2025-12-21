using System.Collections.Generic;

namespace Contract2Markdown4AI;

/// <summary>
/// Represents the type of the generated file.
/// </summary>
public enum GeneratedFileType
{
    /// <summary>
    /// Represents an API operation (endpoint).
    /// </summary>
    Operation,
    
    /// <summary>
    /// Represents the components/schemas section.
    /// </summary>
    Component,
    
    /// <summary>
    /// Represents the index file.
    /// </summary>
    Index,
    
    /// <summary>
    /// Represents any other type of file.
    /// </summary>
    Other
}

/// <summary>
/// Represents a generated Markdown file.
/// </summary>
public class GeneratedFile
{
    /// <summary>
    /// Gets or sets the name of the file.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content of the file.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the file.
    /// </summary>
    public GeneratedFileType FileType { get; set; }

    /// <summary>
    /// Gets or sets additional metadata associated with the file.
    /// </summary>
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}
