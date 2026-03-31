using System.Runtime.CompilerServices;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.CanvasUI;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Serialization;

namespace FrinkyEngine.Core.Tests.TestSupport;

internal sealed class SerializableLinkComponent : Component
{
    public string Label { get; set; } = string.Empty;
    public EntityReference Target { get; set; }
    public AssetReference Asset { get; set; } = new(string.Empty);
}

internal sealed class AssetReferenceHolderComponent : Component
{
    public AssetReference Primary { get; set; } = new(string.Empty);
    public List<AssetReference> Secondary { get; set; } = new();
    public List<AssetReferenceContainer> Nested { get; set; } = new();
}

internal sealed class AssetReferenceContainer
{
    public AssetReference Asset { get; set; } = new(string.Empty);
}

internal sealed class TestFObject : FObject
{
    public override string DisplayName => "Test FObject";
}

internal sealed class ProbePanel : Panel
{
    public int CreatedCount { get; private set; }

    public override void OnCreated()
    {
        CreatedCount++;
    }
}

internal static class TestAssemblyRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        ComponentTypeResolver.RegisterAssembly(typeof(SerializableLinkComponent).Assembly);
    }
}
