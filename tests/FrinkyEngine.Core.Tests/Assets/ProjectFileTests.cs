using System.Text.Json;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Assets;

public sealed class ProjectFileTests
{
    [Fact]
    public void SaveAndLoad_RoundTripAndResolveProjectPaths()
    {
        using var temp = new TempDirectory();
        var project = new ProjectFile
        {
            ProjectName = "SpaceGame",
            AssetsPath = "Content",
            DefaultScene = "Scenes/Intro.fscene",
            GameAssembly = "bin/Debug/net8.0/SpaceGame.dll",
            GameProject = "SpaceGame.csproj"
        };

        var path = temp.GetPath("SpaceGame.fproject");
        project.Save(path);

        var loaded = ProjectFile.Load(path);

        loaded.ProjectName.Should().Be("SpaceGame");
        loaded.GetAbsoluteAssetsPath(temp.Path).Should().Be(Path.GetFullPath(temp.GetPath("Content")));
        loaded.GetAbsoluteScenePath(temp.Path).Should().Be(Path.GetFullPath(temp.GetPath("Content", "Scenes", "Intro.fscene")));
        loaded.GetAbsoluteGameAssemblyPath(temp.Path).Should().Be(Path.GetFullPath(temp.GetPath("bin", "Debug", "net8.0", "SpaceGame.dll")));
        loaded.GetAbsoluteGameProjectPath(temp.Path).Should().Be(Path.GetFullPath(temp.GetPath("SpaceGame.csproj")));
    }

    [Fact]
    public void Load_InvalidJsonThrows()
    {
        using var temp = new TempDirectory();
        var path = temp.GetPath("Broken.fproject");
        File.WriteAllText(path, "{ not valid json");

        Action act = () => ProjectFile.Load(path);

        act.Should().Throw<JsonException>();
    }
}
