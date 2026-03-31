using FrinkyEngine.Core.Scene;
using FrinkyEngine.Core.Serialization;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.SceneGraph;

public sealed class SceneManagerTests
{
    [Fact]
    public void NewSceneAndSaveScene_CreateActiveSceneAndWriteFile()
    {
        using var temp = new TempDirectory();
        var manager = SceneManager.Instance;
        manager.IsSaveDisabled = false;

        var scene = manager.NewScene("Gameplay");
        scene.CreateEntity("Player");
        var path = temp.GetPath("Gameplay.fscene");

        manager.SaveScene(path);

        manager.ActiveScene.Should().BeSameAs(scene);
        scene.FilePath.Should().Be(path);
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void SaveScene_WhenDisabledThrows()
    {
        using var temp = new TempDirectory();
        var manager = SceneManager.Instance;
        manager.NewScene("Locked");
        manager.IsSaveDisabled = true;

        Action act = () => manager.SaveScene(temp.GetPath("Locked.fscene"));

        act.Should().Throw<InvalidOperationException>();
        manager.IsSaveDisabled = false;
    }

    [Fact]
    public void LoadSceneByName_ResolvesBareNameThroughAssetDatabase()
    {
        using var temp = new TempDirectory();
        var assetsDir = temp.GetPath("Assets");
        Directory.CreateDirectory(Path.Combine(assetsDir, "Scenes"));

        var scene = new Scene.Scene { Name = "Loaded" };
        var scenePath = Path.Combine(assetsDir, "Scenes", "Loaded.fscene");
        SceneSerializer.Save(scene, scenePath);

        AssetManager.Instance.AssetsPath = assetsDir;
        AssetDatabase.Instance.Scan(assetsDir);

        var loaded = SceneManager.Instance.LoadSceneByName("Loaded");

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Loaded");
        SceneManager.Instance.ActiveScene.Should().BeSameAs(loaded);
        loaded.FilePath.Should().Be(scenePath);
    }
}
