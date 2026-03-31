namespace FrinkyEngine.Editor.Tests.TestSupport;

internal static class RepoPaths
{
    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(System.IO.Path.Combine(dir, "FrinkyEngine.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
