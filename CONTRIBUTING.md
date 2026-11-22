# Contributing

Build with `build.ps1`; run `test.ps1` before submitting changes. Run `format.ps1 -Verify` with PowerShell 7 and .NET 8 when changing C# files. The Visual Studio solution targets .NET Framework 4.7.2 x64.

Keep hardware operations inside the bridge-owned controller. Preserve input bounds, native return checks, ordered writes, reset acknowledgment and thermal intervention. Add tests around changed behavior or a reproduced failure rather than duplicating implementation details.

Keep UI state changes on the dispatcher. Asynchronous event handlers should delegate to Task-returning methods; hardware operations must not run on the UI thread. Update user-facing documentation when behavior or supported hardware changes.

Do not commit vendor binaries, local settings, worker tokens, startup logs, generated screenshots containing personal information, build products or `.local` history. Public release archives must not embed ASUS or Sysinternals binaries. New dependency versions require provenance and compatibility review.
