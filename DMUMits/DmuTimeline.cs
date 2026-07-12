using System;
using System.Collections.Generic;
using System.Linq;

namespace DMUMits;

public static partial class DmuMitigationData
{
    public const uint DmuTerritoryId = 1363;

    public static readonly IReadOnlyDictionary<DmuPhase, string> PhaseNames = new Dictionary<DmuPhase, string>
    {
        [DmuPhase.P1] = "P1 Kefka",
        [DmuPhase.P2] = "P2 Forsaken Kefka",
        [DmuPhase.P3] = "P3 Chaos & Exdeath",
        [DmuPhase.P4] = "P4 Kefka Says",
        [DmuPhase.P5] = "P5 Ultima Kefka",
    };

    public static IReadOnlyList<DmuTimelineEvent> GetEventsForPhase(DmuPhase phase)
    {
        return Events
            .Where(entry => entry.Phase == phase)
            .OrderBy(entry => entry.PhaseTimeSeconds)
            .ToList();
    }

    public static DmuTimelineEvent? FindSyncEvent(DmuPhase phase, uint actionId, float phaseElapsedSeconds)
    {
        return Events
            .Where(entry => entry.Phase == phase && entry.SyncActionIds.Contains(actionId))
            .OrderBy(entry => MathF.Abs(entry.PhaseTimeSeconds - phaseElapsedSeconds))
            .FirstOrDefault();
    }

    public static DmuTimelineEvent? FindForwardSyncEvent(DmuPhase currentPhase, uint actionId)
    {
        return Events
            .Where(entry => entry.Phase >= currentPhase && entry.SyncActionIds.Contains(actionId))
            .OrderBy(entry => entry.Phase)
            .ThenBy(entry => entry.PhaseTimeSeconds)
            .FirstOrDefault();
    }

    private static IReadOnlyDictionary<PartySlot, string> M(
        string mt = "",
        string ot = "",
        string whm = "",
        string ast = "",
        string sch = "",
        string sge = "",
        string d1 = "",
        string d2 = "",
        string d3 = "",
        string d4 = "")
    {
        var values = new Dictionary<PartySlot, string>();
        Add(values, PartySlot.MT, mt);
        Add(values, PartySlot.OT, ot);
        Add(values, PartySlot.WHM, whm);
        Add(values, PartySlot.AST, ast);
        Add(values, PartySlot.SCH, sch);
        Add(values, PartySlot.SGE, sge);
        Add(values, PartySlot.D1, d1);
        Add(values, PartySlot.D2, d2);
        Add(values, PartySlot.D3, d3);
        Add(values, PartySlot.D4, d4);
        return values;
    }

    private static void Add(Dictionary<PartySlot, string> values, PartySlot slot, string value)
    {
        value = CleanMitigationText(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (values.TryGetValue(slot, out var existing))
        {
            values[slot] = $"{existing} / {value}";
            return;
        }

        values[slot] = value;
    }

    private static string CleanMitigationText(string text)
    {
        return text
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace("  ", " ")
            .Trim();
    }

    private static IReadOnlySet<uint> A(params uint[] actionIds)
    {
        return actionIds.Length == 0
            ? new HashSet<uint>()
            : new HashSet<uint>(actionIds);
    }
}
