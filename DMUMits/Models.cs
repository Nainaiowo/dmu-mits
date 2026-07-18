using System;
using System.Collections.Generic;

namespace DMUMits;

public enum DmuPhase
{
    Unknown,
    P1,
    P2,
    P3,
    P4,
    P5,
}

public enum PartySlot
{
    MT,
    OT,
    WHM,
    AST,
    SCH,
    SGE,
    D1,
    D2,
    D3,
    D4,
}

public enum TimelineTimingSource
{
    ActTimeline,
}

public enum DmuTimelineSyncKind
{
    CastStart,
    ActionResolve,
    HeadMarker,
    StatusGain,
    InCombat,
}

public sealed record DmuTimelineEvent(
    string Id,
    DmuPhase Phase,
    string Name,
    float PhaseTimeSeconds,
    TimelineTimingSource TimingSource,
    IReadOnlySet<uint> SyncActionIds,
    IReadOnlyDictionary<PartySlot, string> Mitigations,
    string Extras)
{
    public bool HasMitigationFor(PartySlot slot)
    {
        return Mitigations.TryGetValue(slot, out var mitigation) && !string.IsNullOrWhiteSpace(mitigation);
    }

    public string GetMitigationFor(PartySlot slot)
    {
        return Mitigations.TryGetValue(slot, out var mitigation) ? mitigation : string.Empty;
    }
}

public sealed record DmuTimelineSyncPoint(
    DmuPhase Phase,
    float PhaseTimeSeconds,
    uint ActionId,
    string Label,
    DmuTimelineSyncKind Kind = DmuTimelineSyncKind.CastStart,
    float WindowBeforeSeconds = 2.5f,
    float WindowAfterSeconds = 2.5f,
    bool IsPhaseAnchor = false,
    float? PreviousPhaseObservedTimeSeconds = null,
    float ForwardWindowBeforeSeconds = 2.5f,
    float ForwardWindowAfterSeconds = 2.5f)
{
    public bool MatchesWindow(float observedPhaseTimeSeconds)
    {
        return observedPhaseTimeSeconds >= PhaseTimeSeconds - WindowBeforeSeconds &&
            observedPhaseTimeSeconds <= PhaseTimeSeconds + WindowAfterSeconds;
    }

    public bool MatchesForwardWindow(float observedPreviousPhaseTimeSeconds)
    {
        return PreviousPhaseObservedTimeSeconds is not { } expectedTime ||
            observedPreviousPhaseTimeSeconds >= expectedTime - ForwardWindowBeforeSeconds &&
            observedPreviousPhaseTimeSeconds <= expectedTime + ForwardWindowAfterSeconds;
    }
}

public sealed record UpcomingMitigationEvent(
    DmuTimelineEvent Event,
    float SecondsRemaining,
    bool IsUseNow,
    bool IsNext);

public sealed record MitigationNote(
    int Number,
    string ShortText,
    string DetailText);

public sealed record PartyMemberInfo(
    string Key,
    string Name,
    int PartyIndex,
    ulong ContentId,
    uint EntityId,
    uint ClassJobId);

[Serializable]
public sealed class PartySlotAssignment
{
    public PartySlot Slot { get; set; }

    public string MemberKey { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;

    public uint ClassJobId { get; set; }
}

public sealed record PhaseState(
    DmuPhase Phase,
    DateTime StartedAtUtc,
    string SyncLabel)
{
    public float ElapsedSeconds(DateTime now)
    {
        return Math.Max(0.0f, (float)(now - StartedAtUtc).TotalSeconds);
    }
}
