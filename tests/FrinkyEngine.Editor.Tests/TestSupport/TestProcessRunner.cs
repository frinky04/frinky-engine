namespace FrinkyEngine.Editor.Tests.TestSupport;

internal sealed class TestProcessRunner : ProjectScaffolder.IProcessRunner
{
    public sealed record Call(string FileName, string WorkingDirectory, string Arguments, int TimeoutMilliseconds);

    private readonly List<Call> _calls = new();

    public IReadOnlyList<Call> Calls => _calls;

    public void Run(string fileName, string workingDirectory, string arguments, int timeoutMilliseconds)
    {
        _calls.Add(new Call(fileName, workingDirectory, arguments, timeoutMilliseconds));
    }
}
