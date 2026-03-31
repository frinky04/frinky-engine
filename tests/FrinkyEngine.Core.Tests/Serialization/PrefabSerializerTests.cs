using System.Numerics;
using System.Text.Json;
using FrinkyEngine.Core.Prefabs;
using FrinkyEngine.Core.Serialization;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Serialization;

public sealed class PrefabSerializerTests
{
    [Fact]
    public void SerializeNodeNormalized_SanitizesRootTransformAndNormalizesReferences()
    {
        var root = new FrinkyEngine.Core.ECS.Entity("Root")
        {
            Prefab = new PrefabInstanceMetadata
            {
                SourceNodeId = Guid.NewGuid().ToString("N")
            }
        };
        root.Transform.LocalPosition = new Vector3(1, 2, 3);

        var childStableId = Guid.NewGuid().ToString("N");
        var child = new FrinkyEngine.Core.ECS.Entity("Child")
        {
            Prefab = new PrefabInstanceMetadata
            {
                SourceNodeId = childStableId
            }
        };
        child.Transform.SetParent(root.Transform);

        var link = root.AddComponent<SerializableLinkComponent>();
        link.Target = child;

        var node = PrefabSerializer.SerializeNodeNormalized(root);

        node.StableId.Should().Be(root.Prefab!.SourceNodeId);
        var rootTransform = node.Components.Single(c => c.Type.Contains("TransformComponent", StringComparison.Ordinal));
        rootTransform.Properties.Should().NotContainKey("LocalPosition");
        rootTransform.Properties.Should().NotContainKey("LocalRotation");
        rootTransform.Properties.Should().NotContainKey("LocalScale");

        var childNode = node.Children.Should().ContainSingle().Subject;
        childNode.StableId.Should().Be(childStableId);
        var serializedTarget = node.Components
            .Single(c => c.Type.Contains(nameof(SerializableLinkComponent), StringComparison.Ordinal))
            .Properties[nameof(SerializableLinkComponent.Target)]
            .GetString();
        serializedTarget.Should().Be(Guid.Parse(childStableId).ToString());
    }

    [Fact]
    public void Load_RepairsStableIdsAndSanitizesRootTransform()
    {
        using var temp = new FrinkyEngine.Core.Tests.TestSupport.TempDirectory();
        var path = temp.GetPath("broken.fprefab");

        var duplicateStableId = Guid.NewGuid().ToString("N");
        var prefab = new PrefabAssetData
        {
            Root = new PrefabNodeData
            {
                StableId = string.Empty,
                Components =
                {
                    new PrefabComponentData
                    {
                        Type = typeof(FrinkyEngine.Core.Components.TransformComponent).FullName!,
                        Properties =
                        {
                            ["LocalPosition"] = PrefabSerializer.SerializeValue(new Vector3(5, 0, 0), typeof(Vector3))
                        }
                    }
                },
                Children =
                {
                    new PrefabNodeData { StableId = duplicateStableId, Name = "ChildA" },
                    new PrefabNodeData { StableId = duplicateStableId, Name = "ChildB" }
                }
            }
        };

        PrefabSerializer.Save(prefab, path);
        var loaded = PrefabSerializer.Load(path);

        loaded.Should().NotBeNull();
        loaded!.Root.StableId.Should().NotBeNullOrWhiteSpace();
        loaded.Root.Children.Select(c => c.StableId).Should().OnlyHaveUniqueItems();
        loaded.Root.Components.Single().Properties.Should().NotContainKey("LocalPosition");
    }

    [Fact]
    public void ComputeAndApplyOverrides_RecreatesInstanceSpecificChanges()
    {
        var source = new PrefabNodeData
        {
            StableId = "root",
            Name = "Root",
            Active = true,
            Components =
            {
                new PrefabComponentData
                {
                    Type = typeof(FrinkyEngine.Core.Components.TransformComponent).FullName!,
                    Properties =
                    {
                        ["LocalPosition"] = PrefabSerializer.SerializeValue(new Vector3(0, 0, 0), typeof(Vector3))
                    }
                },
                new PrefabComponentData
                {
                    Type = "GameplayComponent",
                    Properties =
                    {
                        ["Mode"] = JsonSerializer.SerializeToElement("idle")
                    }
                }
            },
            Children =
            {
                new PrefabNodeData { StableId = "child-a", Name = "A" },
                new PrefabNodeData { StableId = "child-b", Name = "B" }
            }
        };

        var instance = source.Clone();
        instance.Active = false;
        instance.Components[0].Properties["LocalPosition"] = PrefabSerializer.SerializeValue(new Vector3(5, 0, 0), typeof(Vector3));
        instance.Components[1].Properties["Mode"] = JsonSerializer.SerializeToElement("run");
        instance.Children.RemoveAt(1);
        instance.Children.Add(new PrefabNodeData { StableId = "child-c", Name = "C" });

        var overrides = PrefabOverrideUtility.ComputeOverrides(source, instance);
        overrides.PropertyOverrides.Should().Contain(o =>
            o.NodeId == "root" &&
            o.ComponentType == "GameplayComponent" &&
            o.PropertyName == "Mode");
        overrides.PropertyOverrides.Should().NotContain(o =>
            o.NodeId == "root" &&
            o.ComponentType.Contains("TransformComponent", StringComparison.Ordinal) &&
            o.PropertyName == "LocalPosition");

        var applied = source.Clone();
        PrefabOverrideUtility.ApplyOverrides(applied, overrides);

        applied.Name.Should().Be("Root");
        applied.Active.Should().BeFalse();
        applied.Components.Single(c => c.Type == "GameplayComponent").Properties["Mode"].GetString().Should().Be("run");
        applied.Children.Select(c => c.StableId).Should().BeEquivalentTo(new[] { "child-a", "child-c" });
    }

    [Fact]
    public void RemapPrefabEntityReferences_RewritesStableIdsToInstantiatedGuids()
    {
        var childStableId = Guid.NewGuid().ToString("N");
        var instantiatedChildId = Guid.NewGuid();
        var node = new PrefabNodeData
        {
            StableId = Guid.NewGuid().ToString("N"),
            Components =
            {
                new PrefabComponentData
                {
                    Type = "GameplayComponent",
                    Properties =
                    {
                        ["Target"] = JsonSerializer.SerializeToElement(Guid.Parse(childStableId).ToString())
                    }
                }
            },
            Children =
            {
                new PrefabNodeData { StableId = childStableId, Name = "Child" }
            }
        };

        FrinkyEngine.Core.Prefabs.PrefabInstantiator.RemapPrefabEntityReferences(node, new Dictionary<string, Guid>
        {
            [node.StableId] = Guid.NewGuid(),
            [childStableId] = instantiatedChildId
        });

        node.Components.Single().Properties["Target"].GetString().Should().Be(instantiatedChildId.ToString());
    }
}
