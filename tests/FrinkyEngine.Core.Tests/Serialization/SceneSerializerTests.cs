using System.Numerics;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Physics;
using FrinkyEngine.Core.Serialization;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Serialization;

public sealed class SceneSerializerTests
{
    [Fact]
    public void SerializeAndDeserialize_RoundTripsSceneGraphAndMetadata()
    {
        var scene = new FrinkyEngine.Core.Scene.Scene
        {
            Name = "Gameplay",
            EditorCameraPosition = new Vector3(1, 2, 3),
            EditorCameraYaw = 30f,
            EditorCameraPitch = -10f,
            PhysicsSettings = new PhysicsSettings
            {
                Gravity = new Vector3(0, -3.5f, 0)
            }
        };

        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child");
        child.Active = false;
        child.Transform.SetParent(parent.Transform);

        var link = parent.AddComponent<SerializableLinkComponent>();
        link.Label = "to-child";
        link.Target = child;
        link.Asset = new AssetReference("Prefabs/player.fprefab");

        parent.AddUnresolvedComponent(new ComponentData
        {
            Type = "Missing.Component",
            Enabled = false,
            EditorOnly = true
        });

        var json = SceneSerializer.SerializeToString(scene);
        var restored = SceneSerializer.DeserializeFromString(json);

        restored.Should().NotBeNull();
        restored!.Name.Should().Be("Gameplay");
        restored.EditorCameraPosition.Should().Be(new Vector3(1, 2, 3));
        restored.EditorCameraYaw.Should().Be(30f);
        restored.EditorCameraPitch.Should().Be(-10f);
        restored.PhysicsSettings.Gravity.Should().Be(new Vector3(0, -3.5f, 0));

        var restoredParent = restored.FindEntityById(parent.Id);
        var restoredChild = restored.FindEntityById(child.Id);
        restoredParent.Should().NotBeNull();
        restoredChild.Should().NotBeNull();

        restoredParent!.Transform.Children.Should().ContainSingle();
        restoredParent.Transform.Children[0].Entity.Id.Should().Be(child.Id);
        restoredChild!.Active.Should().BeFalse();
        restoredParent.UnresolvedComponents.Should().ContainSingle(c => c.Type == "Missing.Component");

        var restoredLink = restoredParent.GetComponent<SerializableLinkComponent>();
        restoredLink.Should().NotBeNull();
        restoredLink!.Label.Should().Be("to-child");
        restoredLink.Asset.Path.Should().Be("Prefabs/player.fprefab");
        restoredLink.Target.Id.Should().Be(child.Id);
    }

    [Fact]
    public void DuplicateEntity_AssignsNewIdsAndRemapsInternalReferences()
    {
        var scene = new FrinkyEngine.Core.Scene.Scene();
        var source = scene.CreateEntity("Player");
        var child = scene.CreateEntity("Camera");
        child.Transform.SetParent(source.Transform);

        var link = source.AddComponent<SerializableLinkComponent>();
        link.Target = child;

        var duplicate = SceneSerializer.DuplicateEntity(source, scene);

        duplicate.Should().NotBeNull();
        duplicate!.Id.Should().NotBe(source.Id);
        duplicate.Name.Should().Be("Player (1)");
        duplicate.Transform.Children.Should().ContainSingle();

        var duplicateChild = duplicate.Transform.Children[0].Entity;
        duplicateChild.Id.Should().NotBe(child.Id);

        var duplicateLink = duplicate.GetComponent<SerializableLinkComponent>();
        duplicateLink.Should().NotBeNull();
        duplicateLink!.Target.Id.Should().Be(duplicateChild.Id);
    }

    [Fact]
    public void Load_ReturnsNullForMissingOrInvalidFiles()
    {
        using var temp = new FrinkyEngine.Core.Tests.TestSupport.TempDirectory();
        SceneSerializer.Load(temp.GetPath("missing.fscene")).Should().BeNull();

        var invalidPath = temp.GetPath("broken.fscene");
        File.WriteAllText(invalidPath, "{ not valid json");

        Action act = () => SceneSerializer.Load(invalidPath);
        act.Should().Throw<Exception>();
    }
}
