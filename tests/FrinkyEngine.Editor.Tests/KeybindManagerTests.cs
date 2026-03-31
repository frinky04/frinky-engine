using FrinkyEngine.Editor.Tests.TestSupport;
using Hexa.NET.ImGui;

namespace FrinkyEngine.Editor.Tests;

public sealed class KeybindManagerTests
{
    [Fact]
    public void LoadConfigAndSaveConfig_PersistBindings()
    {
        using var temp = new TempDirectory();
        var manager = KeybindManager.Instance;
        manager.ResetToDefaults();

        manager.LoadConfig(temp.Path);
        manager.SetBinding(EditorAction.SaveScene, new Keybind(ImGuiKey.F6, ctrl: true, shift: true));

        var configPath = Path.Combine(temp.Path, ".frinky", "keybinds.json");
        File.Exists(configPath).Should().BeTrue();
        File.ReadAllText(configPath).Should().Contain("SaveScene");

        manager.ResetToDefaults();
        manager.LoadConfig(temp.Path);

        manager.GetBinding(EditorAction.SaveScene).Should().Be(new Keybind(ImGuiKey.F6, ctrl: true, shift: true));
    }

    [Fact]
    public void FindConflictsAndFormatActionName_WorkAsExpected()
    {
        using var temp = new TempDirectory();
        var manager = KeybindManager.Instance;
        manager.ResetToDefaults();
        manager.LoadConfig(temp.Path);

        var binding = manager.GetBinding(EditorAction.SaveScene);
        var conflicts = manager.FindConflicts(EditorAction.NewScene, binding);

        conflicts.Should().Contain(EditorAction.SaveScene);
        KeybindManager.FormatActionName(EditorAction.TogglePhysicsHitboxPreview).Should().Be("Toggle Physics Hitbox Preview");
    }

    [Fact]
    public void ResetBinding_RestoresDefaultShortcut()
    {
        using var temp = new TempDirectory();
        var manager = KeybindManager.Instance;
        manager.ResetToDefaults();
        manager.LoadConfig(temp.Path);
        var defaultBinding = manager.GetDefaultBinding(EditorAction.PlayStop);

        manager.SetBinding(EditorAction.PlayStop, new Keybind(ImGuiKey.F12));
        manager.ResetBinding(EditorAction.PlayStop);

        manager.GetBinding(EditorAction.PlayStop).Should().Be(defaultBinding);
        manager.GetShortcutText(EditorAction.PlayStop).Should().Be(defaultBinding.ToDisplayString());
    }
}
