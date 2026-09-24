# Hosting notes for Zero-K.info

## Why this file exists

`Global.StartApplication` starts the multiplayer lobby server inside the IIS worker
process: `ZkServerRunner.Run()` brings up `ZkLobbyServer` (player connections, chat,
matchmaking, battle hosting), alongside the award calculator, the forum post index,
the Steam depot generator and the autoregistrator.

That means **anything that recycles the app pool disconnects every player**. IIS
recycles on an idle timeout, on a periodic schedule, on memory limits, on a config
change, and on every deploy.

Separating the lobby server into its own process is the real fix and is a large piece
of work - the website reaches into the server's live in-memory state from 116 call
sites. Until that lands, the settings below remove every cause of a restart **except a
deploy**, which is the bulk of the pain for a few minutes of configuration.

## Required server configuration

These live in `applicationHost.config`, not in this repository, so they have to be
applied on the host and re-checked after an IIS upgrade.

    # never recycle on a timer
    appcmd set apppool /apppool.name:"<pool>" /recycling.periodicRestart.time:00:00:00

    # never recycle on a request/memory threshold
    appcmd set apppool /apppool.name:"<pool>" /recycling.periodicRestart.requests:0
    appcmd set apppool /apppool.name:"<pool>" /recycling.periodicRestart.privateMemory:0
    appcmd set apppool /apppool.name:"<pool>" /recycling.periodicRestart.memory:0

    # never shut down when idle
    appcmd set apppool /apppool.name:"<pool>" /processModel.idleTimeout:00:00:00

    # keep the worker process running and start it with IIS
    appcmd set apppool /apppool.name:"<pool>" /startMode:AlwaysRunning

    # warm the app immediately after a start, rather than on first request
    appcmd set app /app.name:"<site>/" /preloadEnabled:true

`preloadEnabled` works together with the `<applicationInitialization>` block in
`Web.config`, which is in source control. The IIS feature "Application Initialization"
must be installed for that block to do anything.

### Check the current values

    appcmd list apppool "<pool>" /text:*

Look at `recycling.periodicRestart`, `processModel.idleTimeout` and `startMode`.

## What this does not fix

- **Deploys still disconnect players.** The GitHub Actions workflow publishes on push
  to `stable`, which restarts the application. Until the lobby server is a separate
  process, releasing the website is a multiplayer outage. Consider deploying at a
  quiet hour, and announce it in-lobby first.
- **An unhandled exception** that kills the worker process still takes multiplayer
  with it.
- **`Application_End` calls `Global.StopApplication()`**, which calls
  `ZkServerRunner.Stop()`. IIS gives a shutting-down worker a limited window
  (`shutdownTimeLimit`, 90s by default), so a slow disconnect of many clients can be
  cut short. Raising it slightly is reasonable:

      appcmd set apppool /apppool.name:"<pool>" /processModel.shutdownTimeLimit:00:02:00

## Compile-checking on Linux

    ./tools/build-website.sh

Builds this project with msbuild under mono in Docker, in about twenty seconds, with no
.NET Framework and no Visual Studio. It is what every modernization change in this
repository has been checked with, and it is worth running before opening a pull request -
the Windows build in CI runs on a self-hosted runner that a fork cannot claim.

Two things it cannot do, which is why the Windows build still matters:

- **Razor views are not compiled.** mono ships no `aspnet_compiler.exe` (MSB6004), so a
  mistake in a `.cshtml` file passes this check untouched.
- **It does not run anything.** Running the site needs Windows, IIS Express and a database.

It works on a copy of the tracked files, so the repository never collects root-owned
`obj/` directories from the container, and it applies one workaround to that copy:
`ZkData.MissionUpdater.UpdateMission` uses `ZipFile.Open`, which trips a
`System.IO.Compression` facade version conflict under mono. That reproduces on unmodified
`master`, so it is an artefact of the container rather than of any change.

## Service endpoints that deployed clients depend on

Three endpoints serve the desktop clients. They look interchangeable and are not, so
before touching any of them:

- **`/ContentService`** - `Controllers/ContentServiceController.cs`, JSON over POST.
  This is the current path. `PlasmaShared.ContentServiceClient` posts here, and
  `GlobalConst.GetContentService()` is what `ZeroKLobby` and `ZkLobbyServer` call.
