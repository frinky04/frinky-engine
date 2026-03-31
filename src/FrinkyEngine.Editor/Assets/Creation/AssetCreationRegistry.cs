namespace FrinkyEngine.Editor.Assets.Creation;

public static class AssetCreationRegistry
{
    private static readonly Dictionary<string, IAssetCreationFactory> FactoriesById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IAssetCreationFactory> OrderedFactories = new();
    private static bool _defaultsRegistered;

    public static IReadOnlyList<IAssetCreationFactory> GetFactories() => OrderedFactories;

    public static IAssetCreationFactory? GetFactory(string id)
    {
        return FactoriesById.TryGetValue(id, out var factory) ? factory : null;
    }

    public static void Register(IAssetCreationFactory factory)
    {
        if (FactoriesById.TryGetValue(factory.Id, out var existing))
        {
            var index = OrderedFactories.IndexOf(existing);
            if (index >= 0)
                OrderedFactories[index] = factory;
            FactoriesById[factory.Id] = factory;
            return;
        }

        FactoriesById[factory.Id] = factory;
        OrderedFactories.Add(factory);
    }

    public static void EnsureDefaultsRegistered()
    {
        if (_defaultsRegistered)
            return;

        Register(new ScriptAssetCreationFactory());
        Register(new CanvasAssetCreationFactory());
        _defaultsRegistered = true;
    }

    internal static void ResetForTests()
    {
        FactoriesById.Clear();
        OrderedFactories.Clear();
        _defaultsRegistered = false;
    }
}
