using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DMUMits;

public static class MitigationTextResolver
{
    private static readonly Regex GenericMitPattern =
        new(@"(?<prefix>->\s*)?(?<term>Party Mit|Buddy Mit|Short Mit|Invulnerability)(?:\s*\((?<qualifier>[^)]*)\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<uint, string> JobAbbreviations = new Dictionary<uint, string>
    {
        [19] = "PLD",
        [20] = "MNK",
        [21] = "WAR",
        [22] = "DRG",
        [23] = "BRD",
        [24] = "WHM",
        [25] = "BLM",
        [27] = "SMN",
        [28] = "SCH",
        [30] = "NIN",
        [31] = "MCH",
        [32] = "DRK",
        [33] = "AST",
        [34] = "SAM",
        [35] = "RDM",
        [37] = "GNB",
        [38] = "DNC",
        [39] = "RPR",
        [40] = "SGE",
        [41] = "VPR",
        [42] = "PCT",
    };

    private static readonly IReadOnlySet<string> KnownJobAbbreviations =
        new HashSet<string>(JobAbbreviations.Values, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GenericMitigations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Party Mit"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WAR"] = "Shake It Off",
                ["PLD"] = "Divine Veil",
                ["DRK"] = "Dark Missionary",
                ["GNB"] = "Heart of Light",
                ["BRD"] = "Troubadour",
                ["MCH"] = "Tactician",
                ["DNC"] = "Shield Samba",
                ["RDM"] = "Magick Barrier",
                ["PCT"] = "Tempera Grassa",
                ["WHM"] = "Temperance",
                ["AST"] = "Neutral Sect",
                ["SCH"] = "Sacred Soil",
                ["SGE"] = "Kerachole",
            },
            ["Buddy Mit"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WAR"] = "Nascent Flash",
                ["PLD"] = "Intervention",
                ["DRK"] = "The Blackest Night",
                ["GNB"] = "Heart of Corundum",
            },
            ["Short Mit"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WAR"] = "Bloodwhetting",
                ["PLD"] = "Holy Sheltron",
                ["DRK"] = "The Blackest Night",
                ["GNB"] = "Heart of Corundum",
            },
            ["Invulnerability"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WAR"] = "Holmgang",
                ["PLD"] = "Hallowed Ground",
                ["DRK"] = "Living Dead",
                ["GNB"] = "Superbolide",
            },
        };

    public static string GetJobAbbreviation(uint classJobId)
    {
        return JobAbbreviations.TryGetValue(classJobId, out var job) ? job : string.Empty;
    }

    public static string ResolveForJob(string text, uint classJobId)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !JobAbbreviations.TryGetValue(classJobId, out var job))
        {
            return text;
        }

        var resolved = GenericMitPattern.Replace(text, match =>
        {
            var term = match.Groups["term"].Value;
            if (!GenericMitigations.TryGetValue(term, out var jobMap) ||
                !jobMap.TryGetValue(job, out var ability))
            {
                return match.Value;
            }

            var qualifier = match.Groups["qualifier"].Value.Trim();
            if (IsJobQualifier(qualifier))
            {
                return QualifierIncludesJob(qualifier, job)
                    ? $"{match.Groups["prefix"].Value}{ability}"
                    : string.Empty;
            }

            return string.IsNullOrWhiteSpace(qualifier)
                ? $"{match.Groups["prefix"].Value}{ability}"
                : $"{match.Groups["prefix"].Value}{ability} ({qualifier})";
        });

        return CleanResolvedText(resolved);
    }

    private static bool IsJobQualifier(string qualifier)
    {
        if (string.IsNullOrWhiteSpace(qualifier))
        {
            return false;
        }

        var tokens = Regex.Split(qualifier, @"[^A-Za-z]+");
        var foundAny = false;
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            foundAny = true;
            if (!KnownJobAbbreviations.Contains(token))
            {
                return false;
            }
        }

        return foundAny;
    }

    private static bool QualifierIncludesJob(string qualifier, string job)
    {
        foreach (var token in Regex.Split(qualifier, @"[^A-Za-z]+"))
        {
            if (string.Equals(token, job, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CleanResolvedText(string text)
    {
        var cleaned = Regex.Replace(text, @"\s+", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"\s*\+\s*(/|$)", " $1").Trim();
        cleaned = Regex.Replace(cleaned, @"^\s*\+\s*", string.Empty).Trim();
        cleaned = Regex.Replace(cleaned, @"^\s*/\s*", string.Empty).Trim();
        cleaned = Regex.Replace(cleaned, @"\s*/\s*$", string.Empty).Trim();
        cleaned = Regex.Replace(cleaned, @"\s*/\s*/\s*", " / ").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+\+\s+", " + ").Trim();
        cleaned = Regex.Replace(cleaned, @"\s*/\s*", " / ").Trim();
        return cleaned;
    }
}
