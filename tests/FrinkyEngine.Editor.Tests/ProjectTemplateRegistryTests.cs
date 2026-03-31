using System.Text.Json;
using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public class ProjectTemplateRegistryTests
{
    [Fact]
    public void DiscoverFromBaseDirectory_FindsTemplatesInOutputLayout()
    {
        using var temp = new TempDirectory();
        var baseDir = System.IO.Path.Combine(temp.RootPath, "EditorApp", "bin", "Debug", "net8.0");
        Directory.CreateDirectory(baseDir);

        var templatesRoot = System.IO.Path.Combine(baseDir, "ProjectTemplates");
        WriteTemplate(templatesRoot, "empty", sortOrder: 2, name: null, sourceName: null, description: null, includeConfig: true);
        WriteTemplate(templatesRoot, "starter", sortOrder: 1, name: "Starter Template", sourceName: "FrinkyGame", description: "Desc", includeConfig: true);

        ProjectTemplateRegistry.DiscoverFromBaseDirectory(baseDir);

        ProjectTemplateRegistry.Templates.Should().HaveCount(2);
        ProjectTemplateRegistry.Templates.Select(t => t.Id).Should().ContainInOrder("starter", "empty");
        ProjectTemplateRegistry.GetById("starter").Should().NotBeNull();
    }

    [Fact]
    public void DiscoverFromBaseDirectory_WalksUpToSourceLayout()
    {
        using var temp = new TempDirectory();
        var root = temp.RootPath;
        File.WriteAllText(System.IO.Path.Combine(root, "FrinkyEngine.sln"), string.Empty);

        var baseDir = System.IO.Path.Combine(root, "src", "FrinkyEngine.Editor", "bin", "Debug", "net8.0");
        Directory.CreateDirectory(baseDir);

        var templatesRoot = System.IO.Path.Combine(root, "templates", "ProjectTemplates");
        WriteTemplate(templatesRoot, "source-template", sortOrder: 5, name: "Source Template", sourceName: "FrinkyGame", description: "Source", includeConfig: true);

        ProjectTemplateRegistry.DiscoverFromBaseDirectory(baseDir);

        ProjectTemplateRegistry.Templates.Should().ContainSingle();
        ProjectTemplateRegistry.Templates[0].Id.Should().Be("source-template");
    }

    [Fact]
    public void DiscoverFromBaseDirectory_SortsAndDefaultsMetadata()
    {
        using var temp = new TempDirectory();
        var baseDir = System.IO.Path.Combine(temp.RootPath, "ProjectTemplatesHost");
        Directory.CreateDirectory(baseDir);

        var templatesRoot = System.IO.Path.Combine(baseDir, "ProjectTemplates");
        WriteTemplate(templatesRoot, "zulu", sortOrder: 20, name: "Zulu", sourceName: "FrinkyGame", description: "Last", includeConfig: false);
        WriteTemplate(templatesRoot, "alpha", sortOrder: 10, name: null, sourceName: null, description: null, includeConfig: false);

        Directory.CreateDirectory(System.IO.Path.Combine(templatesRoot, "broken-metadata"));
        File.WriteAllText(System.IO.Path.Combine(templatesRoot, "broken-metadata", "template.json"), "{ invalid json");

        Directory.CreateDirectory(System.IO.Path.Combine(templatesRoot, "missing-content"));
        File.WriteAllText(System.IO.Path.Combine(templatesRoot, "missing-content", "template.json"), JsonSerializer.Serialize(new
        {
            id = "missing-content",
            name = "Missing Content",
            description = "Skipped",
            sortOrder = 30,
            sourceName = "FrinkyGame"
        }));

        ProjectTemplateRegistry.DiscoverFromBaseDirectory(baseDir);

        ProjectTemplateRegistry.Templates.Should().HaveCount(2);
        ProjectTemplateRegistry.Templates.Select(t => t.Id).Should().ContainInOrder("alpha", "zulu");

        var alpha = ProjectTemplateRegistry.GetById("alpha");
        alpha.Should().NotBeNull();
        alpha!.Name.Should().Be("alpha");
        alpha.Description.Should().BeEmpty();
        alpha.SourceName.Should().Be("FrinkyGame");
        alpha.SortOrder.Should().Be(10);
    }

    private static void WriteTemplate(
        string templatesRoot,
        string id,
        int sortOrder,
        string? name,
        string? sourceName,
        string? description,
        bool includeConfig)
    {
        var templateDir = System.IO.Path.Combine(templatesRoot, id);
        var contentDir = System.IO.Path.Combine(templateDir, "content");
        var configDir = System.IO.Path.Combine(contentDir, ".template.config");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(System.IO.Path.Combine(contentDir, "Scripts"));

        File.WriteAllText(System.IO.Path.Combine(templateDir, "template.json"), JsonSerializer.Serialize(new
        {
            id,
            name = name ?? id,
            description = description ?? string.Empty,
            sortOrder,
            sourceName = sourceName ?? "FrinkyGame"
        }));

        if (includeConfig)
        {
            File.WriteAllText(System.IO.Path.Combine(configDir, "template.json"), JsonSerializer.Serialize(new
            {
                sourceName = sourceName ?? "FrinkyGame"
            }));
        }

        File.WriteAllText(System.IO.Path.Combine(contentDir, "FrinkyGame.csproj"), "<Project />");
        File.WriteAllText(System.IO.Path.Combine(contentDir, "FrinkyGame.fproject"), "{}");
        File.WriteAllText(System.IO.Path.Combine(contentDir, "Scripts", "Player.cs"), "namespace FrinkyGame;");
        File.WriteAllText(System.IO.Path.Combine(contentDir, ".gitignore"), "bin/");
    }
}
