using System;
using System.Collections.Generic;
using System.Linq;

namespace DMUMits;

public static class PartySlotHelper
{
    public const string NameKeyPrefix = "Name:";
    private static readonly uint[] TankJobs = [19, 21, 32, 37];
    private static readonly uint[] HealerJobs = [24, 28, 33, 40];
    private static readonly uint[] MeleeJobs = [20, 22, 30, 34, 39, 41];
    private static readonly uint[] PhysicalRangedJobs = [23, 31, 38];
    private static readonly uint[] CasterJobs = [25, 27, 35, 42];

    public static readonly IReadOnlyList<PartySlot> OrderedSlots =
    [
        PartySlot.MT,
        PartySlot.OT,
        PartySlot.WHM,
        PartySlot.AST,
        PartySlot.SCH,
        PartySlot.SGE,
        PartySlot.D1,
        PartySlot.D2,
        PartySlot.D3,
        PartySlot.D4,
    ];

    public static string GetDisplayName(PartySlot slot)
    {
        return slot switch
        {
            PartySlot.MT => "MT",
            PartySlot.OT => "OT",
            PartySlot.WHM => "White Mage",
            PartySlot.AST => "Astrologian",
            PartySlot.SCH => "Scholar",
            PartySlot.SGE => "Sage",
            PartySlot.D1 => "D1",
            PartySlot.D2 => "D2",
            PartySlot.D3 => "D3",
            PartySlot.D4 => "D4",
            _ => slot.ToString(),
        };
    }

    public static IReadOnlyList<PartySlotAssignment> AutoAssign(IReadOnlyList<PartyMemberInfo> members)
    {
        var assignments = OrderedSlots
            .Select(slot => new PartySlotAssignment { Slot = slot })
            .ToList();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        AssignOrdered(assignments, usedKeys, [PartySlot.MT, PartySlot.OT], members.Where(IsTank));

        foreach (var healer in members.Where(IsHealer).OrderBy(member => member.PartyIndex))
        {
            var slot = healer.ClassJobId switch
            {
                24 => PartySlot.WHM,
                28 => PartySlot.SCH,
                33 => PartySlot.AST,
                40 => PartySlot.SGE,
                _ => (PartySlot?)null,
            };
            if (slot is not null)
            {
                Assign(assignments, usedKeys, slot.Value, healer);
            }
        }

        var dps = members
            .Where(member => !usedKeys.Contains(member.Key) && IsDps(member))
            .OrderBy(member => GetDpsSortBucket(member.ClassJobId))
            .ThenBy(member => member.PartyIndex)
            .ToList();
        var melee = dps.Where(member => MeleeJobs.Contains(member.ClassJobId)).ToList();
        var physicalRanged = dps.Where(member => PhysicalRangedJobs.Contains(member.ClassJobId)).ToList();
        var casters = dps.Where(member => CasterJobs.Contains(member.ClassJobId)).ToList();

        TryAssignFirst(assignments, usedKeys, PartySlot.D1, melee);
        TryAssignFirst(assignments, usedKeys, PartySlot.D2, melee);
        TryAssignFirst(assignments, usedKeys, PartySlot.D3, physicalRanged);
        TryAssignFirst(assignments, usedKeys, PartySlot.D4, casters);

        foreach (var remaining in dps.Where(member => !usedKeys.Contains(member.Key)))
        {
            var emptySlot = assignments.FirstOrDefault(assignment =>
                assignment.Slot is PartySlot.D1 or PartySlot.D2 or PartySlot.D3 or PartySlot.D4 &&
                string.IsNullOrWhiteSpace(assignment.MemberKey));
            if (emptySlot is null)
            {
                break;
            }

            SetAssignment(emptySlot, remaining);
            usedKeys.Add(remaining.Key);
        }

        return assignments;
    }

    public static PartySlot? FindSlotForMember(IReadOnlyList<PartySlotAssignment> assignments, PartyMemberInfo? member)
    {
        if (member is null)
        {
            return null;
        }

        var assignment = assignments.FirstOrDefault(candidate => AssignmentMatchesMember(candidate, member));
        return assignment?.Slot ?? GetDefaultSlotForJob(member.ClassJobId);
    }

    public static bool AssignmentMatchesMember(PartySlotAssignment? assignment, PartyMemberInfo? member)
    {
        if (assignment is null || member is null)
        {
            return false;
        }

        return AssignmentMatchesMemberIdentity(assignment, member.Key, member.Name);
    }

