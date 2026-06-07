using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>Single party-member slot in the party roster.</summary>
public partial class PartyMember : Control
{
    private Label _nameText;
    private TextureProgressBar _hpBar;
    private TextureProgressBar _mpBar;
    private Control _content;

    public int PlayerId { get; private set; }

    public override void _Ready()
    {
        _nameText = GetNode<Label>("Content/NameText");
        _hpBar = GetNode<TextureProgressBar>("Content/HpBar");
        _mpBar = GetNode<TextureProgressBar>("Content/MpBar");
        _content = GetNode<Control>("Content");
        _content.Visible = false;
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
}
