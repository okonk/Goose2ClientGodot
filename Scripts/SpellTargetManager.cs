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
    
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsTargeting) return;
        
        if (@event is InputEventAction action)
        {
            if (action.Action == "TargetDown" && action.Pressed)
                CycleTarget(searchDown: true);
            else if (action.Action == "TargetUp" && action.Pressed)
                CycleTarget(searchDown: false);
            else if (action.Action == "ConfirmTarget" && action.Pressed)
                ConfirmTarget();
            else if (action.Action == "CancelTarget" && action.Pressed)
                CancelTarget();
            else if (action.Action == "TargetHome" && action.Pressed)
                GoHome();
        }
    }
    
    /// <summary>Begin targeting for the given spell.</summary>
    public void Cast(SpellInfo info)
    {
        _pendingSpell = info;
        IsTargeting = true;
        
        var mm = GameManager.Instance.CurrentMapManager;
        if (mm == null) { ExitTargeting(); return; }
        
        // Start with the local player as initial target
        _target = mm.LocalPlayer;
        PositionReticle();
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
        {
            _reticle = GD.Load<PackedScene>("res://Scenes/UI/SpellTarget.tscn").Instantiate<SpellTarget>();
            AddChild(_reticle);
        }
        
        _reticle.GlobalPosition = _target.Position;
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
        _target = null;
        _pendingSpell = null;
        if (_reticle != null && GodotObject.IsInstanceValid(_reticle))
        {
            _reticle.QueueFree();
            _reticle = null;
        }
    }
}
