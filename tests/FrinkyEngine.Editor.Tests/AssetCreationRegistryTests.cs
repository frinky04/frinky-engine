using FrinkyEngine.Editor.Assets.Creation;

namespace FrinkyEngine.Editor.Tests;

public sealed class AssetCreationRegistryTests
{
    [Fact]
    public void Register_ReplaceAndEnsureDefaultsBehavePredictably()
    {
        AssetCreationRegistry.ResetForTests();
        var first = new FakeAssetCreationFactory("script", "Script A");
        var replacement = new FakeAssetCreationFactory("script", "Script B");

        AssetCreationRegistry.Register(first);
        AssetCreationRegistry.Register(replacement);

        AssetCreationRegistry.GetFactory("script").Should().BeSameAs(replacement);
        AssetCreationRegistry.GetFactories().Should().ContainSingle();

        AssetCreationRegistry.EnsureDefaultsRegistered();
        AssetCreationRegistry.EnsureDefaultsRegistered();

        AssetCreationRegistry.GetFactories().Select(factory => factory.Id).Should().Contain(new[] { "script", "canvas" });
        AssetCreationRegistry.GetFactories().Count(factory => factory.Id == "script").Should().Be(1);
    }

    private sealed class FakeAssetCreationFactory(string id, string displayName) : IAssetCreationFactory
    {
        public string Id => id;
        public string DisplayName => displayName;
        public string NameHint => "Example";
        public string Extension => ".txt";
        public AssetType AssetType => AssetType.Unknown;

        public void Reset(EditorApplication app) { }
        public void DrawOptions(EditorApplication app) { }
        public bool TryValidateName(string name, out string? validationMessage)
        {
            validationMessage = null;
            return true;
        }

        public string BuildRelativePath(string name) => $"{name}.txt";

        public bool TryCreate(EditorApplication app, string name, out string createdRelativePath, out string? errorMessage)
        {
            createdRelativePath = BuildRelativePath(name);
            errorMessage = null;
            return true;
        }
    }
}
