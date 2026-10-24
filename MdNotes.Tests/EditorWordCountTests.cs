using Avalonia.Headless.XUnit;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>The status bar's word count, pinned when it moved from Matches(..).Count to Count (finding 9).</summary>
public sealed class EditorWordCountTests : IDisposable
{
    private readonly TempVault _vault = new();

    public void Dispose() => _vault.Dispose();

    [AvaloniaFact]
    public void Words_are_runs_of_non_whitespace_however_they_are_separated()
    {
        var editor = new EditorViewModel(_vault.Service, new MarkdownRenderer(), _vault.Settings, new FakeDialogs());

        editor.Load(_vault.Note("a.md", "  # One\ttwo\r\n\r\nthree-four  five. "));

        Assert.Equal(5, editor.WordCount); // "#", "One", "two", "three-four", "five."
        editor.Close();
    }
}
