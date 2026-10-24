using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// Wikilinks that name a folder: a trailing '/' targets the folder, a bare link still prefers the note
/// and only falls back to a folder where it would otherwise have created one.
/// </summary>
public class WikiLinkFolderTests
{
    [AvaloniaFact]
    public async Task Folder_link_selects_and_expands_the_folder_and_leaves_the_editor_alone()
    {
        using var vault = new TempVault();
        vault.Note("Hub.md", "See [[Work/Projects/]].");
        vault.Note("Work/Projects/Alpha.md");
        var (vm, _) = await OpenAsync(vault);

        vm.Workspace.SelectedNode = vm.Workspace.FindNode(Path.Combine(vault.Root, "Hub.md"));
        Dispatcher.UIThread.RunJobs();
        var open = vm.Editor.CurrentPath;

        vm.Workspace.OpenWikiLinkCommand.Execute("Work/Projects/");
        Dispatcher.UIThread.RunJobs();

        var folder = Path.Combine(vault.Root, "Work", "Projects");
        Assert.Equal(folder, vm.Workspace.SelectedNode!.FullPath);
        Assert.True(vm.Workspace.SelectedNode.IsFolder);
        Assert.True(vm.Workspace.SelectedNode.IsExpanded);
        Assert.True(vm.Workspace.FindNode(Path.Combine(vault.Root, "Work"))!.IsExpanded);
        Assert.Equal(open, vm.Editor.CurrentPath);
    }

    [AvaloniaFact]
    public async Task Folder_link_to_a_missing_folder_creates_it_at_the_root()
    {
        using var vault = new TempVault();
        var (vm, dialogs) = await OpenAsync(vault);

        vm.Workspace.OpenWikiLinkCommand.Execute("Projects/");

        var folder = Path.Combine(vault.Root, "Projects");
        Assert.True(Directory.Exists(folder));
        Assert.Equal(folder, vm.Workspace.SelectedNode!.FullPath);
        Assert.Empty(dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Folder_link_naming_a_path_that_is_not_there_reports_it_and_creates_nothing()
    {
        using var vault = new TempVault();
        var (vm, dialogs) = await OpenAsync(vault);

        vm.Workspace.OpenWikiLinkCommand.Execute("Work/Projects/");

        Assert.False(Directory.Exists(Path.Combine(vault.Root, "Work")));
        Assert.Contains(dialogs.Errors, e => e.Contains("Work/Projects"));
    }

    [AvaloniaFact]
    public async Task Bare_link_prefers_a_note_over_a_folder_of_the_same_name()
    {
        using var vault = new TempVault();
        var note = vault.Note("Projects.md");
        vault.Folder("Projects");
        var (vm, _) = await OpenAsync(vault);

        vm.Workspace.OpenWikiLinkCommand.Execute("Projects");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(note, vm.Workspace.SelectedNode!.FullPath);
        Assert.False(vm.Workspace.SelectedNode.IsFolder);
    }

    [AvaloniaFact]
    public async Task Bare_link_falls_back_to_a_folder_instead_of_creating_a_note()
    {
        using var vault = new TempVault();
        var folder = vault.Folder("Projects");
        var (vm, dialogs) = await OpenAsync(vault);

        vm.Workspace.OpenWikiLinkCommand.Execute("Projects");

        Assert.Equal(folder, vm.Workspace.SelectedNode!.FullPath);
        Assert.False(File.Exists(Path.Combine(vault.Root, "Projects.md")));
        Assert.Empty(dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Bare_link_to_nothing_still_creates_the_note()
    {
        using var vault = new TempVault();
        var (vm, dialogs) = await OpenAsync(vault);

        vm.Workspace.OpenWikiLinkCommand.Execute("Brand New");
        Dispatcher.UIThread.RunJobs();

        Assert.True(File.Exists(Path.Combine(vault.Root, "Brand New.md")));
        Assert.Empty(dialogs.Errors);
    }

    private static async Task<(MainViewModel Vm, FakeDialogs Dialogs)> OpenAsync(TempVault vault)
    {
        var dialogs = new FakeDialogs();
        vault.Settings.LastVaultPath = vault.Root;
        var vm = new MainViewModel(TestServices.For(vault, dialogs));

        Assert.True(await vm.TryOpenInitialVaultAsync());
        Assert.Empty(dialogs.Errors);
        return (vm, dialogs);
    }
}
