# Claude Instructions

Use [AGENTS.md](AGENTS.md) as the authoritative repository instruction file.

Key reminders:

- Use Task for repository operations.
- Read `docs/rewrite-guide.md` before rewrite work and do not implement a child
  until its packet records approval and `Implementation authorized: Yes`.
- Keep `MtgMcp.Core` dependency-light.
- Keep third-party HTTP contracts in adapter projects.
- Keep normal tests offline and free of real Archidekt mutations.
- Run the narrow relevant Task before handing off changes.