- **`ContentService.svc`** - WCF, and marked `[Obsolete]` in its own source. Every
  operation forwards to `GlobalConst.GetContentService()`, which means an HTTP round
  trip out of the worker process and back into `/ContentService` on the same site.
  Nothing in this repository calls it; it exists for clients that were deployed
  before the JSON endpoint. Deciding whether it can go needs production access-log
  evidence, not a grep.
- **`MissionService.svc`** - WCF, and live. `MissionEditor` in this repository builds
  a `ChannelFactory<IMissionService>` against `GlobalConst.BaseSiteUrl +
  "/MissionService.svc"`. Removing it breaks the mission editor.

## Phase 2, measured

The modernization plan describes Phase 2 as "remove dead ends - 3 WebForms pages,
LINQ-to-SQL remnant, WCF". Two of those three are not what they sound like.

**The WebForms pages were not in the live site.** There are no `.aspx`, `.ashx` or `.asmx`
files under `Zero-K.info` at all. The ones the plan counted were in `PlanetWars.old/` and
`PlanetWars/` - two trees last touched in **2010**, carrying their own `PlanetWars2.sln`,
in neither `Zero-K.sln` nor referenced by any tracked file outside themselves. 470 files,
6 MB, now deleted; git keeps them if anyone wants them back. The only WebForms left in the
live site were two `using System.Web.UI` lines that use nothing, already marked as dead
usings by the .NET 9 port.

**Five more trees were dead the same way, and are gone too (2026-09-24).** `PlanetWars.old`
was found by reading the plan's own list; these were found by asking which projects in the
repository no solution contains, and then which of those anything actually uses. 256 files,
about 31,600 lines:

| tree | files | last real change | what referenced it |
|---|---|---|---|
| `ModelBase` (with `ModToXml`, `UnitImporter`) | 108 | 2015 | one `.gitignore` line |
| `LuaAdmin` (with `LuaSharp`, `LuaWrap`, `LuaManagerLib`) | 102 | 2012 | nothing |
| `ModStats` | 27 | 2012 | nothing |
| `NightWatch` | 14 | 2016 | a commented-out `using` in `Fixer`, and a 2012 publish manifest |
| `SpringAccountReader` | 3 | 2011 | nothing |

Two strays went with them: `MissionEditor2.csproj`, a second project file sitting beside the
`MissionEditor.csproj` the solution actually builds and referenced by nothing, and
`Zero-K.info/asp.net.Publish.xml`, a Visual Studio 2010 FTP publish history listing files as
they stood in 2012. That last one carried a production FTP URL and user name - no password,
`savePWD` was false - and nothing reads it.

**The test was "what uses it", not "how old is it".** Ages here are misleading on their own:
every one of these trees has a 2022 or 2023 commit from a repository-wide change, which is why
they look alive in a log. `NightWatch` is the one worth naming, because it had the most signs of
former life and none of them were uses - a `using` behind a `//`, and a filename in a manifest
nothing reads.

**The LINQ-to-SQL remnant is vocabulary, not a dependency.** `InsertOnSubmit`,
`DeleteOnSubmit` and friends appear 185 times across 44 files - and they are extension
methods over EF6, defined in `ZkData/DbExtensions.cs`, with a twin in `ZkData.Core` for
the port. Nothing links LINQ to SQL. Renaming 185 call sites would change no behaviour and
risk a typo in each one; it is cosmetic debt and is left alone deliberately.

**WCF is the real item**, and it splits in two:

