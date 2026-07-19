using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DMUMits;

public static partial class DmuMitigationData
{
    public const uint DmuTerritoryId = 1363;

    private static readonly Regex MitigationNotePattern = new(@"\s*(?:\((\d+)\)|([⁰¹²³⁴⁵⁶⁷⁸⁹]+))", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<DmuPhase, IReadOnlyDictionary<int, MitigationNote>> MitigationNotes =
        new Dictionary<DmuPhase, IReadOnlyDictionary<int, MitigationNote>>
        {
            [DmuPhase.P1] = new Dictionary<int, MitigationNote>
            {
                [1] = new(1, "90s at first Graven re-center", "Use as Kefka re-centers for first Graven. WAR/PLD: after Revolting Ruin III."),
                [2] = new(2, "30s after Graven castbar", "Use after the first Graven Image castbar."),
                [3] = new(3, "Bell before first puddles", "Use before first puddles; it pops on second puddles."),
                [4] = new(
                    4,
                    "Dissipation before Aetherflow OR early Spreadlo",
                    "Dissipation before Aetherflow. OR use first Spreadlo early so it returns for second-Graven Double-Trouble."),
            },
            [DmuPhase.P2] = new Dictionary<int, MitigationNote>
            {
                [1] = new(1, "ST mit tanks in transition", "ST mit and GCD shield tanks in transition; assist last Ultimate Embrace."),
                [2] = new(2, "Spreadlo tanks before Embrace", "Prepare Spreadlo on OT shortly before, or MT during, Ultimate Embrace."),
                [3] = new(3, "Holos first Embrace", "Use on first Ultimate Embrace so it returns for Light of Judgement; or use for Wings + Embrace."),
                [4] = new(4, "WAR: use early", "Use early to avoid losing value to Shake It Off."),
            },
            [DmuPhase.P3] = new Dictionary<int, MitigationNote>
            {
                [1] = new(1, "30s after textbox", "Use after Kefka's textbox disappears; covers autos, Bowels, and Stray Flames/Tsunami."),
                [2] = new(2, "Cover Thunder + next Stray", "Tight timing. If missed, use next GCD."),
                [3] = new(3, "Use if holding Chaos", "If not holding Chaos, save for P4 autos."),
                [4] = new(4, "Do not pop Accretions early", "Avoid self-heals that can pop Accretion early."),
                [5] = new(5, "Exdeath: Reprisal both bosses", "If holding Exdeath, Reprisal both bosses before The Decisive Battle ends."),
                [6] = new(6, "LB3 west of Vacuum Wave", "Either tank can press it; decide before pull."),
                [7] = new(7, "Can shift Seraphism to P4", "Shift Seraphism to P4 if P3 has enough mitigation."),
                [8] = new(8, "Spreadlo tanks", "Prioritize WAR > DRK > GNB/PLD."),
                [9] = new(9, "Prep after Bowels", "Prepare immediately after Bowels of Agony."),
            },
            [DmuPhase.P4] = new Dictionary<int, MitigationNote>
            {
                [1] = new(1, "Use at phase start", "Use at the beginning of the phase for autos."),
            },
            [DmuPhase.P5] = new Dictionary<int, MitigationNote>
            {
                [1] = new(1, "First use on staff-down cue", "First use when Kefka brings his staff to his right side; later uses on cooldown."),
                [2] = new(2, "Watch tanks after invulns", "Prepare burst healing after WAR/DRK invulns for the third auto."),
                [3] = new(3, "2 GCDs after Stray Apocalypse", "Use two GCDs after Stray Apocalypse so it returns for Forsaken."),
                [4] = new(4, "Use during Celestriad castbar", "Use during the Celestriad castbar."),
                [5] = new(5, "After third towers resolve", "Use after the third towers in Celestriad resolve."),
            },
        };

    public static readonly IReadOnlyDictionary<DmuPhase, string> PhaseNames = new Dictionary<DmuPhase, string>
    {
        [DmuPhase.P1] = "P1 Kefka",
        [DmuPhase.P2] = "P2 Forsaken Kefka",
        [DmuPhase.P3] = "P3 Chaos & Exdeath",
        [DmuPhase.P4] = "P4 Kefka Says",
        [DmuPhase.P5] = "P5 Ultima Kefka",
    };

    private static readonly IReadOnlyDictionary<DmuPhase, float> ActPhaseStartTimes = new Dictionary<DmuPhase, float>
    {
        [DmuPhase.P1] = 0.0f,
        [DmuPhase.P2] = 207.6f,
        [DmuPhase.P3] = 540.3f,
        [DmuPhase.P4] = 995.0f,
        [DmuPhase.P5] = 1255.6f,
    };

    public static IReadOnlyList<DmuTimelineEvent> GetEventsForPhase(DmuPhase phase)
    {
        return Events
            .Where(entry => entry.Phase == phase)
            .OrderBy(entry => entry.PhaseTimeSeconds)
            .ToList();
    }

    public static DmuPhase? GetNextPhase(DmuPhase phase)
    {
        return phase switch
        {
            DmuPhase.P1 => DmuPhase.P2,
            DmuPhase.P2 => DmuPhase.P3,
            DmuPhase.P3 => DmuPhase.P4,
            DmuPhase.P4 => DmuPhase.P5,
            _ => null,
        };
    }

    public static bool TryGetActNextPhaseStartElapsed(DmuPhase phase, out float phaseElapsed)
    {
        phaseElapsed = 0.0f;
        if (GetNextPhase(phase) is not { } nextPhase ||
            !ActPhaseStartTimes.TryGetValue(phase, out var currentPhaseStart) ||
            !ActPhaseStartTimes.TryGetValue(nextPhase, out var nextPhaseStart))
        {
            return false;
        }

        phaseElapsed = nextPhaseStart - currentPhaseStart;
        return phaseElapsed >= 0.0f;
    }

    public static DmuTimelineSyncPoint? FindVisibleCastSyncPoint(
        DmuPhase phase,
        uint actionId,
        DmuTimelineSyncKind kind,
        float observedPhaseTimeSeconds,
        IReadOnlySet<string> firedAnchorKeys)
    {
        return GetVisibleCastSyncPoints(actionId, kind)
            .Where(entry =>
                entry.Phase == phase &&
                !firedAnchorKeys.Contains(GetSyncAnchorKey(entry)) &&
                entry.MatchesWindow(observedPhaseTimeSeconds))
            .Select(entry => new
            {
                SyncPoint = entry,
                Difference = MathF.Abs(entry.PhaseTimeSeconds - observedPhaseTimeSeconds),
            })
            .OrderBy(entry => entry.Difference)
            .Select(entry => entry.SyncPoint)
            .FirstOrDefault();
    }

    public static DmuTimelineSyncPoint? FindForwardVisibleCastPhaseAnchor(
        DmuPhase currentPhase,
        uint actionId,
        DmuTimelineSyncKind kind,
        float observedPreviousPhaseTimeSeconds,
        IReadOnlySet<string> firedAnchorKeys)
    {
        return GetVisibleCastSyncPoints(actionId, kind)
            .Where(entry =>
                entry.Phase > currentPhase &&
                entry.IsPhaseAnchor &&
                entry.MatchesForwardWindow(observedPreviousPhaseTimeSeconds) &&
                !firedAnchorKeys.Contains(GetSyncAnchorKey(entry)))
            .OrderBy(entry => entry.Phase)
            .ThenBy(entry => entry.PhaseTimeSeconds)
            .FirstOrDefault();
    }

    public static IReadOnlyList<DmuTimelineSyncPoint> GetVisibleCastSyncPoints(uint actionId, DmuTimelineSyncKind kind)
    {
        return SyncPoints
            .Where(entry => entry.Kind == kind && entry.ActionId == actionId)
            .ToList();
    }

    public static string GetSyncAnchorKey(DmuTimelineSyncPoint syncPoint)
    {
        var phaseTime = syncPoint.PhaseTimeSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"{syncPoint.Phase}:{syncPoint.Kind}:{syncPoint.ActionId:X}:{phaseTime}";
    }

    public static string GetMitigationDisplayText(DmuTimelineEvent entry, PartySlot slot)
    {
        return GetMitigationDisplayText(entry, slot, 0);
    }

    public static string GetMitigationDisplayText(DmuTimelineEvent entry, PartySlot slot, uint classJobId)
    {
        var text = FormatMitigationText(entry.Phase, GetMitigationFor(entry, slot));
        return classJobId == 0 ? text : MitigationTextResolver.ResolveForJob(text, classJobId);
    }

    public static bool HasMitigationFor(DmuTimelineEvent entry, PartySlot slot)
    {
        return !string.IsNullOrWhiteSpace(GetMitigationFor(entry, slot));
    }

    public static string GetMitigationFor(DmuTimelineEvent entry, PartySlot slot)
    {
        var mitigations = GetMitigations(entry);
        return mitigations.TryGetValue(slot, out var mitigation) ? mitigation : string.Empty;
    }

    public static IReadOnlyDictionary<PartySlot, string> GetMitigations(DmuTimelineEvent entry)
    {
        return TryGetSheetMitigationOverride(entry.Id, out var mitigations)
            ? mitigations
            : entry.Mitigations;
    }

    public static IReadOnlyList<MitigationNote> GetMitigationNotes(DmuTimelineEvent entry, PartySlot slot)
    {
        var text = GetMitigationFor(entry, slot);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var notes = new List<MitigationNote>();
        var added = new HashSet<int>();
        foreach (Match match in MitigationNotePattern.Matches(text))
        {
            if (!TryReadMitigationNoteNumber(match, out var number) ||
                !added.Add(number) ||
                !TryGetMitigationNote(entry.Phase, number, out var note))
            {
                continue;
            }

            notes.Add(note);
        }

        return notes;
    }

    public static string GetMitigationNoteMarker(int number)
    {
        return ToSuperscript(number);
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
            .Replace("Fey Illumuniation", "Fey Illumination", StringComparison.OrdinalIgnoreCase)
            .Replace("Scared Soil", "Sacred Soil", StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ")
            .Trim();
    }

    private static string FormatMitigationText(DmuPhase phase, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MitigationNotePattern.Replace(text, match =>
        {
            if (!TryReadMitigationNoteNumber(match, out var number) ||
                !TryGetMitigationNote(phase, number, out _))
            {
                return match.Value;
            }

            return ToSuperscript(number);
        });
    }

    private static bool TryGetMitigationNote(DmuPhase phase, int number, out MitigationNote note)
    {
        if (MitigationNotes.TryGetValue(phase, out var phaseNotes) &&
            phaseNotes.TryGetValue(number, out var foundNote))
        {
            note = foundNote;
            return true;
        }

        note = null!;
        return false;
    }

    private static bool TryReadMitigationNoteNumber(Match match, out int number)
    {
        if (int.TryParse(match.Groups[1].Value, out number))
        {
            return true;
        }

        number = 0;
        var superscript = match.Groups[2].Value;
        if (string.IsNullOrWhiteSpace(superscript))
        {
            return false;
        }

        foreach (var digit in superscript)
        {
            var value = digit switch
            {
                '⁰' => 0,
                '¹' => 1,
                '²' => 2,
                '³' => 3,
                '⁴' => 4,
                '⁵' => 5,
                '⁶' => 6,
                '⁷' => 7,
                '⁸' => 8,
                '⁹' => 9,
                _ => -1,
            };

            if (value < 0)
            {
                number = 0;
                return false;
            }

            number = (number * 10) + value;
        }

        return true;
    }

    private static string ToSuperscript(int number)
    {
        return string.Concat(number.ToString().Select(digit => digit switch
        {
            '0' => '⁰',
            '1' => '¹',
            '2' => '²',
            '3' => '³',
            '4' => '⁴',
            '5' => '⁵',
            '6' => '⁶',
            '7' => '⁷',
            '8' => '⁸',
            '9' => '⁹',
            _ => digit,
        }));
    }

    private static IReadOnlySet<uint> A(params uint[] actionIds)
    {
        return actionIds.Length == 0
            ? new HashSet<uint>()
            : new HashSet<uint>(actionIds);
    }
}
