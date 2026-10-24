# MdNotes features

The details behind the list in the [README](../README.md).

## Search and replace

`Ctrl+Shift+H`, the button beside the sidebar search box, or **Search and replace…** in the tree's
right-click menu opens a panel over the window, seeded with whatever is already in the search box (and,
from a folder's right-click menu, scoped to that folder). It lists every occurrence in the vault grouped
by note, with the replacement previewed on each line:

```
☑ Api            Notes                    3
  12  the widget component holds …
  19  a widget component per row
```

**Aa** matches case (which also stops accents being folded away), **ab** matches whole words only, and
**.\*** reads the find text as a .NET regular expression — `\[\[(\w+)\]\]` → `#$1` turns every wikilink
into a tag, which is what the option is for. A pattern that does not compile says so under the box
instead of searching for nothing. **In** limits the scan to one folder.

Every note and every individual occurrence has a checkbox, all ticked to begin with; unticking survives
edits to the replacement text, so the set can be narrowed and the wording adjusted in either order.
**Replace all** asks for confirmation with the exact counts, and **Undo replace** puts every note it
changed back — one step, held in memory until the next replace or a vault switch, so it is not a
substitute for a backup. A note edited outside the app since the search is left untouched and named.

## Auto-save

By default the file on disk keeps up with the screen: a write lands 0.75 s after you stop typing. That
is the wrong default when something else is watching the file — a vault that is a git repo, a synced
folder, a site generator with a file watcher — so Settings (ribbon, or `Ctrl+,`) offers three modes:

| Mode | Writes |
| --- | --- |
| **After a delay while typing** | after the delay you set (0.1–300 s), as before |
| **When I leave the note** | on another note, another vault, closing the window or leaving the app |
| **Only when I press Ctrl+S** | never on its own |

`Ctrl+S` saves in every mode. In the last mode the status bar reads `Unsaved · Ctrl+S`, and leaving a
note with unsaved text asks first — answer no and the draft is abandoned, which is the point.

## New notes from a template

A new note is blank until you name a template in Settings (`Ctrl+,`), as a path relative to the vault
root — `Templates/New note.md`. From then on **every** new note starts from it: `Ctrl+N`, the quick
switcher's **Create** row, and a click on a `[[wikilink]]` to a note that does not exist yet. That last
one is the point — those notes are the easiest to end up with and the easiest to leave shapeless.

The template is an ordinary note, so you edit it in MdNotes like any other, and it travels with the
vault rather than living in your app settings. Three placeholders are filled in as the note is created:

| Placeholder | Becomes |
| --- | --- |
| `{{title}}` | the name you typed, without `.md` |
| `{{date}}` | `2026-09-18` |
| `{{time}}` | `14:05` |

Both dates take a format: `{{date:dd MMMM yyyy}}` → `18 September 2026`, `{{time:HH-mm}}` → `14-05`
(standard .NET format strings — use `{{date:dd}}` rather than `{{date:d}}`, which means "short date").
So a template like

```
---
created: {{date}}
---

# {{title}}
```

gives you a dated, titled note every time. **Anything MdNotes does not recognise is left exactly as
written** — `{{dataview}}`, `{{#each items}}` and a format it cannot parse all come through untouched,
so a template can document other tools' syntax without being mangled.

The template never renames the note: the name you typed wins.

If the path names a note that is not there, or one that cannot be read, new notes are simply blank
again — nothing interrupts you mid-flow. The line under the setting says which of those is happening.
Clear the box to go back to blank notes.

Frontmatter between `---` fences is metadata, so it is not drawn in the preview.

## Excluding files and folders

A vault that is also a code repo, or that holds an archive or a `node_modules` folder, feeds thousands
of files nobody wants into search and the quick switcher. Settings (`Ctrl+,`) has a box for an ignore
list, kept in the vault as `.mdnotesignore`, one pattern per line:

```
# build output and dependencies
node_modules
/bin
*.tmp
Archive/
```

What it names is **gone**, not merely hidden: out of the sidebar, full-text search, search and replace,
the quick switcher, the folder-scope list and wikilink resolution — and never walked, which is where
the speed comes from. Folders whose name starts with a dot (`.git`, `.obsidian`) are already left out,
so there is no need to list them.

| Pattern | Matches |
| --- | --- |
| `node_modules` | a file or folder of that name, at any depth |
| `build/` | folders only |
| `/docs` | only directly under the vault root |
| `src/docs` | an inner `/` anchors to the root too |
| `*.tmp`, `a?c` | `*` and `?` within one name |
| `**/foo`, `a/**/b` | `**` spans any depth |
| `logs/**` | everything inside `logs`, leaving the folder itself |
| `# …` | a comment |

The file travels with the vault, so a repo can commit it and everyone who opens that vault gets the
same list. It is plain text: edit it in any editor and the sidebar follows.

**`!` exceptions are not supported.** They are skipped, and the line under the box says how many were
skipped, because a rule that can un-hide things makes "is this note indexed" depend on the order the
rules were written in. That is also the catch in **Also use this vault's `.gitignore`**, the checkbox
under the box: it reads the root `.gitignore` only, and a file that hides everything and lets one
folder back in will hide everything here. It is off until you turn it on.

Two things exclusion deliberately does not do. A note you already have open **stays** open when its
folder becomes excluded — it just loses its row in the sidebar — because there may be unsaved text in
it. And creating a note or folder whose name the list would hide is refused rather than allowed: a
note nothing can show is worse than a refusal. Renaming something already excluded stays legal, since
that is how you bring it back into view.

## Note names

Off by default, **Show the title a note declares** in Settings (`Ctrl+,`) names a note by what it says
about itself rather than by its file name. The title is a YAML `title:` property if the note opens with
frontmatter, otherwise its first heading:

```markdown
---
title: Q3 Planning Notes
---
```

A note that declares neither keeps its file name, and so does every folder. The name appears in the
sidebar, the quick switcher, search results, the window title and the pane header — one rule, so those
five cannot disagree.

**The file name stays the note's identity.** `[[Wikilinks]]` resolve by file name, renaming renames the
file, and the breadcrumb is still the path on disk. A title is not unique and can change the moment
someone edits a heading; a path cannot. The quick switcher searches both, so turning this on never puts
a note further away than it was — `draft` and `Q3 Planning` both find it.

The cost is that building the sidebar reads every note rather than just listing folders. Notes are
cached after the first read, and only a note whose title actually changed rebuilds the tree when you
save — but on a very large vault the first tree after opening is slower. That is why it is off by
default.

**File names work on every platform.** A vault is meant to move between machines, so creating, renaming
and moving notes and folders follows Windows' rules everywhere, including on Linux and macOS. They are
the strictest of the three. A name cannot contain `< > : " / \ | ? *` or control characters, cannot
end with `.`, and cannot be a Windows device name such as `CON`, `NUL` or `COM1`. Two names that differ
only in case, such as `Ideas.md` and `ideas.md`, count as the same name, because they are one file on
Windows and macOS. Changing only the case of a note's own name is still allowed. Files that already have
such names are left alone; the rules only apply to names MdNotes creates.

## Loose files, outside the vault

`MdNotes.exe C:\work\repo\README.md` opens that file straight away, without asking for a vault, so
MdNotes can be the program Explorer uses for every `.md` on the machine rather than only the ones in
your notes folder. The sidebar keeps showing your vault, and the window is titled after the file.

A loose file is **never autosaved**, whatever the auto-save setting says — it is usually not one of
your notes, so writes stay deliberate. The status bar shows `Unsaved · Ctrl+S` until you press it, and
closing the window or opening another note asks first. The pane header grows an **Import into vault** button that copies the file, current
edits included, into the selected folder; the original is left where it is.

If MdNotes is already running, opening another file adds a window to it instead of starting a second
copy of the app. All the windows share one vault and one theme: switching either in one window moves
the rest, and a note left open from the previous vault stays open as a loose file, saved manually.

### Making MdNotes the default

The ribbon button below the theme toggle registers MdNotes as a Markdown handler for your user
account — no installer and no admin rights — and then opens **Settings › Default apps** so you can
confirm it. That last click is unavoidable: Windows protects the current default and will not let an
application take it silently. Once MdNotes is the default the button disappears. It is shown only on
Windows; registering a handler is not implemented for Linux or macOS yet.

## Ordering the sidebar

Folders are alphabetical until you move something inside one. The first move writes a plain-text `.order`
file next to the notes, listing that folder's children one per line — folders with a trailing `/`:

```
Chapters/
Intro.md
Methods.md
```

From then on the folder follows that order, and a note keeps its place when you rename it, which is the
point: the order lives beside the files instead of inside their names. Notes and folders can be
interleaved freely, so an intro note can sit above the chapter folders. Anything the file does not list
(a note you just created, or one added outside the app) comes last, alphabetically. The file is yours to
edit by hand, travels with the folder when you move it, and reads cleanly in a diff. **Reset order** in
the right-click menu clears the order of the list the selected item sits in — the same list `Alt+↑` /
`Alt+↓` move it through — and that folder is alphabetical again.

## Settings

Settings (ribbon, or `Ctrl+,`) are remembered — with the theme, view mode, auto-save settings, new-note
template, the `.gitignore` switch, the note-name setting and where you were in the last 200 notes — in
the per-user application data folder (`%APPDATA%\MdNotes\settings.json` on Windows). Use the ribbon
**Open vault** button or `Ctrl+Shift+O` to switch vaults — every open window follows.
