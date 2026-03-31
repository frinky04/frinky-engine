namespace FrinkyEngine.Editor.Tests.TestSupport;

internal sealed class ScopedProcessRunner : IDisposable
{
    private readonly ProjectScaffolder.IProcessRunner _previous;

    private ScopedProcessRunner(ProjectScaffolder.IProcessRunner replacement)
    {
        _previous = ProjectScaffolder.ProcessRunner;
        ProjectScaffolder.ProcessRunner = replacement;
    }

    public static ScopedProcessRunner Use(ProjectScaffolder.IProcessRunner replacement) => new(replacement);

    public void Dispose()
    {
        ProjectScaffolder.ProcessRunner = _previous;
    }
}
