# .NET 11 Preview 4 and C# 15 Union Adoption Plan

## Current Baseline

- Branch/worktree: `ncurry/dotnet-11-csharp-15-unions` at `C:\Users\chase\Documents\Programming\github\nccurry\mtg-mcp-dotnet-11-csharp-15-unions`.
- Installed SDK available locally: `11.0.100-preview.4.26230.115`.
- Current repo baseline targets `net10.0`, pins SDK `10.0.100`, and sets `<LangVersion>latest</LangVersion>`.
- `dotnet list mtg-mcp.slnx package --outdated --include-prerelease` shows the `Microsoft.Extensions.*` packages have `11.0.0-preview.4.26230.115` updates.

## Research Notes

- The latest .NET 11 preview verified on May 25, 2026 is `.NET 11.0.0-preview.4`, released May 12, 2026.
- Microsoft's .NET 11 docs are updated for Preview 4 and call out `net11.0`, runtime async changes, C# 15, and runtime `UnionAttribute` / `IUnion` scaffolding.
- C# 15 unions require preview language features in current preview SDKs. A local compiler probe succeeded with `<TargetFramework>net11.0</TargetFramework>` and `<LangVersion>preview</LangVersion>`.
- Union declarations compile into struct-like union values with implicit conversions from each case type, a `Value` property, and exhaustive `switch` support.
- Union declarations store values through `object?`; avoid them for hot paths involving value-type cases unless using a custom `[Union]` type with the non-boxing access pattern.

Sources:

- https://dotnet.microsoft.com/en-us/download/dotnet
- https://devblogs.microsoft.com/dotnet/dotnet-11-preview-4/
- https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/overview
- https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15
- https://devblogs.microsoft.com/dotnet/csharp-15-union-types/
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/unions
- https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/discriminated-unions

## Union Best-Practice Rules For This Repo

1. Use unions for closed alternatives with different payloads where today a type can represent contradictory states.
2. Prefer enums or existing string constants for payload-free public labels, especially MCP JSON contracts and persisted plan files.
3. Keep unions internal unless the serialized shape is deliberate and covered by MCP surface tests.
4. Keep third-party HTTP DTOs in adapter projects as straightforward JSON models; do not leak preview-language abstractions into external contract mapping unless it removes real ambiguity.
5. Preserve cancellation flow and `ConfigureAwait(false)` in async library code.

## Audit Findings

### Adopt Now

- `src/MtgMcp.App/Cli/ArchidektAuthCommand.cs`: `ParseResult` mixes mutually exclusive states (`ShowHelp`, `Error`, and usable credentials). A private union of `ArchidektAuthOptions`, `ArchidektAuthHelp`, and `ArchidektAuthParseError` will make the command flow exhaustive and prevent accidental mixed states.
- `src/MtgMcp.Core/Plans/DeckPlanService.Apply.cs`: plan apply failure handling carries several nullable fields plus exception wrapper types. A private union for apply attempt outcomes can centralize success, operation failure, and persistence uncertainty while keeping the public `DeckEditPlanApplyResult` JSON-compatible.

### Defer

- `DeckEditPlan.Status`, `DeckEditOperation.Operation`, corpus statuses, simulation profiles, deck roles, and MCP mode strings remain public/persisted labels. Converting them directly to unions would churn JSON contracts and tool schemas without enough safety benefit.
- `WorkspaceMode` and `DeckMutationKind` remain enums because they are payload-free closed sets.
- Adapter JSON parsing in Archidekt/Scryfall has union-shaped inputs, but those branches are tied to third-party wire quirks. Keep the current explicit parsing until a larger adapter-contract cleanup is justified.

## Implementation Phases

### Phase 1: Platform Upgrade