    public static bool AssignmentMatchesMemberIdentity(PartySlotAssignment? assignment, string memberKey, string memberName)
    {
        if (assignment is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(assignment.MemberKey) &&
            string.Equals(assignment.MemberKey, memberKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!ShouldUseNameFallback(assignment.MemberKey, memberKey))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(assignment.MemberName) &&
            !string.IsNullOrWhiteSpace(memberName) &&
            string.Equals(NormalizeMemberName(assignment.MemberName), NormalizeMemberName(memberName), StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMemberName(string name)
    {
        return name.Trim();
    }

    public static string BuildNameKey(string name)
    {
        var normalizedName = NormalizeMemberName(name);
        return string.IsNullOrWhiteSpace(normalizedName) ? string.Empty : $"{NameKeyPrefix}{normalizedName}";
    }

    private static bool ShouldUseNameFallback(string assignmentKey, string memberKey)
    {
        return string.IsNullOrWhiteSpace(assignmentKey) ||
            assignmentKey.StartsWith(NameKeyPrefix, StringComparison.Ordinal) ||
            memberKey.StartsWith(NameKeyPrefix, StringComparison.Ordinal);
    }

    public static PartySlot? GetDefaultSlotForJob(uint classJobId)
    {
        return classJobId switch
        {
            24 => PartySlot.WHM,
            28 => PartySlot.SCH,
            33 => PartySlot.AST,
            40 => PartySlot.SGE,
            _ when TankJobs.Contains(classJobId) => PartySlot.MT,
            _ when MeleeJobs.Contains(classJobId) => PartySlot.D1,
            _ when PhysicalRangedJobs.Contains(classJobId) => PartySlot.D3,
            _ when CasterJobs.Contains(classJobId) => PartySlot.D4,
            _ => null,
        };
    }

    public static bool IsTank(PartyMemberInfo member) => TankJobs.Contains(member.ClassJobId);

    public static bool IsHealer(PartyMemberInfo member) => HealerJobs.Contains(member.ClassJobId);

    public static bool IsDps(PartyMemberInfo member)
    {
        return MeleeJobs.Contains(member.ClassJobId) ||
            PhysicalRangedJobs.Contains(member.ClassJobId) ||
            CasterJobs.Contains(member.ClassJobId);
    }

    public static bool IsKnownJob(uint classJobId)
    {
        return TankJobs.Contains(classJobId) ||
            HealerJobs.Contains(classJobId) ||
            MeleeJobs.Contains(classJobId) ||
            PhysicalRangedJobs.Contains(classJobId) ||
            CasterJobs.Contains(classJobId);
    }

    private static void AssignOrdered(
        List<PartySlotAssignment> assignments,
        HashSet<string> usedKeys,
        IReadOnlyList<PartySlot> slots,
        IEnumerable<PartyMemberInfo> members)
    {
        var index = 0;
        foreach (var member in members.OrderBy(member => member.PartyIndex))
        {
            if (index >= slots.Count)
            {
                return;
            }

            Assign(assignments, usedKeys, slots[index], member);
            index++;
        }
    }

    private static void TryAssignFirst(
        List<PartySlotAssignment> assignments,
        HashSet<string> usedKeys,
        PartySlot slot,
        IReadOnlyList<PartyMemberInfo> candidates)
    {
        var member = candidates.FirstOrDefault(candidate => !usedKeys.Contains(candidate.Key));
        if (member is not null)
        {
            Assign(assignments, usedKeys, slot, member);
        }
    }

    private static void Assign(
        List<PartySlotAssignment> assignments,
        HashSet<string> usedKeys,
        PartySlot slot,
        PartyMemberInfo member)
    {
        var assignment = assignments.First(candidate => candidate.Slot == slot);
        SetAssignment(assignment, member);
        usedKeys.Add(member.Key);
    }

    private static void SetAssignment(PartySlotAssignment assignment, PartyMemberInfo member)
    {
        assignment.MemberKey = member.Key;
        assignment.MemberName = member.Name;
        assignment.ClassJobId = member.ClassJobId;
    }

    private static int GetDpsSortBucket(uint classJobId)
    {
        if (MeleeJobs.Contains(classJobId))
        {
            return 0;
        }

        if (PhysicalRangedJobs.Contains(classJobId))
        {
            return 1;
        }

        if (CasterJobs.Contains(classJobId))
        {
            return 2;
        }

        return 9;
    }
}
