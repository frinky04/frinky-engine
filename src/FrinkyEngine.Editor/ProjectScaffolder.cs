using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;

namespace FrinkyEngine.Editor;

public static class ProjectScaffolder
{
    internal interface IProcessRunner
    {
        void Run(string fileName, string workingDirectory, string arguments, int timeoutMilliseconds);
    }

    internal sealed class DefaultProcessRunner : IProcessRunner
    {
        public void Run(string fileName, string workingDirectory, string arguments, int timeoutMilliseconds)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            process.WaitForExit(timeoutMilliseconds);

            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"{fileName} {arguments} failed: {stderr}");
            }
        }
    }

    private static readonly HashSet<string> TextFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".json", ".fscene", ".fprefab", ".txt", ".md", ".gitignore"
    };

    internal static IProcessRunner ProcessRunner { get; set; } = new DefaultProcessRunner();

    /// <summary>
    /// Creates a new game project on disk and returns the path to the .fproject file.
    /// </summary>
    public static string CreateProject(string parentDirectory, string projectName, ProjectTemplate template)
    {
        var projectDir = Path.Combine(parentDirectory, projectName);
        Directory.CreateDirectory(projectDir);

        // 1. Create .fproject
        var fprojectPath = Path.Combine(projectDir, $"{projectName}.fproject");
        var projectFile = new ProjectFile
        {
            ProjectName = projectName,
            DefaultScene = "Scenes/MainScene.fscene",
            AssetsPath = "Assets",
            GameProject = $"{projectName}.csproj",
            GameAssembly = $"bin/Debug/net8.0/{projectName}.dll"
        };
        projectFile.Save(fprojectPath);

        var settingsPath = ProjectSettings.GetPath(projectDir);
        var settings = ProjectSettings.GetDefault(projectName);
        settings.Save(settingsPath);

        var editorSettings = EditorProjectSettings.GetDefault();
        editorSettings.Save(projectDir);

        // 2. Generate .csproj and .sln with absolute path to running editor's Core DLL
        EnsureProjectFiles(projectDir, projectName);

        // 3. Copy template content (scenes, scripts, etc.)
        CopyTemplateContent(template.ContentDirectory, projectDir, template.SourceName, projectName);

        // 4. Write .gitignore and initialize git repo
        var gitignorePath = Path.Combine(projectDir, ".gitignore");
        File.WriteAllText(gitignorePath, GenerateGitignore());
        InitializeGitRepo(projectDir);

        return fprojectPath;
    }

    /// <summary>
    /// Creates a new game project using the default template (3d-starter).
    /// </summary>
    public static string CreateProject(string parentDirectory, string projectName)
    {
        var template = ProjectTemplateRegistry.GetById("3d-starter")
            ?? ProjectTemplateRegistry.Templates.FirstOrDefault()
            ?? throw new InvalidOperationException("No project templates found. Ensure ProjectTemplateRegistry.Discover() has been called.");
        return CreateProject(parentDirectory, projectName, template);
    }

    /// <summary>
    /// Generates (or regenerates) the .csproj and .sln for a game project.
    /// Uses an absolute HintPath to the running editor's FrinkyEngine.Core.dll.
    /// Only writes files and runs restore if content actually changed.
    /// </summary>
    public static void EnsureProjectFiles(string projectDir, string projectName)
    {
        try
        {
            var coreAssemblyPath = typeof(Component).Assembly.Location;
            if (string.IsNullOrEmpty(coreAssemblyPath) || !File.Exists(coreAssemblyPath))
            {
                FrinkyLog.Warning("Cannot locate FrinkyEngine.Core.dll — skipping project file generation.");
                return;
            }

            var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
            var slnPath = Path.Combine(projectDir, $"{projectName}.sln");

            var csprojContent = GenerateCsproj(coreAssemblyPath);
            var slnContent = GenerateSln(projectName);

            var csprojChanged = WriteIfChanged(csprojPath, csprojContent);
            var slnChanged = WriteIfChanged(slnPath, slnContent);

            if (csprojChanged || slnChanged)
            {
                FrinkyLog.Info("Project files regenerated — running dotnet restore...");
                RunDotnetRestore(projectDir);
            }
            else
            {
                FrinkyLog.Info("Project files are up to date.");
            }

            EnsureGitignoreEntries(projectDir);
        }
        catch (Exception ex)
        {
            FrinkyLog.Warning($"Could not generate project files: {ex.Message}");
        }
    }

    private static string GenerateCsproj(string coreAssemblyAbsolutePath)
    {
        // Use forward slashes for .csproj compatibility
        var normalizedPath = coreAssemblyAbsolutePath.Replace('\\', '/');
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <LangVersion>12</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <OutputType>Library</OutputType>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Raylib-cs" Version="7.0.2" />
                <Reference Include="FrinkyEngine.Core">
                  <HintPath>{normalizedPath}</HintPath>
                  <Private>false</Private>
                </Reference>
              </ItemGroup>

            </Project>
            """;
    }

    private static string GenerateSln(string projectName)
    {
        // Deterministic project GUID from project name
        var nameBytes = Encoding.UTF8.GetBytes(projectName);
        var hashBytes = MD5.HashData(nameBytes);
        var projectGuid = new Guid(hashBytes).ToString("B").ToUpperInvariant();

        // Fixed type GUID for C# projects
        const string csharpTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

        return $"""

            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{csharpTypeGuid}") = "{projectName}", "{projectName}.csproj", "{projectGuid}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            		Release|Any CPU = Release|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{projectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{projectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{projectGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
            		{projectGuid}.Release|Any CPU.Build.0 = Release|Any CPU
            	EndGlobalSection
            EndGlobal
            """;
    }

    /// <summary>
    /// Writes content to a file only if it differs from the existing content.
    /// Returns true if the file was written.
    /// </summary>
    private static bool WriteIfChanged(string path, string newContent)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (existing == newContent)
                return false;
        }

        File.WriteAllText(path, newContent);
        return true;
    }

    private static void RunDotnetRestore(string projectDir)
    {
        try
        {
            RunDotnet(projectDir, "restore");
            FrinkyLog.Info("dotnet restore completed.");
        }
        catch (Exception ex)
        {
            FrinkyLog.Warning($"dotnet restore failed (dotnet SDK may not be installed): {ex.Message}");
        }
    }

    /// <summary>
    /// Appends *.csproj and *.sln to an existing .gitignore if those entries are missing.
    /// Handles migration of existing projects to the new generated-project-files workflow.
    /// </summary>
    private static void EnsureGitignoreEntries(string projectDir)
    {
        var gitignorePath = Path.Combine(projectDir, ".gitignore");
        if (!File.Exists(gitignorePath))
            return;

        var content = File.ReadAllText(gitignorePath);
        var entries = new List<string>();

        if (!content.Contains("*.csproj", StringComparison.Ordinal))
            entries.Add("*.csproj");
        if (!content.Contains("*.sln", StringComparison.Ordinal))
            entries.Add("*.sln");

        if (entries.Count == 0)
            return;

        var sb = new StringBuilder(content);
        if (!content.EndsWith('\n'))
            sb.AppendLine();

        sb.AppendLine();
        sb.AppendLine("## Generated project files");
        foreach (var entry in entries)
            sb.AppendLine(entry);

        File.WriteAllText(gitignorePath, sb.ToString());
        FrinkyLog.Info("Updated .gitignore with *.csproj and *.sln entries.");
    }

    private static void CopyTemplateContent(string contentDir, string projectDir, string sourceName, string projectName)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(contentDir, sourceFile);

            // Skip .template.config directories (dotnet new metadata)
            if (relativePath.StartsWith(".template.config", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip root-level files that the scaffolder generates dynamically
            if (!relativePath.Contains(Path.DirectorySeparatorChar) && !relativePath.Contains(Path.AltDirectorySeparatorChar))
            {
                var ext = Path.GetExtension(relativePath);
                if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".fproject", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(relativePath).Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Skip .frinky/engine/ directory (no longer needed)
            if (relativePath.Replace('\\', '/').StartsWith(".frinky/engine", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetPath = Path.Combine(projectDir, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath)!;
            Directory.CreateDirectory(targetDirectory);

            var extension = Path.GetExtension(sourceFile);
            if (TextFileExtensions.Contains(extension))
            {
                // Perform sourceName → projectName replacement in text files
                var content = File.ReadAllText(sourceFile);
                content = content.Replace(sourceName, projectName);
                File.WriteAllText(targetPath, content);
            }
            else
            {
                File.Copy(sourceFile, targetPath, overwrite: true);
            }
        }

        FrinkyLog.Info($"Scaffold: copied template content from {contentDir}");
    }

    private static string GenerateGitignore()
    {
        return """
            ## .NET
            bin/
            obj/
            *.user
            *.suo
            *.cache

            ## IDE
            .vs/
            .idea/
            *.swp
            *~

            ## Build
            publish/
            out/

            ## OS
            Thumbs.db
            .DS_Store

            ## Engine
            .frinky/
            *.fproject.user
            imgui.ini

            ## Generated project files
            *.csproj
            *.sln
            """;
    }

    private static void InitializeGitRepo(string projectDir)
    {
        try
        {
            RunGit(projectDir, "init");
            RunGit(projectDir, "add .");
            RunGit(projectDir, "commit -m \"Initial commit\"");
            FrinkyLog.Info("Scaffold: initialized git repository.");
        }
        catch (Exception ex)
        {
            FrinkyLog.Warning($"Scaffold: could not initialize git repo (git may not be installed): {ex.Message}");
        }
    }

    private static void RunDotnet(string workingDirectory, string arguments)
    {
        ProcessRunner.Run("dotnet", workingDirectory, arguments, 60_000);
    }

    private static void RunGit(string workingDirectory, string arguments)
    {
        ProcessRunner.Run("git", workingDirectory, arguments, 10_000);
    }
}
