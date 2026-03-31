using FrinkyEngine.Core.Prefabs;
using FrinkyEngine.Core.Serialization;
using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public sealed class ProjectLifecycleIntegrationTests
{
    [Fact]
    public void CreateProject_WriteSceneAndPrefab_ReopenProjectDataSuccessfully()
    {
        using var temp = new TempDirectory();
        var repoRoot = RepoPaths.FindRepoRoot();
        ProjectTemplateRegistry.DiscoverFromBaseDirectory(repoRoot);
        var template = ProjectTemplateRegistry.GetById("3d-starter") ?? ProjectTemplateRegistry.Templates.First();
        var runner = new TestProcessRunner();

        using var scope = ScopedProcessRunner.Use(runner);

        var projectPath = ProjectScaffolder.CreateProject(temp.Path, "IntegrationGame", template);
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var assetsDir = Path.Combine(projectDir, "Assets");
        var scenesDir = Path.Combine(assetsDir, "Scenes");
        var prefabsDir = Path.Combine(assetsDir, "Prefabs");
        Directory.CreateDirectory(scenesDir);
        Directory.CreateDirectory(prefabsDir);

        var scene = new FrinkyEngine.Core.Scene.Scene { Name = "Gameplay" };
        var player = scene.CreateEntity("Player");
        var child = scene.CreateEntity("Companion");
        child.Transform.SetParent(player.Transform);
        ComponentTypeResolver.RegisterAssembly(typeof(IntegrationSerializableLinkComponent).Assembly);
        var link = player.AddComponent<IntegrationSerializableLinkComponent>();
        link.Target = child;

        var scenePath = Path.Combine(scenesDir, "Gameplay.fscene");
        SceneSerializer.Save(scene, scenePath);

        var prefabPath = Path.Combine(prefabsDir, "Player.fprefab");
        PrefabSerializer.Save(PrefabSerializer.CreateFromEntity(player), prefabPath);

        var project = ProjectFile.Load(projectPath);
        var loadedScene = SceneSerializer.Load(scenePath);
        var loadedPrefab = PrefabSerializer.Load(prefabPath);

        project.ProjectName.Should().Be("IntegrationGame");
        loadedScene.Should().NotBeNull();
        loadedScene!.FindEntityById(player.Id).Should().NotBeNull();
        loadedPrefab.Should().NotBeNull();
        loadedPrefab!.Root.Name.Should().Be("Player");
        loadedPrefab.Root.Children.Should().ContainSingle(childNode => childNode.Name == "Companion");
    }

    private sealed class IntegrationSerializableLinkComponent : Component
    {
        public EntityReference Target { get; set; }
    }
}
