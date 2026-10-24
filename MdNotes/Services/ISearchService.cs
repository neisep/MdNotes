using MdNotes.Models;

namespace MdNotes.Services;

public interface ISearchService
{
    /// <summary>
    /// Every note matching <paramref name="query"/>. Reads the whole vault, so it is run on the thread
    /// pool and <paramref name="token"/> is what the next keystroke cancels it with.
    /// </summary>
    IReadOnlyList<SearchHit> Search(string query, CancellationToken token = default);
}
