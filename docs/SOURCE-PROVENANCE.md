# Source snapshot provenance

This source snapshot imports the complete working source tree on 2026-10-14,
including tracked pending edits and new source files. The development checkout
base commit was `8be34337cdf5eda97e25b8c4c9a590d456463c29`; that base commit alone
does not contain all the imported beta work.

All 107 C# files and four project files were copied byte for byte. The import
includes project tools, documentation and plans. Published tool defaults were
then adjusted to locate this checkout and use the current user's temporary
directory instead of the original developer's absolute paths.

The original installable distribution remains at tag
`test-2026-10-14-protocol18` and as an asset in the matching GitHub prerelease.
It is an experimental build, not a completed full beta.
