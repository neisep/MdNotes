using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// The file-system boundary for the notes vault: root path, the folder/note tree, Read/Write,
/// Create/Rename/Delete/MoveTo/ResetOrder, the two wikilink lookups, and a FileSystemWatcher pair
/// behind <see cref="VaultChanged"/>. Validates names at the boundary and owns the cached snapshot,
/// so every caller sees the same vault, filtered the same way.
/// </summary>
public sealed class VaultService(ISettingsService settings) : IDisposable
{
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _ignoreWatcher;
    private VaultIgnore _ignore = VaultIgnore.Empty;
    private IgnoreStamp? _stamp;
    private VaultSnapshot? _snapshot;
    private long _revision;

    /// <summary>What has already been read, so searching the vault does not re-read it every keystroke.</summary>
    private readonly NoteContentCache _contents = new();

    /// <summary>
    /// A vault whose content cache holds <paramref name="contentBudgetChars"/> rather than the default -
    /// small enough, in a test, to overrun with a handful of notes.
    /// </summary>
    internal VaultService(ISettingsService settings, long contentBudgetChars) : this(settings) =>
        _contents = new NoteContentCache(contentBudgetChars);

    public string? RootPath { get; private set; }
    public event EventHandler? VaultChanged;
    /// <summary>
    /// Raised after <see cref="Open"/> retargets the vault, and not when it is asked for the vault it
    /// already has. Windows read <see cref="RootPath"/> rather than caching it; this is how they learn
    /// to re-read. Raised on the calling thread, which is always the UI thread - unlike
    /// <see cref="VaultChanged"/>, which comes off a watcher.
    /// </summary>
    public event EventHandler? VaultOpened;

