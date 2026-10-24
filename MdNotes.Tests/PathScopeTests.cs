using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// Containment by path segment, not by string prefix. Pinned when the test stopped building
/// <c>ancestor + separator</c> per call (finding 14), so the in-place comparison means the same thing.
/// </summary>
public class PathScopeTests
{
    private static readonly char S = Path.DirectorySeparatorChar;

    private static string P(params string[] parts) => S + string.Join(S, parts);

    [Fact]
    public void A_path_is_under_itself() =>
        Assert.True(PathScope.IsSameOrUnder(P("vault", "notes"), P("vault", "notes")));

    [Fact]
    public void A_path_inside_a_folder_is_under_it() =>
        Assert.True(PathScope.IsSameOrUnder(P("vault", "notes", "a.md"), P("vault", "notes")));

    [Fact]
    public void A_sibling_sharing_the_prefix_is_not_under_it() =>
        Assert.False(PathScope.IsSameOrUnder(P("vault", "notes-old", "a.md"), P("vault", "notes")));

    [Fact]
    public void The_parent_is_not_under_its_child() =>
        Assert.False(PathScope.IsSameOrUnder(P("vault"), P("vault", "notes")));

    [Fact]
    public void Case_is_ignored_as_on_the_file_systems_the_app_targets() =>
        Assert.True(PathScope.IsSameOrUnder(P("Vault", "Notes", "a.md"), P("vault", "notes")));
}
