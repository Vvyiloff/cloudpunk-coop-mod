# Source publication verification — 2026-10-14

The source publication imports 107 C# files and four project files byte for byte
from the active development checkout, including pending and previously untracked
beta changes. It adds a source README, contribution instructions and protocol CI.
Tool paths were adjusted only in the publication checkout.

Checks performed before publication:

- Protocol suite: 587 passed, zero failed, zero skipped.
- Plugin Release build against the previously generated clean-install interop:
  zero errors and zero warnings. No game process was started or plugin deployed.
- Standalone test client Release build: zero errors; existing NETSDK1138 warnings
  report its net6.0 target support status.
- All PowerShell and Python source tools passed syntax parsing.
- Credential-pattern scan found no private keys, GitHub tokens, OpenAI-style
  keys or literal bearer credentials in publication source and documentation.
- No game binaries, third-party runtime binaries, generated interop, saves,
  logs, dumps, bin/obj outputs or private configuration are included in main.

The installer release asset retains the verified SHA256:
`DE0835F0FA25E0D71B4B4422D9E520272F58D675B6ED40EB56AF588A53EF38D1`.

This verifies the published source and package preservation. It does not complete
full campaign beta acceptance or establish multiplayer behavior on two PCs.
