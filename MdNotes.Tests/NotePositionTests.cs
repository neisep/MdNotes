using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;
using MdNotes.Views;

namespace MdNotes.Tests;

/// <summary>The bound and the clamp on their own; the editor's use of them is in NoteReturnTests.</summary>
public class NotePositionTests
{
    [Fact]
    public void A_remembered_position_comes_back()
    {
        var positions = new NotePositions();
        positions.Remember(@"C:\v\a.md", 42);

        Assert.Equal(42, positions.Restore(@"C:\v\a.md", length: 100));
    }

    [Fact]
    public void A_note_never_visited_starts_at_the_top() =>
        Assert.Equal(0, new NotePositions().Restore(@"C:\v\unseen.md", length: 100));

    /// <summary>
    /// The stored offset describes a file that may have been edited elsewhere since, so it is a hint
    /// rather than a promise - a caret past the end would throw when the editor applied it.
    /// </summary>
    [Theory]
    [InlineData(500, 10, 10)]
    [InlineData(5, 10, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(7, 0, 0)]
    public void A_position_past_the_end_of_a_shortened_note_is_clamped(int stored, int length, int expected)
    {
        var positions = new NotePositions();
        positions.Remember(@"C:\v\a.md", stored);

        Assert.Equal(expected, positions.Restore(@"C:\v\a.md", length));
    }

    [Fact]
    public void The_oldest_note_is_forgotten_when_the_bound_is_reached()
    {
        var positions = new NotePositions(capacity: 2);
        positions.Remember("a.md", 1);
        positions.Remember("b.md", 2);
        positions.Remember("c.md", 3);

        Assert.Equal(2, positions.Count);
        Assert.Equal(0, positions.Restore("a.md", 100));
        Assert.Equal(3, positions.Restore("c.md", 100));
    }

    /// <summary>Re-recording is what keeps the notes being worked in from ageing out first.</summary>
    [Fact]
    public void Recording_a_note_again_makes_it_the_newest()
    {
        var positions = new NotePositions(capacity: 2);
        positions.Remember("a.md", 1);
        positions.Remember("b.md", 2);
        positions.Remember("a.md", 9);
        positions.Remember("c.md", 3);

        Assert.Equal(9, positions.Restore("a.md", 100));
        Assert.Equal(0, positions.Restore("b.md", 100));
    }

    [Fact]
    public void A_negative_offset_is_not_recorded()
    {
        var positions = new NotePositions();
        positions.Remember("a.md", -1);

        Assert.Equal(0, positions.Count);
    }
}

/// <summary>
/// Coming back to a note lands where it was left. Driven through the real editor view model, because
/// the ordering is the part that breaks: the caret has to be applied after the new text, or it is
/// clamped against the note being replaced.
/// </summary>
public sealed class NoteReturnTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly EditorViewModel _editor;

    public NoteReturnTests() =>
        _editor = new EditorViewModel(_vault.Service, new MarkdownRenderer(), _vault.Settings, new FakeDialogs());

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void Returning_to_a_note_puts_the_caret_back()
    {
        var first = _vault.Note("first.md", new string('a', 200));
        var second = _vault.Note("second.md", "short");

        _editor.Load(first);
        _editor.CaretIndex = 150;
        _editor.Load(second);

        Assert.Equal(0, _editor.CaretIndex);

        _editor.Load(first);

        Assert.Equal(150, _editor.CaretIndex);
    }

    /// <summary>
    /// The ordering bug this is here to catch: a caret of 150 applied before the 5-character note is
    /// loaded would be clamped to 5 and the position lost.
    /// </summary>
    [Fact]
    public void A_position_survives_a_visit_to_a_much_shorter_note()
    {
        var big = _vault.Note("big.md", new string('a', 500));
        var small = _vault.Note("small.md", "tiny");

        _editor.Load(big);
        _editor.CaretIndex = 400;
        _editor.Load(small);
        _editor.Load(big);

        Assert.Equal(400, _editor.CaretIndex);
    }

    [Fact]
    public void A_note_shortened_on_disk_lands_inside_it_rather_than_past_the_end()
    {
        var path = _vault.Note("n.md", new string('a', 200));
        _editor.Load(path);
        _editor.CaretIndex = 180;
        _editor.Load(_vault.Note("other.md", "x"));

        _vault.Note("n.md", "much shorter now");
        _editor.Load(path);

        Assert.Equal("much shorter now".Length, _editor.CaretIndex);
    }

    /// <summary>Renaming does not move the note, so it must not move where you were in it.</summary>
    [Fact]
    public void Renaming_the_open_note_keeps_its_position()
    {
        var path = _vault.Note("before.md", new string('a', 200));
        _editor.Load(path);
        _editor.CaretIndex = 120;

        var renamed = _vault.Service.Rename(path, "after");
        _editor.Retarget(renamed);
        _editor.Load(_vault.Note("other.md", "x"));
        _editor.Load(renamed);

        Assert.Equal(120, _editor.CaretIndex);
    }

    /// <summary>
    /// Positions are written when the editor leaves a note, not when the caret moves - otherwise every
    /// keystroke would rewrite the settings file.
    /// </summary>
    [Fact]
    public void Moving_the_caret_does_not_write_the_settings_file()
    {
        var fake = (FakeSettings)_vault.Settings;
        _editor.Load(_vault.Note("n.md", new string('a', 50)));
        var before = fake.PositionSaves;

        _editor.CaretIndex = 10;
        _editor.CaretIndex = 20;

        Assert.Equal(before, fake.PositionSaves);

        _editor.Load(_vault.Note("other.md", "x"));

        Assert.Equal(before + 1, fake.PositionSaves);
    }
}

/// <summary>
/// The other half of the request, and the half that is not this code: the editor scrolls to the caret
/// on its own, so putting the caret back is what puts the view back. Asserted through a real window
/// because it is Avalonia's behaviour being relied on - if a version ever stopped doing it, the feature
/// would half-break silently and every view-model test would still pass.
/// </summary>
public class NoteReturnScrollTests
{
    [AvaloniaFact]
    public async Task Restoring_the_caret_scrolls_the_editor_to_it()
    {
        using var vault = new TempVault();
        var body = string.Join(Environment.NewLine, Enumerable.Range(0, 400).Select(i => $"line {i}"));
        vault.Note("Long.md", body);

        using var vm = new MainViewModel(TestServices.For(vault, new FakeDialogs()));
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        Assert.True(await vm.TryOpenInitialVaultAsync());

        vm.Workspace.SelectedNode = vm.Workspace.Tree.Single(n => n.Name == "Long");
        window.CaptureRenderedFrame();

        var editor = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Classes.Contains("editor"));
        var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(0, scroller.Offset.Y);

        vm.Editor.CaretIndex = body.Length - 20;
        window.CaptureRenderedFrame();

        // Near the bottom of the note rather than merely non-zero: "it moved" would pass on a jitter.
        Assert.True(scroller.Offset.Y > scroller.Extent.Height * 0.8,
            $"expected the editor to scroll to the caret; offset {scroller.Offset.Y} of {scroller.Extent.Height}");

        window.Close();
    }
}
