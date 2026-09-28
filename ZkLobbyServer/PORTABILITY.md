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
4. ~~`ZkLobbyServer` itself~~ — **`ZkLobbyServer.Core` exists and builds**, in one assembly, and
   CI builds it on every pull request.

## ZkLobbyServer.Core

`ZkLobbyServer.Core/ZkLobbyServer.Core.csproj` compiles the whole chain on `net9.0` — PlasmaShared,
LobbyClient, MonoTorrent, PlasmaDownloader and ZkLobbyServer, plus everything `ZkData.Core` links —
as a **single assembly**, linking production sources rather than copying them. 1.8 MB of output
containing `ZkLobbyServer`, `ServerBattle`, `MatchMaker`, `LoginChecker`, `TasClient`,
`ZkDataContext` and `BEncodedDictionary`, which is how the build is checked to have compiled
something rather than nothing.

It does **not** reference `ZkData.Core`, and the header says why at length: referencing it splits
`GlobalConst` and `Utils`, whose portable halves live there, and the resulting errors name members
that were present all along.

Two of `ZkData.Core`'s own files are excluded, because they are stand-ins its tooling wants and the
server does not: `GlobalConstMode.cs` hard-codes `Mode` and `BaseSiteUrl` to a dev value, and
`Ef6Compat/ImagesDefault.cs` is a minimal `Images`. The real PlasmaShared versions compile on .NET 9
and carry what the server reads.

### Two BCL collisions, which only compiling could find

Both are PlasmaShared back-filling something the Framework lacks and .NET 9 has:

- **`DistinctBy`** — `System.Linq` grew it in .NET 6. Two equally applicable extension methods are an
  *ambiguity error*, not a silent winner. Moved to `Utils.Polyfills.cs`, which the Framework projects
  compile and this one excludes; the semantics are identical, which is what makes the swap safe.
- **`Stream.ReadExactly`** — .NET 7 grew it, returning `void` where PlasmaShared's extension returns
  `bool`. An instance method beats an extension, so the call sites stopped compiling. Renamed to
  `TryReadExactly`, which is also what it does.

Neither was visible from reading. Both appeared the first time the chain was compiled as one
assembly.

### What this does not mean

~~It compiles. It has not been run~~ - **it has now been run.** See "It runs" below.


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

**Fixed, and the answer was already half-built.** `entityEntry` is not EF's entry type — it is
`ZkDataContext.EntityEntry`, a wrapper the port already maintains in both twins so that
`IEntityAfterChange` implementations compile unchanged. Its `State` property was the only thing
leaking EF's namespace to a caller.

So the comparison moved into the wrapper, where each twin already imports its own EF:

    public bool IsAdded     => State == EntityState.Added;
    public bool IsDeleted   => State == EntityState.Deleted;
    public bool IsUnchanged => State == EntityState.Unchanged;

`ForumListManager` asks `entityEntry.IsAdded` and no longer imports `System.Data.Entity` at all.
This is `DbCompat`'s philosophy applied to a type instead of a method: a *type* cannot move behind
one name, so the **comparison** moves to where the type is already unambiguous.

### Where that leaves the chain

Nothing in `ZkLobbyServer` or its dependencies now fails to compile on `net9.0` for a reason that
belongs to the source. Every error the probe still reports — 270 of them — is one of five classes
that only duplicate types produce:

    CS0121  ambiguous between 'Utils.EscapePath(string)' and 'Utils.EscapePath(string)'
    CS0019  Operator '==' cannot be applied to 'ResourceType' and 'ResourceType'
    CS1503 / CS0266 / CS0029  conversions between a type and itself

— because the probe globs PlasmaShared *and* references `ZkData.Core`, which contains it too.

**That is not the same as "it compiles".** A single-assembly probe would settle it, and this survey
has not produced one that is clean, for the reasons in the trap above. What can be said is narrower
and still worth having: after four one-line changes — three stale `using`s and one compat call —
no source-level blocker is known.

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

## It runs

`ZkLobbyServer.Standalone.Core` is the .NET 9 host: two linked files (`Program.cs` and
`StandalonePlanetwarsEventCreator.cs`, the same sources the Framework `ZkLobbyServer.Standalone`
compiles) against `ZkLobbyServer.Core`. It starts, and `tools/lobby-core-start.sh` keeps it that
way - a CI step in `test_database.yml` runs the process against a real database and connects to
the port players use. Twenty seconds, so it costs nothing to keep honest.

### What running found that compiling could not

`TcpTransportServerListener.Bind` P/Invoked `SetHandleInformation` out of `kernel32.dll`. Off
Windows that throws `DllNotFoundException` - and `Bind` catches it, retries 120 times at one
second each, and gives up. **The server then comes up with its API listening and no player port
at all.** Nothing looked wrong: the process was alive, the API answered, and the message was one
line in a log two minutes of retries deep.

The call clears `HANDLE_FLAG_INHERIT` so a spawned Spring server does not inherit the listening
socket. On Unix .NET already opens sockets `FD_CLOEXEC`, so there is nothing to clear and nothing
needed in its place; the call is now guarded by `RuntimeInformation.IsOSPlatform(OSPlatform
.Windows)`. The Framework build never hit this because mono maps `kernel32` for it.

This is the whole argument for the check being a *start*, not a build. Restoring the bug fails
four of its six assertions; a process-liveness check or an API ping passes with the bug in place.

### The runtime questions the survey listed

Unchanged and still unanswered, because starting the server does not reach them: `Mono.Posix`
restores as a Framework package and `System.Drawing.Common` is Windows-only. Both sit on paths a
running game touches - not on the path to the listener.

### Two things to know before running it by hand

- `tools/lobby-config.sh` defaults the API port to **8200**, which is `GlobalConst.LobbyServerPort`
  in Local mode - the API then takes the port the player listener is about to ask for. Pass a
  different one: `./tools/lobby-config.sh set 8300`.
- Left set, those MiscVars make every later host-harness run find a lobby server configured and
  none running. `lobby-core-start.sh` clears them on the way out; by hand, `clear` yourself.

### Still not done

**No player has connected through it.** Opening the port is not serving a game: the protocol
handshake, the Spring process the server spawns, and Planetwars are all untested on .NET 9. And it
is still not in `Zero-K.sln`, which is correct - the Framework solution cannot build a `net9.0`
project.
