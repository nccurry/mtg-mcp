# Code Style

The rules for writing code live in two places. This page points to them and adds nothing to them.

- [AGENTS.md](../../AGENTS.md): the C# and .NET rules, comment rules, XML summary rule, safety and test rules,
  and coverage floor.
- [C# best practices](../llms/csharp-best-practices.md): language, design, async, and test conventions for this
  repository.

The build enforces most of them. The pinned SDK and shared settings are in `global.json` and
`Directory.Build.props`, package versions are in `Directory.Packages.props`, and style is in `.editorconfig`.
Warnings fail the build.

Run `task lint` for the format check and the strict analyzer build. Run `task format` to apply formatting. See
[repository operations](../llms/repo-operations.md) for the full command policy.

For test layout, offline fixtures, and recorded provider fixtures, read
[tests/AGENTS.md](../../tests/AGENTS.md).
