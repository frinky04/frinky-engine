using System.Text.Json;
using FrinkyEngine.Core.Rendering;

namespace FrinkyEngine.Editor;

/// <summary>
/// Discovers and provides access to available project templates.
/// </summary>
public static class ProjectTemplateRegistry
{
    private static readonly List<ProjectTemplate> _templates = new();

    public static IReadOnlyList<ProjectTemplate> Templates => _templates;

    /// <summary>
    /// Scans for project templates in the ProjectTemplates directory.
    /// </summary>
    public static void Discover()
    {
        DiscoverFromBaseDirectory(AppContext.BaseDirectory);
    }

    /// <summary>
    /// Scans for project templates relative to a supplied base directory.
    /// </summary>
    /// <param name="baseDirectory">The directory used to resolve template layouts.</param>
    internal static void DiscoverFromBaseDirectory(string baseDirectory)
    {
        _templates.Clear();

        var templatesDir = FindTemplatesDirectory(baseDirectory);
        if (templatesDir == null)
        {
            FrinkyLog.Warning("ProjectTemplateRegistry: could not locate ProjectTemplates directory.");
            return;
        }

        foreach (var dir in Directory.GetDirectories(templatesDir))
        {
            var metadataPath = Path.Combine(dir, "template.json");
            if (!File.Exists(metadataPath))
                continue;

            try
            {
                var json = File.ReadAllText(metadataPath);
                var metadata = JsonSerializer.Deserialize<TemplateMetadata>(json);
                if (metadata == null)
                    continue;

                var contentDir = Path.Combine(dir, "content");
                if (!Directory.Exists(contentDir))
                    continue;

                _templates.Add(new ProjectTemplate
                {
                    Id = metadata.id ?? Path.GetFileName(dir),
                    Name = metadata.name ?? Path.GetFileName(dir),
                    Description = metadata.description ?? string.Empty,
                    SortOrder = metadata.sortOrder,
                    SourceName = metadata.sourceName ?? "FrinkyGame",
                    ContentDirectory = contentDir
                });
            }
            catch (Exception ex)
            {
                FrinkyLog.Warning($"ProjectTemplateRegistry: failed to load template from {dir}: {ex.Message}");
            }
        }

        _templates.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        FrinkyLog.Info($"ProjectTemplateRegistry: discovered {_templates.Count} template(s).");
    }

    public static ProjectTemplate? GetById(string id) =>
        _templates.Find(t => t.Id == id);

    private static string? FindTemplatesDirectory(string baseDir)
    {
        // Check alongside the executable first (deployed/output directory)
        var candidate = Path.Combine(baseDir, "ProjectTemplates");
        if (Directory.Exists(candidate))
            return candidate;

        // Walk up from base directory looking for FrinkyEngine.sln (source builds)
        var dir = baseDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "FrinkyEngine.sln")))
            {
                candidate = Path.Combine(dir, "templates", "ProjectTemplates");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    // Minimal JSON deserialization target — mirrors template.json structure
    private sealed record TemplateMetadata(
        string? id,
        string? name,
        string? description,
        int sortOrder,
        string? sourceName);
}
