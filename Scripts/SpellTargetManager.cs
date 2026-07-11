using System.Linq;
using Godot;

namespace Goose2Client;

/// <summary>Manages on-screen spell targeting — enter, cycle, confirm, cancel.</summary>
public partial class SpellTargetManager : Node
{
    private Character.Character _target;
    private SpellInfo _pendingSpell;
    private SpellTarget _reticle;
    
    /// <summary>Whether the player is currently in targeting mode.</summary>
    public bool IsTargeting { get; private set; }
    
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _ExitTree()
    {
        ExitTargeting();
    }
    
    // Handle targeting input in _Input (the earliest stage) rather than _UnhandledInput, so it
    // runs BEFORE Godot's GUI focus navigation. Otherwise, when a spell is cast from a focused
    // hotkey button / spell slot, the built-in ui_left/ui_right/ui_up/ui_down navigation eats the
    // arrow keys before targeting ever sees them (only Enter happened to fall through).
    public override void _Input(InputEvent @event)
    {
        if (!IsTargeting) return;

        // Physical key/mouse input arrives as InputEventKey, not InputEventAction — so we must
        // match against the configured actions on the raw event. While targeting, movement keys
        // (WASD) cycle the target alongside the arrow-key TargetUp/TargetDown bindings: up/left
        // step backward through targets, down/right step forward (Unity's "Targeting" map).
        if (@event.IsActionPressed("TargetUp") || @event.IsActionPressed("MoveUp") || @event.IsActionPressed("MoveLeft"))
            CycleTarget(searchDown: false);
        else if (@event.IsActionPressed("TargetDown") || @event.IsActionPressed("MoveDown") || @event.IsActionPressed("MoveRight"))
            CycleTarget(searchDown: true);
        else if (@event.IsActionPressed("ConfirmTarget")) ConfirmTarget();
        else if (@event.IsActionPressed("CancelTarget")) CancelTarget();
        else if (@event.IsActionPressed("TargetHome")) GoHome();
        else return;

        GetViewport().SetInputAsHandled();
    }
    
    /// <summary>Begin targeting for the given spell.</summary>
    /// <remarks>
    /// Cast — Unity parity: remember last confirmed target across casts.
    /// Unity's SpellTargetManager keeps its internal target between casts and only resets
    /// to the local player when the remembered target is no longer valid or is rejected by
    /// the spell's target-type filter. This avoids the jarring "always start on self" behavior.
    /// </remarks>
    public void Cast(SpellInfo info)
    {
        _pendingSpell = info;
        IsTargeting = true;
        GetViewport().GuiReleaseFocus();
        var mm = GameManager.Instance.CurrentMapManager;
        if (mm == null) { ExitTargeting(); return; }
        if (_target == null
            || !GodotObject.IsInstanceValid(_target)
            || mm.GetCharacter(_target.LoginId) != _target
            || FilterRejects(_target))
        {
            _target = mm.LocalPlayer;
        }
        PositionReticle();
    }
    
    /// <summary>
    /// FilterRejects — mirrors Unity SetTarget filter mismatch check.
    /// Returns true when the target's character type does not match the spell's required
    /// target type (e.g. a player-target spell pointed at an NPC), causing the remembered
    /// target to be discarded and replaced with the local player.
    /// </summary>
    private bool FilterRejects(Character.Character target)
    {
        var filteringEnabled = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.TargetFiltering, true);
        if (!filteringEnabled) return false;
        return (_pendingSpell.TargetType != SpellTargetType.Player && target.CharacterType == CharacterType.Player)
            || (_pendingSpell.TargetType == SpellTargetType.Player && target.CharacterType != CharacterType.Player);
    }
    
    private void CycleTarget(bool searchDown)
    {
        var mm = GameManager.Instance.CurrentMapManager;
        if (mm == null) return;
        
        var candidates = mm.Characters.Select(c => 
            new TargetCandidate(c.LoginId, c.X, c.Y, c.CharacterType)).ToList();
        
        var player = mm.LocalPlayer;
        TargetCandidate? current = _target != null ? new TargetCandidate(_target.LoginId, _target.X, _target.Y, _target.CharacterType) : (TargetCandidate?)null;
        
        var filteringEnabled = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.TargetFiltering, true);
        
        var next = TargetCycler.Next(candidates, current, (player.X, player.Y), 
            GameManager.Instance.CurrentMap?.Width ?? 100,
            _pendingSpell.TargetType, filteringEnabled, searchDown);
        
        if (next != null)
        {
            var c = mm.GetCharacter(next.Value.LoginId);
            if (c != null)
            {
                _target = c;
                PositionReticle();
            }
        }
    }
    
    private void GoHome()
    {
        var mm = GameManager.Instance.CurrentMapManager;
        if (mm != null)
        {
            _target = mm.LocalPlayer;
            PositionReticle();
        }
    }
    
    private void PositionReticle()
    {
        if (_target == null) return;
        
        if (_reticle == null || !GodotObject.IsInstanceValid(_reticle))
            _reticle = GD.Load<PackedScene>("res://Scenes/UI/SpellTarget.tscn").Instantiate<SpellTarget>();

        // Parent the reticle to the target character so it follows them and renders in the world
        // canvas — mirrors Unity SpellTargetManager.SetTarget. ResizeTarget then sets a LOCAL
        // position relative to the character. (Previously parented to this autoload-side manager,
        // so ResizeTarget's local Position teleported it to the map origin and it never showed.)
        if (_reticle.GetParent() != _target)
        {
            _reticle.GetParent()?.RemoveChild(_reticle);
            _target.AddChild(_reticle);
        }

        _reticle.ResizeTarget(_target.Height);
    }
    
    private void ConfirmTarget()
    {
        if (_target != null && _pendingSpell != null)
        {
            GameManager.Instance.SpellCooldownManager.Cast(_pendingSpell.SlotNumber);
            GameManager.Instance.NetworkClient.CastSpell(_pendingSpell.SlotNumber, _target.LoginId);
        }
        ExitTargeting();
    }
    
    private void CancelTarget()
    {
        ExitTargeting();
    }
    
    private void ExitTargeting()
    {
        IsTargeting = false;
        _pendingSpell = null;
        if (_reticle != null && GodotObject.IsInstanceValid(_reticle))
        {
            _reticle.QueueFree();
            _reticle = null;
        }
    }
}
