using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DMUMits;

public sealed record MitigationSheetImportResult(
    IReadOnlyDictionary<string, IReadOnlyDictionary<PartySlot, string>> Overrides,
    int ParsedRows,
    int MatchedRows,
    int UnmatchedRows,
    IReadOnlyList<string> UnmatchedMechanics,
    IReadOnlyList<string> Warnings)
{
    public static MitigationSheetImportResult Empty { get; } =
        new(new Dictionary<string, IReadOnlyDictionary<PartySlot, string>>(), 0, 0, 0, [], []);
}

internal static class MitigationSheetImporter
{
    private static readonly Regex PhasePattern = new(@"\b(?:phase\s*)?p?([1-5])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BracketTextPattern = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex AndWordPattern = new(@"\band\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonNameCharacterPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex StandaloneNumberPattern = new(@"\b(?:[0-9]+|i|ii|iii|iv|v|vi|vii|viii|ix|x)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RomanNumeralPattern = new(@"\b(?:i|ii|iii|iv|v|vi|vii|viii|ix|x)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, PartySlot> SlotHeaders =
        new Dictionary<string, PartySlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["MT"] = PartySlot.MT,
            ["MAIN TANK"] = PartySlot.MT,
            ["T1"] = PartySlot.MT,
            ["OT"] = PartySlot.OT,
            ["OFF TANK"] = PartySlot.OT,
            ["T2"] = PartySlot.OT,
            ["WHM"] = PartySlot.WHM,
            ["WHITE MAGE"] = PartySlot.WHM,
            ["AST"] = PartySlot.AST,
            ["ASTRO"] = PartySlot.AST,
            ["ASTROLOGIAN"] = PartySlot.AST,
            ["SCH"] = PartySlot.SCH,
            ["SCHOLAR"] = PartySlot.SCH,
            ["SGE"] = PartySlot.SGE,
            ["SAGE"] = PartySlot.SGE,
            ["D1"] = PartySlot.D1,
            ["DPS1"] = PartySlot.D1,
            ["M1"] = PartySlot.D1,
            ["MELEE 1"] = PartySlot.D1,
            ["D2"] = PartySlot.D2,
            ["DPS2"] = PartySlot.D2,
            ["M2"] = PartySlot.D2,
            ["MELEE 2"] = PartySlot.D2,
            ["D3"] = PartySlot.D3,
            ["DPS3"] = PartySlot.D3,
            ["R1"] = PartySlot.D3,
            ["PRANGE"] = PartySlot.D3,
            ["PHYS RANGE"] = PartySlot.D3,
            ["PHYSICAL RANGE"] = PartySlot.D3,
            ["RANGED"] = PartySlot.D3,
            ["D4"] = PartySlot.D4,
            ["DPS4"] = PartySlot.D4,
            ["R2"] = PartySlot.D4,
            ["CASTER"] = PartySlot.D4,
            ["MAGICAL RANGE"] = PartySlot.D4,
        };

    private sealed record ColumnLayout(int HeaderRow, int PhaseColumn, int MechanicColumn, IReadOnlyDictionary<PartySlot, int> SlotColumns);

    private sealed record ImportedSheetRow(DmuPhase Phase, string Mechanic, IReadOnlyDictionary<PartySlot, string> Mitigations, int SourceRow);

    public static MitigationSheetImportResult Import(string raw, DmuPhase defaultPhase, bool active)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            DmuMitigationData.SetImportedSheetMitigationOverrides(new Dictionary<string, IReadOnlyDictionary<PartySlot, string>>(), false);
            return MitigationSheetImportResult.Empty;
        }

        var warnings = new List<string>();
        var grid = ParseGrid(raw);
        if (grid.Count == 0)
        {
            DmuMitigationData.SetImportedSheetMitigationOverrides(new Dictionary<string, IReadOnlyDictionary<PartySlot, string>>(), active);
            return new MitigationSheetImportResult(new Dictionary<string, IReadOnlyDictionary<PartySlot, string>>(), 0, 0, 0, [], ["No usable rows found."]);
        }

        if (!TryFindLayout(grid, out var layout))
        {
            DmuMitigationData.SetImportedSheetMitigationOverrides(new Dictionary<string, IReadOnlyDictionary<PartySlot, string>>(), active);
            return new MitigationSheetImportResult(
                new Dictionary<string, IReadOnlyDictionary<PartySlot, string>>(),
                0,
                0,
                0,
                [],
                ["Could not find a header row with a mechanic column and mitigation slot columns."]);
        }

