using FrinkyEngine.Core.Serialization;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Serialization;

public sealed class TypeResolverTests
{
    [Fact]
    public void ComponentTypeResolver_RegisterAndUnregisterAssemblyUpdatesVisibility()
    {
        var assembly = typeof(SerializableLinkComponent).Assembly;
        ComponentTypeResolver.UnregisterAssembly(assembly);

        try
        {
            ComponentTypeResolver.Resolve(nameof(SerializableLinkComponent)).Should().BeNull();

            ComponentTypeResolver.RegisterAssembly(assembly);

            var resolved = ComponentTypeResolver.Resolve(nameof(SerializableLinkComponent));
            resolved.Should().Be(typeof(SerializableLinkComponent));
            ComponentTypeResolver.GetAssemblySource(resolved!).Should().Contain("FrinkyEngine.Core.Tests");
            ComponentTypeResolver.GetDisplayName(typeof(SerializableLinkComponent)).Should().Be("Serializable Link");
        }
        finally
        {
            ComponentTypeResolver.RegisterAssembly(assembly);
        }
    }

    [Fact]
    public void FObjectTypeResolver_RegisterAndUnregisterAssemblyUpdatesVisibility()
    {
        var assembly = typeof(TestFObject).Assembly;
        FObjectTypeResolver.UnregisterAssembly(assembly);

        try
        {
            FObjectTypeResolver.Resolve(nameof(TestFObject)).Should().BeNull();

            FObjectTypeResolver.RegisterAssembly(assembly);

            var resolved = FObjectTypeResolver.Resolve(nameof(TestFObject));
            resolved.Should().Be(typeof(TestFObject));
            FObjectTypeResolver.GetDisplayName(typeof(TestFObject)).Should().Be("Test FObject");
            FObjectTypeResolver.GetTypesAssignableTo(typeof(FObject)).Should().Contain(typeof(TestFObject));
        }
        finally
        {
            FObjectTypeResolver.RegisterAssembly(assembly);
        }
    }
}
