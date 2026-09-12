using System;
using System.Collections.Generic;

namespace MapEditor.Rendering;

public sealed class GraphicAssetCatalog
{
    private readonly IReadOnlyList<int> _allSheetIds;
    private readonly IReadOnlyDictionary<GraphicCategory, IReadOnlyList<int>> _sheetsByCategory;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<GraphicCategoryMapping>> _mappingsBySheet;
    private readonly IReadOnlyDictionary<GraphicAnimationKey, GraphicAnimation> _animationsByKey;
    private readonly IReadOnlyDictionary<SpriteReference, IReadOnlyList<GraphicAnimation>> _animationsByFrame;

    private GraphicAssetCatalog(
        IReadOnlyList<int> allSheetIds,
        IReadOnlyDictionary<GraphicCategory, IReadOnlyList<int>> sheetsByCategory,
        IReadOnlyDictionary<int, IReadOnlyList<GraphicCategoryMapping>> mappingsBySheet,
        IReadOnlyDictionary<GraphicAnimationKey, GraphicAnimation> animationsByKey,
        IReadOnlyDictionary<SpriteReference, IReadOnlyList<GraphicAnimation>> animationsByFrame)
    {
        _allSheetIds = allSheetIds;
        _sheetsByCategory = sheetsByCategory;
        _mappingsBySheet = mappingsBySheet;
        _animationsByKey = animationsByKey;
        _animationsByFrame = animationsByFrame;
    }

    public IReadOnlyList<int> GetSheets(GraphicCategory? category)
        => category is null
            ? _allSheetIds
            : _sheetsByCategory.TryGetValue(category.Value, out IReadOnlyList<int>? sheets) ? sheets : Array.Empty<int>();

    public IReadOnlyList<GraphicCategoryMapping> GetMappings(int sheet)
        => _mappingsBySheet.TryGetValue(sheet, out IReadOnlyList<GraphicCategoryMapping>? mappings)
            ? mappings
            : Array.Empty<GraphicCategoryMapping>();

    public IReadOnlyList<GraphicAnimation> GetAnimations(SpriteReference frame)
        => _animationsByFrame.TryGetValue(frame, out IReadOnlyList<GraphicAnimation>? animations)
            ? animations
            : Array.Empty<GraphicAnimation>();

    public bool TryGetAnimation(GraphicAnimationKey key, out GraphicAnimation animation)
        => _animationsByKey.TryGetValue(key, out animation!);

    public static GraphicAssetCatalog Create(SpriteManifest sprites, GraphicAnimationManifest animations)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(animations);

        Dictionary<GraphicCategory, List<int>> sheetsByCategory = new();
        Dictionary<SpriteReference, List<GraphicAnimation>> animationsByFrame = new();

        foreach (int sheetId in animations.SheetIds)
        {
            if (!sprites.ContainsSheet(sheetId))
            {
                throw Fail(GraphicAnimationManifestError.UnknownSheet, $"Categorized sheet {sheetId} is missing from the sprite manifest.");
            }

            foreach (GraphicCategoryMapping mapping in animations.GetCategories(sheetId))
            {
                if (!sheetsByCategory.TryGetValue(mapping.Category, out List<int>? sheets))
                {
                    sheetsByCategory[mapping.Category] = sheets = new List<int>();
                }

                if (!sheets.Contains(sheetId))
                {
                    sheets.Add(sheetId);
                }
            }
        }

        foreach (GraphicAnimation animation in animations.Animations)
        {
            if (!sprites.ContainsSheet(animation.Key.OwnerSheet))
            {
                throw Fail(GraphicAnimationManifestError.UnknownSheet,
                    $"Animation ({animation.Key.OwnerSheet}, {animation.Key.AnimationId}) owner sheet {animation.Key.OwnerSheet} is missing from the sprite manifest.");
            }

            foreach (SpriteReference frame in animation.Frames)
            {
                if (!sprites.TryGetSourceRect(frame, out _))
                {
                    throw Fail(GraphicAnimationManifestError.UnknownFrameReference,
                        $"Animation ({animation.Key.OwnerSheet}, {animation.Key.AnimationId}) frame {frame.Sheet}/{frame.Graphic} is missing from the sprite manifest.");
                }

                if (!animationsByFrame.TryGetValue(frame, out List<GraphicAnimation>? containing))
                {
                    animationsByFrame[frame] = containing = new List<GraphicAnimation>();
                }

                containing.Add(animation);
            }
        }

        Dictionary<GraphicCategory, IReadOnlyList<int>> publishedSheetsByCategory = new();
        foreach ((GraphicCategory category, List<int> sheets) in sheetsByCategory)
        {
            sheets.Sort((a, b) => a.CompareTo(b));
            publishedSheetsByCategory[category] = sheets.AsReadOnly();
        }

        Dictionary<int, IReadOnlyList<GraphicCategoryMapping>> mappingsBySheet = new();
        foreach (int sheetId in animations.SheetIds)
        {
            mappingsBySheet[sheetId] = animations.GetCategories(sheetId);
        }

        Dictionary<SpriteReference, IReadOnlyList<GraphicAnimation>> publishedAnimationsByFrame = new();
        foreach ((SpriteReference frame, List<GraphicAnimation> containing) in animationsByFrame)
        {
            containing.Sort((a, b) =>
            {
                int byOwner = a.Key.OwnerSheet.CompareTo(b.Key.OwnerSheet);
                return byOwner != 0 ? byOwner : a.Key.AnimationId.CompareTo(b.Key.AnimationId);
            });

            publishedAnimationsByFrame[frame] = containing.AsReadOnly();
        }

        Dictionary<GraphicAnimationKey, GraphicAnimation> animationsByKey = new(animations.Animations.Count);
        foreach (GraphicAnimation animation in animations.Animations)
        {
            animationsByKey[animation.Key] = animation;
        }

        return new GraphicAssetCatalog(
            sprites.SheetIds,
            publishedSheetsByCategory,
            mappingsBySheet,
            animationsByKey,
            publishedAnimationsByFrame);
    }

    public static GraphicAssetCatalog Load(string assetDirectory)
        => Create(SpriteManifest.Load(assetDirectory), GraphicAnimationManifest.Load(assetDirectory));

    private static GraphicAnimationManifestException Fail(GraphicAnimationManifestError error, string message)
        => new(error, "<memory>", message);
}
