# Replacing System.Drawing imaging for .NET 9

This is the second blocker in the Phase 3 port, after EF6 (`ZkData/EFCORE-MIGRATION.md`).

## First, the correction

`System.Drawing` is not one thing, and an earlier note in `Zero-K.info/HOSTING.md` was too
broad about it:

- **`System.Drawing.Primitives`** - `Point`, `Size`, `Rectangle`, `Color`, their `F`
  variants, and `ColorTranslator`. Part of the .NET 9 shared framework, fully
  cross-platform, needs no package reference. **Not a blocker.**
- **`System.Drawing.Common`** - `Bitmap`, `Image`, `Graphics`, `ImageCodecInfo`, `Encoder`,
  `InterpolationMode`, `Pen`, `Brush`, `Font`. Since .NET 7 this throws
  `PlatformNotSupportedException` anywhere but Windows. **This is the blocker.**

Verified rather than assumed: `Shared/PlasmaShared/Diagrams/Vector.cs` uses
`System.Drawing.Point` and is linked into `Tests.Portable`, where it compiles and its tests
run on .NET 9 - see `Tests.Portable/VectorTests.cs`.

That halves the apparent size of this job. Nineteen files reference `System.Drawing`; only
**thirteen** touch imaging. One more, `Shared/PlasmaShared/MetaDataCache.cs`, imported it
without using it at all and has been cleaned up.

A caution for anyone repeating this measurement: grepping for type names misses
`ColorTranslator`, which `Zero-K.info/ForumParser/Tags/ColorTag.cs` uses to parse BBCode
colour tags. It is in Primitives, so that file is fine - but it does not look like a
`System.Drawing` user until the compiler says so.

## The actual surface

| Uses | File | What it does |
|---|---|---|
| 21 | `Shared/PlasmaShared/Utils.cs` | `GetResized`, `GetResizedWithCache`, `SaveJpeg`, `ToBytes` - the shared resize and JPEG-encode helpers everything else calls |
| 13 | `Shared/PlasmaShared/UnitSyncLib/UnitSync.cs` | wraps the native unitsync library: `GetHeightMap`, `GetMetalMap`, `GetMinimap`, `FixAspectRatio` |
| 13 | `Shared/PlasmaShared/Diagrams/Node.cs` | diagram rendering |
| 10 | `Zero-K.info/Controllers/PlanetwarsController.cs` | planet icon upload and resize |
| 9 | `Shared/PlasmaShared/ResizedImageCache.cs` | cache of resized images, keyed on `Image` |
| 8 | `Zero-K.info/Controllers/ClansController.cs` | clan avatar upload |
| 8 | `Zero-K.info/AppCode/PlasmaServer.cs` | map minimap / heightmap storage |
| 2 | `ZkData/Ef/PlanetStructure.cs`, `Zero-K.info/Controllers/NewsController.cs`, `Zero-K.info/Controllers/LobbyNewsController.cs`, `Shared/PlasmaShared/UnitSyncLib/Map.cs`, `Shared/PlasmaShared/Diagrams/Diagram.cs` | image load / save |
| 1 | `AutoRegistrator/SteamDepotGenerator.cs` | |

Re-measure with:

    grep -rlE '\b(Bitmap|Graphics|ImageCodecInfo|Encoder|InterpolationMode|ImageFormat)\b' --include=*.cs .

## Shape of the work

`Utils.cs` is the choke point. `GetResized`, `SaveJpeg` and `ToBytes` are what the
controllers and `PlasmaServer` call; port those four methods and most call sites follow
without knowing which library is underneath. That argues for an interface over the four
operations rather than a mechanical type-for-type swap.

**ImageSharp** (`SixLabors.ImageSharp`) is the reasonable target: managed, cross-platform,
supports netstandard2.0 so it can be referenced from the current .NET Framework build
*before* the port, which means the swap can be done and shipped incrementally rather than
in one jump with everything else. SkiaSharp is faster for heavy work but carries native
binaries per platform, which is a worse fit for a job whose point is portability. Note
ImageSharp's licence changed at v3 - v2.1.x is Apache-2.0, v3+ is Six Labors Split, which
is free for open source but wants reading first.

The awkward one is `UnitSync.cs`. It receives raw pixel buffers from a native library and
wraps them in `Bitmap`. That is not a library swap, it is a rewrite of the interop
marshalling, and it cannot be tested without unitsync and real map files.

## Suggested order

1. Put an interface over the four `Utils.cs` operations; keep `System.Drawing` behind it.
   Compile-checkable, no behaviour change.
2. Add an ImageSharp implementation beside it; switch the callers that only resize and
   save. Comparable output can be checked by eye on a test deployment.
3. `ResizedImageCache` - its key is an `Image`, so it changes with whatever type replaces
   it.
4. `UnitSync.cs` last, on its own, with map files to hand.

Only step 1 is verifiable in this repository today. Steps 2-4 need either a test
deployment or the native unitsync library.
