using System.Text.Json;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// Finding 17 moved the settings file to source-generated serialization. The file is the user's, written
/// by every earlier version, so the point is that it reads and writes exactly as before.
/// </summary>
public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mdnotes-settings-").FullName;
    private string File_ => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The shape the reflection serializer wrote: PascalCase names, enums as numbers.</summary>
    [Fact]
    public void A_file_written_by_an_earlier_version_is_read_as_it_was()
    {
        File.WriteAllText(File_, """
            {"LastVaultPath":"/notes","UseNoteTitles":true,"ThemeMode":2,"ViewMode":0,"AutoSave":1,
             "AutoSaveDelayMs":1500,"NewNoteTemplatePath":"Templates/t.md","UseGitIgnore":true,
             "NotePositions":{"/notes/a.md":12}}
            """);

        var settings = new JsonSettingsService(File_);

        Assert.Equal("/notes", settings.LastVaultPath);
        Assert.True(settings.UseNoteTitles);
        Assert.Equal((ThemeMode)2, settings.ThemeMode);
        Assert.Equal((ViewMode)0, settings.ViewMode);
        Assert.Equal((AutoSaveMode)1, settings.AutoSave);
        Assert.Equal(1500, settings.AutoSaveDelayMs);
        Assert.Equal("Templates/t.md", settings.NewNoteTemplatePath);
        Assert.True(settings.UseGitIgnore);
        Assert.Equal(12, settings.NotePositions.Restore("/notes/a.md", 100));
    }

    [Fact]
    public void What_is_written_keeps_the_same_names_and_reads_back()
    {
        var settings = new JsonSettingsService(File_) { LastVaultPath = "/vault", AutoSaveDelayMs = 900 };

        using var written = JsonDocument.Parse(File.ReadAllText(File_));
        Assert.Equal("/vault", written.RootElement.GetProperty("LastVaultPath").GetString());
        Assert.Equal(JsonValueKind.Number, written.RootElement.GetProperty("ThemeMode").ValueKind);

        var again = new JsonSettingsService(File_);
        Assert.Equal("/vault", again.LastVaultPath);
        Assert.Equal(900, again.AutoSaveDelayMs);
    }

    /// <summary>Read before any window exists, so a broken file used to stop the app from starting.</summary>
    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("""{"ThemeMode":"purple"}""")]
    public void An_unreadable_file_starts_on_defaults_and_is_kept_aside(string content)
    {
        File.WriteAllText(File_, content);

        var settings = new JsonSettingsService(File_);

        Assert.Null(settings.LastVaultPath);
        Assert.Equal(ThemeMode.System, settings.ThemeMode);
        Assert.Equal(content, File.ReadAllText(File_ + ".corrupt"));
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void A_null_position_map_is_read_as_empty()
    {
        File.WriteAllText(File_, """{"LastVaultPath":"/notes","NotePositions":null}""");

        var settings = new JsonSettingsService(File_);

        Assert.Equal("/notes", settings.LastVaultPath);
        Assert.Equal(0, settings.NotePositions.Restore("/notes/a.md", 100));
    }
}
