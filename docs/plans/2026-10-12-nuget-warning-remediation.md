# NuGet Warning Remediation Plan

**Goal:** Remove the actionable NuGet vulnerability warnings without suppressing audit diagnostics or introducing a package that cannot build in Release without repository-specific credentials.

**Architecture:** Remove the vulnerable direct `SixLabors.ImageSharp` 3.1.12 dependencies. The Map Editor's sole use is a temporary pixel buffer and can be replaced with direct BGRA writes. The Asset Converter will use SkiaSharp 2.88.9, already used and audited through Avalonia in the main solution, for BMP/PNG decoding and PNG encoding. Treat NU1900 separately as a local cache-permissions issue: verification uses writable local NuGet package and HTTP caches instead of disabling audit warnings.

**Tech Stack:** C#/.NET 8 and 10, Avalonia, SkiaSharp, NuGet audit, xUnit

## APIs verified

- `src/MapEditor.App/Rendering/AvaloniaTintedSpriteCache.cs` used ImageSharp only to fill a contiguous BGRA frame before constructing an Avalonia `Bitmap`.
- `tools/AssetConverter/src/AssetConverter/Png/PayloadDecoder.cs` decodes BMP input to RGBA.
- `tools/AssetConverter/src/AssetConverter/Png/PngWriter.cs` encodes an RGBA buffer to PNG.
- `tools/AssetConverter/src/AssetConverter/Manifest/CustomAssetSheet.cs` decodes two 32x32 PNGs and packs their RGBA rows into a 64x32 sheet.
- GitHub's reviewed advisories report that the five warnings affecting 3.1.12 require ImageSharp 4.1.2 or later: [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44), [GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4), [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q), [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w), and [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g).
- ImageSharp 4 adds build-time license enforcement for direct consumers. A missing key warns in Debug and fails in Release, so upgrading without the repository owner's license is not a complete fix: [Six Labors license enforcement changes](https://sixlabors.com/posts/licence-enforcement-changes/).

## Task 1: Remove ImageSharp from the Map Editor

**Files:**
- Modify: `src/MapEditor.App/MapEditor.App.csproj`
- Modify: `src/MapEditor.App/Rendering/AvaloniaTintedSpriteCache.cs`
- Modify: `tests/MapEditor.App.Tests/AvaloniaTintedSpriteCacheTests.cs`

**Steps:**
1. Remove the package reference.
2. Write blended BGRA channels directly into the existing output byte array.
3. Preserve source unpremultiplication, blend rounding, alpha, and Avalonia bitmap format.

## Task 2: Replace ImageSharp in the Asset Converter

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/AssetConverter.csproj`
- Create: `tools/AssetConverter/src/AssetConverter/Png/SkiaImageCodec.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Png/PngWriter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Png/PayloadDecoder.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/BatchConverter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Manifest/CustomAssetSheet.cs`

**Steps:**
1. Replace ImageSharp with SkiaSharp and its Linux native runtime package.
2. Centralize encoded-image-to-RGBA and RGBA-to-PNG conversion.
3. Preserve the converter's top-down RGBA buffers, BMP behavior, transparency rule, and custom-sheet ordering.

## Task 3: Update and run regression tests

**Files:**
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj`
- Modify: affected image-based converter tests

**Steps:**
1. Remove the direct ImageSharp test dependency and use SkiaSharp for fixtures and assertions.
2. Run the full solution tests and converter tests.
3. Build the full solution and converter in Debug and Release.
4. Run `dotnet package list --vulnerable --include-transitive` for the solution and converter tests and require zero vulnerable packages.
5. Run `git diff --check` and review only this task's files, leaving concurrent workspace changes untouched.

## Failure behavior and scope

- Invalid encoded images produce `InvalidDataException`; invalid RGBA dimensions or lengths fail before encoding.
- NU1900 caused by the execution environment's read-only user cache must not be hidden with `NoWarn` or `NuGetAudit=false`.
- No persistence or data-schema migration is required.
