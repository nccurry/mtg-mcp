# Performance Ratchet

The Task/benchmark infrastructure is reusable, but rows describing legacy role
classification or Stats Lab behavior do not require those product features to
survive the clean-break rewrite. Rewrite children add measurements only for
their approved production paths.

`task perf:report` emits a report-only hot-path timing artifact at
`artifacts/performance-report.txt`.

The report currently covers:

- wide deck analysis for 600 distinct cards
- role classification for 1,000 representative cards
- Stats Lab performance analysis for 1,000 simulations through turn 6

Each row is compared with a generous report budget and labeled
`within-report-budget` or `over-report-budget`. The task does not fail solely
because a timing exceeds the budget. Treat an over-budget row as a release
review signal, not as an automatic blocker, unless the team explicitly promotes
that row to a hard gate.

Run the benchmark suite when a report row moves materially:

```bash
task bench:dry
task bench:analysis -- --job short
task bench:performance -- --job short
```

BenchmarkDotNet artifacts remain under `BenchmarkDotNet.Artifacts`; keep them
out of commits unless publishing a specific performance investigation.
