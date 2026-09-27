# What stands between ZkLobbyServer and .NET 9

Surveyed 2026-09-27. **This is a floor, not a ceiling** — see the caveat at the end before quoting
any of it as "all that is left".

## Why survey it at all

The lobby server is the last large thing on .NET Framework 4.8 that the port needs. It already runs
as its own process (`ZkLobbyServer.Standalone`) and as its own container, but under **mono** — so
the stack still carries a runtime nobody ships new versions of. Moving it to .NET 9 removes mono
from the picture entirely.

The plan estimated this as part of a 10–16 week phase and assumed the hard part would be the code.
Measured, the picture is better than that.

## What the code itself uses

108 files, about 15,200 lines. Imports of the namespaces that usually block a port:

| namespace | files |
|---|---|
| `System.Web` | 0 |
| `System.ServiceModel` (WCF) | 0 |
| `System.Drawing` | 0 |
| `System.Configuration` | 0 |
| `System.Runtime.Remoting` | 0 |
| `System.Data.Entity` (EF6) | **6** |

**EF6 is the only Framework-coupled namespace in the lobby server**, and it is the one the port has
already answered: `ZkData.Core` reproduces the schema, reads and writes the data, and produces
identical WHR ratings, all enforced in CI.

## What a link probe says

Compiling `ZkLobbyServer` + `LobbyClient` + `PlasmaDownloader` + `PlasmaShared` as one `net9.0`
library against `ZkData.Core` reduces to **11 distinct errors**, none of them in the lobby server's
own logic:

| what | where | the answer |
|---|---|---|
| `Mono.Unix` | `PlasmaDownloader/EngineDownload.cs`, `PlasmaShared/SelfUpdater.cs` | `File.SetUnixFileMode`, which .NET 9 has and Framework did not |
| `System.Configuration.SettingChangingEventArgs` | `PlasmaShared/Settings.cs` | the `System.Configuration.ConfigurationManager` package |
| `Neo.IronLua` | `PlasmaShared/UnitSyncLib/ArchiveCache.cs` | a package reference the probe lacked |
| `Discord.Rpc` | `ZkLobbyServer/ChatRelay.cs` | a Discord.Net sub-package |
| `MonoTorrent.Common`, `Torrent` | `PlasmaDownloader/Torrents/*` | **the repository has its own `Shared/MonoTorrent` project**; the probe wrongly used the NuGet package, whose namespaces moved in 2.x |
| `System.Data.Linq` | `ZkLobbyServer/MatchMaker/MatchMaker.MapVetoer.cs` | a stale `using` from the deleted LINQ-to-SQL project — removed |

So: **no blocker was found in ZkLobbyServer's own logic.** What the probe hit was its dependency
chain and a handful of package references.

## The caveat that matters

**A type-resolution error suppresses method-body binding.** While the probe cannot resolve
`SpringPaths` or `ITransport`, the compiler never gets far enough to complain about a
Framework-only *member* — `AppDomain.CurrentDomain.SetupInformation`, a `WebClient` overload that
does not exist any more, a `BinaryFormatter`. Those errors appear only once the type errors are
gone.

This has already caught this project out once, in the Razor view port, where a count of "compiling
views" was measured against a project that had a declaration error in it and was therefore binding
nothing.

**So the honest reading is:** the survey rules out the *structural* blockers — no WCF, no
`System.Web`, no `System.Drawing`, no remoting — and identifies the dependency work. It does not
yet establish that the bodies compile. The next person should resolve the eleven above and re-run
before claiming a number.

## Suggested order

1. `Shared/PlasmaShared` first — 38 of the unresolved names came from it, and everything else
   depends on it.
2. `Shared/LobbyClient` and `Shared/MonoTorrent` next.
3. `Shared/PlasmaDownloader`, which needs the `Mono.Unix` replacement.
4. `ZkLobbyServer` last, against `ZkData.Core` rather than `ZkData`.

Each of those can be a linked-source probe project of its own, the way `ZkData.Core` and
`ZeroKWeb.Core` were — production files compiling in both stacks unmodified, so nothing forks.
