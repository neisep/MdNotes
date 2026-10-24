using Avalonia.Headless.XUnit;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// Files opened from outside the vault - the OS file handler. AvaloniaFact throughout: the editor
/// owns DispatcherTimers, so it needs a UI thread.
/// </summary>
public sealed class LooseFileTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly string _looseDir = Directory.CreateTempSubdirectory("mdnotes-loose-").FullName;
    // TempVault opens its service in the constructor, so "no vault" needs a service of its own.
    private readonly VaultService _noVault = new(new FakeSettings());

    public void Dispose()
    {
        _vault.Dispose();
        _noVault.Dispose();
        Directory.Delete(_looseDir, recursive: true);
    }

    /// <summary>A markdown file well outside the vault root.</summary>
    private string Loose(string name, string content)
    {
        var path = Path.Combine(_looseDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private EditorViewModel NewEditor() => new(_vault.Service, new MarkdownRenderer(), new FakeSettings(), new FakeDialogs());

    private MainViewModel NewViewModel(bool withVault)
    {
        // Without a vault the window gets a service of its own and a settings file naming no vault -
        // TempVault opens its service in the constructor, so its own can never answer "no vault".
        VaultService vault = withVault ? _vault.Service : _noVault;
        var settings = withVault ? _vault.Settings : new FakeSettings();
        if (withVault) settings.LastVaultPath = _vault.Root;
        return new MainViewModel(TestServices.For(vault, settings, _dialogs));
    }

    [AvaloniaFact]
    public void A_file_outside_the_vault_is_loose_one_inside_is_not()
    {
        var editor = NewEditor();

        editor.Load(_vault.Note("Inside.md", "x"));
        Assert.False(editor.IsLooseFile);

        editor.Load(Loose("README.md", "# hi"));
        Assert.True(editor.IsLooseFile);
    }

    [AvaloniaFact]
    public void A_loose_breadcrumb_names_the_file_rather_than_climbing_out_of_the_vault()
    {
        var editor = NewEditor();
        editor.Load(Loose("README.md", "# hi"));

        Assert.DoesNotContain("..", editor.Breadcrumb);
        Assert.Contains("README", editor.Breadcrumb);
    }

    [AvaloniaFact]
    public async Task Vault_notes_autosave_and_loose_files_wait_for_an_explicit_save()
    {
        var note = _vault.Note("Inside.md", "original");
        var loose = Loose("README.md", "original");
        var editor = NewEditor();

        editor.Load(note);
        editor.Text = "edited in the vault";
        await Task.Delay(1200);
        Assert.Equal("edited in the vault", File.ReadAllText(note));

        editor.Load(loose);
        editor.Text = "edited loose";
        await Task.Delay(1200);
        Assert.Equal("original", File.ReadAllText(loose));

        editor.Save();
        Assert.Equal("edited loose", File.ReadAllText(loose));
    }

    [AvaloniaFact]
    public async Task Opening_a_loose_file_keeps_the_vault_and_selects_nothing_in_the_tree()
    {
        _vault.Note("Inside.md", "x");
        var vm = NewViewModel(withVault: true);
        Assert.True(await vm.TryOpenInitialVaultAsync());

        vm.OpenLooseFile(Loose("README.md", "# readme"));

        Assert.True(vm.Editor.HasNote);
        Assert.True(vm.Editor.IsLooseFile);
        Assert.Null(vm.Workspace.SelectedNode);
        Assert.True(vm.Workspace.HasVault);
        Assert.NotEmpty(vm.Workspace.Tree);
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public void A_loose_file_opens_without_any_vault_at_all()
    {
        var vm = NewViewModel(withVault: false);

        vm.OpenLooseFile(Loose("README.md", "# readme"));

        Assert.False(vm.Workspace.HasVault);
        Assert.True(vm.Editor.HasNote);
        Assert.Equal("# readme", vm.Editor.Text);
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public void A_path_that_is_not_there_is_reported_not_swallowed()
    {
        var vm = NewViewModel(withVault: false);

        vm.OpenLooseFile(Path.Combine(_looseDir, "missing.md"));

        Assert.False(vm.Editor.HasNote);
        Assert.Single(_dialogs.Errors);
        Assert.Contains("missing.md", _dialogs.Errors[0]);
    }

    [AvaloniaFact]
    public void Vault_commands_are_off_until_a_vault_is_open()
    {
        var vm = NewViewModel(withVault: false);

        Assert.False(vm.Workspace.HasVault);
        Assert.False(vm.Workspace.NewNoteCommand.CanExecute(null));
        Assert.False(vm.Workspace.NewFolderCommand.CanExecute(null));
        Assert.False(vm.Workspace.OpenQuickSwitcherCommand.CanExecute(null));
        Assert.False(vm.Workspace.OpenSearchReplaceCommand.CanExecute(null));
        Assert.False(vm.Workspace.ImportToVaultCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Importing_copies_the_edited_text_into_the_vault_and_leaves_the_original_alone()
    {
        var vm = NewViewModel(withVault: true);
        Assert.True(await vm.TryOpenInitialVaultAsync());

        var loose = Loose("README.md", "# readme");
        vm.OpenLooseFile(loose);
        vm.Editor.Text = "# readme, edited";

        Assert.True(vm.Workspace.ImportToVaultCommand.CanExecute(null));
        // Decline the "save the original too?" prompt that leaving the loose file raises.
        _dialogs.ConfirmResult = false;
        await vm.Workspace.ImportToVaultCommand.ExecuteAsync(null);

        var imported = Path.Combine(_vault.Root, "README.md");
        Assert.Equal("# readme, edited", File.ReadAllText(imported));
        Assert.True(File.Exists(loose));
        Assert.Equal("# readme", File.ReadAllText(loose));
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Closing_asks_about_an_unsaved_loose_file_and_honours_the_answer()
    {
        var loose = Loose("README.md", "original");
        var vm = NewViewModel(withVault: false);
        vm.OpenLooseFile(loose);
        vm.Editor.Text = "edited";

        _dialogs.ConfirmResult = false;
        Assert.True(await vm.TryCloseAsync());
        Assert.Single(_dialogs.Confirmations);
        Assert.Equal("original", File.ReadAllText(loose));
    }

    [AvaloniaFact]
    public async Task Closing_saves_the_loose_file_when_the_answer_is_yes()
    {
        var loose = Loose("README.md", "original");
        var vm = NewViewModel(withVault: false);
        vm.OpenLooseFile(loose);
        vm.Editor.Text = "edited";

        _dialogs.ConfirmResult = true;
        Assert.True(await vm.TryCloseAsync());
        Assert.Equal("edited", File.ReadAllText(loose));
    }
}

public sealed class StartupArgsTests
{
    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "/notes/README.md" }, "/notes/README.md")]
    [InlineData(new[] { "--debug", "/notes/README.md" }, "/notes/README.md")]
    [InlineData(new[] { "  spaced.md  " }, "spaced.md")]
    [InlineData(new[] { "--debug" }, null)]
    [InlineData(new[] { "" }, null)]
    public void Reads_the_file_the_app_was_asked_to_open(string[] args, string? expected) =>
        Assert.Equal(expected, StartupArgs.FilePath(args));
}
