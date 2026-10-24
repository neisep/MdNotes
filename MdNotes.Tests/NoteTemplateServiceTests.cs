using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// Resolving the configured template against a real vault. Every failure has to land on "" - a template
/// that cannot be used must never stop a note being created - so most of these assert exactly that.
/// </summary>
public sealed class NoteTemplateServiceTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly FakeSettings _settings = new();

    public void Dispose() => _vault.Dispose();

    private NoteTemplateService Service => new(_vault.Service, _settings);

    [Fact]
    public void Without_a_setting_a_new_note_starts_blank()
    {
        Assert.Equal(string.Empty, Service.ContentFor(_vault.Root, "Meeting"));
        Assert.Equal(TemplateState.None, Service.Probe());
    }

    [Fact]
    public void The_template_is_read_and_its_placeholders_filled()
    {
        _vault.Note("Templates/New note.md", "# {{title}}\n\ncreated: {{date}}\n");
        _settings.NewNoteTemplatePath = "Templates/New note.md";

        var content = Service.ContentFor(_vault.Root, "Meeting");

        Assert.StartsWith("# Meeting", content);
        Assert.Contains($"created: {DateTimeOffset.Now:yyyy-MM-dd}", content);
        Assert.Equal(TemplateState.Ready, Service.Probe());
    }

    [Fact]
    public void The_name_may_carry_its_extension()
    {
        _vault.Note("T.md", "# {{title}}");
        _settings.NewNoteTemplatePath = "T";

        Assert.Equal("# Meeting", Service.ContentFor(_vault.Root, "Meeting.md"));
    }

    [Fact]
    public void A_template_that_is_not_there_yet_is_reported_and_leaves_the_note_blank()
    {
        _settings.NewNoteTemplatePath = "Templates/Missing.md";

        Assert.Equal(string.Empty, Service.ContentFor(_vault.Root, "Meeting"));
        Assert.Equal(TemplateState.Missing, Service.Probe());
    }

    /// <summary>A directory where a note is expected is the portable way to make a read fail.</summary>
    [Fact]
    public void A_template_that_cannot_be_read_is_reported_and_leaves_the_note_blank()
    {
        _vault.Folder("Daily.md");
        _settings.NewNoteTemplatePath = "Daily.md";

        Assert.Equal(string.Empty, Service.ContentFor(_vault.Root, "Meeting"));
        Assert.Equal(TemplateState.Unreadable, Service.Probe());
    }

    [Fact]
    public void Creating_the_template_itself_does_not_apply_it()
    {
        _vault.Note("Templates/New note.md", "# {{title}}");
        _settings.NewNoteTemplatePath = "Templates/New note.md";
        var templates = Path.Combine(_vault.Root, "Templates");

        Assert.Equal(string.Empty, Service.ContentFor(templates, "New note"));
        // A note of the same name elsewhere is a different note, and does get the template.
        Assert.Equal("# New note", Service.ContentFor(_vault.Root, "New note"));
    }

    [Fact]
    public void A_path_that_climbs_out_of_the_vault_is_no_template()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_vault.Root)!, "outside.md");
        File.WriteAllText(outside, "secret");
        try
        {
            _settings.NewNoteTemplatePath = "../outside.md";
            Assert.Equal(string.Empty, Service.ContentFor(_vault.Root, "Meeting"));
            Assert.Equal(TemplateState.None, Service.Probe());
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Without_a_vault_there_is_nothing_to_resolve_against()
    {
        _settings.NewNoteTemplatePath = "Templates/New note.md";
        var service = new NoteTemplateService(new VaultService(_settings), _settings);

        Assert.Equal(string.Empty, service.ContentFor(_vault.Root, "Meeting"));
        Assert.Equal(TemplateState.None, service.Probe());
    }
}
