using Avalonia.Headless.XUnit;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// The three autosave modes and the configurable delay. AvaloniaFact throughout: the editor owns
/// DispatcherTimers, so it needs a UI thread.
/// </summary>
public sealed class AutoSaveTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly FakeDialogs _dialogs = new();

    // The vault's own settings, which is the instance the window gets too - one settings service per
    // vault, as AppSession wires it.
    private readonly ISettingsService _settings;

    public AutoSaveTests() => _settings = _vault.Settings;

    public void Dispose() => _vault.Dispose();

    private EditorViewModel NewEditor() => new(_vault.Service, new MarkdownRenderer(), _settings, _dialogs);

    private MainViewModel NewViewModel() => new(TestServices.For(_vault, _dialogs));

    [AvaloniaFact]
    public async Task The_configured_delay_is_the_delay()
    {
        _settings.AutoSaveDelayMs = 200;
        var note = _vault.Note("Note.md", "original");
        var editor = NewEditor();

        editor.Load(note);
        editor.Text = "edited";
        await Task.Delay(500);

        Assert.Equal("edited", File.ReadAllText(note));
    }

    /// <summary>
    /// Code-quality H1: the timer saved bare, so a disk that refused the write reached the crash
    /// barrier and was reported as a bug. It goes through the same guard as Ctrl+S now.
    /// </summary>
    [AvaloniaFact]
    public async Task A_failed_timed_save_is_reported_as_a_failed_save()
    {
        _settings.AutoSaveDelayMs = 200;
        var note = _vault.Note("Gone/Note.md", "original");
        var editor = NewEditor();

        editor.Load(note);
        editor.Text = "edited";
        Directory.Delete(Path.GetDirectoryName(note)!, recursive: true);
        await Task.Delay(500);

        Assert.DoesNotContain("did not expect", Assert.Single(_dialogs.Errors));
        Assert.True(editor.IsDirty);
    }

    /// <summary>The same for leaving the window, which goes through the view model's guarded gate.</summary>
    [AvaloniaFact]
    public void A_failed_save_on_leaving_the_window_is_reported_as_a_failed_save()
    {
        _settings.AutoSaveDelayMs = 10_000;
        var note = _vault.Note("Gone/Note.md", "original");
        var vm = NewViewModel();

        vm.Editor.Load(note);
        vm.Editor.Text = "edited";
        Directory.Delete(Path.GetDirectoryName(note)!, recursive: true);
        vm.SaveImplicitly();

        Assert.DoesNotContain("did not expect", Assert.Single(_dialogs.Errors));
        Assert.True(vm.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task A_long_delay_holds_the_write_back()
    {
        _settings.AutoSaveDelayMs = 10_000;
        var note = _vault.Note("Note.md", "original");
        var editor = NewEditor();

        editor.Load(note);
        editor.Text = "edited";
        await Task.Delay(500);

        Assert.Equal("original", File.ReadAllText(note));
        Assert.True(editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task On_focus_change_writes_when_the_note_is_left_and_not_before()
    {
        _settings.AutoSave = AutoSaveMode.OnFocusChange;
        var note = _vault.Note("Note.md", "original");
        var other = _vault.Note("Other.md", "other");
        var editor = NewEditor();

        editor.Load(note);
        editor.Text = "edited";
        await Task.Delay(500);
        Assert.Equal("original", File.ReadAllText(note));

        editor.Load(other);
        Assert.Equal("edited", File.ReadAllText(note));

        editor.Text = "other edited";
        editor.Close();
        Assert.Equal("other edited", File.ReadAllText(other));
    }

    [AvaloniaFact]
    public async Task Manual_writes_nothing_the_user_did_not_ask_for()
    {
        _settings.AutoSave = AutoSaveMode.Manual;
        var note = _vault.Note("Note.md", "original");
        var other = _vault.Note("Other.md", "other");
        var editor = NewEditor();

        editor.Load(note);
        editor.Text = "edited";
        await Task.Delay(500);
        Assert.Equal("original", File.ReadAllText(note));

        // Neither leaving the note nor closing the editor writes; the caller asks the user instead.
        editor.Load(other);
        Assert.Equal("original", File.ReadAllText(note));

        editor.Load(note);
        editor.Text = "edited again";
        editor.Close();
        Assert.Equal("original", File.ReadAllText(note));

        editor.Text = "saved by hand";
        editor.Save();
        Assert.Equal("saved by hand", File.ReadAllText(note));
    }

    [AvaloniaFact]
    public async Task A_loose_file_stays_manual_whatever_the_setting_says()
    {
        _settings.AutoSave = AutoSaveMode.AfterDelay;
        _settings.AutoSaveDelayMs = 200;
        var looseDir = Directory.CreateTempSubdirectory("mdnotes-loose-").FullName;
        try
        {
            var loose = Path.Combine(looseDir, "README.md");
            File.WriteAllText(loose, "original");
            var editor = NewEditor();

            editor.Load(loose);
            Assert.Equal(AutoSaveMode.Manual, editor.SaveMode);

            editor.Text = "edited";
            await Task.Delay(500);
            Assert.Equal("original", File.ReadAllText(loose));
        }
        finally
        {
            Directory.Delete(looseDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Leaving_a_manual_note_asks_and_honours_no()
    {
        _settings.AutoSave = AutoSaveMode.Manual;
        var note = _vault.Note("A.md", "original");
        _vault.Note("B.md", "b");
        var vm = NewViewModel();
        Assert.True(await vm.TryOpenInitialVaultAsync());

        vm.Workspace.SelectedNode = vm.Workspace.Tree.First(n => n.Name == "A");
        await Task.Delay(50);
        vm.Editor.Text = "edited";

        _dialogs.ConfirmResult = false;
        vm.Workspace.SelectedNode = vm.Workspace.Tree.First(n => n.Name == "B");
        await Task.Delay(50);

        Assert.Single(_dialogs.Confirmations);
        Assert.Equal("original", File.ReadAllText(note));
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Leaving_a_manual_note_asks_and_honours_yes()
    {
        _settings.AutoSave = AutoSaveMode.Manual;
        var note = _vault.Note("A.md", "original");
        _vault.Note("B.md", "b");
        var vm = NewViewModel();
        Assert.True(await vm.TryOpenInitialVaultAsync());

        vm.Workspace.SelectedNode = vm.Workspace.Tree.First(n => n.Name == "A");
        await Task.Delay(50);
        vm.Editor.Text = "edited";

        _dialogs.ConfirmResult = true;
        vm.Workspace.SelectedNode = vm.Workspace.Tree.First(n => n.Name == "B");
        await Task.Delay(50);

        Assert.Single(_dialogs.Confirmations);
        Assert.Equal("edited", File.ReadAllText(note));
    }

    [AvaloniaFact]
    public async Task Closing_a_manual_note_asks_and_honours_the_answer()
    {
        _settings.AutoSave = AutoSaveMode.Manual;
        var note = _vault.Note("A.md", "original");
        var vm = NewViewModel();
        Assert.True(await vm.TryOpenInitialVaultAsync());

        vm.Workspace.SelectedNode = vm.Workspace.Tree.First(n => n.Name == "A");
        await Task.Delay(50);
        vm.Editor.Text = "edited";

        _dialogs.ConfirmResult = true;
        Assert.True(await vm.TryCloseAsync());
        Assert.Single(_dialogs.Confirmations);
        Assert.Equal("edited", File.ReadAllText(note));
    }

    [AvaloniaFact]
    public void The_status_says_what_the_mode_will_actually_do()
    {
        var editor = NewEditor();
        editor.Load(_vault.Note("Note.md", "original"));

        Assert.Equal(AutoSaveMode.AfterDelay, editor.SaveMode);
        Assert.Equal("Saved", editor.SaveStatus);

        editor.Text = "edited";
        Assert.Equal("Editing…", editor.SaveStatus);

        _settings.AutoSave = AutoSaveMode.Manual;
        editor.NotifySaveModeChanged();
        Assert.Equal("Unsaved · Ctrl+S", editor.SaveStatus);
    }

    [Theory]
    [InlineData(0, AutoSaveDelay.MinMs)]
    [InlineData(50, AutoSaveDelay.MinMs)]
    [InlineData(750, 750)]
    [InlineData(10_000_000, AutoSaveDelay.MaxMs)]
    public void The_delay_is_clamped(int given, int expected) =>
        Assert.Equal(expected, AutoSaveDelay.Clamp(given));
}

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly FakeTemplates _templates = new();
    private readonly FakeExclusions _exclusions = new();

    // A real vault, because the panel now tells it to rebuild when the title setting changes.
    private readonly TempVault _vault = new();
    private int _changes;

    public void Dispose() => _vault.Dispose();

    private SettingsViewModel NewViewModel() =>
        new(_settings, _templates, _exclusions, _vault.Service, () => _changes++);

    [Fact]
    public void Choosing_a_mode_persists_it_and_reports_the_change()
    {
        var vm = NewViewModel();
        Assert.True(vm.IsAfterDelay);

        vm.SetSaveModeCommand.Execute(AutoSaveMode.Manual);

        Assert.Equal(AutoSaveMode.Manual, _settings.AutoSave);
        Assert.True(vm.IsManual);
        Assert.False(vm.IsAfterDelay);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void The_template_path_is_normalised_on_the_way_in()
    {
        var vm = NewViewModel();
        Assert.Equal(string.Empty, vm.TemplatePath);

        vm.TemplatePath = " Templates\\New note ";

        Assert.Equal("Templates/New note.md", _settings.NewNoteTemplatePath);
        Assert.Equal("Templates/New note.md", vm.TemplatePath);
    }

    [Fact]
    public void Clearing_the_template_path_goes_back_to_blank_notes()
    {
        var vm = NewViewModel();
        vm.TemplatePath = "Templates/New note.md";

        vm.TemplatePath = "";

        Assert.Null(_settings.NewNoteTemplatePath);
    }

    [Fact]
    public void A_path_that_would_leave_the_vault_is_not_stored()
    {
        var vm = NewViewModel();

        vm.TemplatePath = "../secret.md";

        Assert.Null(_settings.NewNoteTemplatePath);
    }

    [Theory]
    [InlineData(TemplateState.None, "New notes start blank.")]
    [InlineData(TemplateState.Missing, "No note at that path yet - new notes start blank until there is one.")]
    [InlineData(TemplateState.Unreadable, "That file cannot be read - new notes start blank.")]
    public void The_status_says_what_will_actually_happen(TemplateState state, string expected)
    {
        _templates.State = state;

        Assert.Equal(expected, NewViewModel().TemplateStatus);
    }

    [Fact]
    public void The_status_names_the_template_when_there_is_one()
    {
        _templates.State = TemplateState.Ready;
        var vm = NewViewModel();
        vm.TemplatePath = "Templates/New note.md";

        Assert.Equal("Using Templates/New note.md.", vm.TemplateStatus);
    }

    [Fact]
    public void The_exclusion_list_is_written_through_as_it_is_typed()
    {
        var vm = NewViewModel();
        Assert.Equal(string.Empty, vm.ExcludedPatterns);

        vm.ExcludedPatterns = "node_modules\n*.tmp";

        Assert.Equal("node_modules\n*.tmp", _exclusions.Text);
        Assert.Equal(1, _exclusions.Saves);
    }

    /// <summary>Every save rebuilds the tree in every window, so writing the same list must not.</summary>
    [Fact]
    public void Writing_the_list_that_is_already_there_changes_nothing()
    {
        var vm = NewViewModel();
        vm.ExcludedPatterns = "node_modules";

        vm.ExcludedPatterns = "node_modules";

        Assert.Equal(1, _exclusions.Saves);
    }

    [Fact]
    public void The_gitignore_switch_is_off_until_it_is_turned_on()
    {
        var vm = NewViewModel();
        Assert.False(vm.UseGitIgnore);

        vm.UseGitIgnore = true;

        Assert.True(_exclusions.UseGitIgnore);
    }

    [Theory]
    [InlineData(IgnoreState.None, 0, 0, 12, "Nothing is excluded.")]
    [InlineData(IgnoreState.Unreadable, 0, 0, 12, "That list cannot be read - nothing is excluded.")]
    [InlineData(IgnoreState.Active, 1, 0, 1, "1 pattern; 1 note indexed.")]
    [InlineData(IgnoreState.Active, 3, 0, 12, "3 patterns; 12 notes indexed.")]
    [InlineData(IgnoreState.Active, 3, 2, 12, "3 patterns; 12 notes indexed. 2 '!' lines skipped - exceptions are not supported.")]
    public void The_exclude_status_says_what_is_hidden(
        IgnoreState state, int patterns, int negations, int indexed, string expected)
    {
        _exclusions.Status = new IgnoreStatus(state, patterns, negations, indexed);

        Assert.Equal(expected, NewViewModel().ExcludeStatus);
    }

    [Fact]
    public void Choosing_the_mode_that_is_already_set_changes_nothing()
    {
        var vm = NewViewModel();
        vm.SetSaveModeCommand.Execute(AutoSaveMode.AfterDelay);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void The_delay_round_trips_through_seconds_and_clamps()
    {
        var vm = NewViewModel();
        Assert.Equal(0.75, vm.DelaySeconds);

        vm.DelaySeconds = 5;
        Assert.Equal(5000, _settings.AutoSaveDelayMs);
        Assert.Equal(5, vm.DelaySeconds);

        vm.DelaySeconds = 10_000;
        Assert.Equal(AutoSaveDelay.MaxMs, _settings.AutoSaveDelayMs);
    }

    [Fact]
    public void The_delay_is_editable_only_where_it_means_anything()
    {
        var vm = NewViewModel();
        Assert.True(vm.IsDelayEnabled);

        vm.SetSaveModeCommand.Execute(AutoSaveMode.OnFocusChange);
        Assert.False(vm.IsDelayEnabled);
    }
}
