using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;
using System;
using System.Collections.Generic;

namespace Goose2Client.UI;

/// <summary>Party/group roster — 10 member slots with vitals bars and buff icons.</summary>
public partial class PartyWindow : Control, IScalableWindow
{
    public const int MaxMembers = 10;

    private static readonly PackedScene MemberScene = GD.Load<PackedScene>("res://Scenes/UI/PartyMember.tscn");

    private PartyMember[] _members;
    private PartyMemberEffectState _effects;
    private bool _listenersRegistered;
    private List<UiScaleLayout.GeomRecord> _geom = null!;

    public override void _Ready()
    {
        _members = new PartyMember[MaxMembers];
        _effects = new PartyMemberEffectState(MaxMembers);
        var memberList = GetNode<VBoxContainer>("MemberList");

        for (int i = 0; i < MaxMembers; i++)
        {
            var member = MemberScene.Instantiate<PartyMember>();
            memberList.AddChild(member);
            _members[i] = member;
        }

        HudWindowDrag.Attach(this, "Party");

        var pm = GameManager.Instance.PacketManager;
        pm.Listen<GroupUpdatePacket>(OnGroupUpdate);
        pm.Listen<VitalsPercentagePacket>(OnVitalsPercentage);
        pm.Listen<EraseCharacterPacket>(OnEraseCharacter);
        pm.Listen<MakeCharacterPacket>(OnMakeCharacter);
        pm.Listen<PartyBuffAddPacket>(OnPartyBuffAdd);
        pm.Listen<PartyBuffRemovePacket>(OnPartyBuffRemove);
        pm.Listen<PartyBuffClearPacket>(OnPartyBuffClear);
        _listenersRegistered = true;

        var applier = UiScaleApplier.Instance;
        _geom = UiScaleLayout.Snapshot(this);
        applier.RegisterWindow(this);
        Relayout();
        TreeExited += () => applier.UnregisterWindow(this);
    }

    public void Relayout()
    {
        var applier = UiScaleApplier.Instance;
        UiScaleLayout.Apply(_geom, applier.Factor);
        foreach (var tile in _members)
        {
            tile.CustomMinimumSize = PartyMemberMetrics.MinSize(applier.Factor);
            tile.RelayoutEffects(applier.Factor);
        }
        UpdateSize();
        HudWindowDrag.RepositionFromSaved(this, "Party");
    }

    private void UpdateSize()
    {
        int count = 0;
        foreach (var m in _members)
            if (m.Visible) count++;
        var factor = UiScaleApplier.Instance.Factor;
        int w = UiScale.ScaleSize(PartyMemberMetrics.FrameWidthPx, factor);
        int row = PartyMemberMetrics.MinSize(factor).Y;
        int sep = UiScale.ScaleSize(1f, factor);
        int h = count == 0 ? 0 : count * row + (count - 1) * sep;
        var list = GetNode<VBoxContainer>("MemberList");
        list.Size = new Vector2(w, h);
        Size = new Vector2(w, h);
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        var pm = GameManager.Instance.PacketManager;
        pm.Remove<GroupUpdatePacket>(OnGroupUpdate);
        pm.Remove<VitalsPercentagePacket>(OnVitalsPercentage);
        pm.Remove<EraseCharacterPacket>(OnEraseCharacter);
        pm.Remove<MakeCharacterPacket>(OnMakeCharacter);
        pm.Remove<PartyBuffAddPacket>(OnPartyBuffAdd);
        pm.Remove<PartyBuffRemovePacket>(OnPartyBuffRemove);
        pm.Remove<PartyBuffClearPacket>(OnPartyBuffClear);
    }

    private void OnGroupUpdate(object o) => ApplyGroupUpdate((GroupUpdatePacket)o);
    private void OnVitalsPercentage(object o)
    {
        var p = (VitalsPercentagePacket)o;
        UpdateMember(p.LoginId, p.HPPercentage, p.MPPercentage);
    }
    private void OnEraseCharacter(object o) => ApplyEraseCharacter((EraseCharacterPacket)o);
    private void OnMakeCharacter(object o) => ApplyMakeCharacter((MakeCharacterPacket)o);
    private void OnPartyBuffAdd(object o) => ApplyPartyBuffAdd((PartyBuffAddPacket)o);
    private void OnPartyBuffRemove(object o) => ApplyPartyBuffRemove((PartyBuffRemovePacket)o);
    private void OnPartyBuffClear(object o) => ApplyPartyBuffClear((PartyBuffClearPacket)o);

    internal void ApplyGroupUpdate(GroupUpdatePacket p)
    {
        if (p.LineNumber < 0 || p.LineNumber >= MaxMembers) return;
        if (p.LoginId == 0)
            _effects.ClearSlot(p.LineNumber);
        else
            _effects.AssignSlot(p.LineNumber, p.LoginId,
                GameManager.Instance.CurrentMapManager?.GetCharacter(p.LoginId) != null);
        _members[p.LineNumber].OnGroupUpdate(p);
        _members[p.LineNumber].ReconcileEffects(_effects.GetEffects(_members[p.LineNumber].PlayerId));
        UpdateSize();
    }

    internal void ApplyMakeCharacter(MakeCharacterPacket p)
    {
        _effects.MarkVisible(p.LoginId);
        UpdateMember(p.LoginId, p.HPPercent, 1);
        ReconcileMember(p.LoginId);
    }

    internal void ApplyEraseCharacter(EraseCharacterPacket p)
    {
        _effects.Erase(p.LoginId);
        ReconcileMember(p.LoginId);
        UpdateMember(p.LoginId, 0, 0);
    }

    internal bool ApplyPartyBuffAdd(PartyBuffAddPacket p)
    {
        if (!_effects.UpsertEffect(p.LoginId, p.EffectId, p.GraphicId, p.GraphicFile,
                p.RemainingMs, p.TotalMs, p.Name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            return false;
        ReconcileMember(p.LoginId);
        return true;
    }

    internal bool ApplyPartyBuffRemove(PartyBuffRemovePacket p)
    {
        if (!_effects.RemoveEffect(p.LoginId, p.EffectId)) return false;
        ReconcileMember(p.LoginId);
        return true;
    }

    internal bool ApplyPartyBuffClear(PartyBuffClearPacket p)
    {
        if (!_effects.ClearEffects(p.LoginId)) return false;
        ReconcileMember(p.LoginId);
        return true;
    }

    private void ReconcileMember(int loginId)
    {
        for (int i = 0; i < MaxMembers; i++)
            if (_members[i].PlayerId == loginId)
            {
                _members[i].ReconcileEffects(_effects.GetEffects(loginId));
                return;
            }
    }

    private void UpdateMember(int id, float hp, float mp)
    {
        foreach (var m in _members)
            if (m.PlayerId == id)
                m.UpdateHPMP(hp, mp);
    }
}
