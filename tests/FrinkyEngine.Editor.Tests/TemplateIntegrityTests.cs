using System.Text.Json;
using System.Xml.Linq;
using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public class TemplateIntegrityTests
{
    [Fact]
    public void AllTemplates_HaveValidMetadataAndContent()
    {
        var templatesRoot = System.IO.Path.Combine(RepoPaths.FindRepoRoot(), "templates", "ProjectTemplates");
        Directory.Exists(templatesRoot).Should().BeTrue();

        foreach (var templateDir in Directory.GetDirectories(templatesRoot))
        {
            ValidateTemplate(templateDir);
        }
    }

    [Fact]
    public void TemplateAssetFiles_ParseAsJson()
    {
        var templatesRoot = System.IO.Path.Combine(RepoPaths.FindRepoRoot(), "templates", "ProjectTemplates");
        foreach (var assetPath in Directory.EnumerateFiles(templatesRoot, "*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".fproject", StringComparison.OrdinalIgnoreCase)
                                 || path.EndsWith(".fscene", StringComparison.OrdinalIgnoreCase)
                                 || path.EndsWith(".fprefab", StringComparison.OrdinalIgnoreCase)))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(assetPath));
            json.RootElement.ValueKind.Should().BeOneOf(JsonValueKind.Object, JsonValueKind.Array);
        }
    }

    private static void ValidateTemplate(string templateDir)
    {
        var rootMetadataPath = System.IO.Path.Combine(templateDir, "template.json");
        var contentConfigPath = System.IO.Path.Combine(templateDir, "content", ".template.config", "template.json");
        var csprojPath = System.IO.Path.Combine(templateDir, "content", "FrinkyGame.csproj");

        File.Exists(rootMetadataPath).Should().BeTrue();
        File.Exists(contentConfigPath).Should().BeTrue();
        File.Exists(csprojPath).Should().BeTrue();

        var rootMetadata = JsonDocument.Parse(File.ReadAllText(rootMetadataPath));
        rootMetadata.RootElement.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
        rootMetadata.RootElement.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        rootMetadata.RootElement.GetProperty("description").GetString().Should().NotBeNull();
        rootMetadata.RootElement.GetProperty("sourceName").GetString().Should().NotBeNullOrWhiteSpace();

        var contentMetadata = JsonDocument.Parse(File.ReadAllText(contentConfigPath));
        contentMetadata.RootElement.GetProperty("sourceName").GetString().Should().Be(rootMetadata.RootElement.GetProperty("sourceName").GetString());

        XDocument.Load(csprojPath);
    }
}
