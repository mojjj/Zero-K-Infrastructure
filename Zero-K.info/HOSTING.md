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

`ILobbyServerApi` (see `ZkLobbyServer/ILobbyServerApi.cs`) is the seam being
introduced so the website stops touching `ZkLobbyServer` directly. It is implemented
in-process today and changes no behaviour; its purpose is to make the coupling
explicit and countable before a transport is chosen. Members on the interface can move
across a process boundary; anything still reached through `InProcess` cannot yet, and
is the remaining work.

Count what is left with:

    grep -rn "LobbyApi.InProcess" Zero-K.info/

As of this commit that is 8 uses, all in `TourneyController` - a tournament admin
console over live `TourneyBattle` objects, which the Razor view also renders directly.
Closing it needs a tournament API with DTOs rather than a mechanical translation, and
it is the last blocker for running the lobby server in its own process.
