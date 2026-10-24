using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// The containment rule for images. Notes are files the user already owns, so this is not a privilege
/// boundary - it is the same rule every other path-handling site in the app observes, applied at the
/// one site that used to skip it.
/// </summary>
public sealed class NoteImagePathTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mdnotes-image-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string File_(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not really a png");
        return path;
    }

    [Fact]
    public void Resolves_an_image_beside_the_note()
    {
        var expected = File_("pic.png");
        Assert.Equal(expected, NoteImagePath.Resolve(_root, _root, "pic.png"));
    }

    [Fact]
    public void Resolves_an_attachments_folder_above_the_note()
    {
        var expected = File_("assets/pic.png");
        var noteFolder = Path.Combine(_root, "notes");
        Directory.CreateDirectory(noteFolder);

        Assert.Equal(expected, NoteImagePath.Resolve(noteFolder, _root, "../assets/pic.png"));
    }

    [Fact]
    public void Refuses_a_relative_path_that_climbs_out_of_the_root()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, $"outside-{Guid.NewGuid():N}.png");
        File.WriteAllText(outside, "not really a png");
        try
        {
            Assert.Null(NoteImagePath.Resolve(_root, _root, $"../{Path.GetFileName(outside)}"));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    /// <summary>
    /// The trap NoteTemplateService documents: un-normalised, "root/../../x" starts with the root and
    /// would pass a bare prefix test. GetFullPath has to run first.
    /// </summary>
    [Fact]
    public void Refuses_a_path_that_only_looks_like_it_starts_with_the_root()
    {
        Assert.Null(NoteImagePath.Resolve(_root, _root, "../../../../etc/hosts"));
    }

    [Fact]
    public void Refuses_an_absolute_path_outside_the_root()
    {
        var elsewhere = Directory.CreateTempSubdirectory("mdnotes-elsewhere-").FullName;
        var outside = Path.Combine(elsewhere, "pic.png");
        File.WriteAllText(outside, "not really a png");
        try
        {
            Assert.Null(NoteImagePath.Resolve(_root, _root, outside));
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void Refuses_a_web_url()
    {
        Assert.Null(NoteImagePath.Resolve(_root, _root, "https://example.com/pic.png"));
    }

    [Fact]
    public void Refuses_a_file_that_is_not_there()
    {
        Assert.Null(NoteImagePath.Resolve(_root, _root, "missing.png"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_an_empty_url(string? url)
    {
        Assert.Null(NoteImagePath.Resolve(_root, _root, url));
    }

    [Fact]
    public void Refuses_everything_when_there_is_no_base_directory()
    {
        Assert.Null(NoteImagePath.Resolve(null, _root, "pic.png"));
    }

    /// <summary>No root is the closed default: the note's own folder, not "anywhere on disk".</summary>
    [Fact]
    public void Without_a_root_the_note_folder_contains_the_image()
    {
        var inside = File_("notes/pic.png");
        File_("assets/pic.png");
        var noteFolder = Path.Combine(_root, "notes");

        Assert.Equal(inside, NoteImagePath.Resolve(noteFolder, null, "pic.png"));
        Assert.Null(NoteImagePath.Resolve(noteFolder, null, "../assets/pic.png"));
    }

    /// <summary>A root handed over with a trailing separator must behave like the same folder without one.</summary>
    [Fact]
    public void A_trailing_separator_on_the_root_changes_nothing()
    {
        var expected = File_("pic.png");
        Assert.Equal(expected, NoteImagePath.Resolve(_root, _root + Path.DirectorySeparatorChar, "pic.png"));
    }

    [Fact]
    public void Unescapes_a_percent_encoded_name()
    {
        var expected = File_("my pic.png");
        Assert.Equal(expected, NoteImagePath.Resolve(_root, _root, "my%20pic.png"));
    }
}
