# Open questions — performance review follow-up

Decisions taken without you while closing `docs/performance-review.html` (2026-10-03). Each one was
resolved so the work could continue; the question is whether you agree. Nothing here blocks anything.

| # | Task | Question | What I did meanwhile |
|---|------|----------|----------------------|
| 1 | 54 | The cached accent-folded copy counts against the 32M-char cache budget, so an all-accented vault fits ~6,000 notes instead of ~12,000 before scans re-read from disk. Raise the budget (e.g. 48M) instead? | Kept 32M and counted the fold, so the budget still bounds memory. **Answered: yes — raised to 48M.** |
| 2 | 65 | Creating/moving a note during the first seconds after opening a large titled vault still walks synchronously (cache not overrun yet). Worth also checking `TreeReady.IsCompleted`? | Left as is; documented as a known gap. **Answered: fix it — done, async while `TreeReady` is pending.** |
| 3 | 63 | The single-instance fast path uses a `Global\MdNotes-<user>` named mutex. Tested on Linux only (hand-off works, cold start 300 ms → near 0). Please check a second launch on Windows still hands its file to the running window. | Shipped it; any failure in the mutex check falls through to the old 300 ms pipe connect, so the worst case is the old behaviour. **Still open: no Windows machine yet; test later.** |