- `MissionService.svc` - **there is now a JSON endpoint beside it**, `/MissionService`,
  built the same way `/ContentService` is: request and response classes dispatched by name
  through `CommandJsonSerializer`. It does not reimplement anything - `MissionService.svc.cs`
  stays the one implementation and the JSON layer is an envelope over it, because two copies
  of an operation that deletes other people's missions is not a thing to have while both
  endpoints are live.

  The contract is unchanged: `Mission` is `[DataContract]` and Json.NET honours that, so the
  same fields cross as under WCF. One deliberate difference - WCF turned an
  `ApplicationException` into a fault the channel rethrew, and there is no such machinery
  here, so the message comes back in an `Error` field.

  **`MissionEditor` now uses `/MissionService`.** `MissionServiceJsonClient` implements the
  same `IMissionService` the WCF channel did, so nothing calling it changed; it converts the
  `Error` field back into the exception those callers are written around, and keeps the
  one-hour timeout the WCF binding had, because a mission upload is a whole game archive and
  HttpClient defaults to 100 seconds.

  The `.svc` is still hosted, for editors already installed - ClickOnce updates them when a
  user next launches the editor, not when the site deploys, so removing it earlier breaks
  publishing for anyone who has not opened it since.

  **It now says who is still calling it.** Every WCF operation writes a line to `LogEntries`
  (14 days, visible at `Admin/TraceLogs`), and `SendMission` includes the caller's editor
  version:

      MissionService.svc (deprecated WCF endpoint): SendMission by someone, mission editor 1.2.3.4

  So the question "has everyone updated?" is answerable from the site's own data:

  ```sql
  -- anyone still on the WCF endpoint in the last fortnight?
  SELECT Message FROM LogEntries
   WHERE Message LIKE 'MissionService.svc (deprecated%' ORDER BY Time DESC;

  -- which editor versions have published at all, either way
  SELECT MissionEditorVersion, COUNT(*), MAX(ModifiedTime)
    FROM Missions GROUP BY MissionEditorVersion ORDER BY 3 DESC;
  ```

  When the first query comes back empty for a while, `MissionService.svc`,
  `MissionService.svc.cs`'s `MissionService` class and `IMissionService`'s WCF attributes can
  go in one commit - the operations live in `MissionServiceLogic`, which the JSON endpoint
  uses and which stays.

  This is the difference between this endpoint and `ContentService.svc`: that one serves
  clients the repository cannot see, so it needs production access logs; this one reports
  itself.
- `ContentService.svc` - obsolete and uncalled from this repository: every caller here goes
  through `IContentServiceClient` to `/ContentService`, and inside the website `Global.cs`
  overrides that to run the implementation in-process. It is hosted purely for clients
  deployed before the JSON endpoint existed.

  This used to say it **could not be retired from the evidence available here**, because
  the decision needed production access logs. It does not any more: **all 14 operations
  now report their callers**, by user agent and address, plus the login and API version
  where the operation carries one. The same `LogEntries` that answers the question for
  `MissionService.svc` answers it for this one.

  **It counts rather than logs, and the difference is the point.**
  `ZkServerTraceListener` turns every trace into its own `ZkDataContext` and one insert.
  Publishing a mission is rare, so `MissionService.svc` can afford a line per call;
  `DownloadFile` and `GetResourceData` are whatever a fleet of outdated lobbies asks for,
  and a line per call would put a database write on a path that has none today. So the
  first call from each operation and user agent reports immediately - no line means no
  caller, which is what the retirement turns on - and after that one line an hour carries
  the count. `Zero-K.info/AppCode/LegacyCallReporter.cs` holds that, clock-injected, with
  ten tests in `Tests.Portable` and both failure directions covered: reporting per call,
  and losing calls so a busy endpoint looks idle.

  What it still needs is **time**, not evidence. Nothing can be concluded until the
  instrumented build has been deployed and watched.

**Porting note.** Server-side WCF hosting has no in-box successor on .NET 9. The two
`.svc` endpoints are therefore a hard constraint on the port: either host them with
CoreWCF, or re-expose their operations as JSON endpoints and ship updated clients
first. This is the reason the modernization plan's "remove WCF" step could not be
carried out as written.

## Linux and containers: what is done and what is still blocked

The modernization plan puts Linux, containers and CI in Phase 4, after the .NET 9 port.
Most of it genuinely is blocked on that port - .NET Framework 4.8 does not run on Linux,
so the website cannot be containerized before it is ported. Two pieces were not blocked
and are done:

