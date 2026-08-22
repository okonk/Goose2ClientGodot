using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;
using System.Collections.Generic;

namespace Goose2Client.UI;

/// <summary>Party/group roster — 8 member slots with vitals bars.</summary>
public partial class PartyWindow : Control, IScalableWindow
{
    public const int MaxMembers = 8;

    private static readonly PackedScene MemberScene = GD.Load<PackedScene>("res://Scenes/UI/PartyMember.tscn");

    private PartyMember[] _members;
    private bool _listenersRegistered;
    private List<UiScaleLayout.GeomRecord> _geom = null!;

    public override void _Ready()
    {
        _members = new PartyMember[MaxMembers];
        var memberList = GetNode<VBoxContainer>("MemberList");

        for (int i = 0; i < MaxMembers; i++)
        {
            var member = MemberScene.Instantiate<PartyMember>();
            memberList.AddChild(member);
            _members[i] = member;
        }

        GameManager.Instance.PacketManager.Listen<GroupUpdatePacket>(OnGroupUpdate);
        GameManager.Instance.PacketManager.Listen<VitalsPercentagePacket>(OnVitalsPercentage);
        GameManager.Instance.PacketManager.Listen<EraseCharacterPacket>(OnEraseCharacter);
        GameManager.Instance.PacketManager.Listen<MakeCharacterPacket>(OnMakeCharacter);
        _listenersRegistered = true;

        var applier = UiScaleApplier.Instance;
        _geom = UiScaleLayout.Snapshot(this);
        applier.RegisterWindow(this);
        Relayout();
        TreeExited += () => applier.UnregisterWindow(this);
    }

    public void Relayout()
    {
        UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<GroupUpdatePacket>(OnGroupUpdate);
        GameManager.Instance.PacketManager.Remove<VitalsPercentagePacket>(OnVitalsPercentage);
        GameManager.Instance.PacketManager.Remove<EraseCharacterPacket>(OnEraseCharacter);
        GameManager.Instance.PacketManager.Remove<MakeCharacterPacket>(OnMakeCharacter);
    }

    private void OnGroupUpdate(object o)
    {
        var p = (GroupUpdatePacket)o;
        if (p.LineNumber < 0 || p.LineNumber >= _members.Length) return;
        _members[p.LineNumber].OnGroupUpdate(p);
    }

    private void OnVitalsPercentage(object o)
    {
        var p = (VitalsPercentagePacket)o;
        UpdateMember(p.LoginId, p.HPPercentage, p.MPPercentage);
    }

    private void OnEraseCharacter(object o)
    {
        var p = (EraseCharacterPacket)o;
        UpdateMember(p.LoginId, 0, 0);
    }

    private void OnMakeCharacter(object o)
    {
        var p = (MakeCharacterPacket)o;
        UpdateMember(p.LoginId, p.HPPercent, 1);
    }

    private void UpdateMember(int id, float hp, float mp)
    {
        foreach (var m in _members)
            if (m.PlayerId == id)
                m.UpdateHPMP(hp, mp);
    }
}
