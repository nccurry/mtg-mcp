# Claude Instructions

Use [AGENTS.md](AGENTS.md) as the authoritative repository instruction file.

Key reminders:

- Use Task for repository operations.
- Keep `MtgMcp.Core` dependency-light.
- Keep third-party HTTP contracts in adapter projects.
- Keep normal tests offline and free of real Archidekt mutations.
- Run the narrow relevant Task before handing off changes.