**The case-collision is fixed.** `AutoRegistrator` (the project) and `Autoregistrator`
(its resources) were two directories differing only in case. Windows sees one directory
and Linux sees two, and the website referenced `..\Autoregistrator\Autoregistrator.csproj`,
which on Linux matched neither. Everything is now `AutoRegistrator`, matching the solution
file, `Fixer.csproj` and the project's own `AssemblyName`. A Linux build no longer needs
symlinks to get past it.

**CI runs the .NET 9 tests.** `.github/workflows/test_portable.yml` runs
`Tests.Portable` on a stock Linux runner: no Windows, no MSBuild, no database, well under
a minute. `test_pullrequest.yml` still does the full Framework build on the self-hosted
Windows runner, and still has to.

What is still blocked, in the order it has to be unblocked:

1. **EF6.** `ZkDataContext` and 117 migrations. EF Core is a rewrite of the data layer,
   not a retarget, and everything else waits behind it. Measured in
   `ZkData/EFCORE-MIGRATION.md`, along with the two blockers already cleared.
2. **`System.Drawing.Common`** - the imaging types only. `Bitmap`, `Graphics`, `Image`
   and friends throw `PlatformNotSupportedException` off Windows since .NET 7. The
   geometry types (`Point`, `Size`, `Rectangle`, `Color`) are in
   `System.Drawing.Primitives`, are part of the .NET 9 shared framework and are **not** a
   blocker - an earlier version of this note said otherwise. 13 files are affected, with
   `Shared/PlasmaShared/Utils.cs` the choke point. Measured in
   `Shared/PlasmaShared/IMAGING-MIGRATION.md`.
3. **Server-side WCF.** The two `.svc` endpoints above. CoreWCF, or replace them and ship
   updated clients first.
4. **A mono build still needs one workaround**, so it is not CI-able as a Linux check yet:
   `ZkData.MissionUpdater.UpdateMission` hits a `System.IO.Compression` facade version
   conflict under mono 6.12. That is a mono artifact rather than a .NET 9 blocker, but it
   is why there is no Linux compile check of the website in CI.

## Related work in progress

`ILobbyServerApi` (see `ZkLobbyServer/ILobbyServerApi.cs`) is the seam introduced so the
website stops touching `ZkLobbyServer` directly. It is implemented in-process today and
changes no behaviour; its purpose was to make the coupling explicit and countable before
a transport is chosen.

**The website no longer reaches past it.** `Global.LobbyApi` is declared as
`ILobbyServerApi` rather than `ILobbyServerApiInProcess`, so the live battle list, the
live `Battle` objects and the server itself are not merely unused - they do not compile.
That is the check; there is no grep to run, and the earlier one here was wrong anyway.
It matched `LobbyApi.InProcess` and so missed `Global.LobbyApi?.InProcess?.SessionTokens`
in `Global.asax.cs`, which the narrowed type found immediately.

What this does **not** mean is that the server can move out today. Still in the way:

- ~~Deployment.~~ **Configurable.** Setting the `LobbyApiUrl` MiscVar is the entire switch:
  unset - every deployment that has not been changed on purpose - and the website starts the
  lobby server in its own process exactly as before. Set, and it starts none and talks to
  `LobbyApiUrl` with `LobbyApiSecret`. A URL without a secret **refuses to start** rather
  than falling back, because falling back means two lobby servers against one database.
  `ZkLobbyServer.Standalone` is the other end. It is also the only Exe/net48/SDK-style
  project in `Zero-K.sln`, which made it the first here to meet all three conditions of
  the SDK's RuntimeIdentifier inference and so **broke the Windows solution build** -
  restore and compile were choosing different RIDs. Its csproj now says `PlatformTarget`
  out loud and carries the explanation. Nothing in this fork could have caught it: the
  inference only fires on a Windows host, and `tools/build-website.sh` is mono on Linux
  building one project rather than the solution.
- ~~The reverse dependency.~~ **Moved.** The event feed's formatting is now
  `ZkData/ZkHtmlFormat.cs` and `ZkData/PlanetwarsEventFormatter.cs`, one implementation that
  both halves call. It used to be two near-verbatim copies - the website's and the port's -
  with the event creator as a third caller that only the website could satisfy. A standalone
  server writes PlanetWars events now.
