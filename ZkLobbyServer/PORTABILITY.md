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

### The Diagrams code is live, and the duplicate was somewhere else

`PlasmaShared/Diagrams` looked at first like dead weight dragging `System.Drawing` into the portable
subset: `GalaxyDesigner` has its own `Diagram.cs` and `Node.cs`, so the shared copies seemed
shadowed. **That reading was backwards.** Those two files are in no project at all — orphans on disk
since 2011 — and `GalaxyDesigner/MainWindow.xaml.cs` does `using Diagrams;` and builds against the
shared ones. The orphans are deleted; `PlasmaShared/Diagrams` stays.

It is also the least urgent of the three `System.Drawing` users: its only consumer is a WPF desktop
tool that will not be running on Linux whatever happens to the port.

### Step 1 is closed: none of the System.Drawing users is in the server's path

The question was what `UnitSync`, `ResizedImageCache` and `Diagrams` should do off Windows. Asked
the other way round — *who calls them* — it stops being a question:

| | called by | in the lobby server's path? |
|---|---|---|
| `Diagrams` | `GalaxyDesigner`, a WPF tool | no |
| `ResizedImageCache`, and the `GetResized*` helpers in `Utils.cs` | `ZeroKLobby` and `ChobbyLauncher` UI chrome, `AutoRegistrator`, and PlasmaShared's own GDI image processor | no |
| `UnitSync` | `new UnitSync(` appears in **nine** places: AutoRegistrator, ChobbyLauncher, MissionEditor, ZeroKLobby, `UnitsyncResourcePresenceChecker`, `MissionUpdater`. **`ZkLobbyServer` is not one of them** | no |

`ZkLobbyServer` does use `ZkData.UnitSyncLib` — but only `Map` and `Mod`, which are plain data
classes that happen to live in the same namespace as the native wrapper. Worth stating because the
namespace makes it look otherwise: `PlasmaShared/UnitSyncLib/*.cs` declares `namespace
ZkData.UnitSyncLib`, so a `using` in the server reads as if it pulls the whole thing in.

`ZkData.Core` already demonstrates the shape: it links twelve unitsync **data** files and neither
`UnitSync.cs` nor `MissionUpdater.cs`.

**So PlasmaShared does not need to be split or rewritten for the port.** It needs to be linked
selectively, which is what the port has done since `ZkData.Core` — 38 of its 84 files, chosen. The
Windows-only code keeps compiling for the Framework applications that actually want it.

The `Mono.Unix` question is one method in `PlasmaDownloader`, and it waits for that project.

## What is actually left

1. ~~`Shared/PlasmaShared`~~ — answered: it compiles, and its Windows-only parts are not on the
   server's path.
2. ~~`Shared/LobbyClient` and `Shared/MonoTorrent`~~ — both compile.
3. ~~`Shared/PlasmaDownloader`~~ — compiles; its `Mono.Unix` call is the one runtime item.
4. `ZkLobbyServer` itself — the EF6 API call is fixed; `EntityState` in `ForumListManager` is the
   last one, and the partial-class trap above is what a real probe has to avoid.

### Steps 2 and 3, measured: both compile

`Shared/LobbyClient` (23 files) and `Shared/MonoTorrent` (35 files) each compile on `net9.0` with
**zero errors**, alongside PlasmaShared. Checked by looking for `TasClient`, `Battle` and
`BEncodedDictionary` in the produced assemblies, because a glob that matches nothing also produces
zero errors.

### Step 4: one stale using, then one real API

`ZkLobbyServer/ChatRelay.cs` had `using Discord.Rpc;` and nothing from it — Discord.Net dropped that
package at 3.x, and the import was the only thing keeping the tree from resolving. Removed.

With it gone the compiler finally binds method bodies, and **one error is left that is not an
artifact of the probe**:

    ZkServerTraceListener.cs: 'DatabaseFacade' does not contain a definition for 'ExecuteSqlCommand'

That is EF6's API; EF Core spells it `ExecuteSqlRaw`. **Fixed**, and it needed no new machinery:
`ExecuteSqlCommandCompat` already existed in both halves of `DbCompat`, put there for
`ResourceLinkProvider`. The call site moved to it, which is the whole change.

### What is left after that: one namespace, four call sites

    ForumListManager.cs: The name 'EntityState' does not exist in the current context

`ForumListManager` imports `System.Data.Entity` for `EntityState.Added` / `Deleted` / `Unchanged`.
EF Core has the same enum with the same members, in `Microsoft.EntityFrameworkCore`.

**This one is a different shape from the others and does not fit the existing pattern.** `DbCompat`
works because C# lets a method move behind one name; a *type* referenced by name in four
comparisons cannot be aliased the same way without either a `using` alias per file — which is one
more thing for each stack to get right — or moving the four comparisons behind helpers. It is a
design choice, small but real, and it belongs to whoever builds `ZkLobbyServer.Core` rather than
being pre-empted here.

Everything else the probe reports is the duplicate-type artifact described above: roughly 150
errors of the form *"Operator '==' cannot be applied to operands of type 'ResourceType' and
'ResourceType'"*, which is one `ResourceType` from the globbed sources and one from the referenced
`ZkData.Core`. They vanish when the chain is one assembly.

### The trap a ZkLobbyServer.Core will hit

**`GlobalConst` and `Utils` are partial classes split across `.cs` and `.Portable.cs`, and
`ZkData.Core` links only the portable halves.** A probe that *references* `ZkData.Core` and also
compiles the non-portable halves ends up with two different `GlobalConst` types in two assemblies —
the source one wins, and it is missing every member that lives in the portable half.

That produced about 150 errors of the form *"'GlobalConst' does not contain a definition for
`NightwatchName`"*, none of them real: `NightwatchName` is in `GlobalConst.Portable.cs` and
`HashLobbyPassword` is in `Utils.Enumerable.cs`, both perfectly portable and both already linked.

So a real `ZkLobbyServer.Core` must put the whole chain in **one** assembly — linking what
`ZkData.Core` links rather than referencing it — or it will spend a long time chasing errors that
are entirely its own doing. Both of this survey's wrong turns were of that kind.

## Suggested order

1. ~~`Shared/PlasmaShared` first~~ — **done**: it compiles, and the runtime questions turned out
   not to be on the server's path. See "Step 1 is closed" above.
2. `Shared/LobbyClient` and `Shared/MonoTorrent` next.
3. `Shared/PlasmaDownloader`, which needs the `Mono.Unix` replacement.
4. `ZkLobbyServer` last, against `ZkData.Core` rather than `ZkData`.

Each of those can be a linked-source probe project of its own, the way `ZkData.Core` and
`ZeroKWeb.Core` were — production files compiling in both stacks unmodified, so nothing forks.
