using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// The exclusion list from the settings panel through to the sidebar, over a real vault. The tree is
/// rebuilt off a 300 ms debounce, so each test waits for it rather than asserting straight away.
/// AvaloniaFact throughout: MainViewModel owns DispatcherTimers.
/// </summary>
public sealed class ExcludeTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly FakeDialogs _dialogs = new();

    public void Dispose() => _vault.Dispose();

    private async Task<MainViewModel> OpenAsync()
    {
        _vault.Settings.LastVaultPath = _vault.Root;
        var vm = new MainViewModel(TestServices.For(_vault, _dialogs) with
        {
            // The real list, not the fake: this suite is about what the vault actually hides.
            Exclusions = new VaultIgnoreService(_vault.Service, _vault.Settings),
        });

        Assert.True(await vm.TryOpenInitialVaultAsync());
        Assert.Empty(_dialogs.Errors);
        return vm;
    }

    /// <summary>The refresh the vault asks for is debounced, so it has to be waited out.</summary>
    private static async Task SettleAsync()
    {
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Saving_a_list_takes_the_folder_out_of_the_tree()
    {
        _vault.Note("Keep.md");
        _vault.Note("node_modules/left/pad.md");
        using var vm = await OpenAsync();
        Assert.Equal(2, vm.Workspace.Tree.Count);

        vm.Settings.ExcludedPatterns = "node_modules";
        await SettleAsync();

        Assert.Equal(["Keep"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.Empty(_dialogs.Errors);
    }

    /// <summary>
    /// Excluding the folder a note is open from does not close it: there may be unsaved text in there,
    /// and throwing that away to tidy the sidebar is the wrong trade.
    /// </summary>
    [AvaloniaFact]
    public async Task A_note_in_a_newly_excluded_folder_stays_open_and_loses_its_row()
    {
        var note = _vault.Note("Archive/Old.md", "# Old\n");
        using var vm = await OpenAsync();
        vm.Workspace.SelectedNode = vm.Workspace.Tree.Single(n => n.Name == "Archive").Children.Single();
        Assert.Equal(note, vm.Editor.CurrentPath);

        vm.Settings.ExcludedPatterns = "Archive/";
        await SettleAsync();

        Assert.Empty(vm.Workspace.Tree);
        Assert.Equal(note, vm.Editor.CurrentPath);
        Assert.Null(vm.Workspace.SelectedNode);
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Renaming_the_open_note_while_it_is_excluded_does_not_error()
    {
        _vault.Note("Archive/Old.md", "# Old\n");
        using var vm = await OpenAsync();
        vm.Workspace.SelectedNode = vm.Workspace.Tree.Single(n => n.Name == "Archive").Children.Single();
        vm.Settings.ExcludedPatterns = "Archive/";
        await SettleAsync();

        await vm.Workspace.RenameCurrentNoteCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Errors);
        Assert.Empty(vm.Workspace.Tree);
    }

    /// <summary>An excluded note is not a note any more, so the link makes a new one where links do.</summary>
    [AvaloniaFact]
    public async Task A_wikilink_to_an_excluded_note_creates_a_new_one_at_the_root()
    {
        _vault.Note("node_modules/Readme.md", "# From a dependency\n");
        using var vm = await OpenAsync();
        vm.Settings.ExcludedPatterns = "node_modules";
        await SettleAsync();

        vm.Workspace.OpenWikiLinkCommand.Execute("Readme");

        Assert.Empty(_dialogs.Errors);
        Assert.Equal(Path.Combine(_vault.Root, "Readme.md"), vm.Workspace.SelectedNode?.FullPath);
    }

    [AvaloniaFact]
    public async Task Turning_on_the_gitignore_switch_rebuilds_the_tree()
    {
        _vault.Note("Keep.md");
        _vault.Note("build/out.md");
        File.WriteAllText(Path.Combine(_vault.Root, ".gitignore"), "build/\n");
        using var vm = await OpenAsync();
        Assert.Equal(2, vm.Workspace.Tree.Count);

        vm.Settings.UseGitIgnore = true;
        await SettleAsync();

        Assert.Equal(["Keep"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.Empty(_dialogs.Errors);
    }
}