- Update `global.json` to SDK `11.0.100-preview.4.26230.115`.
- Update `Directory.Build.props` to `net11.0` and `LangVersion` `preview`.
- Update central `Microsoft.Extensions.*` packages to `11.0.0-preview.4.26230.115`.
- Keep test and unrelated packages stable unless the upgrade forces a change.
- Validate with `task restore` and `task lint`.
- Run code-quality and abstraction audits over the platform diff, then fix findings.

### Phase 2: CLI Parse Union

- Replace mutable `ParseResult` mode flags with a private C# 15 union in `ArchidektAuthCommand`.
- Keep command behavior, output redaction, and tests unchanged.
- Add focused tests for help and unknown option paths if coverage is missing.
- Validate with `dotnet test tests/MtgMcp.App.Tests/MtgMcp.App.Tests.csproj --configuration Release --filter "Category!=Live"`.
- Run code-quality and abstraction audits over the phase diff, then fix findings.

### Phase 3: Plan Apply Outcome Union

- Introduce private apply outcome case records and a private union inside `DeckPlanService.Apply.cs`.
- Use exhaustive switching to turn operation success, operation failure, and persistence uncertainty into the public result.
- Keep `DeckEditPlanApplyResult` and persisted `DeckEditPlan` shapes stable.
- Validate with `dotnet test tests/MtgMcp.Core.Tests/MtgMcp.Core.Tests.csproj --configuration Release --filter "Category!=Live"`.
- Run code-quality and abstraction audits over the phase diff, then fix findings.

### Phase 4: Final Sweep

- Run `task test` for all non-live tests.
- Run `task lint`.
- Review `git diff --stat` and final changed files for accidental public-contract churn.

## Audit Log

### After Phase 1

- Code-quality audit: .NET 11 Preview 4 promoted many pre-existing IDE style suggestions under `task lint` / `-warnaserror`. Fixed by pinning the observed preview-noisy IDE diagnostics in `.editorconfig` instead of mass-reformatting unrelated files.
- Code-quality audit: removed the unused private `DeckRoleClassifier.HasCategory` wrapper surfaced by the newer analyzer pass.
- Abstraction-quality audit: no project-boundary or ownership drift found. The .NET 11 upgrade remains in root build/package configuration, and `MtgMcp.Core` still does not reference adapter or host projects.
- Validation: `task restore` and `task lint` pass on `net11.0`.

### After Phase 2

- Code-quality audit: the private CLI parse union removes the old mixed-state `ShowHelp` / `Error` / credential options model while keeping command behavior and output redaction stable.
- Code-quality audit: wrapped the long default-union null arm and union declaration so the preview syntax stays scan-friendly.
- Abstraction-quality audit: no public CLI contract, adapter boundary, or persisted data shape changed. The union remains local to `ArchidektAuthCommand`.
- Validation: `dotnet test tests/MtgMcp.App.Tests/MtgMcp.App.Tests.csproj --configuration Release --filter "Category!=Live"` passes with 23 tests.

### After Phase 3

- Code-quality audit: `ApplyDeckPlanAsync` now delegates operation execution to a private apply-attempt union and keeps public result publishing in success/failure completion helpers.
- Code-quality audit: failure details now travel as one private payload, with the failed operation index coupled to the operation object instead of passed as independent nullable arguments.
- Abstraction-quality audit: no Core dependency boundary changes and no public `DeckEditPlanApplyResult` or persisted `DeckEditPlan` shape changes. The union remains private to `DeckPlanService.Apply.cs`.
- Validation: `dotnet test tests/MtgMcp.Core.Tests/MtgMcp.Core.Tests.csproj --configuration Release --filter "Category!=Live"` passes with 205 tests.

### After Phase 4

- Final audit: diff scope is limited to build configuration, the plan document, two private union refactors, one stale helper removal, and focused CLI tests. No adapter HTTP contracts or MCP tool/public JSON result shapes were converted to preview union types.
- Validation: `task test` passes all non-live suites on `net11.0`.
- Validation: `task lint` passes under .NET 11 Preview 4 with preview SDK informational messages only.
