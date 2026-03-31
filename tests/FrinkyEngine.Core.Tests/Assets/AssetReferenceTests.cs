using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Prefabs;
using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Assets;

public sealed class AssetReferenceTests
{
    [Fact]
    public void UpdateReferencesInScene_RewritesFullAndBareAssetPaths()
    {
        var scene = new FrinkyEngine.Core.Scene.Scene();
        var entity = scene.CreateEntity("Entity");
        entity.Prefab = new PrefabInstanceMetadata
        {
            AssetPath = new AssetReference("old.fprefab")
        };

        var component = entity.AddComponent<AssetReferenceHolderComponent>();
        component.Primary = new AssetReference("Textures/old.png");
        component.Secondary = new List<AssetReference>
        {
            new AssetReference("old.png"),
            new AssetReference("keep.wav")
        };

        AssetReferenceUpdater.UpdateReferencesInScene(scene, "Textures/old.png", "Textures/new.png");
        AssetReferenceUpdater.UpdateReferencesInScene(scene, "Prefabs/old.fprefab", "Prefabs/new.fprefab");

        component.Primary.Path.Should().Be("Textures/new.png");
        component.Secondary.Select(x => x.Path).Should().ContainInOrder("new.png", "keep.wav");
        entity.Prefab!.AssetPath.Path.Should().Be("new.fprefab");
    }

    [Fact]
    public void UpdateReferencesOnDiskAndFindReferences_RewritesSceneAndPrefabFiles()
    {
        using var temp = new FrinkyEngine.Core.Tests.TestSupport.TempDirectory();
        var assetsDir = temp.GetPath("Assets");
        Directory.CreateDirectory(assetsDir);

        var scenePath = temp.GetPath("Scenes", "Main.fscene");
        var prefabPath = temp.GetPath("Prefabs", "Player.fprefab");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(prefabPath)!);

        File.WriteAllText(scenePath, @"{""path"":""Textures/old.png"",""short"":""old.png""}");
        File.WriteAllText(prefabPath, @"{""path"":""Textures/old.png""}");

        var refs = AssetReferenceUpdater.FindReferencesOnDisk(assetsDir, "Textures/old.png");
        refs.Should().BeEquivalentTo(new[] { "Scenes/Main.fscene", "Prefabs/Player.fprefab" });

        var modified = AssetReferenceUpdater.UpdateReferencesOnDisk(assetsDir, "Textures/old.png", "Textures/new.png");
        modified.Should().Be(2);
        File.ReadAllText(scenePath).Should().Contain("Textures/new.png").And.Contain("new.png");
        File.ReadAllText(prefabPath).Should().Contain("Textures/new.png");
    }

    [Fact]
    public void ValidateScene_LogsWarningsForBrokenReferencesAcrossSupportedShapes()
    {
        using var temp = new FrinkyEngine.Core.Tests.TestSupport.TempDirectory();
        var assetsDir = temp.GetPath("Assets");
        Directory.CreateDirectory(Path.Combine(assetsDir, "Textures"));
        File.WriteAllText(Path.Combine(assetsDir, "Textures", "existing.png"), "x");

        AssetDatabase.Instance.Clear();
        AssetDatabase.Instance.Scan(assetsDir);
        FrinkyLog.Clear();

        try
        {
            var scene = new FrinkyEngine.Core.Scene.Scene();
            var entity = scene.CreateEntity("Entity");
            var component = entity.AddComponent<AssetReferenceHolderComponent>();
            component.Primary = new AssetReference("Textures/missing.png");
            component.Secondary = new List<AssetReference>
            {
                new AssetReference("existing.png"),
                new AssetReference("missing-2.png")
            };
            component.Nested = new List<AssetReferenceContainer>
            {
                new AssetReferenceContainer { Asset = new AssetReference("Textures/missing-3.png") }
            };

            AssetReferenceValidator.ValidateScene(scene);

            var warnings = FrinkyLog.Entries
                .Where(e => e.Level == LogLevel.Warning)
                .Select(e => e.Message)
                .ToList();

            warnings.Should().ContainSingle(message => message.Contains("Primary", StringComparison.Ordinal));
            warnings.Should().ContainSingle(message => message.Contains("Secondary[1]", StringComparison.Ordinal));
            warnings.Should().ContainSingle(message => message.Contains("Nested[0].Asset", StringComparison.Ordinal));
        }
        finally
        {
            AssetDatabase.Instance.Clear();
            FrinkyLog.Clear();
        }
    }
}
