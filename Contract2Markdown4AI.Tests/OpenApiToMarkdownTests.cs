using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using NSwag;
using Contract2Markdown4AI;

namespace Contract2Markdown4AI.Tests;

public class OpenApiToMarkdownTests
{
    [Fact]
    public async Task GenerateFilesAsync_WithValidDocument_GeneratesFiles()
    {
        // Arrange
        string json = await File.ReadAllTextAsync("petstore.json");
        var document = await OpenApiDocument.FromJsonAsync(json);
        string outputFolder = Path.Combine(Path.GetTempPath(), "Contract2Markdown4AI_Tests_" + Guid.NewGuid());

        try
        {
            // Act
            int fileCount = await OpenApiToMarkdown.GenerateFilesAsync(document, outputFolder);

            // Assert
            Assert.True(fileCount > 0, "Should generate at least one file");
            Assert.True(Directory.Exists(outputFolder), "Output folder should exist");
            
            var files = Directory.GetFiles(outputFolder, "*.md");
            Assert.NotEmpty(files);
            
            // Check for some expected files based on petstore.json
            Assert.Contains(files, f => Path.GetFileName(f).Contains("addPet.md", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(files, f => Path.GetFileName(f).Contains("getPetById.md", StringComparison.OrdinalIgnoreCase));

            // Verify content of getPetById.md
            var getPetByIdFile = files.First(f => Path.GetFileName(f).Contains("getPetById.md", StringComparison.OrdinalIgnoreCase));
            string content = await File.ReadAllTextAsync(getPetByIdFile);
            
            Assert.Contains("Find pet by ID", content);
            Assert.Contains("Returns a single pet", content);
            Assert.Contains("GET /pet/{petId}", content);
            Assert.Contains("petId", content);
            Assert.Contains("ID of pet to return", content);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_WithValidV3Document_GeneratesFiles()
    {
        // Arrange
        string json = await File.ReadAllTextAsync("petstore-v3.json");
        var document = await OpenApiDocument.FromJsonAsync(json);
        string outputFolder = Path.Combine(Path.GetTempPath(), "Contract2Markdown4AI_Tests_V3_" + Guid.NewGuid());

        try
        {
            // Act
            int fileCount = await OpenApiToMarkdown.GenerateFilesAsync(document, outputFolder);

            // Assert
            Assert.True(fileCount > 0, "Should generate at least one file");
            Assert.True(Directory.Exists(outputFolder), "Output folder should exist");
            
            var files = Directory.GetFiles(outputFolder, "*.md");
            Assert.NotEmpty(files);
            
            // Check for some expected files based on petstore-v3.json
            Assert.Contains(files, f => Path.GetFileName(f).Contains("listPets.md", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(files, f => Path.GetFileName(f).Contains("showPetById.md", StringComparison.OrdinalIgnoreCase));

            // Verify content of listPets.md
            var listPetsFile = files.First(f => Path.GetFileName(f).Contains("listPets.md", StringComparison.OrdinalIgnoreCase));
            string content = await File.ReadAllTextAsync(listPetsFile);

            Assert.Contains("List all pets", content);
            Assert.Contains("GET /pets", content);
            Assert.Contains("limit", content);
            Assert.Contains("How many items to return at one time", content);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_WithNullDocument_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => OpenApiToMarkdown.GenerateFilesAsync(null!, "output"));
    }

    [Fact]
    public async Task GenerateFilesAsync_WithEmptyOutputFolder_ThrowsArgumentNullException()
    {
        var document = new OpenApiDocument();
        await Assert.ThrowsAsync<ArgumentNullException>(() => OpenApiToMarkdown.GenerateFilesAsync(document, ""));
    }

    [Fact]
    public async Task GenerateAsync_ReturnsContent()
    {
        // Arrange
        string json = await File.ReadAllTextAsync("petstore.json");
        var document = await OpenApiDocument.FromJsonAsync(json);

        // Act
        var results = new List<GeneratedFile>();
        await foreach (var item in OpenApiToMarkdown.GenerateAsync(document))
        {
            results.Add(item);
        }

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.FileName.Contains("addPet.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.FileName.Contains("getPetById.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.FileName == "components.md");
        Assert.Contains(results, r => r.FileName == "Index.md");

        var addPet = results.First(r => r.FileName.Contains("addPet.md", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(GeneratedFileType.Operation, addPet.FileType);
        Assert.Equal("POST", addPet.Metadata["Method"]);
        Assert.Equal("/pet", addPet.Metadata["Path"]);

        var components = results.First(r => r.FileName == "components.md");
        Assert.Equal(GeneratedFileType.Component, components.FileType);

        var index = results.First(r => r.FileName == "Index.md");
        Assert.Equal(GeneratedFileType.Index, index.FileType);
    }
}
