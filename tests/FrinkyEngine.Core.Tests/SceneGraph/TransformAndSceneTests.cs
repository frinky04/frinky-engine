using System.Numerics;
using FrinkyEngine.Core.Components;
namespace FrinkyEngine.Core.Tests.SceneGraph;

public sealed class TransformAndSceneTests
{
    [Fact]
    public void TransformComponent_OnlyIncrementsVersionForRealChanges()
    {
        var transform = new FrinkyEngine.Core.ECS.Entity("Node").Transform;
        var version = transform.TransformVersion;

        transform.LocalPosition = transform.LocalPosition;
        transform.TransformVersion.Should().Be(version);

        transform.LocalRotation = transform.LocalRotation;
        transform.TransformVersion.Should().Be(version);

        transform.LocalScale = transform.LocalScale;
        transform.TransformVersion.Should().Be(version);

        transform.WorldPosition = transform.WorldPosition;
        transform.TransformVersion.Should().Be(version);

        transform.WorldRotation = transform.WorldRotation;
        transform.TransformVersion.Should().Be(version);

        transform.SetParent(null);
        transform.TransformVersion.Should().Be(version);
    }

    [Fact]
    public void TransformComponent_ConvertsBetweenLocalAndWorldSpace_AndRejectsCycles()
    {
        var parentEntity = new FrinkyEngine.Core.ECS.Entity("Parent");
        var childEntity = new FrinkyEngine.Core.ECS.Entity("Child");
        var grandChildEntity = new FrinkyEngine.Core.ECS.Entity("GrandChild");

        parentEntity.Transform.LocalPosition = new Vector3(5, 0, 0);
        childEntity.Transform.LocalPosition = new Vector3(1, 0, 0);
        childEntity.Transform.SetParent(parentEntity.Transform);
        grandChildEntity.Transform.SetParent(childEntity.Transform);

        childEntity.Transform.WorldPosition.Should().Be(new Vector3(6, 0, 0));

        var versionBefore = childEntity.Transform.TransformVersion;
        childEntity.Transform.WorldPosition = new Vector3(10, 0, 0);
        childEntity.Transform.LocalPosition.Should().Be(new Vector3(5, 0, 0));
        childEntity.Transform.TransformVersion.Should().BeGreaterThan(versionBefore);

        var versionBeforeNoop = childEntity.Transform.TransformVersion;
        childEntity.Transform.SetParent(parentEntity.Transform);
        childEntity.Transform.TransformVersion.Should().Be(versionBeforeNoop);

        parentEntity.Transform.SetParent(grandChildEntity.Transform);
        parentEntity.Transform.Parent.Should().BeNull();
        childEntity.Transform.Parent.Should().Be(parentEntity.Transform);
    }

    [Fact]
    public void Scene_RemoveEntity_RemovesWholeSubtreeAndRegistryEntries()
    {
        var scene = new FrinkyEngine.Core.Scene.Scene();
        var root = scene.CreateEntity("Root");
        var child = scene.CreateEntity("Child");
        child.Transform.SetParent(root.Transform);
        child.AddComponent<CameraComponent>();

        scene.Cameras.Should().ContainSingle();
        scene.FindEntityById(root.Id).Should().Be(root);
        scene.FindEntityById(child.Id).Should().Be(child);

        scene.RemoveEntity(root);

        scene.Entities.Should().BeEmpty();
        scene.Cameras.Should().BeEmpty();
        child.Scene.Should().BeNull();
        root.Scene.Should().BeNull();
        child.Transform.Parent.Should().BeNull();
        root.Transform.Children.Should().BeEmpty();
    }
}
