# TODO — MdNotes Obsidian-style UI overhaul
_Controller (Sonnet) · 2026-09-16_

## Context
Spec: `C:\Users\jimmie.jonsson\.claude\plans\your-a-ux-professional-typed-wozniak.md` (design tokens §1,
layout §2, per-file details §3). Read it before starting a task. Repo: Avalonia 12.1.2, .NET 10, MVVM
(CommunityToolkit.Mvvm), compiled bindings on. No new NuGet packages. `dotnet build` must stay green after
every task. Subagents write ONLY to `agent<N>_log.md` plus the source files their task names.

Tasks 52-65 come from `docs/performance-review.html`. That review was re-read against the working
tree on 2026-09-22: findings 1 and 4 had already been implemented in `89d3067` and are marked Done
there, finding 21 was added during that audit, and the other eighteen were confirmed still open.
The HTML is the record of what has actually been done: when a task starts or lands, update **both**
the Status row on its finding and the Implementation status table (`#impl`), and give a finished
finding a closing `status` note saying what changed and what was measured. Several tasks are gated
on task 52's numbers and may legitimately end as Won't-do — that is a result, recorded the same way.
Measurement tooling: `MdNotes.Tests/ScanBenchmark.cs` (set `MDNOTES_BENCH=1`), the
`MdNotes.Benchmarks` BenchmarkDotNet project, and the `dotnet-counters` / `dotnet-trace` global tools.

