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

## Step 1, measured: PlasmaShared already compiles

Surveyed the next day, because "PlasmaShared first" deserved a number rather than an ordering.

**All 83 of its source files compile on `net9.0`, with zero errors.** Not a subset, not with files
excluded — the whole tree, given the package references the existing project already declares.
38 of them were being linked into `ZkData.Core` individually and 10 more into `Tests.Portable`, so
roughly half was already known to work; the other half had simply never been tried.

Two packages restore but do not mean what the green build suggests, and they are the whole of the
remaining work in this tree:

| package | what the compiler says | what Linux would do |
|---|---|---|
| `Mono.Posix` | resolves, with **NU1701**: restored as `.NETFramework 4.8` against a `net9.0` project | one real user, and it is **not** in this tree — see below |
| `System.Drawing.Common` | resolves and compiles | Windows-only since .NET 6. Five files construct GDI objects: `ResizedImageCache.cs`, `Utils.cs`, `UnitSyncLib/UnitSync.cs`, `Imaging/GdiPixelBridge.cs`, `Imaging/SystemDrawingImageProcessor.cs` |

**Compiling is not running, and this is the case that shows why.** A build with 0 errors says
nothing about `System.Drawing.Common` throwing `PlatformNotSupportedException` on the first call off
Windows. The site already hit this and answered it — `Images.Processor` selects ImageSharp, and
`SystemDrawingImageProcessor` is kept deliberately as the GDI implementation to compare against, not
as something the port uses. The remaining GDI users are unitsync and the diagram code, neither of
which the website needs.

### Mono.Unix is one method, in the other tree

The first survey said two files used `Mono.Unix` and put both in the same sentence. Only one of them
uses it:

- `PlasmaShared/SelfUpdater.cs` had `using Mono.Unix.Native;` and **nothing from it** — a stale
  import, removed. (The file itself is very much alive: `SelfChecker` is what `ZeroKLobby` and
  `ChobbyLauncher` call to find out whether there is an update.)
- `PlasmaDownloader/EngineDownload.cs` is the real user, in one method:

      private static void FixPermissions(string targetDir)
      {
          if (Environment.OSVersion.Platform == PlatformID.Unix)
              Syscall.chmod(tpath, FilePermissions.S_IRWXU | ... );
      }

  making a downloaded engine executable. .NET 7 added `File.SetUnixFileMode`, which does exactly
  this.

**It cannot be swapped yet**, and the reason is the port's own discipline rather than difficulty:
`PlasmaDownloader` still targets 4.8, where `File.SetUnixFileMode` does not exist. Changing it now
means either breaking the Framework build or introducing an `#if` into a file that currently needs
none. It belongs with the move of that project, not before it.

So step 1 is not "port PlasmaShared". It is: decide what `UnitSync`, `ResizedImageCache` and the
`Diagrams` code should do off Windows. The `Mono.Unix` question is one method in `PlasmaDownloader`,
and it waits for that project.

## Suggested order

1. ~~`Shared/PlasmaShared` first~~ — **done as a measurement**: it compiles. What is left there is
   the two runtime questions above, not compilation.
2. `Shared/LobbyClient` and `Shared/MonoTorrent` next.
3. `Shared/PlasmaDownloader`, which needs the `Mono.Unix` replacement.
4. `ZkLobbyServer` last, against `ZkData.Core` rather than `ZkData`.

Each of those can be a linked-source probe project of its own, the way `ZkData.Core` and
`ZeroKWeb.Core` were — production files compiling in both stacks unmodified, so nothing forks.
