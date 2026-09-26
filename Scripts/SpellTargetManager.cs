using System.Linq;
using Godot;

namespace Goose2Client;

/// <summary>Manages on-screen spell targeting — enter, cycle, confirm, cancel.</summary>
public partial class SpellTargetManager : Node
{
    private Character.Character _target;
    private SpellInfo _pendingSpell;
    private SpellTarget _reticle;
    private ulong _hotkeyConfirmFrame = ulong.MaxValue;
    private readonly HoldCastGate _holdGate = new();

    /// <summary>Whether the player is currently in targeting mode.</summary>
    public bool IsTargeting { get; private set; }

    // A hotkey press that confirmed a cast must not re-fire the hotbar's _Process poll on the
    // same frame (IsTargeting is already false by then, so its guard no longer applies).
    public bool HotkeyConfirmThisFrame => Engine.GetProcessFrames() == _hotkeyConfirmFrame;

    // True while the hotkey that hold-cast is still down: the hotbar's held-key repeat then keeps
    // casting on the remembered target rather than reopening targeting behind the player's back.
    private bool IsHoldRepeating =>
        !IsTargeting && _holdGate.IsRepeating && IsHotkeyHeld(_holdGate.Action);
    
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    // Holding the cast hotkey auto-confirms after HoldCastGate's delay, so a single press-and-hold
    // casts without a second keypress. A quick tap releases before the delay and leaves targeting
    // up; a fresh press while targeting confirms immediately in _Input.
    //
    // The gate is advanced on every frame, targeting or not: it has to observe the release that
    // ends a press, otherwise hold time left over from one press would confirm the next press's
    // targeting session on its first frame and pin that hotkey to its previous target for good.
    public override void _Process(double delta)
    {
        if (!_holdGate.Update(HeldHotkeyAction(), delta)) return;
        if (IsTargeting) CastOnTarget();
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
        if (@event.IsActionPressed("TargetUp", allowEcho: true)
            || @event.IsActionPressed("MoveUp", allowEcho: true)
            || @event.IsActionPressed("MoveLeft", allowEcho: true))
            CycleTarget(searchDown: false);
        else if (@event.IsActionPressed("TargetDown", allowEcho: true)
            || @event.IsActionPressed("MoveDown", allowEcho: true)
            || @event.IsActionPressed("MoveRight", allowEcho: true))
            CycleTarget(searchDown: true);
        else if (@event.IsActionPressed("ConfirmTarget")) ConfirmTarget();
        else if (IsHotkeyPressed(@event))
        {
            _hotkeyConfirmFrame = Engine.GetProcessFrames();
            ConfirmTarget();
        }
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
        if (!IsUsableTarget(_target, info.TargetType))
            _target = mm.LocalPlayer;
        PositionReticle();
    }

    /// <summary>
    /// Casts again on the remembered target for a hotkey still held down after its hold cast.
    /// Returns false when no such press is down, so the caller leaves targeting closed instead of
    /// reopening the reticle for a key the player is already holding.
    /// </summary>
    public bool RepeatHoldCast(SpellInfo info)
    {
        if (!IsHoldRepeating) return false;

        var mm = GameManager.Instance.CurrentMapManager;
        if (mm == null) return false;
        if (!IsUsableTarget(_target, info.TargetType))
            _target = mm.LocalPlayer;
        if (_target == null) return false;

        GameManager.Instance.SpellCooldownManager.Cast(info.SlotNumber, info.Cooldown);
        GameManager.Instance.NetworkClient.CastSpell(info.SlotNumber, _target.LoginId);
        return true;
    }

    // A remembered target survives only while it is the same live character on this map, passes the
    // spell's target-type filter, and sits inside the view range.
    private bool IsUsableTarget(Character.Character target, SpellTargetType spellTargetType)
    {
        var mm = GameManager.Instance.CurrentMapManager;
        if (mm == null || target == null || !GodotObject.IsInstanceValid(target)) return false;
        if (mm.GetCharacter(target.LoginId) != target) return false;
        if (FilterRejects(target, spellTargetType)) return false;
        if (target.IsHiddenFromViewer) return false;

        var viewRange = GetViewRange();
        var player = mm.LocalPlayer;
        return System.Math.Abs(target.X - player.X) <= viewRange.X
            && System.Math.Abs(target.Y - player.Y) <= viewRange.Y;
    }
    
    private Vector2I GetViewRange()
    {
        var vw = GameManager.Instance.WorldViewport;
        return vw != null ? vw.ViewRangeTiles
            : new Vector2I(TargetCycler.ViewRangeX, TargetCycler.ViewRangeY);
    }
    
