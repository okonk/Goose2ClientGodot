using Godot;
using Goose2Client.Network.Packets;
using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.UI;

/// <summary>Single party-member slot in the party roster.</summary>
public partial class PartyMember : Control
{
    private static readonly PackedScene EffectScene = GD.Load<PackedScene>("res://Scenes/UI/PartyEffect.tscn");

    private Label _nameText;
    private TextureProgressBar _hpBar;
    private TextureProgressBar _mpBar;
    private Control _content;
    private HBoxContainer _effectRow;
    private readonly Dictionary<int, PartyEffect> _effectNodes = new();

    public int PlayerId { get; private set; }

    public override void _Ready()
    {
        _nameText = GetNode<Label>("Content/NameText");
        _hpBar = GetNode<TextureProgressBar>("Content/HpBar");
        _mpBar = GetNode<TextureProgressBar>("Content/MpBar");
        _content = GetNode<Control>("Content");
        _content.Visible = false;
        _effectRow = GetNode<HBoxContainer>("Content/EffectRow");
    }

    public void OnGroupUpdate(GroupUpdatePacket packet)
    {
        PlayerId = packet.LoginId;
        _content.Visible = PlayerId != 0;

        if (PlayerId == 0) return;

        _nameText.Text = packet.Name;

        var character = GameManager.Instance.CurrentMapManager?.GetCharacter(PlayerId);
        var hp = character?.HPPercent ?? 1f;
        var mp = character?.MPPercent ?? 1f;

        UpdateHPMP(hp, mp);
    }

    public void UpdateHPMP(float hp, float mp)
    {
        _hpBar.Value = hp;
        _mpBar.Value = mp;
    }

    public void ReconcileEffects(IReadOnlyList<PartyMemberEffect> effects)
    {
        var factor = UiScaleApplier.Instance?.Factor ?? 1f;
        var keep = new HashSet<int>();
        foreach (var effect in effects)
        {
            keep.Add(effect.EffectId);
            if (_effectNodes.TryGetValue(effect.EffectId, out var existing))
            {
                existing.Apply(effect);
                continue;
            }
            var node = EffectScene.Instantiate<PartyEffect>();
            _effectRow.AddChild(node);
            node.Relayout(factor);
            _effectNodes[effect.EffectId] = node;
            node.Apply(effect);
        }

        foreach (var id in _effectNodes.Keys.ToList())
            if (!keep.Contains(id))
            {
                var node = _effectNodes[id];
                node.Clear();
                node.QueueFree();
                _effectNodes.Remove(id);
            }
    }

    public void RelayoutEffects(float factor)
    {
        foreach (var node in _effectNodes.Values)
            if (GodotObject.IsInstanceValid(node))
                node.Relayout(factor);
    }
}
