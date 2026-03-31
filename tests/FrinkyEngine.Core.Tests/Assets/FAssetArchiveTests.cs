using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Assets;

public sealed class FAssetArchiveTests
{
    [Fact]
    public void WriteAndExtractAll_RoundTripPreservesRelativePathsAndBytes()
    {
        using var temp = new TempDirectory();
        var sourceDir = temp.GetPath("source");
        var extractDir = temp.GetPath("extract");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Nested"));
        File.WriteAllText(Path.Combine(sourceDir, "scene.fscene"), "{\"name\":\"Scene\"}");
        File.WriteAllBytes(Path.Combine(sourceDir, "Nested", "texture.bin"), new byte[] { 1, 2, 3, 4, 5 });

        var archivePath = temp.GetPath("game.fasset");
        var entries = new[]
        {
            new FAssetEntry { RelativePath = "Assets/scene.fscene", SourcePath = Path.Combine(sourceDir, "scene.fscene") },
            new FAssetEntry { RelativePath = "Assets/Nested/texture.bin", SourcePath = Path.Combine(sourceDir, "Nested", "texture.bin") }
        };

        FAssetArchive.Write(archivePath, entries);
        FAssetArchive.ExtractAll(archivePath, extractDir);

        File.ReadAllText(Path.Combine(extractDir, "Assets", "scene.fscene")).Should().Be("{\"name\":\"Scene\"}");
        File.ReadAllBytes(Path.Combine(extractDir, "Assets", "Nested", "texture.bin")).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void ExtractAll_OverwritesExistingFiles()
    {
        using var temp = new TempDirectory();
        var sourceFile = temp.GetPath("source.txt");
        File.WriteAllText(sourceFile, "new");

        var archivePath = temp.GetPath("overwrite.fasset");
        FAssetArchive.Write(archivePath, new[]
        {
            new FAssetEntry { RelativePath = "Assets/file.txt", SourcePath = sourceFile }
        });

        var extractDir = temp.GetPath("extract");
        Directory.CreateDirectory(Path.Combine(extractDir, "Assets"));
        File.WriteAllText(Path.Combine(extractDir, "Assets", "file.txt"), "old");

        FAssetArchive.ExtractAll(archivePath, extractDir);

        File.ReadAllText(Path.Combine(extractDir, "Assets", "file.txt")).Should().Be("new");
    }

    [Fact]
    public void ExtractAll_CorruptedArchiveThrows()
    {
        using var temp = new TempDirectory();
        var archivePath = temp.GetPath("broken.fasset");
        File.WriteAllBytes(archivePath, new byte[] { 0, 1, 2, 3, 4 });

        Action act = () => FAssetArchive.ExtractAll(archivePath, temp.GetPath("extract"));

        act.Should().Throw<InvalidDataException>();
    }
}
