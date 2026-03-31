using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.UI.ConsoleSystem;

namespace FrinkyEngine.Core.Tests.ConsoleSystem;

public sealed class ConsoleBackendTests : IDisposable
{
    public ConsoleBackendTests()
    {
        Reset();
    }

    public void Dispose()
    {
        Reset();
    }

    [Fact]
    public void Execute_RunsRegisteredCommandsAndPassesArguments()
    {
        IReadOnlyList<string>? captured = null;

        ConsoleBackend.RegisterCommand(
            "echo",
            "echo <args>",
            "Echoes arguments",
            args =>
            {
                captured = args;
                return new ConsoleExecutionResult(true, args.ToArray());
            }).Should().BeTrue();

        var result = ConsoleBackend.Execute("  echo   hello   world  ");

        result.Success.Should().BeTrue();
        result.Lines.Should().Equal("hello", "world");
        captured.Should().Equal("hello", "world");
    }

    [Fact]
    public void RegisteringDuplicateCommandsAndCVars_IsRejected()
    {
        ConsoleBackend.RegisterCommand("helpful", "helpful", "a", _ => new ConsoleExecutionResult(true)).Should().BeTrue();
        ConsoleBackend.RegisterCommand("helpful", "helpful", "b", _ => new ConsoleExecutionResult(true)).Should().BeFalse();

        var cvar = new ConsoleCVar("test_mode", "test_mode [0|1]", "Mode", () => "0", _ => true);
        ConsoleBackend.RegisterCVar(cvar).Should().BeTrue();
        ConsoleBackend.RegisterCommand("test_mode", "test_mode", "shadow", _ => new ConsoleExecutionResult(true)).Should().BeFalse();
    }

    [Fact]
    public void Execute_QueriesSetsAndRejectsInvalidCVarValues()
    {
        var state = "0";

        ConsoleBackend.RegisterCVar(new ConsoleCVar(
            "test_mode",
            "test_mode [0|1]",
            "Mode",
            () => state,
            value =>
            {
                if (value is not "0" and not "1")
                    return false;

                state = value;
                return true;
            }));

        ConsoleBackend.Execute("test_mode").Lines.Should().ContainSingle().Which.Should().Be("test_mode = 0");
        ConsoleBackend.Execute("test_mode 1").Success.Should().BeTrue();
        state.Should().Be("1");

        var invalid = ConsoleBackend.Execute("test_mode 2");
        invalid.Success.Should().BeFalse();
        invalid.Lines.Should().ContainSingle().Which.Should().Contain("Invalid value");
    }

    [Fact]
    public void GetRegisteredEntries_AndHelpOutput_AreSorted()
    {
        ConsoleBackend.RegisterCommand("zeta", "zeta", "last", _ => new ConsoleExecutionResult(true));
        ConsoleBackend.RegisterCommand("alpha", "alpha", "first", _ => new ConsoleExecutionResult(true));
        ConsoleBackend.RegisterCVar(new ConsoleCVar("mid_mode", "mid_mode", "mid", () => "0", _ => true));

        var entries = ConsoleBackend.GetRegisteredEntries();
        entries.Select(e => e.Name).Should().Equal("alpha", "help", "mid_mode", "zeta");

        var help = ConsoleBackend.Execute("help");
        help.Success.Should().BeTrue();
        help.Lines.Should().ContainInOrder("Commands:", "CVars:");
        help.Lines.Should().Contain(line => line.Contains("alpha", StringComparison.Ordinal));
        help.Lines.Should().Contain(line => line.Contains("mid_mode", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_CommandExceptions_AreLoggedAndReported()
    {
        ConsoleBackend.RegisterCommand(
            "boom",
            "boom",
            "Throwing command.",
            _ => throw new InvalidOperationException("boom"));

        var result = ConsoleBackend.Execute("boom");

        result.Success.Should().BeFalse();
        result.Lines.Should().Equal("Command failed: boom");
        FrinkyLog.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("Console command 'boom' failed: boom"));
    }

    private static void Reset()
    {
        ConsoleBackend.ResetForTests();
        FrinkyLog.Clear();
    }
}