## Tasks
| # | Model | Status | Task |
|---|-------|--------|------|
| 1 | Haiku | ✅ Done | Add `MdNotes/Models/ThemeMode.cs` (`System, Light, Dark`) and `MdNotes/Models/ViewMode.cs` (`Edit, Split, Reading`). Add `ThemeMode ThemeMode` and `ViewMode ViewMode` (default `System` / `Split`) to `ISettingsService`, persist them in `JsonSettingsService` like `LastVaultPath`, and update `FakeSettings` in `MdNotes.Tests/MainWindowTests.cs`. |
| 2 | Haiku | ✅ Done | Download Inter static TTFs (Regular, Medium, SemiBold, Bold) + `OFL.txt` from the rsms/inter GitHub release into `MdNotes/Assets/Fonts/`. Add `<AvaloniaResource Include="Assets/**" />` to `MdNotes.csproj`. Add `.With(new FontManagerOptions { DefaultFamilyName = "avares://MdNotes/Assets/Fonts#Inter" })` in `Program.cs` and `MdNotes.Tests/TestAppBuilder.cs`. |
| 3 | Haiku | ✅ Done | Create `MdNotes/Assets/Icons.axaml` (a `ResourceDictionary`) with `StreamGeometry` resources keyed `Icon.<name>` for Lucide icons: files, search, plus, folder-plus, chevrons-down-up, command, sun-moon, folder-open, pencil, columns-2, book-open, x, file-text, corner-down-left, square, square-check. Use the official Lucide 24×24 path data verbatim (stroke icons). |
| 4 | Haiku | ✅ Done | Create `MdNotes/Themes/Palette.axaml` (`ResourceDictionary` with `ThemeDictionaries` Light/Dark) containing every `SolidColorBrush` token from spec §1 (`MdSurface`, `MdSurfaceAlt`, `MdSurfaceCode`, `MdBorder`, `MdText`, `MdTextMuted`, `MdTextFaint`, `MdAccent`, `MdAccentSoft`, `MdHover`, `MdError`) plus legacy aliases `MdCodeBackground`=`MdSurfaceCode`, `MdLink`=`MdWikiLink`=`MdAccent`, `MdQuote`=`MdTextMuted`. Remove the colour dictionaries from `App.axaml` and include the new file via `ResourceInclude`. Build must pass. |
| 5 | Haiku | ✅ Done | In `App.axaml`, give `FluentTheme` a `FluentTheme.Palettes` block with `ColorPaletteResources x:Key="Light"` and `"Dark"` (Accent `#7F6DF2`/`#A78BFA`, RegionColor `#FFFFFF`/`#1E1E1E`, and Alt/Base/Chrome/List colours derived from spec §1 so dialogs, menus and scrollbars match). Also include `Assets/Icons.axaml` in `Application.Resources`. |
| 6 | Haiku | ✅ Done | Create `MdNotes/Themes/Controls.axaml` (`Styles`) and include it in `App.axaml` after `FluentTheme`. Styles: `Path.icon` (Width/Height 18, Stretch Uniform, `Stroke={DynamicResource MdTextMuted}`, `Fill=Transparent`, `StrokeThickness=2`, `StrokeLineCap=Round`, `StrokeJoin=Round`); `Button.icon` (36×36, Padding 0, transparent background, no border, CornerRadius 6, `:pointerover` → `MdHover`, `.active` → `MdAccentSoft` background and `MdAccent` icon stroke); `TextBox.editor` (no border in any state incl. focus, `MdSurface` background, `MdAccent` caret, FontSize 16, LineHeight 26, Padding 0); `TextBox.search` (CornerRadius 6, `MdSurface` background, `MdBorder` border, MinHeight 30, FontSize 13); `GridSplitter` background `MdBorder`. |
| 7 | Haiku | ✅ Done | Extend `MdNotes/Themes/Controls.axaml`: `TreeViewItem` and `ListBoxItem` rows — MinHeight 28, CornerRadius 4, FontSize 13, hover `MdHover`, selected `MdAccentSoft` with normal foreground, no accent side bar. Override the Fluent resource keys in `App.axaml` resources where templates cannot be reached by style (`TreeViewItemBackgroundPointerOver`, `TreeViewItemBackgroundSelected`, `TreeViewItemBackgroundSelectedPointerOver`, `TreeViewItemForegroundSelected`, `ListBoxItemBackgroundPointerOver`, `ListBoxItemBackgroundSelected`, `ListBoxItemBackgroundSelectedPointerOver`, `TextControlBorderBrushFocused`, `TextControlBorderThemeThicknessFocused`); verify names against Avalonia.Themes.Fluent 12 (WebFetch the GitHub source). |
| 8 | Haiku | ✅ Done | `EditorViewModel`: add read-only observable `string? NoteName` (file name without extension), `string Breadcrumb` (relative folder path from vault root + name, joined with " / "), `int WordCount` (`Regex.Matches(Text, @"\S+").Count`), `int CharCount`. Update them inside `RenderPreview()` and when `CurrentPath` changes. `IVaultService.RootPath` is available for the relative path. |
| 9 | Haiku | ✅ Done | `MainViewModel`: add `[ObservableProperty] ViewMode ViewMode`, `ThemeMode ThemeMode` (both loaded from `ISettingsService` in ctor and written back in `On…Changed`), `bool IsSidebarVisible = true`, `string? VaultName` (folder name of `VaultPath`), computed `IsEditVisible`/`IsPreviewVisible`/`IsSplit`/`IsEditOnly`/`IsReadingOnly`. Commands: `SetViewMode(ViewMode)`, `ToggleEditReading` (Edit↔Reading; Split→Reading), `CycleTheme` (System→Light→Dark→System), `ToggleSidebar`, `CollapseAll` (sets `IsExpanded=false` on all nodes). |
| 10 | Haiku | ✅ Done | Create `MdNotes/ViewModels/QuickSwitcherViewModel.cs`: `IsOpen`, `Query`, `ObservableCollection<SwitcherItem> Results`, `Selected`, commands `Open()`, `Close()`, `OpenSelected()`, `MoveSelection(int)`. Items come from `IVaultService.GetTree()` flattened to notes (`Name`, `FullPath`, relative folder); ranking: exact > starts-with > contains (case-insensitive), max 50; when query is non-empty and nothing matches, a single `IsCreate=true` item "Create «query»". `OpenSelected` calls an `Action<string> openPath` and `Action<string> createNote` passed in the ctor. Add `QuickSwitcher` property + `OpenQuickSwitcher` command to `MainViewModel` (wire `SelectPath` and `_vault.CreateNote(RootPath, name)` + `RefreshTree`). Add `MdNotes.Tests/QuickSwitcherViewModelTests.cs` covering ranking, empty query lists all, no match → create item. |
| 11 | Haiku | ✅ Done | `App.axaml.cs`: after creating the VM, apply `vm.ThemeMode` to `Application.Current.RequestedThemeVariant` (`System`→`ThemeVariant.Default`) and re-apply whenever `vm.PropertyChanged` fires for `ThemeMode`. Keep it as a small private static method. |
| 12 | Haiku | ✅ Done | Create `MdNotes/Views/Shell/Ribbon.axaml(.cs)` `UserControl` (`x:DataType="vm:MainViewModel"`): 44px wide `MdSurfaceAlt` column, right border `MdBorder`; top `Button.icon`s with `Path.icon` + `ToolTip.Tip`: Files (`ToggleSidebarCommand`, `.active` bound to `IsSidebarVisible`), Search (`Classes="icon"`, `x:Name="SearchButton"`, no command — window code-behind focuses the box), New note (`NewNoteCommand`), Quick switcher (`OpenQuickSwitcherCommand`); bottom: Theme (`CycleThemeCommand`, tooltip shows current `ThemeMode`), Open vault (`OpenVaultCommand`). |
| 13 | Haiku | ✅ Done | Create `MdNotes/Views/Shell/Sidebar.axaml(.cs)` `UserControl`: `MdSurfaceAlt` background. Header row (height 36, padding 12,0): `VaultName` in 12px SemiBold `MdTextMuted` + right-aligned small icon buttons New note, New folder (`NewFolderCommand`), Collapse all (`CollapseAllCommand`). Below: `TextBox.search` `x:Name="SearchBox"` bound to `SearchQuery` with search `Path.icon` in `InnerLeftContent` and an `x` clear button in `InnerRightContent` (visible when `IsSearching`). Then the existing `TreeView` (move from `MainWindow.axaml`, keep `ContextMenu`, `IsExpanded` style, `PointerPressed` handler moved into this control's code-behind; **remove the 📁 emoji**, show `Name` only, folders SemiBold) and the existing search `ListBox` (snippet in `MdTextMuted`, 12px). |
| 14 | Haiku | ✅ Done | Create `MdNotes/Views/Shell/PaneHeader.axaml(.cs)` `UserControl`: height 40, `MdSurfaceAlt`, bottom border `MdBorder`. Left: `Editor.Breadcrumb` in 13px `MdTextMuted`, and the note name part as a `Button` styled as text that runs `RenameCommand` (tooltip "Rename"). Right: three `Button.icon`s (pencil / columns-2 / book-open) with `Command="{Binding SetViewModeCommand}"` and `CommandParameter` of each `ViewMode`, `Classes.active` bound to `IsEditOnly`, `IsSplit`, `IsReadingOnly`. Hidden when `!Editor.HasNote`. |
| 15 | Haiku | ✅ Done | Create `MdNotes/Views/Shell/StatusBar.axaml` (height 22, `MdSurfaceAlt`, top border `MdBorder`, 12px `MdTextMuted`: left `"{WordCount} words · {CharCount} characters"`, right `Saved`/`Editing…` from `Editor.IsDirty`; hidden when no note) and `MdNotes/Views/Shell/EmptyState.axaml` (centred: file-text icon 40px faint, "No note is open" 16px `MdTextMuted`, two hint rows `Create new note  Ctrl+N` / `Quick switcher  Ctrl+O` in 13px `MdTextFaint`). |
| 16 | Haiku | ✅ Done | Create `MdNotes/Views/Shell/QuickSwitcher.axaml(.cs)` `UserControl` bound to `QuickSwitcher`: full-size scrim (`#80000000`, click closes), centred card 560px wide, top margin 80, `MdSurface` background, `MdBorder` border, radius 8, drop shadow; `TextBox.search` `x:Name="Input"` bound to `Query` (placeholder "Find or create a note…"); `ListBox` of `Results` (name 14px + relative folder 12px muted; create item shows a plus icon); key handling in code-behind: Up/Down → `MoveSelection`, Enter → `OpenSelected`, Escape → `Close`; focus `Input` when `IsOpen` becomes true. |
| 17 | Haiku | ✅ Done | Rewire `MdNotes/Views/MainWindow.axaml(.cs)`: root `Panel`; inside a `Grid` with columns `Auto,260,1,*` and rows `Auto,*,Auto`: Ribbon (col 0, all rows), Sidebar (col 1 rows 0-2), `GridSplitter` (col 2), PaneHeader (col 3 row 0), content `Grid` (col 3 row 1) holding the editor `TextBox` (`Classes="editor"`, keep `AcceptsReturn` etc., `MaxWidth=760`, `Margin=32,24`, `IsVisible` = `IsEditVisible`), inner `GridSplitter` (visible only when `IsSplit`), preview `ScrollViewer` (`IsVisible` = `IsPreviewVisible`; `MarkdownView` `MaxWidth=760`, `HorizontalAlignment=Center`, `Margin=32,24`), `EmptyState` (visible when `!Editor.HasNote`); StatusBar (col 3 row 2). `QuickSwitcher` last in the root `Panel` (`IsVisible` = `QuickSwitcher.IsOpen`). Window `Background` `MdSurface`. KeyBindings: `Ctrl+S` Save, `Ctrl+N` NewNote, `Ctrl+O` OpenQuickSwitcher, `Ctrl+Shift+O` OpenVault, `Ctrl+E` ToggleEditReading. Code-behind: react to `IsSidebarVisible` by setting the sidebar `ColumnDefinition.Width` to 0 or the last width (min 180); Ribbon `SearchButton.Click` → focus `Sidebar.SearchBox`. Remove the old tree/list markup (now in Sidebar). |
| 18 | Haiku | ✅ Done | `BlockRenderer`/`InlineRenderer` typography per spec §3: paragraph FontSize 16, LineHeight 26, `MdText` foreground; document spacing 16; headings sizes 29/26/22/20/18/16 with weights 700/600/600/600/600/600, top margin 24 (h1-2) / 16, h1 bottom border removed (Obsidian has none); quote 2px `MdAccent` left border, `MdTextMuted` text, padding 16,4; code block `MdSurfaceCode` radius 6 padding 14 font 14; inline code `MdTextMuted` foreground; table cells padding 8,6, header `MdSurfaceCode`, outer border radius 4; task list boxes as 16px `Path` (`Icon.square` / `Icon.square-check`, stroke `MdTextMuted` / `MdAccent`) in an `InlineUIContainer` (advance `_position` by 1); rule 1px `MdBorder` margin 0,24; link colour `MdAccent`. Keep all existing tests green. |
| 19 | Haiku | ✅ Done | Restyle `InputDialog.axaml(.cs)` and `MessageDialog.axaml(.cs)`: `MdSurface` background, padding 20, spacing 16, title `TextBlock` 15px SemiBold (InputDialog gets the `title` passed to it; check `DialogService.cs` for how it is constructed), buttons MinWidth 88 with CornerRadius 6, default/primary button `MdAccent` background + white foreground, `TextBox` CornerRadius 6. |
| 20 | Haiku | ✅ Done | Add `MainWindowTests.Renders_shell_in_both_themes`: open a `TempVault` with a folder + two notes, select a note, for `ThemeVariant.Light` and `ThemeVariant.Dark` set `Application.Current!.RequestedThemeVariant`, `CaptureRenderedFrame()`, save `main-window-light.png` / `main-window-dark.png` when `MDNOTES_RENDER_OUT` is set; assert the window contains `Ribbon`, `Sidebar`, `PaneHeader`, `StatusBar` controls and `Path` icons. Run `dotnet test` with `MDNOTES_RENDER_OUT` set to the repo's `.render` folder and report the PNG paths in the log. |
| 21 | Haiku | ✅ Done | Update `README.md` (theme switch, view modes + `Ctrl+E`, quick switcher `Ctrl+O`, open vault now `Ctrl+Shift+O`, Inter/Lucide credits) and `PLAN.md` (Layout section: `Themes/`, `Assets/`, `Views/Shell/`, `QuickSwitcherViewModel`; progress entry dated 2026-09-16). |
| 23 | Haiku | ✅ Done | Visual fix-ups from the PNG review: (a) `Assets/Icons.axaml`: replace the broken `Icon.sun-moon` data with the Lucide `moon` path; (b) `BlockRenderer.List`: no bullet marker for task-list items (item whose first inline is a `TaskList`); (c) `BlockRenderer.TableGrid`: outer border `HorizontalAlignment=Left`; (d) `MainWindowTests.Renders_shell_in_both_themes`: expand the `Ideas` folder before selecting so the selected row is visible; re-render PNGs. |
| 22 | Opus  | ✅ Done | Final review |
| 24 | Haiku | ✅ Done | Review fixes, shell views + MainViewModel (reviewer items 1, 2, 7, 13, 17b): `Sidebar.axaml` header `DockPanel` `LastChildFill="False"` + vault name `TextTrimming`, sidebar New-note icon → `Icon.plus`; `MainViewModel`: `OpenQuickSwitcher` Guard-wrapped, ctor loads `ViewMode`/`ThemeMode` without writing settings back (loading guard), new `RenameCurrentNoteCommand` renaming `Editor.CurrentPath` (reuse `Rename`'s save/retarget/refresh); `PaneHeader.axaml`: breadcrumb itself is the button bound to it, separate pencil removed. |
| 25 | Haiku | ✅ Done | Review fixes, theme + resources (items 3, 4, 5, 8, 9, 12, 17a, 17c, 18b): `Button.primary` styles moved from both dialogs into `Controls.axaml`; Fluent key overrides moved into `Palette.axaml`'s Light/Dark dictionaries and `FluentOverrides.axaml` deleted (+ its include in `App.axaml`); `TextControlBorderBrushFocused`/`ThemeThicknessFocused` overrides dropped; `Button.icon:pointerover Path.icon` narrowed to `:not(.active)`; delete `MdLink`/`MdWikiLink`/`MdQuote`/`MdCodeBackground` aliases (InlineRenderer → `MdSurfaceCode`) and `Icon.corner-down-left`; rename `Icon.sun-moon` → `Icon.moon` (Ribbon); remove `SystemDecorations="Full"` from dialogs; `Watermark` → `PlaceholderText` in `QuickSwitcher.axaml`; unused using in `InlineRenderer.cs`. Build with `--no-incremental` must show 0 warnings. |
| 26 | Haiku | ✅ Done | Review fixes, window wiring + switcher + tests (items 6, 10, 11, 14, 15, 16, 18a) — after 24: `MainViewModel` exposes `GridLength EditorColumnWidth`/`PreviewColumnWidth` (1*/0 by ViewMode); `MainWindow.axaml` binds the pane `ColumnDefinition.Width`s to them and drops the editor/preview `IsVisible` bindings; `MainWindow.axaml.cs` deletes `AdjustPanesSplit`, subscribes to the VM once (unsubscribe previous), removes unused usings; `QuickSwitcher.axaml.cs` uses the generated `Input` field + `Vm` property, no null fallback; `QuickSwitcherViewModel.Open()` refreshes once, `StartsWith` ordinal; `MainWindowTests`: theme loop in `try/finally`, assert PNG files exist, new `Quick_switcher_overlay_opens_and_closes`. |
| 27 | Opus  | ✅ Done | Re-review tasks 24–26 against `reviewer_log.md` items 1–18; confirm each is fixed or explicitly deferred; build 0 warnings, all tests green; re-inspect PNGs. |
| 28 | Haiku | ✅ Done | Re-review fixes (reviewer2 C1–C5): `Sidebar.axaml` header → `Grid ColumnDefinitions="*,Auto"` (name trims, buttons never clipped); `Icons.axaml` make second subpaths absolute in `Icon.chevrons-down-up`, `Icon.pencil`, `Icon.x`; `MainWindow.axaml.cs` unsubscribe `Ribbon.SearchButtonClicked` on VM change (or subscribe once in ctor); `MainWindow.axaml` restore `IsVisible` on editor (`IsEditVisible`) and preview (`IsPreviewVisible`) alongside the width bindings so a hidden pane is not focusable; re-render PNGs; STRUCTURE.md re-indexes via hooks. |
| 29 | Sonnet | ✅ Done | Hotfix (user report: memory grows when navigating links fast): `Views/Markdown/DiagramCache.cs` bounded LRU of Svg.Skia pictures keyed by (source, theme); `MarkdownView` coalesces BaseDirectory+Document rebuilds into one per dispatcher pass; `InternalsVisibleTo` for tests; new `MarkdownViewLeakTests`, `NavigationLeakTests`, `DiagramCacheTests`. |
| 30 | Sonnet | ✅ Done | Scroll memory spikes with many diagrams: diagrams are now rasterized to bitmaps at their displayed pixel width (`DiagramView` control, `DiagramCache.GetBitmap`, generic `Lru`), re-rasterized 150 ms after a resize settles; tests `DiagramViewTests`, bitmap cases in `DiagramCacheTests`. |
| 31 | Sonnet | ✅ Done | App icon: `MdNotes.Tests/AppIconGenerator.cs` draws the icon with SkiaSharp (purple gradient rounded square, white M + down arrow) at 16–256 px and packs a PNG-entry `.ico` when `MDNOTES_GENERATE_ICON` is set → `MdNotes/Assets/mdnotes.ico`; `ApplicationIcon` in csproj; `Icon` on MainWindow and both dialogs. |
| 32 | Opus | ✅ Done | Manual sidebar ordering · storage + vault service. New `MdNotes/Services/FolderOrderFile.cs` (`internal static`): per-folder `.order` file, one child key per line, key = `Path.GetFileName(FullPath)` + `/` for folders; `Apply` (stable sort, unlisted entries keep their incoming order last), `Save` (prunes stale names), `Delete`, `Rename` (rewrites one line in place). `IVaultService`/`VaultService`: `string MoveTo(string fullPath, string targetFolder, int newIndex)` (index counts the destination's children *after removal*, clamped; validates entry/target exist, rejects a folder into its own subtree and a name clash) and `void ResetOrder(string folderPath)`; `ReadChildren` returns `FolderOrderFile.Apply(...)`; `Rename` updates the parent's `.order` so order survives renames. |
| 33 | Opus | ✅ Done | Manual sidebar ordering · view models. New `MdNotes/Models/DropPosition.cs` (`Before, After, Into`). `TreeNodeViewModel`: `[ObservableProperty] bool IsDropTarget`. `MainViewModel`: `MoveUp`/`MoveDown` commands (current sibling index ± 1, no-op at the ends), `ResetOrder` command, `public (string Folder, int Index)? ResolveDrop(source, over, where)` (pure index maths incl. the same-parent adjustment; rejects onto-itself, into-own-subtree and no-op drops), `public void MoveNode(...)` (Guard + `Editor.Retarget` like `RenameAsync`), `FindNode` made public, and `RefreshTree` reselects `SelectedNode?.FullPath ?? Editor.CurrentPath`. |
| 34 | Opus | ✅ Done | Manual sidebar ordering · sidebar UI. `Sidebar.axaml`: `DragDrop.AllowDrop` on the `TreeView`, `Alt+Up`/`Alt+Down` KeyBindings, context-menu `Move up`/`Move down`/`Reset order`, item template wrapped in a `Border` with `Classes.drop="{Binding IsDropTarget}"`, and a 2px `MdAccent` drop-line `Border` in the tree `Panel`. `Sidebar.axaml.cs`: press/move threshold → `DragDrop.DoDragDrop` with a private `mdnotes/node` format; `DragOver`/`Drop` hit-test the row via `Border#PART_LayoutRoot` (folder middle band 0.25–0.75 = `Into`, else `Before`/`After`), call `Vm.ResolveDrop` to allow/reject and `Vm.MoveNode` to execute; drop line positioned with `TranslatePoint`. `Themes/Controls.axaml`: `Border.drop` (MdAccentSoft, radius 4). |
| 35 | Opus | ✅ Done | Manual sidebar ordering · tests. `VaultServiceTests`: `.order` applied incl. free interleaving, unlisted entries last, stale names ignored, first `MoveTo` materialises the sibling order, **rename keeps position**, cross-folder `MoveTo` + source `.order` pruned, rejects folder-into-itself and destination name clash, `ResetOrder` restores alphabetical. New `MdNotes.Tests/Fakes.cs` lifting the duplicated `FakeSettings`/`FakeDialogs` out of `MainWindowTests.cs` and `NavigationLeakTests.cs`. New `MdNotes.Tests/TreeOrderTests.cs`: `MoveUpCommand` reorders `vm.Tree` and keeps selection, no-op at the top, `ResolveDrop` Before/After/Into + same-parent adjustment + rejections. |
| 36 | Opus | ✅ Done | Manual sidebar ordering · docs. `README.md`: reordering by drag and by `Alt+↑`/`Alt+↓`, and the hand-editable `.order` file format. `PLAN.md`: `FolderOrderFile` in the layout tree, a behaviour note, and a 2026-09-17 progress entry. `--no-incremental` build 0 warnings, `dotnet test` green. |
| 37 | Opus | ✅ Done | Bugfix (user report: drag & drop does not work at all): `Sidebar.OnTreePointerPressed` was registered via the XAML `PointerPressed` attribute, i.e. the bubble route with `handledEventsToo: false`. `TreeViewItem` marks `PointerPressed` handled while selecting, so a left press never reached the handler, the drag was never armed and `PointerMoved` returned immediately. Re-registered on `RoutingStrategies.Tunnel` in the ctor. New `MdNotes.Tests/SidebarDragTests.cs` (4 headless tests) covering AllowDrop, arming on left press, disarming on release, and right-press selecting without arming. |
| 38 | Opus | ✅ Done | Accent-insensitive matching (user report: `resume` does not find `Résumé`, `jonkoping` does not find `Jönköping`). New `MdNotes/Services/DiacriticFolder.cs`: length-preserving fold (FormD base letter, precomputed table for U+0000–U+02FF, explicit map for stroke/bar letters `øđðłħŧı`, surrogates and `æ`/`ß` passed through, allocation-free ASCII fast path). Wired into `SearchService.Search` (index into folded content still addresses the original, so `Snippet` is unchanged and keeps its accents), `QuickSwitcherViewModel.RefreshResults` (replaces the redundant `ToLowerInvariant` + `OrdinalIgnoreCase`), and `VaultService.FindNoteByName` (exact pass first, folded fallback second — this stops `[[Jonkoping]]` silently creating a duplicate note next to `Jönköping.md`). New `MdNotes.Tests/DiacriticFolderTests.cs`; accent cases added to `SearchServiceTests`, `QuickSwitcherViewModelTests`, `VaultServiceTests`. 108 tests, 0 warnings. |
| 39 | Opus | ✅ Done | Vault-wide search and replace (user request, forum.obsidian.md/t/4395). Plan: `C:\Users\jimmie.jonsson\.claude\plans\global-vault-wide-search-and-tidy-fox.md`. New `Models/SearchOptions.cs`, `Models/NoteMatches.cs` (`NoteMatches` + `TextMatch`), `Models/ReplaceReport.cs`; `Services/MatchFinder.cs` (internal, the single matcher: literal via `DiacriticFolder` or regex with `$1` expansion, whole word, zero-width skipped, `Apply` splices); `Services/IReplaceService.cs` + `ReplaceService.cs` (scoped `Find`, dot-folders skipped; `Replace` re-verifies every span and returns the undo snapshot; `Restore`); `ViewModels/SearchReplaceViewModel.cs` (+ `NoteResultViewModel`, `MatchViewModel`); `Views/Shell/SearchReplace.axaml(.cs)`; `MainViewModel.OpenSearchReplaceCommand` + `OnNotesRewritten`; `Ctrl+Shift+H`, sidebar search-box button, tree context menu; `Icon.chevron-down`/`replace`/`rotate-ccw`; `ToggleButton.option`/`.expander` styles. Tests `MatchFinderTests`, `ReplaceServiceTests`, `SearchReplaceViewModelTests`, `MainWindowTests.Search_replace_overlay_opens_and_closes`. |
| 40 | Opus | ✅ Done | Configurable auto-save (user request, forum.obsidian.md/t/14230 — #21 in `docs/obsidian-feature-requests.html`, now `d:1`). Plan: `C:\Users\jimmie.jonsson\.claude\plans\shimmering-coalescing-lagoon.md`. New `Models/AutoSave.cs` (`AutoSaveMode` AfterDelay/OnFocusChange/Manual + `AutoSaveDelay` bounds and `Clamp`), `ViewModels/SettingsViewModel.cs`, `Views/Shell/SettingsPanel.axaml(.cs)`, `Icon.settings`. `ISettingsService`/`JsonSettingsService`: `AutoSave` + `AutoSaveDelayMs` (clamped both ways). `EditorViewModel`: `SaveMode` (a loose file is always `Manual`), `SaveStatus`, `SaveImplicitly()` as the single gate for unrequested writes, timer interval from the setting. `MainViewModel`: `FlushLooseEditAsync` → `FlushUnsavedAsync`, the four bare `Editor.Save()` flushes gated, `OpenSearchReplace` made async, `Settings` + `OpenSettingsCommand`. `Ctrl+,`, ribbon button, `Deactivated` flush, status bar bound to `SaveStatus`. Tests `AutoSaveTests`, `SettingsViewModelTests`, `MainWindowTests.Settings_overlay_opens_and_closes`. 199 tests, 0 warnings. |
| 41 | Opus | ✅ Done | Default template for new notes (user request, forum.obsidian.md/t/10332 — #26 in `docs/obsidian-feature-requests.html`, now `d:1`). Plan: `/home/neisep/.claude/plans/im-not-sure-if-virtual-octopus.md`. New `Models/NoteTemplate.cs` (`Apply` - one regex pass, `{{title}}`/`{{date}}`/`{{time}}` each with an optional `:format`, unknown tokens verbatim, invariant culture; `NormalizePath`; `TemplateState`), `Services/INoteTemplateService.cs` + `NoteTemplateService.cs` (resolve inside the vault via `GetFullPath` then `PathScope`, read through `IVaultService`, every failure → `""`, guard against creating the template itself). `IVaultService.CreateNote` gained `string content = ""`; `VaultService` writes it; `ImportToVault` collapsed its `CreateNote` + `Write` into one call. `ISettingsService`/`JsonSettingsService`: `NewNoteTemplatePath` (normalised both ways). `MainViewModel`: 8th ctor arg, one private `CreateFromTemplate` used by `NewNote`, the quick-switcher Create row and the `OpenWikiLink` fallback. `SettingsViewModel`: `TemplatePath` + `TemplateStatus`; `SettingsPanel.axaml` "New notes" section (`PlaceholderText`, `UpdateSourceTrigger=LostFocus`, `{}` brace escape). Prerequisite fix: `MarkdownRenderer` gained `.UseYamlFrontMatter()` and `BlockRenderer` a `YamlFrontMatterBlock => null` arm above `CodeBlock` (it derives from it). Tests `NoteTemplateTests`, `NoteTemplateServiceTests`, `NewNoteTemplateTests` + cases in `VaultServiceTests`, `SettingsViewModelTests`, `MarkdownViewTests`, `MainWindowTests`. 247 tests + 1 skipped, 0 warnings. |
| 42 | Opus | ✅ Done | Exclude files or folders from all indexers and parsers (user request, forum.obsidian.md/t/52025 — #29 in `docs/obsidian-feature-requests.html`, now `d:1`). Plan: `/home/neisep/.claude/plans/lets-implement-this-exclude-glimmering-starlight.md`. New `Models/VaultIgnore.cs` (gitignore subset - `#` comments, bare names at any depth, trailing `/` folders-only, a leading or inner `/` anchors to the root, `*`/`?` within a segment, `**` across them; `!` skipped and counted; literal names go to a `HashSet` fast path, the rest to an uncompiled `IgnoreCase` regex; `Matches` is the entry alone, `IsIgnored` walks its ancestors) plus `IgnoreState`/`IgnoreStatus`. New `Services/IgnoreFile.cs` (the `FolderOrderFile` twin: `.mdnotesignore` and the root `.gitignore`, read/write, the write-time+length stamp a cached list is checked against, never throws) and `IVaultIgnoreService`/`VaultIgnoreService` (`Text`, `Save`, the `UseGitIgnore` switch, `Probe()`). `VaultService` takes `ISettingsService`, caches the compiled list against the stamp, and filters at `GetTree`, `EnumerateNotes` and `EnumerateFolders` - the last two hand-rolled recursions, since `SearchOption.AllDirectories` cannot be stopped from descending and skips nothing, which is why `.git` was being searched. `ReadChildren` split into an unfiltered `ReadEntries` (what `.order` is written from, so an excluded sibling keeps its line and its place) and a filtered, pruning `ReadChildren` (the tree's only); `MoveTo` translates the sidebar's visible index into a full-sibling index. `RejectExcluded` at `CreateNote`/`CreateFolder`/`Rename`; watcher events for excluded paths suppressed via a disk-free cached list; a second watcher filtered to the two file names catches content edits; `RefreshExclusions` reuses `VaultChanged`, so one window's panel rebuilds every window's tree. `ReplaceService` lost `IsHidden` (now dead) and `IsUnder` (a copy of `PathScope.IsSameOrUnder`). `ISettingsService.UseGitIgnore`; `MainViewModel` 9th ctor arg + `SelectIfVisible` so renaming an excluded open note is not an error; `SettingsViewModel` `ExcludedPatterns`/`UseGitIgnore`/`ExcludeStatus`; `SettingsPanel.axaml` "Excluded files" section, the card now a `ScrollViewer` with Done pinned outside it and Escape/scrim focusing Done first so a `LostFocus` binding commits. Tests `VaultIgnoreTests`, `VaultIgnoreServiceTests`, `ExcludeTests` + cases in `VaultServiceTests`, `SearchServiceTests`, `ReplaceServiceTests`, `QuickSwitcherViewModelTests`, `SearchReplaceViewModelTests`, `SettingsViewModelTests`, `MainWindowTests`. 313 tests + 1 skipped, 0 warnings. |
| 43 | Opus | ✅ Done | Architecture review F9 (`docs/architecture-review.html#f9`): `Sidebar.axaml.cs` holds drop policy and a template-private name. Move the "which drop position does this fraction of the row mean" rule out of the view and next to `ResolveDrop` in `VaultWorkspaceViewModel`, so the view reports the pointer fraction and the view model decides `Before`/`After`/`Into`; cover the 25/75 folder band and the 50% split in `TreeOrderTests`. Make `Sidebar.RowUnder` an internal seam and add a `SidebarDragTests` case that fails loudly if Fluent's `PART_LayoutRoot` stops resolving. Already done under F6, not F9: the `IsDropTarget` write-back is gone. Deliberately left: the drag payload stays a path string, and `Vm.SelectedNode`/`Vm.SearchQuery` stay direct writes. |
| 44 | Opus | ✅ Done | Architecture review F7 (`docs/architecture-review.html#f7`): the "exact name beats accent-folded" rule is written three times. New `MdNotes/Services/NameMatch.cs` (`NameRank` ladder Exact/ExactFolded/Prefix/PrefixFolded/Contains/ContainsFolded/None, `Rank` for ranking and `Best` for equality-only lookup), next to `DiacriticFolder` and `PathScope`. `VaultService.FindNoteByName`/`FindFolderByName` and `QuickSwitcherViewModel.RefreshResults` all go through it; the folder lookup keeps its own bare-name-vs-relative-path `Key` selector. Deliberate behaviour change: the switcher no longer folds both sides up front, so an exact name outranks a folded one as it already did for wikilinks. New `NameMatchTests`, plus the ordering case in `QuickSwitcherViewModelTests`. Left alone, as the review says: `SearchService`, `MatchFinder` and the switcher's content matching stay three distinct matchers. |
| 45 | Opus | ✅ Done | Architecture review F8 (`docs/architecture-review.html#f8`): the diagram renderer and both caches were statics on `MarkdownView`, set from `App`. `IDiagramRenderer` is the tenth member of `AppServices`, built in `AppSession`; `MainViewModel.Diagrams` exposes it and `MainWindow.axaml` binds it on the preview. `MarkdownView.DiagramRenderer` is a `StyledProperty`, `_diagrams`/`_images` are instance fields, and `Lru`/`DiagramCache`/`ImageCache`/`MarkdownView` are `IDisposable` - the window disposes the preview in `OnClosing`, not on detach. `TestServices` supplies the renderer so the twelve static writes go; `DiagramPaletteTests` uses `services with { Diagrams = … }`. New `LruTests`, `DiagramCacheTests.Dispose_releases_both_maps`, `MarkdownViewTests.Replacing_the_renderer_rebuilds_the_diagram_through_it`, `MainWindowTests.Every_window_renders_diagrams_through_the_injected_renderer`. Not closed: F11. |
| 46 | Opus | ✅ Done | Architecture review F11 (`docs/architecture-review.html#f11`): `DiagramView` stored a bitmap the `Lru` owned and drew it every frame, while the `Lru` disposed on eviction with no idea what was on screen. Reproduced first - `bitmapCapacity: 1`, two visible diagrams, `ObjectDisposedException: Ref<IBitmapImpl>` out of `DiagramView.Render`. The view now keeps only `_pixelWidth`, the cache key; `CurrentBitmap()` asks the cache and `Render` borrows for that one call, which is safe because Avalonia takes its own reference on the draw it records. `Rasterize` still runs off the settle timer and still warms the cache. `DiagramView.Bitmap` → `CurrentBitmap()` (a miss rasterizes, which is not a property's job) in `DiagramViewTests`, `MarkdownViewTests`, `MainWindowTests`. New `DiagramViewTests.A_visible_diagram_survives_another_ones_rasterization`. Found and not fixed: `ImageCache` has the same split via `InlineRenderer.AddImage`, where `Image.Source` must hold across frames so borrow-per-draw does not apply. |
| 47 | Opus | ✅ Done | Architecture review F2 remainder (`docs/architecture-review.html#f2`), the half the snapshot did not cover: vault reads are still synchronous on the UI thread. `SingleInstance.cs:51` is the only `Task.Run` in the app, and `SearchService.Search` calls `vault.Read(path)` for every note inline - the `VaultSnapshot` added in `9bf07e5` caches paths, not content. Decide the shape first: a content cache keyed like `ImageCache` (path + write stamp) behind the same `_revision`/watcher invalidation, or move `Search` and `ReplaceService.Find` off-thread and marshal results back. The rest of F2 is done - one traversal, 250 ms debounce, `Probe` on the snapshot - so this is about where content lives, not about a fifth walk. |
| 48 | Opus | ✅ Done | Architecture review F10 (`docs/architecture-review.html#f12`): two error-to-dialog funnels, one claiming to be the only one. `DialogGuard.cs:6` still says "The single place a service exception becomes a message to the user" while `App.axaml.cs:32-36` sets `args.Handled = true` on everything. Keep both, as the review says, but give them distinct roles: the guard for expected, user-caused failures; the global handler as a crash barrier that says so in its comment and presents itself as one. At minimum the `DialogGuard` comment must stop claiming exclusivity. Small - a comment and some framing, not a refactor. |
| 49 | Opus | ✅ Done | `ImageCache` has the ownership split task 46 fixed for diagrams (found there, deliberately left; `docs/architecture-review.html#f10`). `InlineRenderer.AddImage` puts a cache-owned `Bitmap` on `Image.Source`, which must hold it across frames, so the borrow-per-draw shape used for `DiagramView` does not apply. Reproduced with `new ImageCache(capacity: 1)`, two images and a re-measure: `ObjectDisposedException` out of `Image.MeasureOverride`. Needs a different bound - liveness-tracked entries, or a generational cache cleared one document behind - which is a design decision rather than a translation of the F11 fix. Write the failing test first, as task 46 did. |

| 50 | Opus | ✅ Done | Use H1 or a YAML `title` as the display name (user request, forum.obsidian.md/t/712 - #4 in `docs/obsidian-feature-requests.html`, now `d:1`; 712 votes, the top open request that fits MdNotes). New `Models/NoteTitle.cs` (frontmatter `title:` beats first heading, top 4 KB only, fenced code and `#Tag` are not headings, a rule with no closing fence is not frontmatter). `VaultEntry` gained `Title` + `Display`; `VaultWalk.Of` takes an optional `readNote` delegate so the walk is told whether to open files rather than deciding; `VaultSnapshot` carries `Titles` and a path->title map, so titles resolve once per snapshot and no surface re-reads per keystroke. `VaultService.DisplayName` is the one answer, used by the sidebar (`TreeNodeViewModel.Display`, re-assigned in `Reconcile`), the quick switcher (ranks title *and* file name), `SearchService` and `EditorViewModel.NoteName`. `ISettingsService.UseNoteTitles` off by default; `SettingsViewModel` gained the vault so the toggle can call the new `VaultService.Refresh` (`RefreshExclusions` now delegates to it). Wikilinks and rename deliberately unchanged - identity stays the file name. `Write` rebuilds the tree only when the title actually changed. New `NoteTitleTests` (22) and `NoteTitleDisplayTests` (10), README section, 449 tests + 1 skipped, 0 warnings. |
| 51 | Opus | ✅ Done | Restore scroll and cursor position per note (user request, forum.obsidian.md - #38 in `docs/obsidian-feature-requests.html`, now `d:1`; 278 votes). New `Models/NotePositions.cs`: full path -> caret offset, bounded LRU (200) with an explicit clock, offsets clamped on read because the note may have been edited elsewhere. `ISettingsService` gained `NotePositions` + `SaveNotePositions()` (a mutate-then-save pair rather than a setter, so the file is written per note switch and not per keystroke); `JsonSettingsService` persists it and reloads oldest first. `EditorViewModel.CaretIndex` is two-way bound from `MainWindow.axaml`; `SetContent` remembers the outgoing note then restores the incoming one **after** setting `Text`, `Retarget` moves the entry on rename, `Close` records on window close. Scroll needed no code: Avalonia's TextBox scrolls to the caret, verified in a real window (offset 0 -> 9758 of a 10400 extent) and pinned by `NoteReturnScrollTests`. New `NotePositionTests` (9), `NoteReturnTests` (5), `NoteReturnScrollTests` (1). 464 tests + 1 skipped, 0 warnings. |
| 52 | Opus | ✅ Done | Performance review baseline (`docs/performance-review.html#profiling`). `MdNotes.Tests/ScanBenchmark.cs` already covers the three vault-wide scans with a generated vault (`MDNOTES_VAULT_NOTES` / `_KB` / `_SEED`) or a real one (`MDNOTES_BENCH_VAULT`), skipped unless `MDNOTES_BENCH=1` — extend it rather than rebuilding it. What it does not cover, and what the remaining findings need: BenchmarkDotNet micro-benchmarks in `MdNotes.Benchmarks` for `DiacriticFolder.Fold`, `SvgCssInliner.Inline` over a real Mermaid SVG, `NameMatch.Rank` and `QuickSwitcherViewModel.RefreshResults` over 5 000 items, and `MarkdownView.Rebuild` on a 100 KB note; plus a `dotnet-counters monitor -p <pid> System.Runtime` run while typing in the sidebar search box, for allocation rate, gen0/1/2 and LOH size (finding 3). Findings 8 and 12 need a call tree, so a VS CPU Usage session on 30 s of Split-mode typing is the user's step. Record every number in the task output. No product behaviour changes. |
| 53 | Opus | ✅ Done | Perf finding 2, High (`docs/performance-review.html#f2`): `SearchService.Search` line 24 calls `vault.DisplayName(path)` once per note, which goes `CurrentSnapshot` → `CurrentIgnore` → `IgnoreFile.StampOf` and so stats `.mdnotesignore` (and `.gitignore`) once or twice per note per scan. Take the snapshot once per search and look display names up from it — expose `IReadOnlyDictionary<string,string>` or a `DisplayNames()` accessor on `VaultService`, keyed `OrdinalIgnoreCase`. The scan runs on the thread pool now (finding 1), so whatever is exposed has to be safe to read from there. Test: one search over a vault with an ignore file stats it once, not once per note. |
| 54 | Opus | ✅ Done | Perf finding 3, High (`docs/performance-review.html#f3`): `SearchService.Search` line 28 folds a full copy of every note on every scan, and `MatchFinder.Find` line 46 builds a `Lines` index for every note including those with no matches — whole-vault allocation traffic per scan, with notes over ~42K chars landing in the LOH. Store the folded text lazily beside the raw text in `NoteContentCache` (folded once per read, dropped with the entry, under the existing `_gate`) and build `Lines` on the first match instead of up front. `DiacriticFolder` must stay length-preserving so offsets remain valid against the raw text, which is what `Snippet` slices. Tests: a note read twice folds once; a note with no matches never builds `Lines`. |
| 55 | Opus | ✅ Done | Perf allocation one-liners, findings 9, 10, 14 and 19 (all on the review's easy-improvements list). (a) `EditorViewModel.RenderPreview` line 260: `WordRegex.Count(Text)` instead of `WordRegex.Matches(Text).Count`, which materialises one `Match` per word every 250 ms. (b) `SvgCssInliner.Inline` lines 35-39: one `StringBuilder` pass instead of re-searching from the start and rebuilding the whole SVG per `var()` — same output, linear instead of O(length × occurrences). (c) `PathScope.IsSameOrUnder` line 9: drop the `ancestor + Path.DirectorySeparatorChar` concatenation for `path.Length > ancestor.Length && path[ancestor.Length] == Path.DirectorySeparatorChar && path.StartsWith(ancestor, OrdinalIgnoreCase)`. (d) `SearchReplaceViewModel.Close`: clear `_excluded`, which today is cleared only after a replace (line 221) and so accumulates unticked match keys across overlay sessions. One test per change. |
| 56 | Opus | ✅ Done | Perf finding 11, Medium (`docs/performance-review.html#f11`): `ImageCache.Get` line 61 decodes images at native resolution (`new Bitmap(path)`) while `InlineRenderer.AddImage` caps display at 800 DIP, so a 12-megapixel photo costs roughly 48 MB of BGRA and the generational cache holds up to two copies. Decode with `Bitmap.DecodeToWidth(stream, target)` where target is 800 × the window's render scaling, with a sensible ceiling. Keep the cache key and the current/previous-build lifetime logic exactly as they are (task 49). |
| 57 | Opus | ✅ Done | Perf finding 7, Medium (`docs/performance-review.html#f7`) — the largest recurring cost during ordinary typing. In Edit-only mode `MainWindow.axaml` (74-83) hides the preview's `ScrollViewer`, but `EditorViewModel.RenderPreview` still parses and `MarkdownView.Rebuild` still builds the whole control tree every 250 ms, constructing `DiagramView`s (synchronous Mermaid) and decoding images for output nobody sees; only layout is skipped. Skip `Rebuild` when the view is not effectively visible, mark it dirty, and rebuild once on becoming visible; optionally skip `_renderer.Parse` too, keeping the word and character counts. Tests: switching to Edit-only stops rebuilds, switching back renders the current text exactly once. |
| 58 | Opus | ✅ Done | Perf finding 8, Medium (`docs/performance-review.html#f8`): every paragraph, inline code run, link, list marker and table cell gets live `DynamicResource` subscriptions — nine uses in `InlineRenderer` and ten in `BlockRenderer` — although `MarkdownView` already rebuilds the whole tree on `ActualThemeVariantChanged`, so the dynamic behaviour is never used. Resolve the handful of brushes once per `Rebuild` and pass them through `RenderContext`, the pattern `MarkdownView.Palette` already uses for the diagram palette. Only after task 52 shows resource lookup is a real share of rebuild time on a 100 KB note; `Renders_shell_in_both_themes` (task 20) stays green. |
| 59 | Opus | ✅ Done | Perf finding 12, Medium (`docs/performance-review.html#f12`): every edit inside a mermaid fence re-renders Mermaid → SVG → `SvgSource` synchronously in the `DiagramView` constructor (line 38, via `cache.NaturalSize`), and each half-typed state is retained as an unmanaged picture (up to 64) and bitmap (up to 32). Measure a real render first (task 52). If it is more than a few milliseconds, move `MermaidRenderer.RenderSvg` + `SvgSource.LoadFromSvg` to the thread pool and keep showing the previous bitmap until the new one arrives; consider a smaller `DiagramCache` picture capacity, since only the current note's diagrams need to be hot. If it is fast, mark the finding Won't-do with the numbers. Ownership rules from tasks 45/46 must hold. |
| 60 | Opus | ✅ Done | Perf findings 13 and 20, both in the replace overlay. (a) `SearchReplaceViewModel.Rescan` (lines 168-178) and `VaultWorkspaceViewModel.RunSearch` `Clear()` a bound collection and `Add` each row, one `CollectionChanged` plus container generation and layout per row on every debounce tick; build the rows into a plain list and swap them in one step (a read-only `IReadOnlyList` property replaced wholesale, or a collection with a reset notification), and cap or virtualise the overlay results list. (b) `OnSelectionChanged` and `UpdateStatus` walk every note and every match on each checkbox click; pass the changed match into the callback and update `_excluded` and the running counts incrementally. |
| 61 | Opus | ✅ Done | Perf finding 5, Medium (`docs/performance-review.html#f5`): `QuickSwitcherViewModel.RefreshResults` re-runs `FlattenNotes` (line 89) per keystroke, allocating a `SwitcherItem` and folder-path strings per note, then sorts every candidate to take 50; `NameMatch.Rank` is called twice per note and folds both sides each time, so the query is folded 2N times. Cache the flattened list against the tree instance from `GetTree()` (the same reference until the vault changes), pre-fold names when flattening, fold the query once per refresh, and replace the full `OrderBy` with a bounded top-50 selection or a bucket per `NameRank`. The exact-name-beats-accent-folded rule from task 44 and the title-or-filename ranking from task 50 must both survive. After 52 confirms the lag on a 5 000-note vault. |
| 62 | Opus | ✅ Done | Perf finding 6, Medium (`docs/performance-review.html#f6`): each `DragOver` resolves the dragged path with `VaultWorkspaceViewModel.FindNode` (line 643) and then `ResolveDrop` → `SiblingsOf` (line 649) calls it a second time — two full tree walks per pointer move, where `FindNode` is `SelectMany` over a recursive iterator costing O(depth) per node; `Sidebar.RowUnder` adds a LINQ pass over visual ancestors and `SelectPath` allocates a string per folder. `TreeNodeViewModel` has no `Parent` today. Resolve the dragged node once on drag start and keep it in a field, add that `Parent` reference so siblings need no lookup, keep a path→node dictionary refreshed in `Reconcile` (comparer stays `OrdinalIgnoreCase`), and replace the recursive iterator with an explicit stack. Drop policy and `Alt+↑`/`Alt+↓` behaviour from tasks 32-37 and 43 must not change. After 52. |
| 63 | Opus | ✅ Done | Perf findings 15 and 16, both Low, both about work done when nothing is asked for. (a) `VaultService.OnFsChanged` (line 486): every non-quiet event allocates a relative path on the watcher thread and posts a closure per open window whose only effect is restarting a 300 ms timer, so a build or git checkout inside the vault queues thousands of dispatcher items; coalesce on the service side with an `Interlocked` flag raising `VaultChanged` once per quiet period, or skip the post when the timer is already armed. (b) `SingleInstance.TryHandOff` line 35: `Connect(ConnectTimeoutMs)` retries the whole 300 ms when no instance is running, so every cold start pays it before Avalonia begins; check existence first (a named `Mutex` held by the listener, or a `File.Exists` on the pipe path under the Windows named-pipe root) and keep the timeout as a fallback. Second-instance hand-off must still work. |
| 64 | Opus | ✅ Done | Perf findings 17 and 18, both Low and both explicitly measure-before-spending-time-here. (a) `JsonSettingsService` lines 32 and 135 serialise and deserialise the whole settings object with the reflection-based `JsonSerializer`, written synchronously on the UI thread per property set and per note switch; add a `JsonSerializerContext` (source generation, which also helps trimming) and optionally a short write-behind to coalesce bursts. `Moving_the_caret_does_not_write_the_settings_file` (task 51) must stay green. (b) `VaultWalk.Entries` lines 46-53 compute `Path.GetFileName` twice per directory, copy the collection and re-create every entry with a `with` expression, and `FolderOrderFile.Apply` stats `.order` separately; enumerate once with `DirectoryInfo.EnumerateFileSystemInfos`, note whether `.order` was seen during that pass, and construct each `VaultEntry` once. Only if task 52 shows the walk is slow on a large vault; otherwise mark both Won't-do with the numbers. |
| 65 | Opus | ✅ Done | Perf finding 21, Medium (`docs/performance-review.html#f21`) — found on 2026-09-22 while auditing finding 1, not part of the original review. Finding 1 moved the cold walks to `RefreshTreeAsync` but deliberately left `RefreshTree` (563-569) synchronous at the ~9 call sites where the user just created, renamed, deleted or moved one entry, on the stated grounds that `NoteContentCache` holds every other note's head so the walk is one disk read. That holds only under the 32M-character budget; above it the cache degrades to re-reading and the same synchronous walk reads a large share of the vault on the UI thread, which on a 5 000-note vault is the 559 ms already measured. Have `RefreshTree` fall back to `RefreshTreeAsync` when `HeldChars` is at `BudgetChars` (both already exposed, so no new state); the generation counter in `WalkAsync` already makes the async path safe to take from anywhere. Alternatively drop the synchronous path and measure whether the extra dispatcher hop shows on a small vault. |

---
## Outputs

### Task 1 · Haiku · ✅ Done
Created `Models/ThemeMode.cs` (System, Light, Dark) and `Models/ViewMode.cs` (Edit, Split, Reading).
`ISettingsService` + `JsonSettingsService` gained `ThemeMode`/`ViewMode` (defaults System/Split,
write-through like `LastVaultPath`). `FakeSettings` in `MainWindowTests.cs` updated. Build green.

### Task 2 · Haiku · ✅ Done
Inter 4.1 static TTFs (Regular, Medium, SemiBold, Bold, ~410–420 KB each) + `OFL.txt` in
`MdNotes/Assets/Fonts/`. `MdNotes.csproj`: `<AvaloniaResource Include="Assets/**" />`. `Program.cs` and
`MdNotes.Tests/TestAppBuilder.cs`: `FontManagerOptions { DefaultFamilyName = "avares://MdNotes/Assets/Fonts#Inter" }`.
Build green, 37/37 tests.

### Task 4 · Haiku · ✅ Done
`Themes/Palette.axaml` with Light/Dark theme dictionaries: MdSurface, MdSurfaceAlt, MdSurfaceCode, MdBorder,
MdText, MdTextMuted, MdTextFaint, MdAccent, MdAccentSoft, MdHover, MdError + aliases MdCodeBackground,
MdLink, MdWikiLink, MdQuote. `App.axaml` now merges it via `ResourceInclude`; old colour block removed.
Also fixed a transient compile error in `EditorViewModel.Breadcrumb` left by task 8 mid-flight. Build green, 37/37.

### Task 8 · Haiku · ✅ Done
`EditorViewModel`: `NoteName`, `Breadcrumb` (relative to vault root, " / " separators, updated in
`OnCurrentPathChanged`), `WordCount` (`\S+` regex), `CharCount` (both computed in `RenderPreview`).
Build green, 37/37.

### Task 3 · Haiku · ✅ Done
`Assets/Icons.axaml`: 16 `StreamGeometry` resources `Icon.files`, `Icon.search`, `Icon.plus`, `Icon.folder-plus`,
`Icon.chevrons-down-up`, `Icon.command`, `Icon.sun-moon`, `Icon.folder-open`, `Icon.pencil`, `Icon.columns-2`,
`Icon.book-open`, `Icon.x`, `Icon.file-text`, `Icon.corner-down-left`, `Icon.square`, `Icon.square-check`
(Lucide 24×24 stroke paths, rect/circle converted to path syntax). `Assets/LUCIDE-LICENSE.txt` added. XML validated.
Not yet merged into `App.axaml` (task 5).

### Task 6 · Haiku · ✅ Done
`Themes/Controls.axaml` created with `Path.icon`, `Button.icon` (+ `:pointerover`, `:pressed`, `.active`),
`TextBox.editor` (borderless in all states, accent caret), `TextBox.search`, `GridSplitter`; all colours via
`DynamicResource` Md* tokens. `App.axaml` includes it after `FluentTheme`. Build green, 37/37.

### Task 9 · Haiku · ✅ Done
`MainViewModel`: `ViewMode`/`ThemeMode` (loaded from and persisted to `ISettingsService`), `IsSidebarVisible`,
`VaultName` (set in `OpenVaultAt`), computed `IsEditVisible`/`IsPreviewVisible`/`IsSplit`/`IsEditOnly`/
`IsReadingOnly`; commands `SetViewMode`, `ToggleEditReading`, `CycleTheme`, `ToggleSidebar`, `CollapseAll`.
Build green, 37/37.

### Task 5 · Haiku · ✅ Done
`App.axaml`: `FluentTheme.Palettes` with Light (Accent #7F6DF2, Region #FFFFFF) and Dark (Accent #A78BFA,
Region #1E1E1E) `ColorPaletteResources`, Alt/Base/Chrome/List/ErrorText derived from the palette; all
attributes valid in Avalonia 12. `Assets/Icons.axaml` merged into Application resources. Build green, 53/53.

### Task 7 · Haiku · ✅ Done
`Controls.axaml`: `TreeViewItem` (MinHeight 28, 13px, `PART_LayoutRoot` radius 4), `ListBoxItem` (radius 4,
margin 4,1). New `Themes/FluentOverrides.axaml` (Light/Dark) overriding TreeViewItem/ListBoxItem hover,
selected, pressed and foreground brushes, `TextControlBorderBrushFocused`/`ThemeThicknessFocused`, and
`SystemControlHighlightListAccentLow/Medium`; merged in `App.axaml`. Build green.

### Task 10 · Haiku · ✅ Done
`ViewModels/QuickSwitcherViewModel.cs` (+ `SwitcherItem` record): `IsOpen`, `Query`, `Results`, `Selected`;
commands `Open`, `Close`, `OpenSelected`, `MoveSelection(int)`; ranking exact > prefix > contains, cap 50,
create item on no match; callbacks `openPath`/`createNote`. `MainViewModel.QuickSwitcher` +
`OpenQuickSwitcherCommand` (Guard-wrapped). `MdNotes.Tests/QuickSwitcherViewModelTests.cs`: 16 tests.
Also repaired transient compile errors other in-flight tasks left in `InlineRenderer.cs` and the dialogs.
Build green, 53/53.

### Task 11 · Haiku · ✅ Done
`App.axaml.cs`: `ApplyTheme(ThemeMode)` maps to `ThemeVariant.Default/Light/Dark`; called at start and on
`MainViewModel.ThemeMode` PropertyChanged. Build green.

### Task 18 · Haiku · ✅ Done
Renderers: paragraph 16/26 `MdText`; document spacing 16; headings 29/26/22/20/18/16, Bold/SemiBold, h1 border
removed; quote 2px `MdAccent` + `MdTextMuted`; code block 14px, padding 14, `MdSurfaceCode`; inline code
`MdTextMuted`; task boxes as `Icon.square`/`Icon.square-check` `Path`s in `InlineUIContainer`; table wrapped in
1px `MdBorder` radius-4 border, header `MdSurfaceCode`; rule margin 0,24; list markers `MdTextMuted`; links
`MdAccent`. `MarkdownViewTests` green.

### Task 19 · Haiku · ✅ Done
`InputDialog` and `MessageDialog`: `MdSurface` background, 20px padding, 15px SemiBold title, 13px body,
`TextBox` radius 6, buttons MinWidth 88 radius 6, primary button `MdAccent`/white via `Window.Styles`
(`Button.primary`). MessageDialog title "MdNotes" for errors, "Confirm" for confirmations. Build green.

### Task 12 · Haiku · ✅ Done
`Views/Shell/Ribbon.axaml(.cs)`: 44px `MdSurfaceAlt` column; top: Files (toggle sidebar, `.active`), Search
(`SearchButton`, focused by the window), New note, Quick switcher; bottom: Theme (tooltip shows mode), Open vault.

### Task 13 · Haiku · ✅ Done
`Views/Shell/Sidebar.axaml(.cs)`: vault-name header with New note / New folder / Collapse all icon buttons,
`SearchBox` (`TextBox.search`, search icon, clear button when searching), tree without emoji (folders SemiBold,
context menu + `IsExpanded` style kept, `OnTreePointerPressed` moved here), search results list.

### Task 14 · Haiku · ✅ Done
`Views/Shell/PaneHeader.axaml(.cs)`: 40px header, breadcrumb + pencil rename button, Edit / Split / Reading
icon toggles bound to `SetViewModeCommand` with `.active` from `IsEditOnly`/`IsSplit`/`IsReadingOnly`; hidden
when no note.

### Task 15 · Haiku · ✅ Done
`Views/Shell/StatusBar.axaml(.cs)`: 22px bar, "N words · M characters" (MultiBinding), Saved / Editing….
`Views/Shell/EmptyState.axaml(.cs)`: file-text icon, "No note is open", Ctrl+N / Ctrl+O hints.

### Task 16 · Haiku · ✅ Done
`Views/Shell/QuickSwitcher.axaml(.cs)`: scrim (click closes), 560px card with search box and results list
(folder subtitle, plus icon for create); tunnelled Up/Down/Enter/Escape handling; input focused when shown.
Build green, 53/53 after wave D.

### Task 17 · Haiku · ✅ Done
`MainWindow.axaml` rewritten: root `Panel` → `Grid` (Auto,260,1,* × Auto,*,Auto) with Ribbon, Sidebar,
splitter, PaneHeader, editor/preview `Panes` grid (+ EmptyState overlay), StatusBar; QuickSwitcher overlay
on top. Key bindings Ctrl+S / Ctrl+N / Ctrl+O (switcher) / Ctrl+Shift+O (vault) / Ctrl+E. Code-behind:
sidebar column collapses to 0 and restores its width; pane columns set to 1*/0, 1*/1*, 0/1* by ViewMode;
ribbon Search → `Sidebar.FocusSearchBox()` (shows the sidebar first). Old tree/list markup removed.
Build green, 53/53.

### Task 21 · Haiku · ✅ Done
`README.md`: shell, view modes (Ctrl+E), quick switcher (Ctrl+O), theme chooser, vault switch now
Ctrl+Shift+O, credits for Inter (OFL) and Lucide (ISC). `PLAN.md`: layout tree with Assets/, Themes/,
Views/Shell/, QuickSwitcherViewModel; behaviour notes; "2026-09-16 (UI overhaul)" progress entry.

### Task 20 · Haiku · ✅ Done
`MainWindowTests.Renders_shell_in_both_themes`: renders the full shell Light + Dark, asserts Ribbon / Sidebar /
PaneHeader / StatusBar / ≥6 icon Paths / word count, dumps `.render/main-window-{light,dark}.png` (~46–48 KB).
54/54 tests. Controller inspected both PNGs: shell, palette, Inter, icons, checkboxes, table, code, quote and
mermaid all render; defects → task 23.

### Task 23 · Haiku · ✅ Done
`Icon.sun-moon` replaced with the Lucide moon path; `BlockRenderer.List` skips the bullet for task-list items
(`IsTaskListItem`); table wrapper `HorizontalAlignment=Left`; render test expands the folder first.
54/54 tests; PNGs re-rendered and inspected by the controller: all four defects gone.

### Task 22 · Opus · ✅ Done
# Reviewer log — Task 22 (Opus)
_2026-09-16 · MdNotes Obsidian-style UI overhaul_

STATUS: DONE

## A. Summary

Verified: `dotnet build -nologo -v q --no-incremental` → **0 errors, 3 warnings**;
`dotnet test -nologo -v q` → **54/54 green**. Both PNGs in `.render/` read as images.

The shell matches the spec: ribbon, sidebar, pane header, split panes, status bar, empty state and
quick-switcher overlay all exist, both themes render with the §1 palette, Inter and Lucide strokes,
and the preview typography (headings, quote, code, table, task boxes, wikilinks) is correct in both
variants. Key bindings verified in `MdNotes/Views/MainWindow.axaml:13-19`: **`Ctrl+O` →
`OpenQuickSwitcherCommand`** and **`Ctrl+Shift+O` → `OpenVaultCommand`** — both correct, and Avalonia
matches gestures on exact modifiers so `Ctrl+Shift+O` does not also fire `Ctrl+O`.

The QuickSwitcher overlay's compiled bindings (`MdNotes/Views/MainWindow.axaml:81-83`) are **correct**:
`x:DataType="vm:QuickSwitcherViewModel"` on the instance scopes `IsVisible="{Binding IsOpen}"` to the
switcher VM, while Avalonia's XAML compiler special-cases the `DataContext` property itself back to the
parent scope, so `{Binding QuickSwitcher}` still resolves against `MainViewModel`. Nothing silently
does nothing there — but nothing tests it either (issue 15).

**The 3 build warnings do not matter functionally** — all three are `AVLN5001` obsolete-API warnings on
properties that still work. They are worth clearing anyway because two of them are pure noise
(`SystemDecorations="Full"` is the *default*, so the attribute can just be deleted) and the third marks
a real inconsistency: `QuickSwitcher.axaml` uses `Watermark` while `Sidebar.axaml` already uses
`PlaceholderText` for the same kind of search box. See issue 9.

One **High** defect survived into the rendered screenshots, plus a cluster of Mediums that are exactly
the parallel-agent leftovers the brief asked about: duplicated styles across two dialogs, the palette
hard-coded in three files, a dead Fluent override fighting a style, and defensive code at a non-boundary.

Sidebar/pane toggling edge cases checked: toggle-while-hidden is safe (`_sidebarWidth` is only captured
from an absolute width and the bool cannot fire twice in the same direction); ViewMode change with no
note is safe (`EmptyState` spans all three pane columns, so a 0-width column 0 does not shrink it).

## B. Strengths

- `MainViewModel` keeps the `Guard`/`GuardAsync` boundary intact; the new commands add no `try/catch`
  of their own. `TryOpenInitialVaultAsync`/`TryClose` still own their own boundary as before.
- `QuickSwitcherViewModel` depends only on `IVaultService` + two callbacks — no view, no dialogs, no
  `MainViewModel` back-reference. Genuinely testable, and the 16 tests exercise ranking, cap, create
  item, clamping and folder paths.
- Palette/typography tokens are real semantic tokens, and the renderers consume them via
  `DynamicResourceExtension`, so the live theme switch actually repaints the preview.
- `EditorViewModel` computes `WordCount`/`CharCount` inside the already-debounced `RenderPreview`
  rather than on every keystroke.
- `TextBox.editor` is borderless in every state, and the editor is still a `TextBox` with
  `AcceptsReturn`, so the pre-existing smoke test kept working unmodified.
- Shell controls are small, single-purpose `UserControl`s with `x:DataType` — the split is clean.

## C. Issues Found

### High

**1. `MdNotes/Views/Shell/Sidebar.axaml:11-34` — sidebar header buttons are mis-laid-out and clipped.**
The inner `DockPanel` has `LastChildFill="True"` (the default) and its **last** child is the
`StackPanel` marked `DockPanel.Dock="Right"`. `LastChildFill` overrides `Dock`, so that panel *fills*
the remainder instead of docking right: the three icon buttons render immediately after the vault name
and the third one (Collapse all) is cut off by the sidebar edge. Visible in both
`.render/main-window-dark.png` and `main-window-light.png` at x≈295. The longer the vault name, the
more is clipped. `PaneHeader.axaml` and `StatusBar.axaml` use the same idiom but declare the docked
panel *first*, so only the sidebar is broken.

### Medium

**2. `MdNotes/ViewModels/MainViewModel.cs:219` — `OpenQuickSwitcher` is not `Guard`-wrapped.**
`QuickSwitcher.Open()` → `RefreshResults()` → `_vault.GetTree()` → `RequireRoot()`, which throws
`InvalidOperationException` when no vault is open. Every other command routes service exceptions through
the `Guard` boundary; this one escapes to the global `Dispatcher.UIThread.UnhandledException` handler in
`App.axaml.cs:30`. The task-10 output note claims it is "Guard-wrapped" — it is not.

**3. `MdNotes/Views/Dialogs/InputDialog.axaml:7-16` and `MessageDialog.axaml:7-16` — the `Button.primary`
style block is duplicated verbatim** (both the base style and the `:pointerover` template style). It
belongs once in `Themes/Controls.axaml` next to `Button.icon`. Classic parallel-agent duplication.

**4. `MdNotes/Themes/FluentOverrides.axaml` — the whole palette is hard-coded a second time**, and
`MdNotes/App.axaml:7-15` hard-codes it a third time in `ColorPaletteResources`. `#267F6DF2`,
`#26A78BFA`, `#10000000`, `#13FFFFFF`, `#FF222222`, `#FFDADADA` each appear in two or three files.
Changing the accent now means editing three files consistently, with nothing to catch a miss.

**5. `MdNotes/Themes/FluentOverrides.axaml:22-23, 48-49` — `TextControlBorderBrushFocused` is dead, and
the thickness override has global reach.** `TextControlBorderThemeThicknessFocused` is `0`, so the
focused border brush can never paint. The `0` also strips the focus ring from *every* `TextBox`,
including the dialog input — and then `Controls.axaml:89-92` puts a 1px accent border back for
`TextBox.search` only. Two mechanisms fighting; the global one only exists for `TextBox.editor`, which
already removes its own border at `Controls.axaml:71-75`.

**6. `MdNotes/Views/Shell/QuickSwitcher.axaml.cs:26-31` — defensive lookup with a silent fallback at a
non-boundary.** `x:Name="Input"` already generates a strongly-typed field, so
`this.FindControl<TextBox>("Input")` re-does a lookup the compiler did, and `if (input != null)` makes a
missing control silently skip focusing instead of failing. House rules: validate only at system
boundaries, no silent fallbacks.

**7. `MdNotes/Views/Shell/PaneHeader.axaml:52-62` — the rename button acts on the wrong thing.**
`RenameCommand` (`MainViewModel.cs:146-160`) renames `SelectedNode`. If the user last clicked a *folder*
in the tree while a note is open, the pencil beside the note's breadcrumb renames the folder. Spec §2
also asked for the note-name part of the breadcrumb to be the clickable button; this is a separate
pencil icon next to a plain `TextBlock`.

**8. Dead code left behind by the parallel waves.**
- `MdNotes/ViewModels/EditorViewModel.cs:25,86,91` — `NoteName` is written but never read anywhere
  (`PaneHeader` binds `Editor.Breadcrumb`). Task 8 built it for a consumer task 14 never used.
- `MdNotes/Assets/Icons.axaml:16` — `Icon.corner-down-left` has zero references.
- `MdNotes/Themes/Palette.axaml:19-21, 38-40` — `MdLink`, `MdWikiLink`, `MdQuote` have zero references;
  task 18 moved the renderers onto `MdAccent`/`MdTextMuted` and nobody removed the aliases.

**9. The 3 build warnings, and one real inconsistency behind them.**
- `MdNotes/Views/Dialogs/InputDialog.axaml:6` and `MessageDialog.axaml:6` — `SystemDecorations="Full"`
  is obsolete *and* redundant (`Full` is the default).
- `MdNotes/Views/Shell/QuickSwitcher.axaml:29` — obsolete `Watermark`, while the sibling search box in
  `Sidebar.axaml:38` uses `PlaceholderText`. Two agents, two APIs, same widget.

### Low

**10. `MdNotes/Views/MainWindow.axaml.cs:24-34` — `vm.PropertyChanged +=` inside `OnDataContextChanged`.**
Subscribes on every DataContext change and never unsubscribes; a second assignment would double-fire
`AdjustPanesSplit` and leak the old VM.

**11. `MdNotes/Views/MainWindow.axaml:52,63` vs `MainWindow.axaml.cs:73-97` — two sources of truth.**
Pane visibility is driven both by `IsVisible` bindings *and* by `AdjustPanesSplit` zeroing the column
widths. Either alone is sufficient; keeping both means a future change has to remember the code-behind.

**12. `MdNotes/Themes/Controls.axaml:43-50` — style order defeats the active state.**
`Button.icon:pointerover Path.icon` (line 48) is declared *after* `Button.icon.active Path.icon`
(line 43), so hovering an already-active toggle drops its stroke from `MdAccent` back to `MdText`.

**13. `MdNotes/ViewModels/MainViewModel.cs:65-66` — the ctor writes settings back to disk on every
startup.** Assigning `ViewMode`/`ThemeMode` fires `On…Changed`, each of which does a full
`File.WriteAllText` in `JsonSettingsService`. Two pointless disk writes per launch.

**14. `MdNotes/ViewModels/QuickSwitcherViewModel.cs:32-33` — `Open()` scans the vault twice.**
`Query = string.Empty` fires `OnQueryChanged` → `RefreshResults()`, then `Open()` calls
`RefreshResults()` again. Also line 105 `StartsWith(string)` is culture-sensitive while line 107
`Contains(string)` is ordinal — inconsistent comparison semantics in the same ranking expression.

**15. No test covers the switcher's view wiring.** `QuickSwitcherViewModelTests` covers the VM well, but
nothing asserts that `OpenQuickSwitcherCommand` makes the overlay visible, or that `Ctrl+O` reaches the
command. That overlay is the one place in this change where a binding *could* silently do nothing.

**16. `MdNotes.Tests/MainWindowTests.cs:89-102` — global theme state is not restored on failure.**
`Application.Current.RequestedThemeVariant` is mutated and only reset on the happy path (line 102, not
in a `finally`), so an assertion failure leaves the shared headless app in Dark for later tests. The
test also never asserts the PNGs were actually written when `MDNOTES_RENDER_OUT` is set.

**17. Naming and icon inconsistencies.**
- `MdNotes/Assets/Icons.axaml:9` — the key is `Icon.sun-moon` but task 23 put the Lucide *moon* path in
  it. The name now lies about the content.
- `MdNotes/Views/Shell/Sidebar.axaml:19` uses `Icon.file-text` for New note while
  `Ribbon.axaml:58` uses `Icon.plus` for the same command.
- `MdNotes/Views/Markdown/InlineRenderer.cs:191` is the only reference keeping the `MdCodeBackground`
  alias alive; everything else uses `MdSurfaceCode`.

**18. Unused usings and stale index.**
- `MdNotes/Views/MainWindow.axaml.cs:2-3` — `Avalonia.Controls.Primitives` and `Avalonia.VisualTree`
  are unused (the tree handler moved to `Sidebar`).
- `MdNotes/Views/Markdown/InlineRenderer.cs:4` — `Avalonia.Controls.Shapes` is unused; every `Path`
  reference is fully qualified because of the global `System.IO` using.
- `STRUCTURE.md` still indexes `MainWindow.OnTreePointerPressed` at `MainWindow.axaml.cs:15`; that
  method lives at `Sidebar.axaml.cs:17` now.

## D. Recommended Fixes

One Haiku task each; all are small and independent unless noted.

1. `Sidebar.axaml:11` — add `LastChildFill="False"` to the header `DockPanel` (or move the
   `DockPanel.Dock="Right"` StackPanel to be the first child and give the vault-name `TextBlock`
   `TextTrimming="CharacterEllipsis"`). Re-render both PNGs and confirm all three header buttons are
   fully visible with a long vault name.
2. `MainViewModel.cs:219` — `private void OpenQuickSwitcher() => Guard(QuickSwitcher.Open);`
3. Move the `Button.primary` base + `:pointerover` styles from both dialogs into
   `Themes/Controls.axaml`; delete the two `<Window.Styles>` blocks.
4. `FluentOverrides.axaml` — replace the hard-coded hex with `{StaticResource …}` where the key is in
   the same theme dictionary, or (simpler) move the 11 Fluent key overrides into the matching Light/Dark
   dictionaries of `Palette.axaml` so each colour literal exists exactly once. Leave `App.axaml`'s
   `ColorPaletteResources` alone — `ColorPaletteResources` takes `Color`, not `Brush`.
5. `FluentOverrides.axaml` — delete `TextControlBorderBrushFocused` and
   `TextControlBorderThemeThicknessFocused` (both variants); the borderless editor is already handled by
   `TextBox.editor:focus` in `Controls.axaml:71-75`, and dialog TextBoxes get their focus ring back.
6. `QuickSwitcher.axaml.cs:24-32` — replace the body with `Input.Focus(); Input.SelectAll();` using the
   generated field. While there, add a `private QuickSwitcherViewModel Vm => (QuickSwitcherViewModel)DataContext!;`
   and use it in the four handlers, matching `Sidebar.axaml.cs:12`.
7. `PaneHeader.axaml` — make the breadcrumb itself the rename affordance and rename the *open note*:
   add `RenameCurrentNoteCommand` to `MainViewModel` (rename `Editor.CurrentPath`, reusing `Rename`'s
   save/retarget/refresh sequence), bind the breadcrumb `Button` to it, drop the separate pencil.
   Depends on nothing else; touches two files.
8. Delete `EditorViewModel.NoteName` (3 lines), `Icon.corner-down-left`, and the `MdLink`/`MdWikiLink`/
   `MdQuote` aliases from both theme dictionaries. Build to confirm no reference remains.
9. Delete `SystemDecorations="Full"` from both dialog windows; change `Watermark` to `PlaceholderText`
   in `QuickSwitcher.axaml:29`. Build must report 0 warnings.
10. `MainWindow.axaml.cs` — move the `PropertyChanged` subscription out of `OnDataContextChanged`:
    unsubscribe from the previous VM first, or subscribe once in the constructor via
    `this.GetObservable(DataContextProperty)`.
11. `MainWindow.axaml.cs` — drop the `IsVisible` bindings on the editor/preview (keep `AdjustPanesSplit`),
    or drop `AdjustPanesSplit` and give the columns `Width="*"` with `IsVisible` doing the work. Pick one;
    prefer deleting the code-behind method so ViewMode lives entirely in XAML.
12. `Controls.axaml` — move the `Button.icon:pointerover Path.icon` style *above*
    `Button.icon.active Path.icon`, or narrow it to `Button.icon:not(.active):pointerover Path.icon`.
13. `MainViewModel.cs:65-66` — assign the backing fields directly (`ViewMode` / `ThemeMode` have
    generated partial setters; set `field` via the generated property only after wiring, or add a
    `_loading` guard in `OnViewModeChanged`/`OnThemeModeChanged` mirroring `EditorViewModel._loading`).
14. `QuickSwitcherViewModel.Open()` — drop the explicit `RefreshResults()` when `Query` was already
    empty, or set the field and call `RefreshResults()` once. Change line 105 to
    `StartsWith(queryLower, StringComparison.Ordinal)`.
15. Add `MainWindowTests.Quick_switcher_overlay_opens_and_closes`: show the window, run
    `vm.OpenQuickSwitcherCommand.Execute(null)`, assert the `QuickSwitcher` visual is `IsVisible` and its
    `DataContext` is the `QuickSwitcherViewModel`, then `CloseCommand` and assert hidden.
16. `MainWindowTests.cs:87-102` — wrap the theme loop in `try/finally` and restore
    `ThemeVariant.Default` in the `finally`; assert `File.Exists(filepath)` after each save.
17. Rename the `Icon.sun-moon` key to `Icon.moon` (one definition, one use in `Ribbon.axaml:20`); use
    `Icon.plus` for the sidebar New-note button; point `InlineRenderer.cs:191` at `MdSurfaceCode` and
    delete the `MdCodeBackground` alias.
18. Remove the unused usings in `MainWindow.axaml.cs` and `InlineRenderer.cs`; re-run `index-file.js`
    on `MdNotes/Views/MainWindow.axaml.cs` and `MdNotes/Views/Shell/Sidebar.axaml.cs` to drop the stale
    `MainWindow.OnTreePointerPressed` entry from `STRUCTURE.md`.

## E. Refactored Example

Issue 1, the one defect visible in the shipped screenshots:

```xml
<!-- MdNotes/Views/Shell/Sidebar.axaml -->
<DockPanel DockPanel.Dock="Top" Height="36" Margin="12,0,6,0" LastChildFill="False">
    <TextBlock Text="{Binding VaultName}" FontSize="12" FontWeight="SemiBold"
               Foreground="{DynamicResource MdTextMuted}" VerticalAlignment="Center"
               TextTrimming="CharacterEllipsis" />
    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="4">
        <!-- …unchanged… -->
    </StackPanel>
</DockPanel>
```

Issue 6, the pattern to copy into the other three handlers:

```csharp
private QuickSwitcherViewModel Vm => (QuickSwitcherViewModel)DataContext!;

protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
{
    base.OnPropertyChanged(change);
    if (change.Property == IsVisibleProperty && IsVisible)
        Dispatcher.UIThread.Post(() => { Input.Focus(); Input.SelectAll(); });
}
```

---

Review complete — no source file, `todos.md` or log other than this one was modified.
**The Controller should SYNC.**

### Task 24 · Haiku · ✅ Done
Sidebar header `LastChildFill="False"` + ellipsis on vault name, New-note icon `Icon.plus`; PaneHeader breadcrumb
is now the rename button bound to new `RenameCurrentNoteCommand` (shared `RenameAsync` with `Rename`);
`OpenQuickSwitcher` Guard-wrapped; ctor no longer writes settings back (`_loading`). 54/54, PNGs re-rendered.

### Task 25 · Haiku · ✅ Done
`Button.primary` lives once in `Controls.axaml`; Fluent overrides merged into `Palette.axaml`, `FluentOverrides.axaml`
deleted; `TextControl*Focused` overrides dropped; `:not(.active):pointerover`; dead aliases + `Icon.corner-down-left`
removed; `Icon.sun-moon` → `Icon.moon`; obsolete `SystemDecorations`/`Watermark` gone. `--no-incremental` build:
0 warnings. 54/54.

### Task 26 · Haiku · ✅ Done
Pane split now pure MVVM: `EditorColumnWidth`/`PreviewColumnWidth` (`GridLength`) bound to the ColumnDefinitions,
`AdjustPanesSplit` deleted, VM subscription unsubscribes the previous VM; QuickSwitcher code-behind uses the
generated `Input` field + `Vm`; `Open()` refreshes once, ordinal-ignore-case ranking; render test `try/finally` +
PNG existence asserts; new `Quick_switcher_overlay_opens_and_closes`. 0 warnings, 55/55.

### Task 27 · Opus · ✅ Done
# Reviewer log 2 — re-check of tasks 24/25/26 (Opus)
_2026-09-16 · MdNotes · verifying reviewer_log.md items 1–18_

STATUS: DONE

Verified: `dotnet build -nologo -v q --no-incremental` → **0 errors, 0 warnings**.
`dotnet test -nologo -v q` → **55/55 passed**. Both `.render/*.png` read as images and
pixel-measured.

## A. Verdict

**Not yet — one fix short of ship.** 17 of 18 items are genuinely fixed in the code, the build is
clean, the suite is green, and the two risky refactors hold up: I ran the real shell headless and
confirmed the `GridLength` compiled bindings on `ColumnDefinition` actually resolve at runtime
(Split `1*/1*`, Reading `0/1*`, Edit `1*/0`, sidebar toggle `260 → 0 → 260`), and the `_loading`
guard, the shared `RenameAsync` and the unsubscribe logic are all correct. But **item 1, the one
High defect that reviewer 1 found in the shipped screenshots, is still in the shipped screenshots.**
`LastChildFill="False"` was necessary but not sufficient: the header `DockPanel` still declares the
right-docked `StackPanel` *after* the vault-name `TextBlock`, so the `TextBlock` is measured first
and consumes 163 px of the 242 px header, leaving 79 px for a 92 px button group. Avalonia then
clamps the `Dock="Right"` child to `max(left, right − width)` — it starts at x≈220 instead of 206
and its right edge lands at 312, past the sidebar edge at 304. `TextTrimming` does not help because
trimming never shrinks `DesiredSize`. Measured ink columns are identical in both themes: buttons
centred at 234.5 / 266.5 / ~298, third icon clipped at x=304 with ~3 px of stroke missing and zero
right margin. Fix item 1 properly (one-line reorder, below), re-render, re-measure — then ship.

## B. Item-by-item

1. **Partially / still broken** — `Sidebar.axaml:11` has `LastChildFill="False"` and `:14` has
   `TextTrimming`, but `:16` still declares the right `StackPanel` last → 3rd button clipped at the
   sidebar edge in both PNGs. See C1.
2. **Fixed** — `MainViewModel.cs:243` `OpenQuickSwitcher() => Guard(QuickSwitcher.Open);`
3. **Fixed** — `Controls.axaml:15-24` owns `Button.primary` + `:pointerover`; both
   `Dialogs/*.axaml` have no `<Window.Styles>` block.
4. **Fixed** — `FluentOverrides.axaml` deleted, include gone from `App.axaml:22-28`, 13 Fluent keys
   live in `Palette.axaml:17-30` / `:45-58` with comments. (DRY only partial — see C7.)
5. **Fixed** — `TextControlBorderBrushFocused` / `…ThicknessFocused` gone from both variants;
   dialog `TextBox` gets the Fluent focus ring back, `TextBox.editor` stays borderless via
   `Controls.axaml:83-87`.
6. **Fixed** — `QuickSwitcher.axaml.cs:18` `Vm` property; `:28-29` uses the generated `Input` field,
   no `FindControl`, no null-fallback; all four handlers use `Vm`.
7. **Fixed** — `PaneHeader.axaml:45-57` breadcrumb *is* the button →
   `MainViewModel.cs:165-170 RenameCurrentNote` → `:172-184 RenameAsync(Editor.CurrentPath, …)`.
   Renames the open note, not `SelectedNode`. Separate pencil gone.
8. **Fixed** — `EditorViewModel.NoteName` now has a real consumer (`MainViewModel.cs:169`);
   `Icon.corner-down-left`, `MdLink`, `MdWikiLink`, `MdQuote` all deleted, 0 references remain.
9. **Fixed** — build reports 0 warnings; `SystemDecorations` gone from both dialogs;
   `QuickSwitcher.axaml:29` uses `PlaceholderText`.
10. **Partially** — `MainWindow.axaml.cs:20-31` unsubscribes the previous VM before subscribing.
    But `:34` `Ribbon.SearchButtonClicked += …` is still a lambda added inside `OnDataContextChanged`
    with no unsubscribe — same bug, second event. See C3.
11. **Fixed (runtime-verified)** — `MainViewModel.cs:30-31` + `MainWindow.axaml:42,44`;
    `AdjustPanesSplit` deleted; `IsVisible` bindings removed from editor/preview. One source of truth.
12. **Fixed** — `Controls.axaml:60` `Button.icon:not(.active):pointerover Path.icon`.
13. **Fixed** — `MainViewModel.cs:18,69-78` `_loading` set in ctor `try/finally`;
    `:269-279` both handlers early-return. Guard is correct (ctor-only, no re-entrancy).
14. **Fixed** — `QuickSwitcherViewModel.cs:32-36` refreshes exactly once; `:107,:109` both
    `OrdinalIgnoreCase`.
15. **Fixed** — `MainWindowTests.cs:134-174 Quick_switcher_overlay_opens_and_closes` asserts
    visibility, `DataContext` type and non-empty `Results`, then hidden after `CloseCommand`.
    (Exercises the command, not the `Ctrl+O` gesture — acceptable.)
16. **Fixed** — `MainWindowTests.cs:87-108` `try/finally` restores `ThemeVariant.Default`;
    `:100` asserts `File.Exists`.
17. **Fixed** — `Icons.axaml:9` `Icon.moon` + `Ribbon.axaml:20`; `Sidebar.axaml:20` uses
    `Icon.plus`; `InlineRenderer.cs:190` uses `MdSurfaceCode`, alias deleted.
18. **Partially** — usings removed from `MainWindow.axaml.cs:1-3` and `InlineRenderer.cs`.
    `STRUCTURE.md` is still stale: `:836 MainWindow.OnTreePointerPressed` and
    `:856 MainWindow.AdjustPanesSplit` both index methods that no longer exist.

## C. New issues

**C1 — High. `MdNotes/Views/Shell/Sidebar.axaml:11-34` — item 1 is not actually fixed.**
Measured from `.render/main-window-dark.png` and `-light.png` (identical): vault-name ink ends at
x=219, button icons centre at 234.5 / 266.5 / ~298; the third icon's ink runs 290–303 and is cut by
the sidebar border at x=304, losing ~3 px of stroke and all 6 px of right margin. The group should
sit at 206–298. Cause: with `LastChildFill="False"` the `Dock="Left"` `TextBlock` is still measured
first and `TextTrimming` does not reduce `DesiredSize`, so the `Dock="Right"` panel is starved and
Avalonia clamps its x to the left edge instead of a negative offset.
Fix — swap the header for a Grid (guaranteed, and the intent is clearer):
```xml
<Grid DockPanel.Dock="Top" Height="36" Margin="12,0,6,0" ColumnDefinitions="*,Auto">
    <TextBlock Grid.Column="0" Text="{Binding VaultName}" … TextTrimming="CharacterEllipsis" />
    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4"> <!-- 3 buttons unchanged --> </StackPanel>
</Grid>
```
Or, minimally, move the `<StackPanel DockPanel.Dock="Right">` to be the **first** child of the
existing `DockPanel` and keep `LastChildFill="False"`. Then re-run the render test and re-measure.

**C2 — Medium. `MdNotes/Assets/Icons.axaml:7,11,14` — three icon geometries are malformed.**
A lowercase `m` after a subpath is a *relative* moveto, so the second subpath is drawn displaced
instead of in place, and the inflated bounding box makes `Stretch="Uniform"` squash the glyph:
- `:7 Icon.chevrons-down-up` — `…5 5 m7 4 5 5 5-5` puts the second chevron at (24,24)→(34,24);
  bbox becomes 27×14, so it renders as one `^` plus a stray diagonal (visible in both PNGs).
- `:11 Icon.pencil` — trailing `m15 5 4 4`; the pane-header pencil shows a stray tick (visible).
- `:14 Icon.x` — `M18 6 6 18 m6 6 12 12` draws the second stroke at (12,24)→(24,36).
Pre-existing (task 23), not caused by these fixes. Fix: make the second subpath absolute, e.g.
`M7 20l5-5 5 5 M7 4l5 5 5-5`, `M15 5l4 4`, `M18 6 6 18 M6 6l12 12`.

**C3 — Low. `MdNotes/Views/MainWindow.axaml.cs:34` — the other subscription still leaks.**
`Ribbon.SearchButtonClicked += (…) => …` is added on every `DataContext` change and never removed,
so item 10's fix is half-applied. Move it into the constructor (it only needs `Vm`, which resolves
lazily) or store and detach the handler alongside `OnVmPropertyChanged`.

**C4 — Low. `MdNotes/Views/MainWindow.axaml:48-58` — the editor is still tab-reachable when hidden.**
Removing `IsVisible="{Binding IsEditVisible}"` means that in Reading mode the `TextBox` is a
0-width but visible, enabled, focusable control: `Tab` can land the caret in an invisible editor.
Same for the preview `ScrollViewer` in Edit mode. Add `IsVisible` back purely as a focus/a11y guard,
or set `IsTabStop="{Binding IsEditVisible}"`.

**C5 — Low. `STRUCTURE.md:836, :856` — stale index entries** for `MainWindow.OnTreePointerPressed`
and `MainWindow.AdjustPanesSplit`, both deleted methods. Re-run `index-file.js` on
`MdNotes/Views/MainWindow.axaml.cs`.

**C6 — Low. `MdNotes/ViewModels/MainViewModel.cs:169` — `Editor.NoteName ?? ""` is a silent
fallback at a non-boundary.** `NoteName` is non-null whenever `CurrentPath` is non-null (guarded on
`:168`), and `_vault.Rename` would reject `""` anyway. Use `Editor.NoteName!`.
*(Checked: `VaultService.ReadChildren:103` builds note entries with `GetFileNameWithoutExtension`,
so `node.Name` and `Editor.NoteName` are the same shape — `Rename` and `RenameCurrentNote` prompt
with identical text and `EnsureExtension` re-adds `.md`. Folder renames still take the
`Directory.Exists` branch and `IsSameOrUnder` still retargets a note under a renamed folder. Correct.)*

**C7 — Info. `MdNotes/Themes/Palette.axaml`** — item 4 removed the *cross-file* duplication but
`#267F6DF2` / `#26A78BFA` still appear 4× each and `#10000000` / `#13FFFFFF` 2× each inside the one
file. Acceptable (one file, commented), but `{StaticResource MdAccentSoft}` would finish the job.

## D. Remaining manual verification for the user

Headless capture cannot cover these; check in a real window after C1 is fixed:
1. **Sidebar header with a long vault name** — open a vault whose folder name is ≥30 chars and
   confirm the name ellipsises and all three buttons sit flush against the right edge with the 6 px
   margin intact. This is the C1 regression check.
2. **Hover and active states** — hover an already-active view-mode toggle in the pane header and
   confirm the stroke stays accent-coloured (item 12); hover an inactive one and confirm it goes
   `MdText`. Styles do not participate in a headless frame capture.
3. **Focus rings** — `Ctrl+N` → the dialog `TextBox` should now show a focus border (item 5);
   the main editor must still show none.
4. **`Ctrl+O` / `Ctrl+Shift+O`** — confirm `Ctrl+O` opens the switcher with the input already
   focused and text selected, `Esc`/scrim-click closes it, `↑`/`↓`/`Enter` navigate, and
   `Ctrl+Shift+O` opens the folder picker without also firing the switcher.
5. **`Ctrl+O` with no vault open** — should now show the error dialog rather than crash (item 2).
6. **Breadcrumb rename** — click the note name in the pane header, rename, and confirm the tree,
   the title bar and the breadcrumb all retarget; then select a *folder* in the tree, click the
   breadcrumb, and confirm it still renames the **note** (item 7's whole point).
7. **Settings write-back** — launch twice and confirm `%APPDATA%/MdNotes/settings.json` mtime does
   not change on a launch where nothing was toggled (item 13).
8. **Tab order in Reading mode** — `Ctrl+E` to Reading, press `Tab` repeatedly, confirm focus never
   disappears into the collapsed editor (C4).
9. **Icon shapes** — look at the sidebar collapse-all, the pane-header pencil and the search clear
   `x`; they should be clean Lucide glyphs with no stray strokes (C2).

---

Review complete — no source file, `todos.md` or log other than this one was modified.
**The Controller should SYNC.**

### Task 28 · Haiku · ✅ Done
Sidebar header is a `Grid *,Auto` (no clipping); `Icon.chevrons-down-up`, `Icon.pencil`, `Icon.x` subpaths made
absolute; `Ribbon.SearchButtonClicked` subscribed once in the ctor; editor/preview `IsVisible` restored alongside
the width bindings. `--no-incremental` build 0 warnings, 55/55 tests, PNGs re-rendered.

### Task 29 · Sonnet (controller hotfix) · ✅ Done
Diagnosis (headless, GC-forced, weak references): managed navigation path is clean — replaced preview trees and
tree nodes are collected for plain text, task lists, code, tables and mermaid. Unmanaged growth isolated to
`SvgSource.LoadFromSvg` (Svg.Skia): ~20–25 KB per call, not recovered by GC/finalizers, not fixed by `Dispose`;
Mermaider alone is flat; `TextBox` undo history is bounded (UndoLimit 10). Each navigation rendered every diagram
twice (BaseDirectory + Document rebuilds). Fix: `DiagramCache` (LRU 64, dispose on eviction, failures not cached)
+ coalesced rebuild. 64/64 tests; `NavigationLeakTests` asserts 2 renders for 2 diagrams across 10 visits.

### Task 30 · Sonnet (controller) · ✅ Done
Cause of the scroll spike: each visible diagram was a vector `SKPicture` replayed every frame while scrolling
(text, layers) — Skia's resource cache grows during activity and purges when idle. Fix: `Views/Markdown/DiagramView.cs`
measures to the diagram's natural size capped to the available width, draws a bitmap rasterized at
`Bounds.Width × RenderScaling`, and re-rasterizes once the size has settled (150 ms). `DiagramCache` now holds
pictures (LRU 64) and bitmaps per (source, theme, pixel width) (LRU 32), rasterizing straight into a
`WriteableBitmap` buffer via SkiaSharp. 66/66 tests, 0 warnings, PNGs re-rendered and inspected.

### Task 31 · Sonnet (controller) · ✅ Done
`Assets/mdnotes.ico` (9 sizes, 23 KB) generated by `AppIconGenerator` (skipped unless `MDNOTES_GENERATE_ICON`
points at the Assets folder; `MDNOTES_RENDER_OUT` also gets `icon-256.png` and `icon-16x8.png` for review).
Wired via `<ApplicationIcon>` and `Icon="/Assets/mdnotes.ico"` on the three windows. Build 0 warnings, 66 tests + 1 skipped.

### Task 32 · Opus · ✅ Done
`Services/FolderOrderFile.cs`: `.order` per folder, key = file name + `/` for folders; `Apply` (stable
`OrderBy`, unlisted entries rank `int.MaxValue` so they keep the alphabetical tail), `Save` (prunes stale
names), `Delete`, `Rename`, `Exists`. Duplicate keys in a hand-edited file are tolerated (`TryAdd`).
`VaultService`: `ReadChildren` ends in `FolderOrderFile.Apply`; `Rename` restructured around one
`isFolder` branch and rewrites the parent's key in place; new `MoveTo` (validates, moves cross-folder,
prunes the source order only when it had one, inserts at the clamped index) and `ResetOrder`.

### Task 33 · Opus · ✅ Done
`Models/DropPosition.cs`; `TreeNodeViewModel.IsDropTarget`. `MainViewModel`: `MoveUp`/`MoveDown` (sibling
index ± 1 doubles as the after-removal index, no-op at the ends), `ResetOrder`, `MoveNode`, and
`ResolveDrop` with the same-parent adjustment and the three rejections. `FindNode` made public;
`ParentFolder`/`SiblingsOf` helpers. `RefreshTree` now reselects the selected item first and falls back to
the open note, so a reorder keeps its selection while a delete still behaves as before.

### Task 34 · Opus · ✅ Done
Avalonia 12 replaced `DataObject`/`DoDragDrop` with `IDataTransfer`/`DoDragDropAsync`, so the drag uses
`DataFormat.CreateInProcessFormat<string>` carrying the node path and `e.DataTransfer.TryGetValue`.
`DoDragDropAsync` takes `PointerPressedEventArgs`, so the press args are held and the drag starts from
`PointerMoved` once past a 4 px threshold — a plain click still selects. Rows are hit-tested via
`Border#PART_LayoutRoot` (bounds exclude expanded children). `Sidebar.axaml`: AllowDrop, `Alt+Up`/`Alt+Down`
KeyBindings, three context-menu items, `Border Classes.drop` in the item template, `DropLine` in the tree
panel. `Controls.axaml`: `Border.drop`.

### Task 35 · Opus · ✅ Done
8 new `VaultServiceTests` (order applied incl. note-before-folder, stale/unlisted handling, first `MoveTo`
writes the whole order, **rename keeps position**, cross-folder move + source prune, the two rejections,
`ResetOrder`). New `Fakes.cs` with the shared `FakeSettings`/`FakeDialogs`; `NavigationLeakTests` keeps its
throwing variant, now named `ThrowingDialogs` to say why. New `TreeOrderTests` (5 `[AvaloniaFact]`).
79 tests + 1 skipped, 0 warnings.

### Task 36 · Opus · ✅ Done
`README.md`: feature bullet + an "Ordering the sidebar" section with the file format. `PLAN.md`:
`DropPosition`/`FolderOrderFile` in the layout, two behaviour notes (manual order, drag & drop), and a
2026-09-17 progress entry. Shell PNGs re-rendered and inspected — tree rows unchanged.

**Not covered by the tests, needs a real window:** drag & drop itself (headless cannot synthesise a drag) —
the insertion line, the folder-row highlight, the "no drop" cursor on a rejected target, and `Alt+↑`/`Alt+↓`
reaching the tree's KeyBindings.

### Task 37 · Opus · ✅ Done
Diagnosed with a throwaway headless probe that counted handlers per routing strategy on a real tree row:
`press bubble/tunnel = 0/1`, `move = 1/1`, `up = 1/1`. Only the press was unreachable, and it is the event
that arms the drag — so nothing downstream could ever run. The pre-existing right-click selection kept
working because `TreeViewItem` does not handle the right button the same way, which is why the original
handler looked fine. Fix: register `OnTreePointerPressed` on the tunnel route (XAML attribute removed);
`PointerMoved`/`PointerReleased` stay on bubble, where they do arrive.

Regression test asserts the arming step via a new `internal Sidebar.PendingDragNode` seam; reverting the
fix to `RoutingStrategies.Bubble` turns it red, which was verified. 83 tests + 1 skipped, 0 warnings.

**Still unverified, needs a real window:** whether `DoDragDropAsync` accepts the *stored* `PointerPressedEventArgs`
once the movement threshold is crossed. Headless cannot answer it — `DoDragDropAsync` blocks on a platform
drag loop, which hung the probe run. If dragging still does nothing after this fix, that stored-args pattern
is the next suspect (Avalonia 12 narrowed the parameter type from `PointerEventArgs` to
`PointerPressedEventArgs`, hinting the intended call site is inside `PointerPressed` itself).

### Task 39 · Opus · ✅ Done
Search found every occurrence and then stopped: `SearchService` reported only the *first* match per note,
and acting on the result set meant opening each file by hand. Now `Ctrl+Shift+H` (also the button beside
the sidebar search box, and **Search and replace…** in the tree menu) opens a modal 720px overlay seeded
with the sidebar query and, from a folder, scoped to it.

- **`MatchFinder`** is the one matcher, built once per search so an invalid pattern fails at construction
  rather than per file. Literal matching keeps today's semantics (`DiacriticFolder` fold + ordinal
  ignore-case, and the fold is length-preserving so the offset still addresses the original text);
  `MatchCase` switches to ordinal, accents included. Regex uses `Regex.Result` for `$1` expansion and
  skips zero-width matches, which would otherwise splice forever. Whole word is a char check either side
  for literals and `\b(?:…)\b` for patterns. `Apply` splices ascending and rejects overlaps.
- **`ReplaceService`** is stateless: `Find` scans `EnumerateNotes()` filtered by scope and dot-folder
  (`EnumerateNotes` does not skip those, unlike the tree builder), `Replace` re-reads each note and
  verifies every span still equals what was previewed — a note that moved is skipped *whole* and named in
  the report, never partially rewritten — and the pre-replace text of everything it wrote comes back as
  the undo snapshot, which `Restore` writes back.
- **`SearchReplaceViewModel`** holds the tri-state per-note / per-match selection. Exclusions are stored
  as `(path, offset)` and reapplied after each rescan, so tightening the replacement text does not throw
  away unticked matches. Rescan is debounced 250 ms; pattern errors show under the box, never as a dialog.
- **Open-note reconciliation** was the one real trap: the editor autosaves 750 ms after a keystroke and
  the watcher does not watch content, so a replace on the open note would have been overwritten by the
  stale buffer. `OpenSearchReplaceCommand` flushes the editor before showing the overlay (it is modal, so
  nothing can dirty it again) and `OnNotesRewritten` reloads the note if it was among those changed.

`--no-incremental` build: 0 warnings. 147 tests + 1 skipped (was 108). `.render/search-replace.png`
re-rendered and inspected: chevrons, tri-state boxes, struck-out find text beside the accent-coloured
replacement, counts and the disabled Undo button all correct — two render passes were needed, the first
for a Fluent-filled expander toggle, the second for a `▾` glyph Inter does not carry (now `Icon.chevron-down`).

**Not covered by the tests, needs a real window:** the scrim click, `Escape`, focus landing in the find
box, and the scope `ComboBox` dropdown.

### Task 40 · Opus · ✅ Done
Configurable auto-save. The 750 ms debounce was a constant, so the file on disk always matched the screen
whether or not anything else was watching it — a git-repo vault, a sync folder, a site generator's watcher,
a draft worth abandoning. Now `ISettingsService.AutoSave` picks between `AfterDelay` (with
`AutoSaveDelayMs`, default 750, clamped to 0.1–300 s), `OnFocusChange` and `Manual`.

What kept the diff small: a loose file already *was* manual mode — no timer, no implicit save, prompt
instead — so `IsLooseFile` collapsed into `EditorViewModel.SaveMode`, every `if (!IsLooseFile) Save()`
became `SaveImplicitly()`, and `FlushLooseEditAsync` became `FlushUnsavedAsync` with one widened
condition. `SaveImplicitly` being the single gate also settled the four scattered `Editor.Save()` calls in
`MainViewModel`: rename and move retarget the editor so a dirty buffer survives them, while opening
another vault or the replace overlay would lose it and therefore ask first (`OpenSearchReplace` became
`GuardAsync` for that). The setting is read at the point of use, so a change applies from the next
keystroke without any event plumbing; only the status bar needed a nudge, and it now shows
`EditorViewModel.SaveStatus` rather than claiming "Editing…" when nothing will be written.

First settings UI in the app: `SettingsPanel` follows the existing modal-overlay pattern (scrim + 480px
card, Escape and scrim click close), reached from a new ribbon button or `Ctrl+,`. Three radio buttons
with a line each saying what the mode does, plus the delay in seconds (ms is what is stored).
`MainWindow.Deactivated` flushes through the gate, so "when I leave the note" includes leaving the app.

**Not covered by the tests, needs a real window:** the scrim click, `Escape`, the `NumericUpDown` spinner,
and whether `Deactivated` fires on the modal `InputDialog`/`MessageDialog` (harmless either way — it
writes what is on screen, and does nothing in manual mode).

### Task 43 · Opus · ✅ Done
Architecture review F9. The half of drag-and-drop that decides *what the user meant* by a pointer position
was in `Sidebar.axaml.cs` as bare thresholds - a folder row took a drop into it between 25% and 75% of its
height, every row split before/after at 50%. The index maths behind it was already in the view model and
well tested; this half sat where only a pointer-driven UI test could reach it.

`VaultWorkspaceViewModel.PositionIn(over, fraction)` now owns it, directly above `ResolveDrop`, with the
band edge as a named `IntoBandEdge` constant carrying its reasoning. `Sidebar.HitTest` measures the fraction
down the row and passes it on - the one thing the view genuinely knows. The rule needs neither a vault nor a
window now, so `TreeOrderTests.PositionIn_reads_the_pointer_fraction_down_a_row` is a plain `[Fact]` over two
hand-made rows, covering the band's open edges (0.25 and 0.75 order rather than nest) and a note's plain
midpoint split.

`Sidebar.RowUnder` became `internal`. `PART_LayoutRoot` is an unversioned internal of Fluent's `TreeViewItem`
template that nothing checks at compile time; a rename would leave every drop landing at the vault root with
nothing in the build log. The review called the lookup a known Avalonia constraint rather than a design
error, so it stays and gains a comment saying so plus
`SidebarDragTests.The_row_under_the_pointer_resolves_through_the_fluent_template`, which drives the real
lookup and asserts it finds the hovered row's own node. Renaming the constant was tried: the test fails.

Already gone before this task, from the F6 tree-reconcile work rather than F9: the `node.IsDropTarget`
write-back, replaced by the `drop` class put straight on the row's `Border`.

**Left alone deliberately, and why:** the drag payload stays a `FullPath` string re-resolved through
`FindNode` - the in-process `DataFormat` is still the platform's channel and a path is what survives it -
and `Vm.SelectedNode` / `Vm.SearchQuery` stay direct writes, which is what a code-behind is for.

Docs: `PLAN.md` behaviour note rewritten (it also still said `MainViewModel.ResolveDrop`) plus a 2026-09-19
progress entry; `STRUCTURE.md` entries for `Sidebar`, `RowUnder`, the new `HitTest` and `PositionIn`.
378 passing + 1 skipped, 0 warnings - the same two `VaultServiceTests` cases still fail on Linux for the
same unrelated reasons.

### Task 44 · Opus · ✅ Done
F7. `Services/NameMatch.cs` is now the one place that decides what a typed name names: a `NameRank`
ladder where every unfolded tier beats its folded twin, `Rank` for the quick switcher's ordering and
`Best` for the two vault lookups, which are equality only - a link names a note rather than hunting
for one that starts with what was typed. `Best` cannot be a `FirstOrDefault` because an exact match
at the end of the list has to beat a folded one at the front.

`FindNoteByName` and `FindFolderByName` are one line each now; the folder lookup keeps the half that
is genuinely its own, which is whether a bare name means a folder name anywhere or a name with
separators means a vault-relative path.

**Deliberate behaviour change:** the switcher folded both sides before ranking, so `Resume` and
`Résumé` tied and the alphabetical tie-break decided while a wikilink to either resolved correctly.
That is the disagreement F7 names, so it is fixed rather than preserved, and
`An_exact_name_outranks_one_that_only_matches_folded` says so. The existing accent cases in
`QuickSwitcherViewModelTests` and `VaultServiceTests` are untouched and still pass, which is the
proof the rest of the extraction was behaviour-preserving.

**Left alone, as the review asks:** `SearchService`, `MatchFinder` and the switcher's *content*
matching stay three distinct matchers - they produce different outputs, and `MatchFinder` being the
one matcher behind both the replace preview and the write is a correctness invariant.

### Task 45 · Opus · ✅ Done
F8. The renderer is a service now: tenth member of `AppServices`, built in `AppSession`, exposed as
`MainViewModel.Diagrams` and bound onto the preview in `MainWindow.axaml`. `App.axaml.cs` no longer
writes anything into the view layer, and the control no longer has a hidden initialisation-order
requirement on `App`.

The caches became instance fields. One `MarkdownView` per window, living as long as the window, is
a real owner where a static was none; the price is that a second window renders the same diagram
again, which `Every_window_renders_diagrams_through_the_injected_renderer` asserts rather than
hides. Ownership needed a release path: `Lru` gained `Dispose` (it only disposed on eviction, so a
cache dropped whole leaked all of it), and `DiagramCache`, `ImageCache` and `MarkdownView` are
`IDisposable` above it. The window disposes the preview in `OnClosing` beside `Vm.Dispose()`, not in
`OnDetachedFromVisualTree` - a control can detach and reattach, and closing is the event that means
done. Replacing the renderer disposes the cache built on the previous one.

Eleven of the twelve `MarkdownView.DiagramRenderer = …` lines in the suite simply went, because
`TestServices` supplies it; `DiagramPaletteTests` swapped its save-and-restore `try/finally` for
`services with { Diagrams = … }`, which removes the ordering hazard the review named.

**Not closed:** F11. `DiagramView` still holds a cache-owned `Bitmap` across frames while the `Lru`
disposes on eviction with no in-use tracking; `Lru.Dispose` is about a cache dropped whole and does
not make that ownership single-valued.

398 passing + 1 skipped (was 378), 0 warnings, the same two `VaultServiceTests` Linux failures.
**Not verified in the app:** display `:2` was not up, so the switcher ordering on a real vault and a
diagram surviving a theme switch in a second window are still owed.

### Task 46 · Opus · ✅ Done
F11, and it was real rather than theoretical. The review filed it at Medium confidence because reaching
32 live `(source, palette, pixelWidth)` keys needs an unusual document; setting the bound to one settles
the question. Two visible diagrams, the second's rasterization evicts the first, and the next frame
throws `ObjectDisposedException: Ref<Avalonia.Platform.IBitmapImpl>` out of `DiagramView.Render`. That
test was written before the fix and fails without it.

The fix is the first shape the review offered: the cache owns bitmaps outright, the view borrows one for
the length of a single draw. `DiagramView` keeps `_pixelWidth` - the cache *key* - and nothing else.
A key owns nothing, so it cannot go stale the way a reference could. Borrowing for one call is enough
rather than merely shorter because Avalonia takes its own reference on the draw it records, so the
bitmap outliving `Render` is the compositor's business.

None of the performance work the cache exists for changes: `Rasterize` still runs off the 150 ms settle
timer rather than per resize frame, and still warms the cache so the cost stays off the draw path. A
frame after an eviction pays one rasterization instead of drawing a disposed bitmap.

**Found while fixing it, not fixed:** `ImageCache` has the same ownership split -
`InlineRenderer.AddImage` puts a cache-owned `Bitmap` on an `Image.Source`, which must hold it across
frames, so borrow-per-draw is not available. Reproduced with `new ImageCache(capacity: 1)`, two images
and a re-measure: `ObjectDisposedException` out of `Image.MeasureOverride`. The fix needs a different
bound - liveness-tracked entries, or a generational cache cleared one document behind - which is a
design decision rather than a translation of this one.

399 passing + 1 skipped, 0 warnings, the same two `VaultServiceTests` Linux failures.

**Verified in the app on `:2`** (input driven over XTEST; no `xdotool` on this machine), clearing what
tasks 44 and 45 left owed: `Ctrl+O` then `Resume` puts `Resume` first and `Résumé` second; the theme
button repaints both diagrams in the dark palette; a second window from the single-instance hand-off
renders its own diagrams; resizing re-rasterizes and keeps drawing, with an empty app log. Not covered:
typing `Résumé` into the switcher, because that keysym is not on the `:2` layout - the mirror direction
is asserted headlessly instead.

### Task 49 · Opus · ✅ Done
The other half of F11, left there deliberately (`docs/architecture-review.html#f10`). Reproduced first, end
to end rather than against a shrunken bound: 30 images in one note, `ObjectDisposedException:`
`Ref<Avalonia.Platform.IBitmapImpl>` out of `Image.MeasureOverride` on the first layout pass, because the
count bound was 24. `ImageRenderTests.A_note_with_more_images_than_a_count_bound_still_renders` is that
repro, written failing.

`DiagramView`'s fix was not available here. An `Image` holds its `Source` for as long as it is on screen,
so it cannot borrow a bitmap for the length of one draw - which is what made borrow-per-draw work for
diagrams. The bound had to change in kind rather than in size.

`ImageCache` is now bounded by build instead of by count, and no longer uses `Lru` (`DiagramCache` still
does). Two dictionaries: `_current` is what this build has asked for, `_retired` what the last one left.
`BeginDocument`, called at the top of `MarkdownView.Rebuild`, disposes `_retired`, moves `_current` into
it and starts a new one; `Get` carries an entry over from `_retired` rather than decoding it again. So a
bitmap is disposed only after two builds in a row have gone without it, and by then the tree that used it
was replaced and so was its replacement - nothing on screen can still hold it. Nothing the current build
asked for is ever disposed, which is the property the count bound could not have.

The size that follows is one document's images plus one document of lag. That is not unbounded caching:
every image in the document is on screen at once, held by an `Image` control, so the visual tree already
costs that much. The cache adds the lag, not the document.

`capacity` is gone from the constructor, so
`ImageCacheTests.Evicts_and_disposes_the_least_recently_used_bitmap_when_full` went with it - it asserted
the behaviour that was the bug. Three replace it: `No_image_the_current_build_asked_for_is_disposed` (40
images, one build, none disposed), `An_image_the_next_build_asks_for_again_is_carried_over` (the point of
the cache) and `An_image_two_builds_have_gone_without_is_disposed` (still disposed after one build, freed
after the second - the one-build lag asserted directly).

404 passing + 1 skipped, 0 warnings, on Windows. The 399 recorded under task 46 was a Linux run with two
`VaultServiceTests` failures that do not occur here; the baseline on this machine was 401.

**Not verified in a running app.** The repro and the fix are both headless; a note with 30 images in the
real window is worth one look.

### Task 48 · Opus · ✅ Done
Architecture review F10 (`docs/architecture-review.html#f12`): two error-to-dialog funnels, one claiming
to be the only one. Kept both, as the review asked - a last-resort handler is right for a desktop app -
and gave them different jobs and different words.

`DialogGuard`'s comment no longer says "the single place". It now says what the guard is for (a failure a
command could see coming, shown in the words it was thrown with, because whoever threw it wrote them for
the user), names the crash barrier as the other path, and records the diagnostic that follows: a message
that arrives looking like the barrier's means a guard is missing, not that the failure was unusual.

`App.StartDesktop` keeps `args.Handled = true` - a notes app that exits takes the buffer someone was
typing into with it - but no longer shows `ex.Message` raw, which was what made an unguarded path
indistinguishable from a handled one. New `App.CrashMessage` says the failure was not expected, that the
app may no longer be sound, and names the exception type. That is the "presents itself as one" half of
the review's suggestion; the comment above the subscription is the other.

No new file and no refactor: the guard and the barrier were already in the right places, they were only
saying the wrong things about each other.

New `MdNotes.Tests/ErrorFunnelTests.cs`, three tests: a guarded failure reaches the dialog verbatim, the
crash message names the failure unexpected and gives the exception type, and - the one that pins the
split - a guarded failure does not arrive wearing the barrier's words.

407 passing + 1 skipped, 0 warnings.

### Task 47 · Opus · ✅ Done
The F2 remainder, done as the content cache rather than as off-thread I/O (user's call between the two
shapes offered).

New `MdNotes/Services/NoteContentCache.cs`. Keyed by path plus the file's write stamp - the freshness
test `ImageCache` already uses in this repo - so an edit from outside the app misses and is re-read.
Bounded by total characters rather than entry count, because notes differ in size by orders of magnitude
and memory is what the bound is for; a note larger than the whole budget is kept rather than evicting
itself, since the caller is about to use it. Eviction here is forgetting: strings are managed, so unlike
the two bitmap caches there is nothing to dispose and nothing that can be disposed mid-use. No lock -
every caller is on the UI thread, and the watcher invalidates by changing the file, which the stamp
already catches.

`VaultService.Read` goes through it, so all five callers (`SearchService`, `ReplaceService` twice,
`NoteTemplateService`, `EditorViewModel`) benefit from one change - the point of the vault being the
read boundary. `Write` and `CreateNote` put their own text in directly rather than leaving it to a stamp
comparison: a save landing in the same tick at the same length would not look like a change, and the
editor reading back the version before its own save is the one case where a stale tick costs an edit.
`Open` clears, since another vault shares no path with this one.

**Measured** over 2000 notes (~5 MB of text), 5 searches each:
- uncached 267 ms per search → cached **91 ms**, so ~2.9x.
- Of the 91 ms that remain, **61 ms is the `FileInfo` stat alone** - 2000 syscalls to ask whether
  anything changed. Only ~30 ms is the fold and the match over 5 MB.

So the freshness check is now the dominant cost, and it can be removed: the main watcher could carry
`NotifyFilters.LastWrite` into a handler that drops one content entry without touching the tree (which
is what task 42 avoided LastWrite for), with the watcher's `Error` event clearing the cache on buffer
overflow. That trades a syscall for trusting the watcher, so it is a correctness decision rather than a
tuning one, and it is left open deliberately.

Tests: new `NoteContentCacheTests` (second read is the same instance - `ReadAllText` allocates, so that
alone proves the disk was not touched; external edit re-read; stored text served where the stamp could
not tell; the character budget evicting least-recently-read; an oversized note still returned; a missing
file failing as before). Plus `SearchServiceTests` - searching twice does not re-read, and a note edited
outside is searched as it now reads - and `VaultServiceTests` - read-after-write with an
indistinguishable stamp, and opening another vault forgetting this one's text.

417 passing + 1 skipped, 0 warnings.

**Not verified in a running app**, and worth one look: type into the sidebar search over a real vault.

### Task 50 · Opus · ✅ Done
The design question this feature turns on is whether the declared title also resolves `[[wikilinks]]`.
It does not, deliberately. A file name is unique in its folder and cannot change without a rename; a
title is neither - two notes may declare the same one, and editing a heading would silently break every
link into that note. So identity stays the file name and the title is display only, which is also how
the tree already treats `FullPath`.

The second question was where titles get read. Resolving them per surface would have put a vault-wide
read behind the quick switcher's every keystroke - the F2 shape again, in the one place F2 named. So the
walk resolves them once and the snapshot carries them: `VaultWalk.Of` takes a `readNote` delegate (null =
do not open files), `VaultSnapshot` gained `Titles` and a path->title map, and the snapshot's reuse test
gained the setting so flipping it rebuilds. Reads go through `NoteContentCache` from task 47, so the
pass is the same one search already pays for.

Surfaces, all through `VaultService.DisplayName` so they cannot disagree: sidebar rows
(`TreeNodeViewModel.Display`, observable and re-assigned in `Reconcile` because a row survives a refresh
by design), the quick switcher, search results, the window title and the pane header. The switcher ranks
the title *and* the file name and takes the better rank, so turning this on never puts a note further
away than it was.

`Write` invalidates only when the title actually changed - autosave runs on a timer while someone types,
and a tree rebuild is a walk of the vault. Reading the old title is guarded on `File.Exists`, because
`Write` creates and asking a missing file for its title would have turned a working call into a throw.
A note that cannot be read (locked, half-written, gone since the listing) falls back to its file name
rather than failing the build.

`SettingsViewModel` gained the vault, because the toggle changes what the vault shows while nothing on
disk changes. `VaultService.Refresh` is the honest name for what `RefreshExclusions` already did;
the old name now delegates to it and keeps its meaning for the exclusion service.

Off by default: turning it on means the tree build reads every note, which on a very large vault is a
slower first tree. Said plainly in the panel and in the README.

449 passing + 1 skipped, 0 warnings.

**Not verified in a running app.** Worth one look: a vault where some notes declare titles and some do
not, the sidebar and the switcher side by side, and a heading edited while the note is open.

### Task 51 · Opus · ✅ Done
Two things about this one were not what I expected going in.

**The scroll half needed no code.** The request names scroll and cursor, and I assumed the scroll offset
would have to be captured and reapplied through the TextBox's inner `ScrollViewer`. It does not: Avalonia
scrolls to the caret when `CaretIndex` is set. Probed in a real window first - offset 0 -> 9758 of a
10400 extent - and the probe became `NoteReturnScrollTests` rather than being thrown away, because the
feature now leans on Avalonia behaviour that no view-model test would notice breaking.

**`Dictionary` does not keep insertion order across a removal.** The first `NotePositions` used
remove-then-add to move a note to the newest end, and `Recording_a_note_again_makes_it_the_newest` caught
it: the freed slot is reused, so the re-recorded note kept its old place and was evicted first. Replaced
with an explicit clock, the same shape `Lru` and `NoteContentCache` already use here. The original
comment claiming insertion order was wrong and went with it.

Ordering inside `SetContent` is the other thing that had to be right: the caret is applied **after**
`Text`, or the binding clamps it against the note being replaced - 400 in a 500-char note becomes 4 on
the way through a 4-char one. `A_position_survives_a_visit_to_a_much_shorter_note` is that case.

Writes happen when the editor leaves a note, not when the caret moves, which is why the settings
contract is `NotePositions` + `SaveNotePositions()` rather than a property setter - the write-through
setters the other settings use would rewrite the file on every keystroke.
`Moving_the_caret_does_not_write_the_settings_file` pins that.

Bounded at 200 notes, least-recently-used, because this outlives the session and a vault visited for
years would otherwise grow an entry per note in `settings.json` forever. A deleted note falls out on its
own: the key stops being asked for and the bound forgets it.

464 passing + 1 skipped, 0 warnings.

**Not verified in a running app.** The scroll assertion is headless; worth one look that landing
mid-note looks right rather than merely measuring right, and that reopening MdNotes restores the last
note's position.

### Task 52 · Opus · ✅ Done
The baseline the measure-first tasks were waiting on. Every number is in
`docs/performance-review.html#baseline`; what follows is what the numbers changed.

**Two findings are now decided, and not the way the review guessed.**

*Finding 12 is not about Mermaid.* The review says to time a real render before adding asynchrony.
Rendering the 13-node flowchart is 149 µs and inlining it 244 µs — but `SvgSource.LoadFromSvg`, the
step after them in `DiagramCache.GetPicture`, is **3,591 µs**. The synchronous cost of a keystroke
inside a fence is 4.4 ms per changed diagram and 81% of it is the SVG load. Task 59 as written moves
`RenderSvg` first and `LoadFromSvg` as an afterthought; it is the other way round. The Mermaid render
and the CSS inlining together are 0.85 ms of the 4.4 and the SVG load is the remaining 3.6.

*Finding 5 has no lag to fix.* One keystroke in the quick switcher over 5,000 notes is **428 µs** at
its worst, and `NameMatch.Rank` — the part the finding dwells on — is 25 ns a call, so all 10,000
calls together are 0.25 ms. The review's gate was "confirm switcher lag on a 5,000-note vault"; there
is none. What is real is 664 KB allocated per keystroke. Task 61 is an allocation task if it is
anything, not a latency one.

**Finding 7 is as bad as the review says, with a number.** One rebuild of a 104 KB note is
**56.0 ms and 18.9 MB**, of which the Markdig parse is 651 µs — one percent. That runs every 250 ms
while typing, in Edit-only mode into a preview nobody is looking at.

**Finding 3 in one number: 29.2 MB allocated per keystroke** on a warm 5,000-note vault, 536 MB once
the vault is over the cache budget. A warm scan does no disk I/O, so that is almost entirely the
folded copy of every note plus the `Lines` index.

**Finding 6 is cheap in time and expensive in garbage.** `FindNode` is 129 µs worst case on 5,000
notes but allocates 304 KB doing it, twice per pointer move. Latency is not the argument for task 62.

**Finding 10's quadratic, measured:** inlining allocates **5.3 MB to produce a 15,418-character SVG**, and
1.9× the `var()` count costs 9× the allocation.

**What I did not measure, and would not claim.** Finding 8 wanted the share of rebuild time spent in
Avalonia resource lookup. I have the 56 ms total but not the split — that needs a CPU call tree from
Visual Studio or `dotnet-trace`, which is a person driving a running app. **Task 58 is still gated.**
Two more are open the same way: private bytes over a minute of editing in a Mermaid fence (finding
12's unmanaged half), and a vault on OneDrive rather than a local SSD — every scan number here is a
floor. All three are listed in the review under "Still to measure".

**Tooling.** `MdNotes.Benchmarks` gained six benchmark classes: `FoldBenchmarks`,
`NameMatchBenchmarks`, `SwitcherBenchmarks`, `DragBenchmarks`, `DiagramBenchmarks` and
`RebuildBenchmarks`. The last one runs a real `MarkdownView` against a headless Avalonia platform
inside the BenchmarkDotNet process, which works: `AppBuilder.Configure<App>().UseHeadless(…)
.SetupWithoutStarting()` in `[GlobalSetup]`, then `Dispatcher.UIThread.RunJobs(Render)` to make the
posted rebuild happen. Two documents alternate so each iteration is exactly one rebuild.

`ScanBenchmark` was extended rather than rebuilt, with the GC counters the review wanted from
`dotnet-counters`. Measuring them beat watching them: nine debounced scans with `GC.Collect` fences
around them gives a reproducible number, where a counters session gives one reading of one person's
typing.

Two visibility changes in product code, no behaviour: `QuickSwitcherViewModel.RefreshResults` is
`internal` so a benchmark can time one keystroke instead of two, and `VaultShape` moved out of
`VaultGenerator.cs` into its own file, linked into `MdNotes.Benchmarks` as source so the generated
note shape has one definition rather than two. `VaultShape.Note(index, kb, random)` is the new public
seam.

**Not run against a real vault.** `MDNOTES_BENCH_VAULT=<path>` still points `ScanBenchmark` at one,
and the OneDrive numbers are the ones that would actually settle finding 1.

469 passing + 3 skipped, 0 warnings across the solution. **One flake seen once and not since:**
`ExcludeTests.A_note_in_a_newly_excluded_folder_stays_open_and_loses_its_row` failed a full run
with the Archive row still in the tree, then passed four full runs and three targeted ones. It
waits on `SettleAsync` for the async tree walk from `89d3067`, so a race there is the obvious
suspect. Not chased down: it is not this task, and one observation is not a reproduction.

### Task 53 · Opus · ✅ Done
`VaultService.GetNamedNotes()` hands back every note with its display name from one snapshot, and
`SearchService.Search` iterates that instead of `GetNotes()` plus `DisplayName(path)` per note. Not an
iterator method on purpose: the snapshot (and `RequireRoot`'s throw) happen at the call, not on the
first `MoveNext`. `DisplayName` and the new method share one private `NameIn(snapshot, path)`, so the
title-or-file-name fallback still has one definition. Thread-safety comes from the snapshot being
immutable once published - the arrays and the `Displays` dictionary are never written after `VaultWalk.Of`.

The test counts ignore-file checks without a product seam: `CurrentIgnore` reads
`settings.UseGitIgnore` exactly once per stat, so `FakeSettings.GitIgnoreReads` is that counter.
`One_search_checks_the_exclusion_list_once_not_once_per_note` asserts 1; the old path was 1 + one per note.

### Task 54 · Opus · ✅ Done
`NoteContentCache.ReadFolded` keeps the fold on the cache entry, lazily: made on the first request after
a read, dropped when `Store` or an outside edit replaces the entry. The fold runs outside `_gate` like the
disk read, and is attached only if the entry still holds the very text it was folded from. It counts
against the budget when it is a copy; an all-ASCII note's fold is the text itself and costs nothing.
`Put`'s eviction loop moved into `Trim()` so attaching a fold can evict too.

**The trade-off, stated plainly:** a vault where every note has an accent now holds roughly twice its
text, so the 32M-char budget covers about half as many such notes (~6,000 at 2.7 KB rather than ~12,000)
before a scan degrades to re-reading. The alternative was not counting the fold, which breaks the
budget's one promise - that it bounds memory. Task 65 and the 20,000-note column are where this bites.

Follow-up 2026-10-03: default budget raised to 48M chars (~18,000 plain / ~9,000 all-accented notes).

`MatchFinder.Find(text, folded?)` takes the cached fold (replace passes it), and `Lines` is built on the
first match rather than up front, for both literal and regex. `LineIndexesBuilt` exposes the count.

Measured on Linux (this machine), `ScanBenchmark` with `MDNOTES_VAULT_NOTES=5000`, medians, before → after:
search warm 79.4 → 14.4 ms, replace find warm 33.8 → 14.9 ms, allocation while typing
**29.9 → 2.0 MB per keystroke**, Gen0/1/2 over nine keystrokes 29/29/2 → 3/2/2. Cold scans and tree
walks unchanged, as expected.

474 passing + 3 skipped; the 2 failures are the known Linux-only `VaultServiceTests` path-rule cases.

### Task 65 · Opus · ✅ Done
`NoteContentCache.HasOverrun` (set when `Trim` evicts, cleared by `Clear`) replaces the suggested
`HeldChars`/`BudgetChars` comparison, which can never be exactly at budget because `Trim` keeps it under.
`VaultService.TreeWalkIsCheap => !UseNoteTitles || !HasOverrun`. `RefreshTree()` became
`RefreshTreeAfterEditAsync()`: inline and already complete while cheap, `RefreshTreeAsync()` otherwise. All
nine callers await it before selecting. `OpenWikiLink`, `ResetOrder`, `MoveUp/Down`, `MoveNode` are now
Task-returning (the generated commands become async ones; Sidebar discards `MoveNode`'s task).

Tests: `PostEditWalkTests` (cheap until titles overrun the cache; titles off stays cheap; over budget a move
does not complete synchronously and still selects the moved row; under budget it completes before returning)
and `Evicting_marks_the_cache_as_overrun_until_it_is_cleared`. `TempVault(contentBudgetChars)` and an internal
`VaultService(settings, contentBudgetChars)` constructor make a tiny budget possible.

Known gap closed 2026-10-03: `RefreshTreeAfterEditAsync` also goes async while `TreeReady` is incomplete
(`During_the_first_walk_a_new_note_walks_off_the_UI_thread`).

479 passing + 3 skipped, twice; the 2 failures are the known Linux path-rule cases.

### Task 55 · Opus · ✅ Done
(a) `WordRegex.Count(Text)`. (b) `SvgCssInliner.Inline` single `StringBuilder` pass, input returned as-is when
nothing inlines: 13-node SVG 5,267 KB -> 195 KB allocated, 244 -> 43 us (BenchmarkDotNet short job, Linux).
(c) `PathScope.IsSameOrUnder` allocation-free. (d) `SearchReplaceViewModel.Close` clears `_excluded`.

Tests: `EditorWordCountTests`, `PathScopeTests` (new), two in `SvgCssInlinerTests`, one in
`SearchReplaceViewModelTests`. 488 passing + 3 skipped; 2 known Linux failures.

### Task 56 · Opus · ✅ Done
`ImageCache.DecodeWidth` (set per build from `TopLevel.RenderScaling`, 800 x clamp(scaling, 1, 3)); header
read with `SKBitmap.DecodeBounds` so small images are not enlarged; `Bitmap.DecodeToWidth` otherwise.
`ImageCache.DisplayWidth` is now the renderer's `MaxWidth` too. Key and lifetime untouched.
12 MP photo: ~48 MB -> 1.9 MB (1x) / 7.7 MB (2x). Five new tests in `ImageCacheTests`. 495 passing + 3 skipped.

### Task 57 · Opus · ✅ Done
`MarkdownView` skips `QueueRebuild` while `!IsVisible`, marks `_stale`, and rebuilds once when `IsVisible` turns
true with something owed. Own `IsVisible` because Avalonia's `IsEffectivelyVisibleChanged` is internal;
`MainWindow.axaml` binds the view's `IsVisible` to `IsPreviewVisible`. `Rebuilds` counter is internal for tests.
Parse skip not done: 1% of the cost per the baseline.

Tests: `HiddenPreviewTests` (hidden builds nothing, shows the latest text with exactly one rebuild; shown again
with nothing owed does not rebuild) and a `MainWindowTests` case through the real window, mutation-checked.

### Task 60 · Opus · ✅ Done
(a) `Results` / `SearchResults` are `IReadOnlyList` observable properties set once per scan;
`NoteResultViewModel.Matches` is a fixed list. Overlay virtualised (template ScrollViewer + VirtualizingStackPanel;
`ItemsPresenter ItemsPanel="{TemplateBinding ItemsPanel}"` is required, or it falls back to StackPanel).
300 notes -> 6 realised containers. Not capped: Replace all acts on the full list.
(b) `NoteResultViewModel.SelectedCount` + `(note, previous)` callback; overlay keeps running totals and resyncs
only the clicked note into `_excluded`.

Tests: one notification per rescan (overlay and sidebar), running totals vs recount, and a window test for
virtualisation including scrolling to the last note. 502 passing + 3 skipped.

### Task 61 · Opus · ✅ Done
Rows flattened once per `GetTree()` instance with pre-folded names; query folded once (new
`NameMatch.Rank(candidate, foldedCandidate, query, foldedQuery)`); bounded top-50 heap keyed (rank, name with
`Comparer<string>.Default`, tree index) so the order is exactly the old stable sort's. `Enum.CompareTo` boxing
was 350 KB/keystroke by itself - compare ranks as ints.
SwitcherBenchmarks @5,000: broad 394 us / 8.8 KB (was 428 us / 664 KB), narrow 34 us / 560 B, empty 1.3 us / 3.9 KB.
Tests: row reuse until vault change; top-50 equals a full sort across all rank tiers. 504 passing + 3 skipped.

### Task 62 · Opus · ✅ Done
`TreeNodeViewModel.Parent` (fixed per row) drives siblings, parent folder and SelectPath's ancestor expansion.
`FindNode` is a path index (`OrdinalIgnoreCase`, first in drawing order wins) rebuilt per reconcile, cleared on
vault switch. `TreeNodeViewModel.Walk` is an explicit-stack pre-order walk; `SelfAndDescendants` uses it.
Not done: caching the dragged node (O(1) lookup makes it pointless and a cached row can go stale) and
`RowUnder`'s ancestor LINQ (a few visuals). DragBenchmarks @5,000: lookup 38 ns / 0 B vs walk 88 us / 107 KB;
index rebuild 0.27 ms per reconcile. Tests: parent links incl. rows added later, walk order, index finds a new
row in any case. 507 passing + 3 skipped.

### Task 63 · Opus · ✅ Done
(a) `BurstGate`: first signal of a burst posts the timer restart; the rest record a timestamp. The tick settles
only after 300 ms of quiet, else re-arms, so it is still one refresh after the burst (a bare armed-flag would
have refreshed mid-burst every 300 ms). (b) `SingleInstance` holds `Global\MdNotes-<user>` while listening;
`TryHandOff` returns false at once when `Mutex.TryOpenExisting` finds none, else falls through to the old
connect. Cold start 300 ms -> <150 ms (mutation-checked). Windows untested: queued in docs/questions-queue.md.
Tests: `BurstGateTests` (4), `SingleInstanceTests` (3). 514 passing + 3 skipped.
Corrected during task 64: re-arming for a full interval delayed a two-signal burst to ~600 ms and made
`ExcludeTests` (500 ms waits) fail intermittently; `TrySettle(out remaining)` now waits only the rest.

### Task 64 · Opus · ✅ Done
(a) `JsonSettingsService` uses a nested source-generated `JsonSerializerContext` (default options, same file
format). Fresh-process first read 42.7 -> 32.0 ms; steady write 34 -> 26 us. Internal `JsonSettingsService(file)`
for tests; `JsonSettingsServiceTests` pins old-format read and written names. No write-behind (crash risk).
(b) `VaultWalk.Entries`: one `FileSystemEnumerable` pass per folder (same path strings, Win32 `*.md` match,
platform casing, nothing hidden skipped, stable sort) noting `.order` on the way; `Apply` only when present.
Cold walk titles off 9.8 -> 7.6 ms @5,000 (Linux). The `with` re-creation in `Children` is kept.
Also fixed the task-63 gate (see there). Full suite green apart from the 2 known Linux failures.

### Task 59 · Opus · ✅ Done
Measured 8.7 ms (Linux) / 4.4 ms (Windows baseline) per changed diagram, mostly `SvgSource.LoadFromSvg`, so it
moved. `DiagramSlots` (per-position memory of last build), `DiagramCache.LoadInBackground` (thread pool, lands on UI
thread, `landed()` decides keep vs dispose), `HasPicture`/`FailureOf`, `Lru.Contains`. Edited diagram = previous
picture shown until the new one lands and the view rebuilds; first showing stays synchronous; typed-past renders
disposed; background failures remembered (cap 64) so a landing cannot loop. One process-wide render lock keeps
Mermaider/Svg.Skia single-threaded as before. `DiagramView.Source` and `MarkdownView.DiagramCacheInUse` are
internal for tests. `DiagramBackgroundRenderTests` (3). 519 passing + 3 skipped.

### Task 58 · Opus · ✅ Done
Gate settled by A/B instead of a call tree: `PreviewResources` (6 brushes + 3 icons, `UnsetValue` when missing)
resolved once per `Rebuild`, passed as `RenderContext.Look`; all 19 `DynamicResourceExtension` uses are direct
assignments. RebuildBenchmarks, same machine: 100 KB 58.4 -> 48.3 ms, 18.9 -> 16.3 MB; 10 KB 4.98 -> 4.15 ms.
Theme switch still repaints (new `PreviewResourcesTests`); light/dark renders checked by eye.
520 passing + 3 skipped; 2 known Linux failures.
