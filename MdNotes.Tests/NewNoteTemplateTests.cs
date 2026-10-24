using Avalonia.Headless.XUnit;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// Every path that creates a note starts it from the template - New note, the quick switcher's Create
/// row and a wikilink to a note that is not there - and an import still does not.
/// AvaloniaFact throughout: MainViewModel owns DispatcherTimers.
/// </summary>
public sealed class NewNoteTemplateTests : IDisposable
{
    private const string Template = "---\ncreated: {{date}}\n---\n\n# {{title}}\n";

    private readonly TempVault _vault = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeSettings _settings = new();

    public NewNoteTemplateTests() => _settings.LastVaultPath = _vault.Root;

    public void Dispose() => _vault.Dispose();

    private string Expected(string title) =>
        $"---\ncreated: {DateTimeOffset.Now:yyyy-MM-dd}\n---\n\n# {title}\n";

    private async Task<MainViewModel> OpenAsync(string? templatePath = "Templates/New note.md")
    {
        _settings.NewNoteTemplatePath = templatePath;
        var vm = new MainViewModel(TestServices.For(_vault, _dialogs) with
        {
            // The real services, not the fakes: this suite is about what a new note is actually born with.
            Templates = new NoteTemplateService(_vault.Service, _settings),
            Exclusions = new VaultIgnoreService(_vault.Service, _settings),
        });

        Assert.True(await vm.TryOpenInitialVaultAsync());
        Assert.Empty(_dialogs.Errors);
        return vm;
    }

    [AvaloniaFact]
    public async Task A_new_note_starts_from_the_template()
    {
        _vault.Note("Templates/New note.md", Template);
        using var vm = await OpenAsync();

        // FakeDialogs answers a prompt with the value it was offered.
        await vm.Workspace.NewNoteCommand.ExecuteAsync(null);

        var note = Path.Combine(_vault.Root, "Untitled.md");
        Assert.Equal(Expected("Untitled"), File.ReadAllText(note));
        Assert.Equal(Expected("Untitled"), vm.Editor.Text);
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task A_wikilink_to_a_note_that_is_not_there_creates_it_from_the_template()
    {
        _vault.Note("Templates/New note.md", Template);
        using var vm = await OpenAsync();

        vm.Workspace.OpenWikiLinkCommand.Execute("Meeting notes");

        Assert.Equal(Expected("Meeting notes"), File.ReadAllText(Path.Combine(_vault.Root, "Meeting notes.md")));
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task The_quick_switcher_create_row_uses_the_template()
    {
        _vault.Note("Templates/New note.md", Template);
        using var vm = await OpenAsync();

        vm.Workspace.QuickSwitcher.Open();
        vm.Workspace.QuickSwitcher.Query = "Reading list";
        vm.Workspace.QuickSwitcher.OpenSelectedCommand.Execute(null);

        Assert.Equal(Expected("Reading list"), File.ReadAllText(Path.Combine(_vault.Root, "Reading list.md")));
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Without_a_template_a_new_note_is_blank_as_before()
    {
        using var vm = await OpenAsync(templatePath: null);

        await vm.Workspace.NewNoteCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(_vault.Root, "Untitled.md")));
        Assert.Empty(_dialogs.Errors);
    }

    /// <summary>An import brings its own content, so the template must stay out of it.</summary>
    [AvaloniaFact]
    public async Task Importing_a_loose_file_ignores_the_template()
    {
        _vault.Note("Templates/New note.md", Template);
        using var vm = await OpenAsync();

        var loose = Path.Combine(Path.GetTempPath(), $"mdnotes-loose-{Guid.NewGuid():N}.md");
        File.WriteAllText(loose, "# readme");
        try
        {
            vm.OpenLooseFile(loose);
            vm.Editor.Text = "# readme, edited";
            _dialogs.ConfirmResult = false;
            await vm.Workspace.ImportToVaultCommand.ExecuteAsync(null);

            var imported = Path.Combine(_vault.Root, Path.GetFileName(loose));
            Assert.Equal("# readme, edited", File.ReadAllText(imported));
            Assert.Empty(_dialogs.Errors);
        }
        finally
        {
            File.Delete(loose);
        }
    }

    /// <summary>A stale path must cost a blank note, not a dialog on every note the user creates.</summary>
    [AvaloniaFact]
    public async Task A_template_that_is_not_there_still_creates_the_note_and_says_nothing()
    {
        using var vm = await OpenAsync("Templates/Gone.md");

        await vm.Workspace.NewNoteCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(_vault.Root, "Untitled.md")));
        Assert.Empty(_dialogs.Errors);
    }
}