    /// <summary>
    /// Points the vault at <paramref name="rootPath"/>. Asking for the vault already open does nothing:
    /// every new window restores the last vault, and tearing the watchers down for that would reset the
    /// windows already showing it.
    /// </summary>
    /// <returns>True when the vault changed and <see cref="VaultOpened"/> was raised; false when it
    /// was already open, in which case a joining window must sync itself.</returns>
    public bool Open(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Vault folder does not exist: {rootPath}");

        if (Path.GetFullPath(rootPath) == RootPath) return false;

        _watcher?.Dispose();
        _ignoreWatcher?.Dispose();
        RootPath = Path.GetFullPath(rootPath);

        lock (_gate)
        {
            _ignore = VaultIgnore.Empty;
            _stamp = null;
            _snapshot = null;
            _revision++;
        }

        // Another vault shares no path with this one, so everything held is now the wrong note.
        _contents.Clear();

        _watcher = new FileSystemWatcher(RootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnFsChanged;
        _watcher.Deleted += OnFsChanged;
        _watcher.Renamed += OnFsChanged;

        // A second, narrow watcher because the exclusion list changes by being edited, and LastWrite on
        // the main watcher would fire on every autosave and rebuild the tree for nothing.
        _ignoreWatcher = new FileSystemWatcher(RootPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _ignoreWatcher.Filters.Add(IgnoreFile.FileName);
        _ignoreWatcher.Filters.Add(IgnoreFile.GitFileName);
        _ignoreWatcher.Created += OnIgnoreFileChanged;
        _ignoreWatcher.Changed += OnIgnoreFileChanged;
        _ignoreWatcher.Deleted += OnIgnoreFileChanged;
        _ignoreWatcher.Renamed += OnIgnoreFileChanged;
        _ignoreWatcher.EnableRaisingEvents = true;

        VaultOpened?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// The vault's exclusion list, compiled and cached. Everything the vault enumerates - the tree, the
    /// notes, the folders - is already filtered by it; this is here for what wants to describe the list.
    /// </summary>
    public VaultIgnore Exclusions => CurrentIgnore();

    /// <summary>
    /// Drops the cached view of the vault and tells every window to rebuild. For the settings that
    /// change what the vault shows while no file changes at all - the '.gitignore' switch, and whether
    /// notes are read for the titles they declare.
    /// </summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _stamp = null;
            _snapshot = null;
            _revision++;
        }
        VaultChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The name the exclusion service calls, kept because that is what it means there: the list may
    /// have been edited by hand, which the watcher cannot see as a content change.
    /// </summary>
    public void RefreshExclusions() => Refresh();

    public IReadOnlyList<VaultEntry> GetTree() => CurrentSnapshot().Tree;

    /// <summary>
    /// Every note in the vault, as full paths, in the order the sidebar draws them. The same set the
    /// tree shows and already filtered the same way, because one walk answers both - and that answer
    /// is cached until a mutation or the watcher says the vault changed, so asking twice is free.
    /// </summary>
    public IReadOnlyList<string> GetNotes() => CurrentSnapshot().Notes;

    /// <summary>
    /// What this note is called on screen: the title it declares, or its file name. The one place that
    /// answers it, so the sidebar, the switcher, the search results and the window title cannot disagree
    /// - and a path from outside the vault simply gets its file name.
    /// </summary>
    public string DisplayName(string fullPath) => NameIn(CurrentSnapshot(), fullPath);

    /// <summary>
    /// Every note, as <see cref="GetNotes"/> lists them, with the name <see cref="DisplayName"/> gives it -
    /// for a caller that names every note in one pass. Asking <see cref="DisplayName"/> per note re-checks
    /// the exclusion list on disk once per note; this takes the snapshot once. Safe off the UI thread,
    /// which is where search calls it: a snapshot is never changed after the walk that built it.
    /// </summary>
    public IEnumerable<(string FullPath, string Name)> GetNamedNotes()
    {
        var snapshot = CurrentSnapshot();
        return snapshot.Notes.Select(path => (path, NameIn(snapshot, path)));
    }

    private static string NameIn(VaultSnapshot snapshot, string fullPath) =>
        snapshot.Displays.TryGetValue(fullPath, out var title)
            ? title
            : Path.GetFileNameWithoutExtension(fullPath);

    /// <summary>
    /// The note a wikilink names, matched on its <em>file name</em> by <see cref="NameMatch"/>. Titles
    /// deliberately do not resolve links: a file name is unique in its folder and a declared title is
    /// not, and a link that stopped resolving because someone edited a heading would be worse than one
    /// that never resolved at all.
    /// </summary>
    public string? FindNoteByName(string name) =>
        NameMatch.Best(CurrentSnapshot().Notes, Path.GetFileNameWithoutExtension, name);

    /// <summary>
    /// The folder a wikilink names: a bare name is matched against folder names anywhere in the vault,
    /// a name with separators against the folder's vault-relative path. Which of the two is this
    /// method's own business; how a name matches is <see cref="NameMatch"/>, as it is for notes.
    /// </summary>
    public string? FindFolderByName(string name)
    {
        var isPath = name.IndexOfAny(['/', '\\']) >= 0;
        name = isPath ? name.Replace('\\', '/').Trim('/') : name;
        string Key(string folder) => isPath ? RelativeKey(folder) : Path.GetFileName(folder);

        return NameMatch.Best(CurrentSnapshot().Folders, Key, name);
    }

    /// <summary>
    /// The note's text. Goes through <see cref="NoteContentCache"/>, so a vault-wide search costs one
    /// read of each note rather than one per keystroke; an edit from outside the app changes the file's
    /// write stamp and is picked up on the next read.
    /// </summary>
    public string Read(string fullPath) => _contents.Read(fullPath);

    /// <summary>
    /// The note's text with its <see cref="DiacriticFolder"/> fold, which is what an accent-blind search
    /// runs over. The fold is cached beside the text, so a scan does not copy every note again.
    /// </summary>
    public (string Text, string Folded) ReadFolded(string fullPath) => _contents.ReadFolded(fullPath);

    /// <summary>
    /// Whether walking the tree right now costs directory listings and nothing more. True with note
    /// titles off, because the walk opens no note; true with them on only while the content cache still
    /// holds the whole vault. Past its budget every walk re-reads a large share of the notes, which is
    /// not something to do on the UI thread.
    /// </summary>
    public bool TreeWalkIsCheap => !settings.UseNoteTitles || !_contents.HasOverrun;

    public void Write(string fullPath, string content)
    {
        // Read before the write, not after: this is what the tree is currently showing for the note.
        // Guarded on Exists because Write creates, and asking a file that is not there for its title
        // would turn a working call into a FileNotFoundException.
        var wasTitled = settings.UseNoteTitles && File.Exists(fullPath)
            ? NoteTitle.From(_contents.Read(fullPath))
            : null;

        File.WriteAllText(fullPath, content);
        // The app's own text goes in directly rather than being re-read, and rather than being left for
        // a stamp comparison to notice - a save that lands in the same tick at the same length would not
        // look like a change, and this is the one caller where serving the older text costs an edit.
        _contents.Store(fullPath, content);

        // Only when the sidebar would now be wrong. Saving is on a timer while someone types, and a
        // tree rebuild per keystroke is the cost this is avoiding - the title changes once, if ever.
        if (settings.UseNoteTitles && NoteTitle.From(content) != wasTitled) Invalidate();
    }

    /// <summary>
    /// Creates the note and writes <paramref name="content"/> into it in one go. What that content is -
    /// a template, an imported buffer, nothing - is the caller's decision, not the vault's.
    /// </summary>
    public string CreateNote(string folderPath, string name, string content = "")
    {
        var path = Path.Combine(folderPath, EnsureExtension(ValidateName(name)));
        RejectExcluded(path, isFolder: false);
        if (NameTaken(folderPath, Path.GetFileName(path)))
            throw new IOException($"A note named '{Path.GetFileName(path)}' already exists.");
        File.WriteAllText(path, content);
        _contents.Store(path, content);
        Invalidate();
        return path;
    }

    public string CreateFolder(string parentPath, string name)
    {
        var path = Path.Combine(parentPath, ValidateName(name));
        RejectExcluded(path, isFolder: true);
        if (NameTaken(parentPath, Path.GetFileName(path)))
            throw new IOException($"A folder named '{name}' already exists.");
        Directory.CreateDirectory(path);
        Invalidate();
        return path;
    }

    public string Rename(string fullPath, string newName)
    {
        var parent = Path.GetDirectoryName(fullPath)
                     ?? throw new ArgumentException($"Cannot rename vault root: {fullPath}");
        newName = ValidateName(newName);
        var isFolder = Directory.Exists(fullPath);
        var target = Path.Combine(parent, isFolder ? newName : EnsureExtension(newName));

        // Renaming something already excluded stays legal: it is how a name is brought back into view.
        if (!IsExcluded(fullPath, isFolder)) RejectExcluded(target, isFolder);

        // Two disk steps, so the invalidation is in a finally: a throw between them has already
        // moved the file, and a cache still holding the old name would be worse than the error.
        try
        {
            if (isFolder)
            {
                if (NameTaken(parent, newName, self: fullPath)) throw new IOException($"A folder named '{newName}' already exists.");
                Directory.Move(fullPath, target);
            }
            else
            {
                if (NameTaken(parent, Path.GetFileName(target), self: fullPath)) throw new IOException($"A note named '{Path.GetFileName(target)}' already exists.");
                File.Move(fullPath, target);
            }

            // Keeps the entry's manual position: the whole point of ordering outside the name.
            FolderOrderFile.Rename(parent, FolderOrderFile.Key(fullPath, isFolder), FolderOrderFile.Key(target, isFolder));
        }
        finally
        {
            Invalidate();
        }

        return target;
    }

    /// <summary>
    /// Places the entry at <paramref name="newIndex"/> among the children of <paramref name="targetFolder"/>,
    /// moving it there first when it lives elsewhere, and persists the folder's manual order.
    /// The index counts the destination's children with the entry removed, and is clamped.
    /// </summary>
    /// <returns>The entry's new full path.</returns>
    public string MoveTo(string fullPath, string targetFolder, int newIndex)
    {
        var isFolder = Directory.Exists(fullPath);
        if (!isFolder && !File.Exists(fullPath))
            throw new FileNotFoundException($"Nothing to move at: {fullPath}");
        if (!Directory.Exists(targetFolder))
            throw new DirectoryNotFoundException($"Target folder does not exist: {targetFolder}");

        var source = Path.GetDirectoryName(fullPath)
                     ?? throw new ArgumentException($"Cannot move the vault root: {fullPath}");
        var name = Path.GetFileName(fullPath);
        var target = Path.Combine(targetFolder, name);

        if (isFolder && PathScope.IsSameOrUnder(targetFolder, fullPath))
            throw new IOException($"Cannot move the folder '{name}' into itself.");

        // Several disk steps, so the invalidation is in a finally: a throw after the move has already
        // changed the vault, and every caller rebuilds the sidebar the moment this returns.
        try
        {
            if (!string.Equals(source, targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (isFolder)
                {
                    if (NameTaken(targetFolder, name)) throw new IOException($"A folder named '{name}' already exists there.");
                    Directory.Move(fullPath, target);
                }
                else
                {
                    if (NameTaken(targetFolder, name)) throw new IOException($"A note named '{name}' already exists there.");
                    File.Move(fullPath, target);
                }

                // Only rewrite the source order when it had one; an alphabetical folder stays alphabetical.
                if (FolderOrderFile.Exists(source))
                    FolderOrderFile.Save(source, VaultWalk.Entries(source));
            }

            // Deliberately the unfiltered list read straight off the disk, not the snapshot: '.order'
            // records every sibling, and an excluded one must keep its line and its place, or
            // un-excluding it later would silently move it to the bottom.
            var children = VaultWalk.Entries(targetFolder).ToList();
            var moved = children.First(c => string.Equals(c.FullPath, target, StringComparison.OrdinalIgnoreCase));
            children.Remove(moved);
            children.Insert(VisibleToFull(children, newIndex, CurrentIgnore()), moved);
            FolderOrderFile.Save(targetFolder, children);
        }
        finally
        {
            Invalidate();
        }

        return target;
    }

    /// <summary>Drops the folder's manual order, restoring alphabetical order.</summary>
    public void ResetOrder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder does not exist: {folderPath}");
        FolderOrderFile.Delete(folderPath);
        Invalidate();
    }

    public void Delete(string fullPath)
    {
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        else if (File.Exists(fullPath)) File.Delete(fullPath);
        else throw new FileNotFoundException($"Nothing to delete at: {fullPath}");

        Invalidate();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _ignoreWatcher?.Dispose();
    }

    private string RequireRoot() => RootPath ?? throw new InvalidOperationException("No vault is open.");

    /// <summary>
    /// The exclusion list as it stands, re-read when either file has changed underneath us. One stat
    /// per enumeration, which is nothing beside the walk it is about to save.
    /// </summary>
    private VaultIgnore CurrentIgnore()
    {
        if (RootPath is not { } root) return VaultIgnore.Empty;

        var stamp = IgnoreFile.StampOf(root, settings.UseGitIgnore);
        lock (_gate)
        {
            if (_stamp == stamp) return _ignore;

            _ignore = VaultIgnore.Parse(
                IgnoreFile.Read(root),
                stamp.UseGitIgnore ? IgnoreFile.ReadGit(root) : null);
            _stamp = stamp;
            return _ignore;
        }
    }

    /// <summary>The last compiled list, without touching the disk: the watcher fires far too often to stat per event.</summary>
    private VaultIgnore CachedIgnore()
    {
        lock (_gate) return _ignore;
    }

    /// <summary>
    /// What is in this vault, as of the last change anything told us about. Four callers used to walk
    /// it themselves on demand, one of them per keystroke; now they all ask here.
    ///
    /// The walk runs outside the lock on purpose: holding <see cref="_gate"/> across a recursive
    /// directory walk would stall the watcher thread, which takes the same lock once per event and
    /// sees thousands of them during a build inside the vault.
    ///
    /// Called from the thread pool as well as the UI thread, because the sidebar and the tree walk both
    /// run there now. <see cref="RootPath"/> is the one thing read here without the lock: a vault switch
    /// mid-walk makes this answer about the vault before it, and what discards that answer is the caller
    /// - the workspace drops a walk whose generation has moved on rather than reconciling it.
    /// </summary>
    private VaultSnapshot CurrentSnapshot()
    {
        var root = RequireRoot();

        // Outside the lock, and first: this is the one stat that already happened per enumeration, and
        // it hands back the very same instance until the list changes - which is what makes the
        // reference check below an exact test for "the filter still means the same thing".
        var ignore = CurrentIgnore();

        // Read outside the lock and once, so the walk and the test below agree about it even if the
        // user toggles the setting while the walk is running.
        var titles = settings.UseNoteTitles;

        long revision;
        lock (_gate)
        {
            if (_snapshot is { } cached
                && cached.Revision == _revision
                && cached.Root == root
                && cached.Titles == titles
                && ReferenceEquals(cached.Ignore, ignore)) return cached;

            revision = _revision;
        }

        var fresh = VaultWalk.Of(root, ignore, revision, titles ? _contents.Read : null);

        lock (_gate)
        {
            // Something changed while we were walking, so this answer is already behind. It still goes
            // to the caller that asked for it, but publishing it would pin a tree we know is stale.
            if (_revision == revision) _snapshot = fresh;
        }

        return fresh;
    }

    /// <summary>
    /// Forgets what is in the vault. Every structural change ends here - the six mutations, the
    /// watcher, and the tests, which seed a vault behind the service's back the way an editor does.
    /// It only bumps and clears: it must never walk, because the watcher thread calls it.
    /// </summary>
    internal void Invalidate()
    {
        lock (_gate)
        {
            _revision++;
            _snapshot = null;
        }
    }

    /// <summary>A path relative to the vault root, always with '/' - the key every pattern is matched against.</summary>
    private string RelativeKey(string fullPath) =>
        Path.GetRelativePath(RequireRoot(), fullPath).Replace('\\', '/');

    /// <summary>
    /// Turns an index counted over the rows the sidebar draws into one over every sibling on disk.
    /// A drop past the last visible row lands last, which is where the sidebar showed it.
    /// </summary>
    private int VisibleToFull(IReadOnlyList<VaultEntry> children, int visibleIndex, VaultIgnore ignore)
    {
        if (ignore.IsEmpty) return Math.Clamp(visibleIndex, 0, children.Count);

        var visible = new List<int>();
        for (var i = 0; i < children.Count; i++)
            if (!ignore.Matches(RelativeKey(children[i].FullPath), children[i].IsFolder))
                visible.Add(i);

        visibleIndex = Math.Clamp(visibleIndex, 0, visible.Count);
        return visibleIndex < visible.Count ? visible[visibleIndex] : children.Count;
    }

    private bool IsExcluded(string fullPath, bool isFolder)
    {
        if (RootPath is null) return false;

        var key = RelativeKey(fullPath);
        return !key.StartsWith("..", StringComparison.Ordinal) && CurrentIgnore().IsIgnored(key, isFolder);
    }

    /// <summary>
    /// Refuses to make something nothing could then show: the sidebar, search and the quick switcher
    /// would all behave as if it had never been made, which is worse than saying no up front.
    /// </summary>
    private void RejectExcluded(string fullPath, bool isFolder)
    {
        if (IsExcluded(fullPath, isFolder))
            throw new IOException(
                $"'{Path.GetFileName(fullPath)}' is excluded by {IgnoreFile.FileName} and would not be shown.");
    }

    /// <summary>
    /// Windows' rules on every platform, because they are the strictest of the three and a vault is
    /// meant to be synced: a name the host would accept but Windows would not is a note that cannot
    /// exist on the user's other machine. The host's own invalid set is a subset, so it is covered.
    /// </summary>
    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Name cannot be empty.");
        if (name is "." or "..")
            throw new ArgumentException($"'{name}' is not a usable name.");
        foreach (var c in name)
        {
            if (c < ' ')
                throw new ArgumentException($"Name contains a control character: {name}");
            if (PortableInvalidChars.Contains(c))
                throw new ArgumentException($"Name cannot contain '{c}': {name}");
        }
        if (name.EndsWith('.'))
            throw new ArgumentException($"Name cannot end with '.': {name}");
        var stem = name.Split('.')[0].TrimEnd();
        if (WindowsDeviceNames.Contains(stem))
            throw new ArgumentException($"'{stem}' is reserved by Windows and cannot be used as a name.");
        return name;
    }

    private const string PortableInvalidChars = "<>:\"/\\|?*";

    private static readonly HashSet<string> WindowsDeviceNames = new(
        ["CON", "PRN", "AUX", "NUL",
         "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="folder"/> already holds <paramref name="name"/>, ignoring case on every
    /// platform: 'Ideas.md' and 'ideas.md' can sit side by side on Linux, but are one file on Windows
    /// and macOS, so a synced vault would lose one of them. <paramref name="self"/> is the entry being
    /// renamed, so changing only the case of its own name stays allowed.
    /// </summary>
    private static bool NameTaken(string folder, string name, string? self = null) =>
        Directory.EnumerateFileSystemEntries(folder).Any(entry =>
            string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry, self, StringComparison.Ordinal));

    private static string EnsureExtension(string name) =>
        name.EndsWith(VaultWalk.Extension, StringComparison.OrdinalIgnoreCase)
            ? name
            : name + VaultWalk.Extension;

    private void OnFsChanged(object sender, FileSystemEventArgs e)
    {
        // A quiet event is one inside an excluded folder, which the walk never descends into - so the
        // snapshot cannot be holding it and there is nothing to forget.
        if (IsQuiet(e)) return;

        // Invalidated before the event is raised, never after: a handler reads the vault synchronously.
        Invalidate();
        VaultChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnIgnoreFileChanged(object sender, FileSystemEventArgs e) => RefreshExclusions();

    /// <summary>
    /// Whether a file-system event is one nobody can see. Excluded paths must not churn the tree - a
    /// build inside an excluded folder is thousands of events. Disk-free on purpose: this runs on a
    /// watcher thread. Nothing here knows whether the path was a folder, so it asks as a file and lets
    /// the ancestor walk catch the rest; the cost of guessing wrong is one refresh too many.
    /// </summary>
    private bool IsQuiet(FileSystemEventArgs e)
    {
        var ignore = CachedIgnore();
        if (ignore.IsEmpty || RootPath is null) return false;
        if (!IsHidden(e.FullPath)) return false;

        // A rename is only quiet at both ends, or a note moved out of an excluded folder never appears.
        return e is not RenamedEventArgs renamed || IsHidden(renamed.OldFullPath);

        bool IsHidden(string path)
        {
            var key = Path.GetRelativePath(RootPath, path).Replace('\\', '/');
            return !key.StartsWith("..", StringComparison.Ordinal) && ignore.IsIgnored(key, isFolder: false);
        }
    }
}