    /// <summary>
    /// FilterRejects — mirrors Unity SetTarget filter mismatch check.
    /// Returns true when the target's character type does not match the spell's required
    /// target type (e.g. a player-target spell pointed at an NPC), causing the remembered
    /// target to be discarded and replaced with the local player. The local player itself is
    /// never rejected — it is always a valid target.
    /// </summary>
    private bool FilterRejects(Character.Character target, SpellTargetType spellTargetType)
    {
        if (target.IsLocalPlayer) return false;
        var filteringEnabled = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.TargetFiltering, true);
        if (!filteringEnabled) return false;
        var playerSide = target.CharacterType is CharacterType.Player or CharacterType.Pet;
        if (spellTargetType == SpellTargetType.Player) return !playerSide;
        if (spellTargetType == SpellTargetType.NPC) return playerSide;
        if (spellTargetType == SpellTargetType.NPCPlayer) return playerSide && !CurrentMapFlags.Value.PvPEnabled;
        return false;
    }
    
    public void OnCharacterBecameHidden(Character.Character c)
    {
        if (c != _target) return;
        ResetTargetToPlayer();
    }

    public void OnCharacterErased(Character.Character c)
    {
        if (c != _target) return;
        ResetTargetToPlayer();
    }

    private void ResetTargetToPlayer()
    {
        var mm = GameManager.Instance.CurrentMapManager;
        if (mm == null) return;
        _target = mm.LocalPlayer;
        // PositionReticle() instantiates the reticle, which is only freed by ExitTargeting —
        // reposition only while actively targeting or a stray reticle would sit in the world.
        if (IsTargeting) PositionReticle();
    }

    private string HeldHotkeyAction()
    {
        // Keep tracking the press already in progress so a second hotkey held down mid-press cannot
        // restart the hold; otherwise the lowest hotkey down wins.
        var action = _holdGate.Action;
        if (action != null && IsHotkeyHeld(action)) return action;

        for (int i = 0; i < 10; i++)
        {
            var candidate = HotkeyAction(i);
            if (IsHotkeyHeld(candidate)) return candidate;
        }
        return null;
    }

    private static string HotkeyAction(int index) => index == 9 ? "Hotkey0" : $"Hotkey{index + 1}";

    // exactMatch: Shift+digit is the emote layer and must not cast.
    private static bool IsHotkeyHeld(string action) => Input.IsActionPressed(action, exactMatch: true);

    // allowEcho: false — OS key-repeat must not confirm; holding the key is the hold-to-cast path
    // in _Process (echo would win with an OS-dependent delay).
    private static bool IsHotkeyPressed(InputEvent @event)
    {
        for (int i = 0; i < 10; i++)
            if (@event.IsActionPressed(HotkeyAction(i), exactMatch: true, allowEcho: false)) return true;
        return false;
    }
    
    private void CycleTarget(bool searchDown)
    {
        var mm = GameManager.Instance.CurrentMapManager;
        if (mm == null) return;
        
        var candidates = mm.Characters.Where(c => !c.IsHiddenFromViewer)
            .Select(c => new TargetCandidate(c.LoginId, c.X, c.Y, c.CharacterType)).ToList();
        
        var player = mm.LocalPlayer;
        TargetCandidate? current = _target != null ? new TargetCandidate(_target.LoginId, _target.X, _target.Y, _target.CharacterType) : (TargetCandidate?)null;
        
        var filteringEnabled = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.TargetFiltering, true);

        var viewRange = GetViewRange();

        var next = TargetCycler.Next(candidates, current, (player.X, player.Y), player.LoginId,
            GameManager.Instance.CurrentMap?.Width ?? 100,
            (viewRange.X, viewRange.Y),
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
    
    // A confirm the player asked for (Enter, or a fresh hotkey press) ends the hold: the key has to
    // come up before it can hold-cast again. A hold-confirm deliberately leaves the press repeating.
    private void ConfirmTarget()
    {
        _holdGate.Spend();
        CastOnTarget();
    }

    private void CastOnTarget()
    {
        if (_target != null && _pendingSpell != null)
        {
            GameManager.Instance.SpellCooldownManager.Cast(_pendingSpell.SlotNumber, _pendingSpell.Cooldown);
            GameManager.Instance.NetworkClient.CastSpell(_pendingSpell.SlotNumber, _target.LoginId);
        }
        ExitTargeting();
    }

    private void CancelTarget()
    {
        // Cancel must stick while the key stays down — otherwise the held key would reopen
        // targeting (or start recasting) a moment later.
        _holdGate.Spend();
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
