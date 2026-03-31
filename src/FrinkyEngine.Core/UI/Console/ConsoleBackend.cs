using FrinkyEngine.Core.Rendering;

namespace FrinkyEngine.Core.UI.ConsoleSystem;

/// <summary>
/// Central backend for developer-console commands and cvars.
/// </summary>
public static class ConsoleBackend
{
    private sealed class RegisteredCommand(
        string name,
        string usage,
        string description,
        Func<IReadOnlyList<string>, ConsoleExecutionResult> handler)
    {
        public string Name { get; } = name;
        public string Usage { get; } = usage;
        public string Description { get; } = description;
        public Func<IReadOnlyList<string>, ConsoleExecutionResult> Handler { get; } = handler;
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<string, RegisteredCommand> Commands = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ConsoleCVar> CVars = new(StringComparer.OrdinalIgnoreCase);
    private static bool _builtinsRegistered;

    /// <summary>
    /// Registers built-in commands. Safe to call repeatedly.
    /// </summary>
    public static void EnsureBuiltinsRegistered()
    {
        lock (Sync)
        {
            if (_builtinsRegistered)
                return;

            RegisterCommandNoLock("help", "help", "List all console commands and cvars.", ExecuteHelpCommand);
            _builtinsRegistered = true;
        }
    }

    /// <summary>
    /// Registers a command handler.
    /// </summary>
    /// <param name="name">Unique command name.</param>
    /// <param name="usage">Usage string shown in help text.</param>
    /// <param name="description">Human-readable command description.</param>
    /// <param name="handler">Command callback that receives parsed arguments.</param>
    /// <returns><c>true</c> if registered; <c>false</c> if the name already exists.</returns>
    public static bool RegisterCommand(
        string name,
        string usage,
        string description,
        Func<IReadOnlyList<string>, ConsoleExecutionResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (Sync)
        {
            return RegisterCommandNoLock(name, usage, description, handler);
        }
    }

    /// <summary>
    /// Registers a cvar definition.
    /// </summary>
    /// <param name="cvar">CVar to register.</param>
    /// <returns><c>true</c> if registered; <c>false</c> if the name already exists.</returns>
    public static bool RegisterCVar(ConsoleCVar cvar)
    {
        ArgumentNullException.ThrowIfNull(cvar);

        lock (Sync)
        {
            if (Commands.ContainsKey(cvar.Name) || CVars.ContainsKey(cvar.Name))
                return false;

            CVars[cvar.Name] = cvar;
            return true;
        }
    }

    /// <summary>
    /// Executes one line of console input.
    /// </summary>
    /// <param name="input">Raw user input.</param>
    /// <returns>Execution result with output lines for console history.</returns>
    public static ConsoleExecutionResult Execute(string input)
    {
        EnsureBuiltinsRegistered();

        if (string.IsNullOrWhiteSpace(input))
            return new ConsoleExecutionResult(success: true);

        var tokens = Tokenize(input);
        if (tokens.Length == 0)
            return new ConsoleExecutionResult(success: true);

        var name = tokens[0];
        IReadOnlyList<string> args = tokens.Length == 1
            ? Array.Empty<string>()
            : tokens[1..];

        RegisteredCommand? command;
        ConsoleCVar? cvar;
        lock (Sync)
        {
            Commands.TryGetValue(name, out command);
            CVars.TryGetValue(name, out cvar);
        }

        if (command != null)
            return ExecuteCommand(command, args);

        if (cvar != null)
            return ExecuteCVar(cvar, args);

        return FailureLine($"Unknown command or cvar: {name}");
    }

    /// <summary>
    /// Gets all registered command and cvar descriptors sorted by name.
    /// </summary>
    /// <returns>A read-only list of command and cvar descriptors.</returns>
    public static IReadOnlyList<ConsoleEntryDescriptor> GetRegisteredEntries()
    {
        EnsureBuiltinsRegistered();

        lock (Sync)
        {
            var entries = new List<ConsoleEntryDescriptor>(Commands.Count + CVars.Count);
            entries.AddRange(Commands.Values.Select(static command =>
                new ConsoleEntryDescriptor(ConsoleEntryKind.Command, command.Name, command.Usage, command.Description)));
            entries.AddRange(CVars.Values.Select(static cvar =>
                new ConsoleEntryDescriptor(ConsoleEntryKind.CVar, cvar.Name, cvar.Usage, cvar.Description)));

            return entries
                .OrderBy(static e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static e => e.Kind)
                .ToArray();
        }
    }

    /// <summary>
    /// Gets all registered command and cvar names sorted alphabetically.
    /// </summary>
    /// <returns>A read-only list of command and cvar names.</returns>
    public static IReadOnlyList<string> GetRegisteredNames()
    {
        return GetRegisteredEntries()
            .Select(static e => e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool RegisterCommandNoLock(
        string name,
        string usage,
        string description,
        Func<IReadOnlyList<string>, ConsoleExecutionResult> handler)
    {
        var normalizedName = NormalizeName(name);
        if (Commands.ContainsKey(normalizedName) || CVars.ContainsKey(normalizedName))
            return false;

        var normalizedUsage = string.IsNullOrWhiteSpace(usage) ? normalizedName : usage.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? "No description." : description.Trim();
        Commands[normalizedName] = new RegisteredCommand(normalizedName, normalizedUsage, normalizedDescription, handler);
        return true;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        var normalized = name.Trim();
        if (normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException("Name cannot contain whitespace.", nameof(name));

        return normalized;
    }

    private static string[] Tokenize(string input)
    {
        return input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static ConsoleExecutionResult ExecuteCommand(RegisteredCommand command, IReadOnlyList<string> args)
    {
        try
        {
            return command.Handler(args);
        }
        catch (Exception ex)
        {
            FrinkyLog.Error($"Console command '{command.Name}' failed: {ex.Message}");
            return FailureLine($"Command failed: {command.Name}");
        }
    }

    private static ConsoleExecutionResult ExecuteCVar(ConsoleCVar cvar, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return SuccessLine($"{cvar.Name} = {cvar.GetValue()}");

        var value = string.Join(' ', args);
        if (!cvar.TrySetValue(value))
            return FailureLine($"Invalid value for {cvar.Name}. Usage: {cvar.Usage}");

        return SuccessLine($"{cvar.Name} = {cvar.GetValue()}");
    }

    private static ConsoleExecutionResult ExecuteHelpCommand(IReadOnlyList<string> _)
    {
        List<(string Usage, string Description)> commandEntries;
        List<(string Usage, string Description)> cvarEntries;

        lock (Sync)
        {
            commandEntries = Commands.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => (c.Usage, c.Description))
                .ToList();

            cvarEntries = CVars.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => (c.Usage, c.Description))
                .ToList();
        }

        var lines = new List<string>();

        if (commandEntries.Count > 0)
        {
            lines.Add("Commands:");
            foreach (var command in commandEntries)
                lines.Add($"  {command.Usage} - {command.Description}");
        }

        if (cvarEntries.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add(string.Empty);

            lines.Add("CVars:");
            foreach (var cvar in cvarEntries)
                lines.Add($"  {cvar.Usage} - {cvar.Description}");
        }

        if (lines.Count == 0)
            lines.Add("No commands or cvars registered.");

        return new ConsoleExecutionResult(success: true, lines);
    }

    private static ConsoleExecutionResult SuccessLine(string line)
    {
        return new ConsoleExecutionResult(success: true, new[] { line });
    }

    private static ConsoleExecutionResult FailureLine(string line)
    {
        return new ConsoleExecutionResult(success: false, new[] { line });
    }

    /// <summary>
    /// Resets all registered console state for tests.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Sync)
        {
            Commands.Clear();
            CVars.Clear();
            _builtinsRegistered = false;
        }
    }
}
