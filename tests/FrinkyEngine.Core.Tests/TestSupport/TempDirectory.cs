namespace FrinkyEngine.Core.Tests.TestSupport;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frinky-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(params string[] parts)
    {
        var values = new string[parts.Length + 1];
        values[0] = Path;
        Array.Copy(parts, 0, values, 1, parts.Length);
        return System.IO.Path.Combine(values);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
