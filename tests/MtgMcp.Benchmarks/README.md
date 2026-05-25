# mtg-mcp Benchmarks

This project contains deterministic, offline BenchmarkDotNet coverage for core mtg-mcp hot paths. Benchmarks should not call live Scryfall, Archidekt, Moxfield, or playgroup services.

Run a fast smoke check before changing benchmark fixtures or task wiring:

```powershell
task bench:dry
```

Run the focused Stats Lab performance benchmarks:

```powershell
task bench:performance -- --job short
```

Run role/deck analysis, mana helper, and facet predicate benchmarks:

```powershell
task bench:analysis -- --job short
task bench:mana -- --job short
task bench:facets -- --job short
```

Pass any BenchmarkDotNet arguments after `--`. For example, to run one benchmark type:

```powershell
task bench -- --job short --filter "*DeckPerformanceAnalyzerBenchmarks*"
```

BenchmarkDotNet writes reports under `BenchmarkDotNet.Artifacts`; keep those generated outputs out of commits unless a report is intentionally being published.

The Taskfile benchmark aliases run BenchmarkDotNet in-process while mtg-mcp targets the .NET 11 preview. That keeps the local workflow aligned with Roci and MonoChess until BenchmarkDotNet's runtime detection catches up.
