using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public sealed class EditorProjectSettingsTests
{
    [Fact]
    public void LoadOrCreate_MissingFileCreatesDefaults()
    {
        using var temp = new TempDirectory();

        var settings = EditorProjectSettings.LoadOrCreate(temp.Path);

        settings.TargetFps.Should().Be(120);
        settings.HideUnrecognisedAssets.Should().BeTrue();
        File.Exists(EditorProjectSettings.GetPath(temp.Path)).Should().BeTrue();
    }

    [Fact]
    public void Normalize_CleansHierarchyState()
    {
        var duplicateId = Guid.NewGuid().ToString("N");
        var settings = new EditorProjectSettings
        {
            TargetFps = 999,
            Hierarchy = new HierarchyEditorSettings
            {
                Scenes = new Dictionary<string, HierarchySceneState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["  Main  "] = new()
                    {
                        Folders =
                        {
                            new HierarchyFolderState { Id = duplicateId, Name = "B", Order = 5 },
                            new HierarchyFolderState { Id = duplicateId, Name = "A", Order = -1 },
                            new HierarchyFolderState { Id = "child", Name = "Child", ParentFolderId = "missing", Order = 1 }
                        },
                        RootEntityFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [" "] = duplicateId,
                            ["entity-1"] = duplicateId,
                            ["entity-2"] = "missing"
                        },
                        ExpandedFolderIds = new() { duplicateId, duplicateId, "missing" },
                        ExpandedEntityIds = new() { "entity-1", "entity-1", "entity-2" }
                    }
                }
            }
        };

        settings.Normalize();

        settings.TargetFps.Should().Be(120);
        settings.Hierarchy.Scenes.Keys.Should().ContainSingle().Which.Should().Be("Main");
        var scene = settings.Hierarchy.Scenes["Main"];
        scene.Folders.Should().HaveCount(2);
        scene.Folders.Should().ContainSingle(folder => folder.Id == duplicateId && folder.Name == "B");
        scene.Folders.Should().ContainSingle(folder => folder.Id == "child" && folder.ParentFolderId == null);
        scene.Folders.Select(folder => folder.Order).Should().BeEquivalentTo(new[] { 0, 1 });
        scene.RootEntityFolders.Should().ContainSingle(pair => pair.Key == "entity-1" && pair.Value == duplicateId);
        scene.ExpandedFolderIds.Should().ContainSingle().Which.Should().Be(duplicateId);
        scene.ExpandedEntityIds.Should().BeEquivalentTo(new[] { "entity-1", "entity-2" });
    }

    [Fact]
    public void Clone_CreatesDeepCopy()
    {
        var original = EditorProjectSettings.GetDefault();
        original.Hierarchy.Scenes["Main"] = new HierarchySceneState
        {
            Folders = new List<HierarchyFolderState> { new() { Id = "folder-1", Name = "Folder" } }
        };

        var clone = original.Clone();
        clone.Hierarchy.Scenes["Main"].Folders[0].Name = "Renamed";

        original.Hierarchy.Scenes["Main"].Folders[0].Name.Should().Be("Folder");
    }
}
