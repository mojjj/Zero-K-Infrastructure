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

## A defect found while separating the arithmetic

`Utils.ToBytes` is what `AutoRegistrator` uploads every new map's minimap, metal map and
height map through. It does two things wrong, both preserved for now in
`ImageSizing.LegacyToBytesSize` and pinned by tests:

1. **It applies the aspect ratio twice.** `UnitSync.GetMinimap` already calls
   `FixAspectRatio`, which turns unitsync's square minimap into the map's real proportions.
   `ToBytes` then computes the ratio of *that already-correct image* and applies it again,
   so a 2:1 minimap is uploaded as 4:1. Square images are unaffected, which is why it has
   survived.
2. **It ignores its `size` argument.** `AutoRegistrator` passes `ImageSize = 256`,
   commented "max size of minimap to be sent to server". Nothing downscales; a 1024-wide
   minimap is uploaded at 1024.

`ImageSizing.BoundedByLongestSide` is what it was presumably meant to do, and is tested but
deliberately **not wired up**: switching changes the images uploaded for every new map, and
whether to re-process the existing ones is a decision, not a refactor.

## Decisions taken, 2026-09-21

Two of the three open imaging questions were decided; the third stands.

### `Images.Processor` is ImageSharp — **decided, done**

One line in `Imaging/Images.cs`. Every upload through the seam - clan avatars and
backgrounds, news and lobby-news thumbnails, structure icons - is now encoded by ImageSharp
rather than System.Drawing.

**This changes bytes on disk for new images.** Both implementations ask for bicubic; different
libraries with different kernels do not produce identical pixels, so images uploaded from now
on differ slightly from those already stored. **Nothing stored is touched.**

Done now rather than when the port forced it: `System.Drawing.Common` is Windows-only on
.NET 9, so the line had to move before the port completes, and moving it while there is slack
means the difference lands somewhere it can be looked at. One line reverts it.

`Tests.Portable` now pins the choice - a revert, deliberate or by a bad merge, would otherwise
change what the site writes to disk with nothing to say so. A second test asserts the test
assembly has no `System.Drawing.Common` reference, which is only true because `Images.cs` names
an implementation that does not need one; while it named `SystemDrawingImageProcessor` that
file could not be linked into a .NET 9 project at all.

### Structure icons stay bicubic — **decided, accepted**

`PlanetStructure.GenerateResized` asked for `HighQualityBilinear`; the seam asks for
`HighQualityBicubic`, so icons regenerated since the seam landed differ slightly from earlier
ones. Accepted rather than reverted: the alternative is widening `IImageProcessor.SaveResized`
with a resampler argument for one call site, and these icons are small and regenerate
routinely.

### `ToBytes` — **still open**

The aspect-ratio and size defects below are unchanged and unwired. The decision is not about
imaging libraries: it is whether to re-process the minimaps already uploaded, which needs a
backfill and a way to tell a corrected image from an uncorrected one. Fixing it for new maps
only would leave two generations of minimaps side by side with no way to tell them apart,
which is worse than the known defect.

## Suggested order

0. **Done:** the target-size arithmetic is separated into
   `Shared/PlasmaShared/Imaging/ImageSizing.cs`, linked into `Tests.Portable` and covered
   by 14 tests. It is pure, uses only `System.Drawing.Primitives`, and therefore already
   runs on .NET 9. Sizing is where a change silently distorts an image, so it is worth
   holding still before anything underneath it moves.
1. **Done:** `Imaging/IImageProcessor.cs` is the seam - `Measure`, `Save`, `SaveResized`,
   in terms of encoded bytes and `System.Drawing.Size`. Nothing in it names `Bitmap`,
   `Image` or `Graphics`, so it compiles on .NET 9; it is linked into `Tests.Portable` and
   exercised there, which is what stops someone quietly adding an `Image` to it.
   `SystemDrawingImageProcessor` implements it as a thin wrapper that does exactly what
   each call site did before, so no pixels changed.

   Migrated to it: clan avatar and background upload (both actions in `ClansController`),
   the news thumbnail, the lobby news thumbnail. Five call sites; `Images.Processor` is the
   one place an ImageSharp implementation gets substituted.

   Not migrated, and why:
   - `PlanetwarsController.SaveJpeg` renders a galaxy image in memory with `Graphics` and
     saves the `Bitmap` it already holds. Nothing crosses a byte boundary, so the seam does
     not fit until the renderer itself is ported.
   - `AutoRegistrator` / `UnitSync` receive `Bitmap` objects from a native library. That is
     the interop rewrite described above, not a call-site change.
   - `GetResized` / `GetResizedWithCache` in `ZeroKLobby`: a WinForms client that is not
     part of this port and keeps System.Drawing either way.
2. **Half done:** `Imaging/ImageSharpImageProcessor.cs` exists and is tested. It is
   linked into `Tests.Portable` and exercised on **.NET 9, on Linux**, where
   System.Drawing.Common cannot run at all - real bytes, real files: dimensions, that the
   output reads back as an image, and that the file extension still chooses the format,
   which the news upload path depends on.

   It is **not wired up**. `Images.Processor` still defaults to System.Drawing, because
   switching what the server writes to disk wants comparing on a deployment rather than in
   a compiler. The two libraries use different resampling kernels, so output will differ
   slightly by design; whether that difference matters is a question for eyes, not
   assertions.

   Version, since it is less obvious than it looks: **ImageSharp 2.1.13**. The 2.x line is
   Apache-2.0 and supports .NET Framework 4.8, so it lives in the codebase *before* the
   port rather than after - which is what makes an incremental switch possible at all. 3.x
   and later need .NET 6+ and carry the Six Labors Split Licence. Older 2.1.x patch levels
   have published high-severity advisories (2.1.9 does, and so does 3.1.6); 2.1.13 is
   clean. Verified by restoring each on both target frameworks.
3. `ResizedImageCache` - its key is an `Image`, so it changes with whatever type replaces
   it.
4. `UnitSync.cs` last, on its own, with map files to hand.

Only step 1 is verifiable in this repository today. Steps 2-4 need either a test
deployment or the native unitsync library.
