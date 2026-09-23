using System;
using System.Collections.Generic;

namespace Goose2Client;

public sealed record PartyMemberEffect(
    int EffectId,
    int GraphicId,
    int GraphicFile,
    string Name,
    long TotalMs,
    bool IsPermanent,
    long ExpiresAt);

public sealed class PartyMemberEffectState
{
    private static readonly IReadOnlyList<PartyMemberEffect> Empty = Array.Empty<PartyMemberEffect>();

    private readonly int[] _slotLoginIds;
    private readonly Dictionary<int, Member> _members = new();

    public PartyMemberEffectState(int maxSlots = 10)
    {
        _slotLoginIds = new int[maxSlots];
        Array.Fill(_slotLoginIds, -1);
    }

    public bool AssignSlot(int slot, int loginId, bool alreadyVisible = false)
    {
        if (slot < 0 || slot >= _slotLoginIds.Length) return false;

        int oldId = _slotLoginIds[slot];
        if (oldId == loginId)
        {
            if (!alreadyVisible) return false;
            var existing = GetOrCreate(loginId);
            if (existing.Visible) return false;
            existing.Visible = true;
            return true;
        }

        if (oldId != -1)
            _members.Remove(oldId);

        _slotLoginIds[slot] = loginId;
        GetOrCreate(loginId).Visible = alreadyVisible;
        return true;
    }

    public bool ClearSlot(int slot)
    {
        if (slot < 0 || slot >= _slotLoginIds.Length) return false;

        int oldId = _slotLoginIds[slot];
        if (oldId == -1) return false;

        _slotLoginIds[slot] = -1;
        _members.Remove(oldId);
        return true;
    }

    public bool MarkVisible(int loginId)
    {
        if (!IsCurrentMember(loginId)) return false;
        var member = GetOrCreate(loginId);
        if (member.Visible) return false;
        member.Visible = true;
        return true;
    }

    public bool Erase(int loginId)
    {
        if (!_members.TryGetValue(loginId, out var member)) return false;
        if (!member.Visible && member.Effects.Count == 0) return false;
        member.Visible = false;
        member.Effects.Clear();
        member.EffectIndex.Clear();
        return true;
    }

    public bool ClearEffects(int loginId)
    {
        if (!_members.TryGetValue(loginId, out var member)) return false;
        if (member.Effects.Count == 0) return false;
        member.Effects.Clear();
        member.EffectIndex.Clear();
        return true;
    }

    public bool UpsertEffect(int loginId, int effectId, int graphicId, int graphicFile, long remainingMs, long totalMs, string name, long nowMs)
    {
        if (!IsCurrentMember(loginId)) return false;
        if (!_members.TryGetValue(loginId, out var member) || !member.Visible) return false;

        bool permanent = remainingMs == 0 && totalMs == 0;
        var effect = new PartyMemberEffect(effectId, graphicId, graphicFile, name, totalMs, permanent, permanent ? 0 : nowMs + remainingMs);

        if (member.EffectIndex.TryGetValue(effectId, out int index))
            member.Effects[index] = effect;
        else
        {
            member.EffectIndex[effectId] = member.Effects.Count;
            member.Effects.Add(effect);
        }

        return true;
    }

    public bool RemoveEffect(int loginId, int effectId)
    {
        if (!_members.TryGetValue(loginId, out var member)) return false;
        if (!member.EffectIndex.TryGetValue(effectId, out int index)) return false;

        member.Effects.RemoveAt(index);
        member.EffectIndex.Remove(effectId);
        for (int i = index; i < member.Effects.Count; i++)
            member.EffectIndex[member.Effects[i].EffectId] = i;

        return true;
    }

    public IReadOnlyList<PartyMemberEffect> GetEffects(int loginId)
    {
        return _members.TryGetValue(loginId, out var member) ? member.Effects : Empty;
    }

    public bool IsVisible(int loginId)
    {
        return _members.TryGetValue(loginId, out var member) && member.Visible;
    }

    private bool IsCurrentMember(int loginId)
    {
        for (int i = 0; i < _slotLoginIds.Length; i++)
            if (_slotLoginIds[i] == loginId) return true;
        return false;
    }

    private Member GetOrCreate(int loginId)
    {
        if (!_members.TryGetValue(loginId, out var member))
        {
            member = new Member();
            _members[loginId] = member;
        }
        return member;
    }

    private sealed class Member
    {
        public bool Visible;
        public List<PartyMemberEffect> Effects = new();
        public Dictionary<int, int> EffectIndex = new();
    }
}
