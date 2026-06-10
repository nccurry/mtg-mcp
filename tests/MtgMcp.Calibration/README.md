# Stats Lab Calibration

This project runs a deterministic offline calibration corpus for Stats Lab. It
is a developer report harness, not an MCP tool and not a full Magic rules
simulation. The default corpus combines built-in synthetic fixtures with
checked-in, source-attributed benchmark snapshots under `Corpus/`.

Run:

```powershell
task calibrate:stats-lab
```

Generated artifacts are written under `artifacts/stats-lab-calibration`:

- `report.json`: machine-readable fixture, expectation, pressure diagnostic,
  and drift results.
- `report.md`: human-readable summary.
- `baseline.json`: compact current metric baseline that can be supplied to a
  later run with `--baseline <path>`.

Useful options:

- `--simulations <count>`
- `--max-turn <turn>`
- `--seed <seed>`
- `--baseline <path>`
- `--corpus <path>` to load a JSON corpus file or a directory of JSON files
- `--synthetic-only` to skip checked-in benchmark snapshots for fast diagnostics
- `--validate-only` to validate corpus files and deck shapes without simulation
- `--profile-sweep <profileIds>` to run non-failing alternate-profile diagnostics
- `--no-mulligans`
- `--allow-failures`

Corpus fixtures are serialized `DeckWorkspace` snapshots. They are captured and
normalized once, with public source metadata retained, so normal tests and local
calibration runs do not require network access.

Benchmark labels are advisory. Pairwise expectations compare Stats Lab metric
ordering, not objective deck strength and not real multiplayer win rates.
Pressure diagnostics compare a deck's scorecard metrics against source-derived
benchmark pressure profiles; they are not matchup simulations. Profile sweeps
compare existing `SimulationProfile` behavior only; they do not introduce a
separate decision-policy layer or affect pass/fail.
