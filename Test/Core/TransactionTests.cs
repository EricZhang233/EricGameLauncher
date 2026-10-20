using System.IO.Compression;
using updater.main;
using Xunit;

namespace EricGameLauncher.Tests;

public sealed class TransactionTests : IDisposable
{
    private readonly string root;

    public TransactionTests()
    {
        root = EricGameLauncher.Testing.TestWorkspace.NewFixture("transactions-");
        File.WriteAllText(Path.Combine(root, "EricGameLauncher.exe"), "old");
    }

    private static ZipArchive Package(params (string Name, string Value)[] entries)
    {
        var bytes = new MemoryStream();
        using (var writer = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                using var text = new StreamWriter(writer.CreateEntry(entry.Name).Open());
                text.Write(entry.Value);
            }
        bytes.Position = 0;
        return new ZipArchive(bytes, ZipArchiveMode.Read);
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("..\\escape.exe")]
    [InlineData("._update_backup/journal.json")]
    [InlineData(".migration-test/data")]
    [Trait("Category", "Core")]
    public void UnsafeEntriesCannotAlterInstallation(string name)
    {
        using var zip = Package(("EricGameLauncher.exe", "new"), (name, "untrusted"));
        Assert.Throws<InvalidDataException>(() => UpdateTransaction.Apply(root, zip));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "EricGameLauncher.exe")));
        Assert.False(Directory.Exists(Path.Combine(root, "._update_staging")));
    }

    [Fact]
    [Trait("Category", "Core")]
    public void MissingExecutableCannotReplaceInstallation()
    {
        using var zip = Package(("other.dll", "new"));
        Assert.Throws<InvalidDataException>(() => UpdateTransaction.Apply(root, zip));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "EricGameLauncher.exe")));
    }

    [Fact]
    [Trait("Category", "Core")]
    public void PackageCannotOverwriteUserDataOrStorageMode()
    {
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        File.WriteAllText(Path.Combine(root, "Data", "settings.yaml"), "user");
        File.WriteAllText(Path.Combine(root, "storage.mode"), "portable");
        using var zip = Package(("EricGameLauncher.exe", "new"), ("Data/settings.yaml", "bad"), ("storage.mode", "bad"));
        UpdateTransaction.Apply(root, zip);
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "EricGameLauncher.exe")));
        Assert.Equal("user", File.ReadAllText(Path.Combine(root, "Data", "settings.yaml")));
        Assert.Equal("portable", File.ReadAllText(Path.Combine(root, "storage.mode")));
    }

    [Theory]
    [InlineData("backup:EricGameLauncher.exe")]
    [InlineData("install:EricGameLauncher.exe")]
    [InlineData("commit")]
    [Trait("Category", "Core")]
    public void FailureAtEachStageRestoresOriginal(string failure)
    {
        using var zip = Package(("EricGameLauncher.exe", "new"), ("new.dll", "new"));
        Assert.Throws<IOException>(() => UpdateTransaction.Apply(root, zip, step => { if (step == failure) throw new IOException("injected"); }));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "EricGameLauncher.exe")));
        Assert.False(File.Exists(Path.Combine(root, "new.dll")));
        Assert.False(Directory.Exists(Path.Combine(root, "._update_backup")));
    }

    public void Dispose() => EricGameLauncher.Testing.TestWorkspace.DeleteFixture(root);
}
