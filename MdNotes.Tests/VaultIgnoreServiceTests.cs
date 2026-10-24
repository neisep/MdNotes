using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// The exclusion list as the settings panel reaches it, over a real vault. Every failure - no vault,
/// no file, a file that will not read - has to be a status the panel can say, never a throw.
/// </summary>
public sealed class VaultIgnoreServiceTests : IDisposable
{
    private readonly TempVault _vault = new();

    private VaultIgnoreService Service => new(_vault.Service, _vault.Settings);

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void The_list_round_trips_through_the_file()
    {
        Service.Save("node_modules\n*.tmp\n");

        Assert.Equal("node_modules\n*.tmp\n".ReplaceLineEndings(), Service.Text);
        Assert.True(File.Exists(Path.Combine(_vault.Root, ".mdnotesignore")));
    }

    [Fact]
    public void Saving_it_is_what_the_vault_then_hides()
    {
        _vault.Note("Keep.md");
        _vault.Note("node_modules/pad.md");

        Service.Save("node_modules");

        Assert.Equal(["Keep"], _vault.Service.GetTree().Select(e => e.Name));
    }

    /// <summary>An empty list and no list are the same thing, and an empty file is litter.</summary>
    [Fact]
    public void Clearing_the_list_deletes_the_file()
    {
        Service.Save("node_modules");
        Service.Save("   ");

        Assert.False(File.Exists(Path.Combine(_vault.Root, ".mdnotesignore")));
        Assert.Equal(string.Empty, Service.Text);
    }

    [Fact]
    public void With_no_list_nothing_is_excluded()
    {
        _vault.Note("Keep.md");

        var status = Service.Probe();

        Assert.Equal(IgnoreState.None, status.State);
        Assert.Equal(0, status.PatternCount);
        Assert.Equal(1, status.IndexedNotes);
    }

    [Fact]
    public void An_active_list_reports_its_patterns_and_what_is_left_indexed()
    {
        _vault.Note("Keep.md");
        _vault.Note("node_modules/pad.md");
        Service.Save("node_modules\n!keep-me");

        var status = Service.Probe();

        Assert.Equal(IgnoreState.Active, status.State);
        Assert.Equal(1, status.PatternCount);
        Assert.Equal(1, status.SkippedNegations);
        Assert.Equal(1, status.IndexedNotes);
    }

    /// <summary>A directory where the list is expected is the portable way to make a read fail.</summary>
    [Fact]
    public void A_list_that_cannot_be_read_excludes_nothing_and_says_so()
    {
        Directory.CreateDirectory(Path.Combine(_vault.Root, ".mdnotesignore"));
        _vault.Service.RefreshExclusions();

        Assert.Equal(IgnoreState.Unreadable, Service.Probe().State);
        Assert.Equal(string.Empty, Service.Text);
    }

    [Fact]
    public void The_gitignore_switch_is_off_until_it_is_turned_on()
    {
        _vault.Note("build/out.md");
        File.WriteAllText(Path.Combine(_vault.Root, ".gitignore"), "build/");

        Assert.Equal(0, Service.Probe().PatternCount);

        var service = Service;
        service.UseGitIgnore = true;

        Assert.True(_vault.Settings.UseGitIgnore);
        Assert.Equal(1, service.Probe().PatternCount);
        Assert.Empty(_vault.Service.GetTree());
    }

    [Fact]
    public void Without_a_vault_there_is_nothing_to_read_or_write()
    {
        using var vault = new VaultService(_vault.Settings);
        var service = new VaultIgnoreService(vault, _vault.Settings);

        service.Save("node_modules");

        Assert.Equal(string.Empty, service.Text);
        Assert.Equal(IgnoreState.None, service.Probe().State);
    }
}
