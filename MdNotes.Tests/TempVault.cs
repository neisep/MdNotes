using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>A VaultService opened on a fresh temp folder that is deleted on dispose.</summary>
public sealed class TempVault : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("mdnotes-test-").FullName;

    /// <summary>The settings the vault reads - exposed so a test can turn the '.gitignore' switch on.</summary>
    public ISettingsService Settings { get; } = new FakeSettings();

    public VaultService Service { get; }

    /// <param name="contentBudgetChars">
    /// The content cache's budget, when a test needs one small enough to overrun with a few notes.
    /// </param>
    public TempVault(long? contentBudgetChars = null)
    {
        Service = contentBudgetChars is { } budget
            ? new VaultService(Settings, budget)
            : new VaultService(Settings);
        Service.Open(Root);
    }

    /// <summary>
    /// Seeds a note. Like <see cref="Ignore"/>, this lands behind the service's back - as an external
    /// editor's write would - so the vault is told at once rather than waiting out its watcher, and a
    /// test can assert on the very next read.
    /// </summary>
    public string Note(string relativePath, string content = "")
    {
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Service.Invalidate();
        return path;
    }

    /// <summary>Writes the vault's exclusion list and makes the vault pick it up at once.</summary>
    public void Ignore(string patterns)
    {
        File.WriteAllText(Path.Combine(Root, ".mdnotesignore"), patterns);
        Service.RefreshExclusions();
    }

    /// <summary>Seeds a folder, told to the vault at once for the same reason <see cref="Note"/> is.</summary>
    public string Folder(string relativePath)
    {
        var path = Directory.CreateDirectory(Path.Combine(Root, relativePath)).FullName;
        Service.Invalidate();
        return path;
    }

    public void Dispose()
    {
        Service.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
