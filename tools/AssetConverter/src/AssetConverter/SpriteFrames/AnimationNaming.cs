using System.Collections.Generic;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.SpriteFrames;

/// <summary>
/// Pure helper methods for constructing animation clip names, aliases, and resource paths.
/// No file I/O, no state mutation.
/// </summary>
public static class AnimationNaming
{
    /// <summary>
    /// Maps an <see cref="AnimationDirection"/> to its lowercase string suffix.
    /// Order follows the CompiledEnc layout: left, down, right, up.
    /// </summary>
    public static string DirectionName(AnimationDirection direction) => direction switch
    {
        AnimationDirection.Left => "left",
        AnimationDirection.Down => "down",
        AnimationDirection.Right => "right",
        AnimationDirection.Up => "up",
        _ => throw new System.ArgumentOutOfRangeException(nameof(direction), direction, null)
    };

    /// <summary>
    /// Returns the Godot animation clip name for a given order and direction.
    /// Format: &lt;state&gt;-&lt;direction&gt; (e.g. "walk-no-equip-down").
    /// </summary>
    public static string ClipName(AnimationOrder order, AnimationDirection direction)
    {
        string state = order switch
        {
            AnimationOrder.WalkingNoEquip => "walk-no-equip",
            AnimationOrder.WalkingEquip => "walk-equip",
            AnimationOrder.AttackNoEquip => "attack-no-equip",
            AnimationOrder.Attack1Hand => "attack-1hand",
            AnimationOrder.AttackStaff => "attack-staff",
            AnimationOrder.Attack2Hand => "attack-2hand",
            AnimationOrder.AttackBow => "attack-bow",
            AnimationOrder.SpellCast => "cast",
            AnimationOrder.Knealing => "kneel",
            AnimationOrder.Death => "death",
            AnimationOrder.Mounted => "mounted-walk",
            _ => throw new System.ArgumentOutOfRangeException(nameof(order), order, null)
        };
        return $"{state}-{DirectionName(direction)}";
    }

    /// <summary>
    /// Returns an idle variant name for walking/mounted orders.
    /// Only WalkingNoEquip, WalkingEquip, and Mounted generate idle names.
    /// </summary>
    public static bool TryIdleName(AnimationOrder sourceOrder, AnimationDirection direction, out string name)
    {
        string? idleBase = sourceOrder switch
        {
            AnimationOrder.WalkingNoEquip => "idle-no-equip",
            AnimationOrder.WalkingEquip => "idle-equip",
            AnimationOrder.Mounted => "mounted-idle",
            _ => null
        };

        if (idleBase is null)
        {
            name = string.Empty;
            return false;
        }

        name = $"{idleBase}-{DirectionName(direction)}";
        return true;
    }

    /// <summary>
    /// Returns legacy compatibility aliases for the given order/direction.
    /// WalkingNoEquip → "walk-{dir}", AttackNoEquip → "attack-{dir}".
    /// Returns empty list for all other orders.
    /// </summary>
    public static IReadOnlyList<string> CompatibilityAliases(AnimationOrder order, AnimationDirection direction)
    {
        var dir = DirectionName(direction);

        return order switch
        {
            AnimationOrder.WalkingNoEquip => new[] { $"walk-{dir}" },
            AnimationOrder.AttackNoEquip => new[] { $"attack-{dir}" },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Maps an <see cref="AnimationType"/> to its plural folder name under Assets/Sprites/.
    /// </summary>
    public static string TypeFolder(AnimationType type) => type switch
    {
        AnimationType.Body => "Bodies",
        AnimationType.Hair => "Hair",
        AnimationType.Eyes => "Eyes",
        AnimationType.Chest => "Chest",
        AnimationType.Helm => "Helms",
        AnimationType.Legs => "Legs",
        AnimationType.Feet => "Feet",
        AnimationType.Hand => "Hands",
        _ => throw new System.ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>
    /// Returns the relative path to an animation resource.
    /// Example: Assets/Sprites/Bodies/1/animations.tres
    /// </summary>
    public static string ResourceRelativePath(AnimationType type, int id)
        => $"Assets/Sprites/{TypeFolder(type)}/{id}/animations.tres";

    /// <summary>
    /// Returns the full Godot resource path (res://...).
    /// Example: res://Assets/Sprites/Bodies/1/animations.tres
    /// </summary>
    public static string ResourcePath(AnimationType type, int id)
        => $"res://{ResourceRelativePath(type, id)}";
}
