namespace FrinkyEngine.Editor.Tests.TestSupport;

internal sealed class TempDirectory : IDisposable
{
    public string RootPath { get; }

    public string Path => RootPath;

    public TempDirectory()
    {
        RootPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frinky-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
