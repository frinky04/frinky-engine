namespace FrinkyEngine.Core.Tests.TestSupport;

internal sealed class ConsoleErrorCapture : IDisposable
{
    private readonly TextWriter _originalError;
    private readonly StringWriter _capture = new();

    public ConsoleErrorCapture()
    {
        _originalError = Console.Error;
        Console.SetError(_capture);
    }

    public string Text => _capture.ToString();

    public void Dispose()
    {
        Console.SetError(_originalError);
        _capture.Dispose();
    }
}
