using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public class ProjectScaffolderTests
{
    [Fact]
    public void CreateProject_CopiesTemplateContentAndUsesProcessRunner()
    {
        using var temp = new TempDirectory();
        var template = CreateTemplate(temp.RootPath);
        var runner = new TestProcessRunner();

        using var scope = ScopedProcessRunner.Use(runner);

        var projectPath = ProjectScaffolder.CreateProject(temp.RootPath, "MyGame", template);

        projectPath.Should().Be(System.IO.Path.Combine(temp.RootPath, "MyGame", "MyGame.fproject"));
        File.Exists(projectPath).Should().BeTrue();

        var projectDir = System.IO.Path.Combine(temp.RootPath, "MyGame");
        File.Exists(System.IO.Path.Combine(projectDir, "MyGame.csproj")).Should().BeTrue();
        File.Exists(System.IO.Path.Combine(projectDir, "MyGame.sln")).Should().BeTrue();
        File.Exists(ProjectSettings.GetPath(projectDir)).Should().BeTrue();
        File.Exists(EditorProjectSettings.GetPath(projectDir)).Should().BeTrue();

        var playerScriptPath = System.IO.Path.Combine(projectDir, "Scripts", "Player.cs");
        File.Exists(playerScriptPath).Should().BeTrue();
        File.ReadAllText(playerScriptPath).Should().Contain("MyGame");
        File.ReadAllText(playerScriptPath).Should().NotContain("FrinkyGame");

        var binaryPath = System.IO.Path.Combine(projectDir, "Binary", "logo.bin");
        File.Exists(binaryPath).Should().BeTrue();
        File.ReadAllBytes(binaryPath).Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });

        File.Exists(System.IO.Path.Combine(projectDir, "FrinkyGame.csproj")).Should().BeFalse();
        File.Exists(System.IO.Path.Combine(projectDir, "FrinkyGame.fproject")).Should().BeFalse();
        File.Exists(System.IO.Path.Combine(projectDir, ".template.config", "template.json")).Should().BeFalse();

        var gitignore = File.ReadAllText(System.IO.Path.Combine(projectDir, ".gitignore"));
        gitignore.Should().Contain("*.csproj");
        gitignore.Should().Contain("*.sln");

        runner.Calls.Should().Contain(call => call.FileName == "dotnet" && call.Arguments == "restore");
        runner.Calls.Should().Contain(call => call.FileName == "git" && call.Arguments == "init");
        runner.Calls.Should().Contain(call => call.FileName == "git" && call.Arguments == "add .");
        runner.Calls.Should().Contain(call => call.FileName == "git" && call.Arguments == "commit -m \"Initial commit\"");
    }

    [Fact]
    public void EnsureProjectFiles_IsIdempotentAndAppendsGitignoreEntries()
    {
        using var temp = new TempDirectory();
        var projectDir = temp.RootPath;
        File.WriteAllText(System.IO.Path.Combine(projectDir, ".gitignore"), "bin/\n");

        var runner = new TestProcessRunner();
        using var scope = ScopedProcessRunner.Use(runner);

        ProjectScaffolder.EnsureProjectFiles(projectDir, "MyGame");
        var firstGitignore = File.ReadAllText(System.IO.Path.Combine(projectDir, ".gitignore"));
        var firstCallCount = runner.Calls.Count;

        ProjectScaffolder.EnsureProjectFiles(projectDir, "MyGame");
        var secondGitignore = File.ReadAllText(System.IO.Path.Combine(projectDir, ".gitignore"));

        firstGitignore.Should().Be(secondGitignore);
        runner.Calls.Count.Should().Be(firstCallCount);
        runner.Calls.Should().ContainSingle(call => call.FileName == "dotnet" && call.Arguments == "restore");
        firstGitignore.Should().Contain("## Generated project files");
        firstGitignore.Should().Contain("*.csproj");
        firstGitignore.Should().Contain("*.sln");

        var coreAssemblyPath = typeof(Component).Assembly.Location.Replace('\\', '/');
        File.ReadAllText(System.IO.Path.Combine(projectDir, "MyGame.csproj")).Should().Contain(coreAssemblyPath);
    }

    private static ProjectTemplate CreateTemplate(string root)
    {
        var contentDir = System.IO.Path.Combine(root, "TemplateContent");
        Directory.CreateDirectory(contentDir);
        Directory.CreateDirectory(System.IO.Path.Combine(contentDir, "Scripts"));
        Directory.CreateDirectory(System.IO.Path.Combine(contentDir, "Binary"));
        Directory.CreateDirectory(System.IO.Path.Combine(contentDir, ".template.config"));

        File.WriteAllText(System.IO.Path.Combine(contentDir, "Scripts", "Player.cs"), """
            namespace FrinkyGame;

            public sealed class Player
            {
                public string Name => "FrinkyGame";
            }
            """);
        File.WriteAllBytes(System.IO.Path.Combine(contentDir, "Binary", "logo.bin"), new byte[] { 1, 2, 3, 4 });
        File.WriteAllText(System.IO.Path.Combine(contentDir, ".template.config", "template.json"), "{}");
        File.WriteAllText(System.IO.Path.Combine(contentDir, "FrinkyGame.csproj"), "<Project />");
        File.WriteAllText(System.IO.Path.Combine(contentDir, "FrinkyGame.fproject"), "{}");
        File.WriteAllText(System.IO.Path.Combine(contentDir, ".gitignore"), "ignored");

        return new ProjectTemplate
        {
            Id = "template",
            Name = "Template",
            Description = "Template",
            SortOrder = 1,
            SourceName = "FrinkyGame",
            ContentDirectory = contentDir
        };
    }
}