- ~~No transport.~~ **Written.** `ZkLobbyServer/Api/` holds an HTTP+JSON host
  (`LobbyApiHost`) and client (`RemoteLobbyServerApi`); `Tests.Database` drives all 40
  members over real loopback HTTP and checks the arguments and results arrive intact.
  HTTP because both halves are .NET Framework 4.8 today, which rules out grpc-dotnet, and
  because HttpListener and HttpClient are unchanged on .NET 9 - the transport does not have
  to be rewritten by the port going on around it.

  **It is not wired up.** Nothing constructs a `RemoteLobbyServerApi`; `Global.LobbyApi` is
  still the in-process one. Choosing between them is deployment configuration, and it is
  not worth adding before the item below is answered.
- **The shared statics: mostly answered.** `RatingSystems.Init()` is split. A process that
  only READS ratings calls `RatingSystems.CreateRatingSystems()`, which creates the systems
  and computes nothing; each one then serves `GetPlayerRating` out of the `AccountRatings`
  table, which is the rating of record. That covers `Account.GetRating` (15 sites),
  `GetPlayerRating`, `GetTopPlayers` and the PlanetWars faction stats - the website reads
  ratings with no lobby server, and `ZeroKWeb.Host` runs that way so it stays true.

  The five that have no database behind them are now `ILobbyServerApi` members:
  `GetPlayerRatingHistory`, `GetInternalRating` and `GetMapRanking` as queries,
  `ForceRatingsUpdate` and `ResetPlanetwarsRatings` as commands. **The website no longer
  names `RatingSystems` or `MapRatings` for anything the database cannot answer.**

  The interface is 45 members, 45 crossable, and the transport test drives all of them.
- ~~Six members pass EF entities.~~ **Done.** All 40 members now take primitives and
  protocol DTOs only; the six that took `Account`, `Clan`, `Planet` or the caller's
  `ZkDataContext` take ids, and the server loads what it needs from its own context.
  `ZkLobbyServer/seam-inventory.txt` is **generated** and checked on every pull request:

      dotnet run --project ZkData.Core -- seam --update

  It walks each member's types transitively, so a DTO holding an entity two levels down is
  caught too - which reading signatures does not do, and which is how the list came to say
  six when an earlier hand-written version of this section said two.
- ~~`RedeemSessionToken` is a bearer credential exchange.~~ **Handled, on the wire.** The
  transport refuses plaintext off loopback - the shared secret and the sign-on token both
  cross it - so it needs `https` unless both ends are on the machine. A private segment
  without a certificate is still deployable by setting the
  `LobbyApiAllowInsecureTransport` MiscVar, which has to be said rather than fallen into.
  The token itself now comes from a cryptographic generator rather than `Guid.NewGuid()`.

  **It expires**, 24 hours after issue by default, configurable with the
  `LobbySessionTokenLifetimeHours` MiscVar. The client is handed its token once, at login,
  and nothing refreshes it - so a client connected for longer than that finds the website no
  longer signs it in automatically. The user sees a login page, reconnecting to the lobby
  fixes it, and nothing is lost. That is the cost of bounding the credential.

  **One property is unchanged**: the game client passes the token to the website in a
  **query string** (`ZeroKLobby/BrowserInterop.cs`), which is where URLs end up in history,
  referer headers and proxy logs. Expiry bounds how long a leaked one is worth having;
  moving it out of the URL is a change to a shipped client and is still worth deciding on.
- **Shared statics the seam never modelled.** `Ratings.RatingSystems` and
  `Ratings.MapRatings` are filled only by `ZkLobbyServer.ZkLobbyServer`, and eight website
  files read them; `Global.AutoRegistrator`, `Global.SteamDepotGenerator` and
  `PlasmaShared.ContentService` are the same shape. None are API calls, so no count of
  `ILobbyServerApi` members finds them - they were found by requesting the pages on the
  .NET 9 port and getting 500s. The login rate limiter (`VerifyIp`/`LogIpFailure`) IS on
  the interface, but its state is the server's memory, so the port has none today.

The remaining `ILobbyServerApiInProcess` members are still implemented and still used -
by the lobby server itself, and by the `Fixer` tool through `Global.Server`. What changed
is that Zero-K.info is not one of their callers.