        var importedRows = ParseRows(grid, layout, defaultPhase, warnings);
        var result = MergeRows(importedRows, warnings);
        DmuMitigationData.SetImportedSheetMitigationOverrides(result.Overrides, active);
        return result;
    }

    private static MitigationSheetImportResult MergeRows(IReadOnlyList<ImportedSheetRow> rows, IReadOnlyList<string> warnings)
    {
        var overrides = new Dictionary<string, IReadOnlyDictionary<PartySlot, string>>();
        var unmatched = new List<string>();
        var usedEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedRows = 0;

        foreach (var row in rows)
        {
            var match = FindTimelineMatch(row, usedEventIds);
            if (match is null)
            {
                unmatched.Add($"{row.Phase} row {row.SourceRow}: {row.Mechanic}");
                continue;
            }

            usedEventIds.Add(match.Id);
            overrides[match.Id] = row.Mitigations;
            matchedRows++;
        }

        return new MitigationSheetImportResult(
            overrides,
            rows.Count,
            matchedRows,
            unmatched.Count,
            unmatched,
            warnings);
    }

    private static DmuTimelineEvent? FindTimelineMatch(ImportedSheetRow row, ISet<string> usedEventIds)
    {
        var candidates = DmuMitigationData.Events
            .Where(entry => entry.Phase == row.Phase && !usedEventIds.Contains(entry.Id))
            .OrderBy(entry => entry.PhaseTimeSeconds)
            .Select(entry => new
            {
                Event = entry,
                StrictName = NormalizeMechanicName(entry.Name, removeOccurrenceNumbers: false),
                LooseName = NormalizeMechanicName(entry.Name, removeOccurrenceNumbers: true),
            })
            .ToList();

        var rowStrict = NormalizeMechanicName(row.Mechanic, removeOccurrenceNumbers: false);
        var rowLoose = NormalizeMechanicName(row.Mechanic, removeOccurrenceNumbers: true);
        return candidates.FirstOrDefault(entry => entry.StrictName == rowStrict)?.Event ??
            candidates.FirstOrDefault(entry => IsContainedName(rowStrict, entry.StrictName))?.Event ??
            candidates.FirstOrDefault(entry => entry.LooseName == rowLoose)?.Event ??
            candidates.FirstOrDefault(entry => IsContainedName(rowLoose, entry.LooseName))?.Event;
    }

    private static bool IsContainedName(string left, string right)
    {
        return left.Length >= 5 &&
            right.Length >= 5 &&
            (left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
                right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ImportedSheetRow> ParseRows(
        IReadOnlyList<string[]> grid,
        ColumnLayout layout,
        DmuPhase defaultPhase,
        ICollection<string> warnings)
    {
        var rows = new List<ImportedSheetRow>();
        var currentPhase = defaultPhase;
        for (var index = layout.HeaderRow + 1; index < grid.Count; index++)
        {
            var row = grid[index];
            var rowNumber = index + 1;
            var phase = layout.PhaseColumn >= 0
                ? TryReadPhase(Cell(row, layout.PhaseColumn))
                : DmuPhase.Unknown;
            if (phase == DmuPhase.Unknown && LooksLikePhaseHeader(row, out var sectionPhase))
            {
                currentPhase = sectionPhase;
                continue;
            }

            if (phase != DmuPhase.Unknown)
            {
                currentPhase = phase;
            }

            var mechanic = CleanCell(Cell(row, layout.MechanicColumn));
            if (string.IsNullOrWhiteSpace(mechanic))
            {
                continue;
            }

            if (currentPhase == DmuPhase.Unknown)
            {
                warnings.Add($"Skipped row {rowNumber} ({mechanic}) because no phase was found before it.");
                continue;
            }

            var mitigations = new Dictionary<PartySlot, string>();
            foreach (var (slot, column) in layout.SlotColumns)
            {
                var text = CleanMitigationCell(Cell(row, column));
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                mitigations[slot] = text;
            }

            if (mitigations.Count == 0)
            {
                continue;
            }

            rows.Add(new ImportedSheetRow(currentPhase, mechanic, mitigations, rowNumber));
        }

        return rows;
    }

    private static bool TryFindLayout(IReadOnlyList<string[]> grid, out ColumnLayout layout)
    {
        for (var rowIndex = 0; rowIndex < Math.Min(grid.Count, 20); rowIndex++)
        {
            var row = grid[rowIndex];
            var slotColumns = new Dictionary<PartySlot, int>();
            for (var columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                if (TryReadSlotHeader(row[columnIndex], out var slot) && !slotColumns.ContainsKey(slot))
                {
                    slotColumns[slot] = columnIndex;
                }
            }

            if (slotColumns.Count == 0)
            {
                continue;
            }

            var phaseColumn = FindPhaseColumn(row);
            var mechanicColumn = FindMechanicColumn(row, slotColumns.Values);
            if (mechanicColumn < 0)
            {
                continue;
            }

            layout = new ColumnLayout(rowIndex, phaseColumn, mechanicColumn, slotColumns);
            return true;
        }

        layout = null!;
        return false;
    }

    private static int FindPhaseColumn(IReadOnlyList<string> header)
    {
        for (var index = 0; index < header.Count; index++)
        {
            var value = NormalizeHeader(header[index]);
            if (value is "PHASE" or "PART" or "SECTION")
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindMechanicColumn(IReadOnlyList<string> header, IEnumerable<int> slotColumnIndexes)
    {
        for (var index = 0; index < header.Count; index++)
        {
            var value = NormalizeHeader(header[index]);
            if (value.Contains("MECHANIC", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("DAMAGE", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("HIT", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CAST", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("ABILITY", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("EVENT", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        var firstSlotColumn = slotColumnIndexes.DefaultIfEmpty(-1).Min();
        if (firstSlotColumn > 0)
        {
            for (var index = firstSlotColumn - 1; index >= 0; index--)
            {
                var value = NormalizeHeader(header[index]);
                if (!string.IsNullOrWhiteSpace(value) && value is not "TIME" and not "TIMER" and not "PHASE" and not "PART" and not "SECTION")
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static bool TryReadSlotHeader(string value, out PartySlot slot)
    {
        var header = NormalizeHeader(value);
        if (SlotHeaders.TryGetValue(header, out slot))
        {
            return true;
        }

        slot = default;
        return false;
    }

    private static bool LooksLikePhaseHeader(IReadOnlyList<string> row, out DmuPhase phase)
    {
        phase = DmuPhase.Unknown;
        var nonEmpty = row.Select(CleanCell).Where(cell => !string.IsNullOrWhiteSpace(cell)).ToList();
        if (nonEmpty.Count is 0 or > 2)
        {
            return false;
        }

        phase = TryReadPhase(nonEmpty[0]);
        return phase != DmuPhase.Unknown;
    }

    private static DmuPhase TryReadPhase(string value)
    {
        value = CleanCell(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return DmuPhase.Unknown;
        }

        var match = PhasePattern.Match(value);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var phase))
        {
            return DmuPhase.Unknown;
        }

        return phase switch
        {
            1 => DmuPhase.P1,
            2 => DmuPhase.P2,
            3 => DmuPhase.P3,
            4 => DmuPhase.P4,
            5 => DmuPhase.P5,
            _ => DmuPhase.Unknown,
        };
    }

    private static string NormalizeHeader(string value)
    {
        return CleanCell(value)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }

    private static string NormalizeMechanicName(string value, bool removeOccurrenceNumbers)
    {
        value = CleanCell(value)
            .Replace("judgement", "judgment", StringComparison.OrdinalIgnoreCase)
            .Replace("embrance", "embrace", StringComparison.OrdinalIgnoreCase)
            .Replace("&", " ", StringComparison.Ordinal)
            .Replace("+", " ", StringComparison.Ordinal);
        value = BracketTextPattern.Replace(value, " ");
        value = value.ToLowerInvariant();
        value = ReplaceRomanNumerals(value);
        value = AndWordPattern.Replace(value, " ");
        if (removeOccurrenceNumbers)
        {
            value = StandaloneNumberPattern.Replace(value, " ");
        }

        value = NonNameCharacterPattern.Replace(value, " ");
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ReplaceRomanNumerals(string value)
    {
        return RomanNumeralPattern.Replace(value, match => match.Value.ToLowerInvariant() switch
        {
            "i" => "1",
            "ii" => "2",
            "iii" => "3",
            "iv" => "4",
            "v" => "5",
            "vi" => "6",
            "vii" => "7",
            "viii" => "8",
            "ix" => "9",
            "x" => "10",
            _ => match.Value,
        });
    }

    private static string CleanMitigationCell(string value)
    {
        value = CleanCell(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "-" or "--" or "N/A" or "NA" or "NONE" or "OK"
            ? string.Empty
            : value;
    }

    private static string CleanCell(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\u00a0', ' ')
            .Replace("Fey Illumuniation", "Fey Illumination", StringComparison.OrdinalIgnoreCase)
            .Replace("Scared Soil", "Sacred Soil", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static List<string[]> ParseGrid(string raw)
    {
        var delimiter = raw.Contains('\t') ? '\t' : ',';
        var rows = new List<string[]>();
        foreach (var line in raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            rows.Add(SplitLine(line, delimiter));
        }

        return rows;
    }

    private static string[] SplitLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == delimiter && !inQuotes)
            {
                cells.Add(builder.ToString().Trim());
                builder.Clear();
            }
            else
            {
                builder.Append(current);
            }
        }

        cells.Add(builder.ToString().Trim());
        return cells.ToArray();
    }

    private static string Cell(IReadOnlyList<string> row, int index)
    {
        return index >= 0 && index < row.Count ? row[index] : string.Empty;
    }
}
