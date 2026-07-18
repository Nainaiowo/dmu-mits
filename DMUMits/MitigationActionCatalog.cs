using System;
using System.Collections.Generic;
using System.Linq;
using GameAction = Lumina.Excel.Sheets.Action;

namespace DMUMits;

public sealed record MitigationActionInfo(string Name, uint ActionId, uint IconId);

public static class MitigationActionCatalog
{
    private static readonly string[] Names =
    [
        "Reprisal",
        "Rampart",
        "Feint",
        "Addle",
        "Arm's Length",
        "Holmgang",
        "Vengeance",
        "Damnation",
        "Thrill of Battle",
        "Shake It Off",
        "Bloodwhetting",
        "Nascent Flash",
        "Raw Intuition",
        "Equilibrium",
        "Sentinel",
        "Guardian",
        "Hallowed Ground",
        "Bulwark",
        "Sheltron",
        "Holy Sheltron",
        "Intervention",
        "Divine Veil",
        "Passage of Arms",
        "Shadow Wall",
        "Dark Mind",
        "Living Dead",
        "The Blackest Night",
        "Oblation",
        "Dark Missionary",
        "Camouflage",
        "Nebula",
        "Superbolide",
        "Heart of Light",
        "Heart of Stone",
        "Heart of Corundum",
        "Aurora",
        "Sacred Soil",
        "Expedient",
        "Fey Illumination",
        "Seraph",
        "Seraphism",
        "Recitation",
        "Whispering Dawn",
        "Temperance",
        "Plenary Indulgence",
        "Asylum",
        "Liturgy of the Bell",
        "Divine Caress",
        "Collective Unconscious",
        "Neutral Sect",
        "Macrocosmos",
        "Exaltation",
        "Sun Sign",
        "Kerachole",
        "Holos",
        "Panhaima",
        "Physis II",
        "Krasis",
        "Zoe",
        "Philosophia",
        "Magick Barrier",
        "Tempera Grassa",
        "Tactician",
        "Troubadour",
        "Shield Samba",
        "Improvisation",
        "Dismantle",
    ];

    private static readonly HashSet<string> WantedNames = new(Names, StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<MitigationActionInfo>? actionsByNameLength;

    public static uint ResolveIconId(string mitigationText)
    {
        return FindActions(mitigationText, includeCarryover: true)
            .Select(action => action.IconId)
            .FirstOrDefault(iconId => iconId != 0);
    }

    public static IEnumerable<MitigationActionInfo> FindActions(string mitigationText, bool includeCarryover)
    {
        if (string.IsNullOrWhiteSpace(mitigationText))
        {
            yield break;
        }

        EnsureBuilt();
        if (actionsByNameLength is null || actionsByNameLength.Count == 0)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in SplitSegments(mitigationText))
        {
            var trimmed = segment.Text.Trim();
            if (!includeCarryover && segment.IsCarryover)
            {
                continue;
            }

            foreach (var action in actionsByNameLength)
            {
                if (seen.Contains(action.Name) || !ContainsWholeActionName(trimmed, action.Name))
                {
                    continue;
                }

                seen.Add(action.Name);
                yield return action;
            }
        }
    }

    private static void EnsureBuilt()
    {
        if (actionsByNameLength is not null)
        {
            return;
        }

        var actions = new Dictionary<string, MitigationActionInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<GameAction>();
            foreach (var row in sheet)
            {
                if (row.ClassJobLevel == 0 || row.IsPvP)
                {
                    continue;
                }

                var name = row.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name) ||
                    !WantedNames.Contains(name) ||
                    actions.ContainsKey(name))
                {
                    continue;
                }

                actions[name] = new MitigationActionInfo(name, row.RowId, row.Icon);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to build mitigation action catalog.");
        }

        actionsByNameLength = actions.Values
            .OrderByDescending(action => action.Name.Length)
            .ToList();
    }

    private static IEnumerable<MitigationTextSegment> SplitSegments(string text)
    {
        var previousSlashSegmentWasCarryover = false;
        foreach (var slashSegment in text.Split('/'))
        {
            var trimmedSlashSegment = slashSegment.TrimStart();
            var slashSegmentIsCarryover =
                trimmedSlashSegment.StartsWith("->", StringComparison.Ordinal) ||
                previousSlashSegmentWasCarryover && trimmedSlashSegment.StartsWith("+", StringComparison.Ordinal);
            foreach (var plusSegment in slashSegment.Split('+'))
            {
                yield return new MitigationTextSegment(
                    plusSegment,
                    slashSegmentIsCarryover || plusSegment.TrimStart().StartsWith("->", StringComparison.Ordinal));
            }

            previousSlashSegmentWasCarryover = slashSegmentIsCarryover;
        }
    }

    private static bool ContainsWholeActionName(string text, string actionName)
    {
        var index = text.IndexOf(actionName, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 ? ' ' : text[index - 1];
            var afterIndex = index + actionName.Length;
            var after = afterIndex >= text.Length ? ' ' : text[afterIndex];
            if (!char.IsLetter(before) && !char.IsLetter(after))
            {
                return true;
            }

            index = text.IndexOf(actionName, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private readonly record struct MitigationTextSegment(string Text, bool IsCarryover);
}
