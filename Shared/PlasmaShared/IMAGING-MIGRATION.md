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

## 2026-09-25: the last two call sites, and 34 of 34 controllers

**Every controller in Zero-K.info now compiles on .NET 9.** The two that were left were the
two blocked here, and they needed the seam to grow by two operations.

**`SaveResizedJpeg(bytes, size, path, quality)`.** `PlasmaServer` writes map thumbnails at
JPEG quality **100** and the galaxy render at **85**, both chosen deliberately; ImageSharp's
default is 75. Porting those call sites through the quality-less `SaveResized` would have
compiled, passed every test, and quietly made both worse - so quality is on the interface, and
a test asserts that quality 100 produces a bigger file than quality 10. Breaking it (dropping
the encoder argument) fails that test, checked.

**`ComposeJpeg(background, overlays, quality)`.** The galaxy map is a background with a planet
icon drawn at each planet's position, which is compositing and did not fit a seam built around
resize-and-save. Overlays carry a `System.Drawing.Rectangle` and are stretched into it, which
is what `Graphics.DrawImage(image, x, y, w, h)` did.

It returns **bytes rather than writing a file**, and that is what let the call site cross:
`Index` needs the dimensions as well as the image, and both are now had without naming an
imaging type. It also makes the operation testable - the tests decode the result and assert
which colour won at a point inside the overlay and at a point outside it, compared loosely
because JPEG is lossy. Drawing every overlay at (0,0) instead of its rectangle fails two of
them, checked.

**The arithmetic went to `ImageSizing.PlanetIconPlacement` first**, with four tests, including
one that pins the *truncation*: the width truncates before the height is derived from it, and
the halving truncates again. Computing in doubles and rounding once would be defensible and
would move every planet by a pixel, which is not what a port is for.

**`PlasmaServer`'s thumbnail sizing was already extracted** - `ImageSizing.ScaledToFit` was
written from exactly those lines and had been waiting, tested, since step 0.

**One branch was not ported, deliberately.** `GenerateGalaxyImage`'s `antiAliasingFactor`
composed at a multiple and resized down. It was unreachable: the only caller takes the
defaults, and the parameter was pinned to 1 by a FIXME saying the Bitmap path had issues. It
now throws if anything passes something else, rather than being silently ignored or ported
untested - supersampling is a change to what the galaxy looks like, not a port.

**Two more things were found stranded in the GDI+ half of `Utils.cs`**, which is now a
familiar shape: `SafeDelete` (eleven lines that swallow an exception, and the last thing
keeping `PlasmaServer` off the port) and, for `ContentServiceImplementation`,
`EngineDownload.VersionNumberComparer`, split into a portable half.

**Still not done, and unchanged:** `UnitSync.cs` - the interop rewrite - and `ToBytes`, whose
aspect-ratio and size defects are a backfill decision rather than an imaging one. `ZeroKLobby`
keeps System.Drawing either way. `ResizedImageCache` is keyed on an `Image` and is only used
by paths that have not moved.

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
2. **Done.** `Imaging/ImageSharpImageProcessor.cs` is what `Images.Processor` returns, and has
   been since 2026-09-21. It is linked into `Tests.Portable` and exercised on **.NET 9, on
   Linux**, where System.Drawing.Common cannot run at all - real bytes, real files.

   (This section said "not wired up" for four days after it was. The decision above was the
   true one; this is why the two halves of a document should not both describe state.)

   Version, since it is less obvious than it looks: **ImageSharp 2.1.13**. The 2.x line is
   Apache-2.0 and supports .NET Framework 4.8, so it lives in the codebase *before* the
   port rather than after - which is what makes an incremental switch possible at all. 3.x
   and later need .NET 6+ and carry the Six Labors Split Licence. Older 2.1.x patch levels
   have published high-severity advisories (2.1.9 does, and so does 3.1.6); 2.1.13 is
   clean. Verified by restoring each on both target frameworks.
3. `ResizedImageCache` - its key is an `Image`, so it changes with whatever type replaces
   it.
4. **Half done - see below.** `UnitSync.cs`: the arithmetic is out and tested, the Bitmaps remain.

## 2026-09-25: unitsync, as far as it goes without a decision

**What moved:** the arithmetic. `PixelBuffers` unpacks unitsync's raw buffers - 16-bit RGB565 for
the minimap, 8-bit greyscale for the height and metal maps - into tightly packed RGB24, and
`ImageSizing.MinimapAspectCorrection` is `FixAspectRatio`'s sizing. Both are pure, both are tested
on .NET 9 on Linux, and `UnitSync.cs` now calls them instead of doing it inline.

**What did not move:** `UnitSync` still returns `Bitmap`, because `Map` holds `Image` fields,
`Utils.ToBytes` takes an `Image`, and `AutoRegistrator` passes them along. `GdiPixelBridge` is the
one remaining piece of System.Drawing in that path, and it is where the next cut goes.

**The interesting part is how it was verified**, because "cannot be tested without unitsync and
real map files" was true of the old code and is no longer true of this part. Two things could not
be checked on Linux and both are now checked on the **Windows CI job**, which gained its first
Windows-only reason to exist:

- **5-6-5 to 8-8-8 scaling.** GDI+ unpacked the minimap by its own rule, and the plausible
  candidates - `x * 255 / 31` and bit replication `(x << 3) | (x >> 2)` - agree at 0 and 31 and
  differ in between, which is a shade on every pixel of every minimap. `Tests/UnitSyncPixelTests`
  compares the two implementations for **all 65,536 values**.
- **Channel order and row padding.** `Format24bppRgb` is BGR in memory despite the name, and GDI+
  pads every row to a four-byte boundary. Both mistakes are invisible on the height and metal
  maps, which are grey - so they are asserted on coloured pixels and at a width (3) where the
  padding bites.

**Copying rather than wrapping.** The old `new Bitmap(size, size, stride, format, pointer)` handed
GDI+ a pointer into unitsync's own memory and kept it. The bytes are copied out now, so nothing
downstream holds a buffer unitsync may free.

**What finishing requires is a decision, not more porting.** `Map.Minimap` would become encoded
bytes, `Utils.ToBytes` would move onto the imaging seam, and `AutoRegistrator`'s three call sites
would follow. That is mechanical - but `ToBytes` is where the aspect-ratio and size defects live,
and porting it means either preserving them deliberately in the new code or fixing them, which is
the backfill question below and is not the port's to answer.


Only step 1 is verifiable in this repository today. Steps 2-4 need either a test
deployment or the native unitsync library.
