using DMUMits.Windows;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.DutyState;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DMUMits;

public sealed class Plugin : IDalamudPlugin
{
    private const string MainCommandName = "/dmumits";
    private const string ShortCommandName = "/dmit";
    private static readonly TimeSpan SyncPollInterval = TimeSpan.FromSeconds(1);
    private const float SyncDriftToleranceSeconds = 1.5f;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("DMUMits");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private DateTime lastSyncPollAtUtc = DateTime.MinValue;
    private int? lastPartySignature;
    private bool wasInCombat;

    public Configuration Configuration { get; }

    public IReadOnlyList<PartyMemberInfo> CurrentParty { get; private set; } = [];

    public PhaseState? CurrentPhaseState { get; private set; }

    public PartySlot? LocalSlot { get; private set; }

    public bool IsInDmu => ClientState.TerritoryType == DmuMitigationData.DmuTerritoryId;

    public bool IsInCombat => Condition[ConditionFlag.InCombat];

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        EnsureSlotList();

        mainWindow = new MainWindow(this)
        {
            IsOpen = Configuration.ShowWindow,
        };
        configWindow = new ConfigWindow(this);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(MainCommandName, new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Open DMU Mits settings.",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Open DMU Mits settings.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigWindow;
        Framework.Update += OnFrameworkUpdate;
        DutyState.DutyStarted += OnDutyReset;
        DutyState.DutyWiped += OnDutyReset;
        DutyState.DutyRecommenced += OnDutyReset;
    }

    public void Dispose()
    {
        DutyState.DutyRecommenced -= OnDutyReset;
        DutyState.DutyWiped -= OnDutyReset;
        DutyState.DutyStarted -= OnDutyReset;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigWindow;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        CommandManager.RemoveHandler(ShortCommandName);
        CommandManager.RemoveHandler(MainCommandName);
        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        mainWindow.Dispose();
    }

    public void SetHelperWindowLocked(bool locked)
    {
        Configuration.LockHelperWindow = locked;
        SaveConfiguration();
    }

    public void ToggleConfigWindow()
    {
        configWindow.Toggle();
    }

    public void SetShowWindow(bool enabled)
    {
        Configuration.ShowWindow = enabled;
        mainWindow.IsOpen = enabled;
        SaveConfiguration();
    }

    public void SetPreviewWhenInactive(bool enabled)
    {
        Configuration.PreviewWhenInactive = enabled;
        SaveConfiguration();
    }

    public void SetFontScale(float value)
    {
        Configuration.FontScale = Math.Clamp(value, 0.75f, 1.75f);
        SaveConfiguration();
    }

    public void SetWindowOpacity(float value)
    {
        Configuration.WindowOpacity = Math.Clamp(value, 0.0f, 1.0f);
        SaveConfiguration();
    }

    public void SetLookAheadSeconds(float value)
    {
        Configuration.LookAheadSeconds = Math.Clamp(value, 30.0f, 180.0f);
        SaveConfiguration();
    }

    public void SetUseNowLeadSeconds(float value)
    {
        Configuration.UseNowLeadSeconds = Math.Clamp(value, 4.0f, 25.0f);
        SaveConfiguration();
    }

    public void SaveConfiguration()
    {
        EnsureSlotList();
        Configuration.Save();
    }

    public void AutoAssignPartySlots()
    {
        RefreshParty(force: true);
        EnsureSlotList();
        var autoAssignments = PartySlotHelper.AutoAssign(CurrentParty)
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.MemberKey))
            .ToList();

        foreach (var autoAssignment in autoAssignments)
        {
            ClearMatchingAssignments(autoAssignment.MemberKey, autoAssignment.MemberName);
        }

        foreach (var autoAssignment in autoAssignments)
        {
            var target = Configuration.PartySlots.FirstOrDefault(assignment => assignment.Slot == autoAssignment.Slot);
            if (target is null)
            {
                continue;
            }

            CopyAssignment(target, autoAssignment);
        }

        RefreshLocalSlot();
        SaveConfiguration();
    }

    public void AssignSlot(PartySlot slot, PartyMemberInfo? member)
    {
        EnsureSlotList();
        var assignment = Configuration.PartySlots.First(entry => entry.Slot == slot);
        if (member is null)
        {
            assignment.MemberKey = string.Empty;
            assignment.MemberName = string.Empty;
            assignment.ClassJobId = 0;
        }
        else
        {
            foreach (var other in Configuration.PartySlots.Where(slot =>
                slot.Slot != assignment.Slot &&
                PartySlotHelper.AssignmentMatchesMember(slot, member)))
            {
                ClearAssignment(other);
            }

            assignment.MemberKey = member.Key;
            assignment.MemberName = member.Name;
            assignment.ClassJobId = member.ClassJobId;
        }

        RefreshLocalSlot();
        SaveConfiguration();
    }

    public IReadOnlyList<UpcomingMitigationEvent> GetUpcomingEvents(DateTime now)
    {
        var phase = CurrentPhaseState?.Phase ?? DmuPhase.P1;
        var phaseElapsed = CurrentPhaseState?.ElapsedSeconds(now) ?? 0.0f;
        if (CurrentPhaseState is null && !Configuration.PreviewWhenInactive)
        {
            return [];
        }

        var slot = LocalSlot;
        var events = DmuMitigationData.GetEventsForPhase(phase)
            .Where(entry => entry.PhaseTimeSeconds >= phaseElapsed - 2.0f)
            .Select(entry => new UpcomingMitigationEvent(
                entry,
                entry.PhaseTimeSeconds - phaseElapsed,
                slot is not null && entry.HasMitigationFor(slot.Value) && entry.PhaseTimeSeconds - phaseElapsed <= Configuration.UseNowLeadSeconds,
                false))
            .Where(entry => entry.SecondsRemaining <= Configuration.LookAheadSeconds)
            .OrderBy(entry => entry.SecondsRemaining)
            .Take(8)
            .ToList();

        var nextMitIndex = slot is null
            ? -1
            : events.FindIndex(entry => entry.Event.HasMitigationFor(slot.Value));
        if (nextMitIndex < 0)
        {
            return events;
        }

        return events
            .Select((entry, index) => entry with { IsNext = index == nextMitIndex })
            .ToList();
    }

    public string GetUseNowText(DateTime now)
    {
        var slot = LocalSlot;
        if (slot is null)
        {
            return "Set your party slot";
        }

        var upcoming = GetUpcomingEvents(now);
        var useNow = upcoming.FirstOrDefault(entry => entry.IsUseNow && entry.Event.HasMitigationFor(slot.Value));
        if (useNow is not null)
        {
            return useNow.Event.GetMitigationFor(slot.Value);
        }

        var next = upcoming.FirstOrDefault(entry => entry.Event.HasMitigationFor(slot.Value));
        if (next is null)
        {
            return "No upcoming mit";
        }

        return $"Next: {next.Event.GetMitigationFor(slot.Value)}";
    }

    private void OnMainCommand(string command, string args)
    {
        ToggleConfigWindow();
    }

    private void OnDutyReset(IDutyStateEventArgs args)
    {
        ResetPhaseState();
        lastPartySignature = null;
        wasInCombat = false;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now - lastSyncPollAtUtc < SyncPollInterval)
        {
            return;
        }

        lastSyncPollAtUtc = now;
        RefreshParty();
        RefreshPhaseState(now);
    }

    private void RefreshParty(bool force = false)
    {
        var signature = BuildPartySignature();
        if (!force && lastPartySignature == signature)
        {
            return;
        }

        lastPartySignature = signature;
        RebuildPartySnapshot();
    }

    private int BuildPartySignature()
    {
        var hash = new HashCode();
        hash.Add(ClientState.TerritoryType);
        hash.Add(PlayerState.ContentId);
        hash.Add(PartyList.Length);
        hash.Add(PartyList.PartyId);
        hash.Add(PartyList.IsAlliance);

        foreach (var member in PartyList)
        {
            hash.Add(member.Name.TextValue, StringComparer.OrdinalIgnoreCase);
            hash.Add(member.ContentId);
            hash.Add(member.EntityId);
            hash.Add(member.ClassJob.RowId);
        }

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is not null)
        {
            hash.Add(localPlayer.Name.TextValue, StringComparer.OrdinalIgnoreCase);
            hash.Add(localPlayer.EntityId);
            hash.Add(localPlayer.ClassJob.RowId);
        }

        return hash.ToHashCode();
    }

    private void RebuildPartySnapshot()
    {
        var members = new List<PartyMemberInfo>();
        var trackedEntityIds = new HashSet<uint>();
        var trackedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localContentId = PlayerState.ContentId;
        var index = 0;
        foreach (var member in PartyList)
        {
            var name = member.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                index++;
                continue;
            }

            var key = member.ContentId != 0
                ? member.ContentId.ToString("X16")
                : PartySlotHelper.BuildNameKey(name);
            members.Add(new PartyMemberInfo(
                key,
                name,
                index,
                member.ContentId,
                member.EntityId,
                member.ClassJob.RowId));
            if (member.EntityId != 0)
            {
                trackedEntityIds.Add(member.EntityId);
            }

            trackedNames.Add(name);
            index++;
        }

        AddLocalPlayerIfMissing(members, trackedEntityIds, trackedNames, index, localContentId);

        CurrentParty = members;
        ReconcilePartyAssignments();
        if (Configuration.PartySlots.All(slot => string.IsNullOrWhiteSpace(slot.MemberKey)) && CurrentParty.Count > 0)
        {
            Configuration.PartySlots = PartySlotHelper.AutoAssign(CurrentParty).ToList();
            SaveConfiguration();
        }

        RefreshLocalSlot();
    }

    private static void AddLocalPlayerIfMissing(
        List<PartyMemberInfo> members,
        HashSet<uint> trackedEntityIds,
        HashSet<string> trackedNames,
        int partyIndex,
        ulong localContentId)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return;
        }

        var memberName = localPlayer.Name.TextValue;
        if (string.IsNullOrWhiteSpace(memberName) ||
            (localPlayer.EntityId != 0 && trackedEntityIds.Contains(localPlayer.EntityId)) ||
            trackedNames.Contains(memberName))
        {
            return;
        }

        var memberKey = localContentId != 0
            ? localContentId.ToString("X16")
            : PartySlotHelper.BuildNameKey(memberName);
        if (string.IsNullOrWhiteSpace(memberKey))
        {
            return;
        }

        members.Add(new PartyMemberInfo(
            memberKey,
            memberName,
            partyIndex,
            localContentId,
            localPlayer.EntityId,
            localPlayer.ClassJob.RowId));
    }

    private void ReconcilePartyAssignments()
    {
        var changed = false;
        foreach (var assignment in Configuration.PartySlots.Where(assignment => !string.IsNullOrWhiteSpace(assignment.MemberName)))
        {
            var matchingMember = CurrentParty.FirstOrDefault(member =>
                !string.IsNullOrWhiteSpace(assignment.MemberKey) &&
                string.Equals(assignment.MemberKey, member.Key, StringComparison.Ordinal)) ??
                FindUniqueCurrentPartyMemberByName(assignment.MemberName);
            if (matchingMember is null)
            {
                continue;
            }

            if (!string.Equals(assignment.MemberKey, matchingMember.Key, StringComparison.Ordinal) ||
                !string.Equals(assignment.MemberName, matchingMember.Name, StringComparison.Ordinal) ||
                assignment.ClassJobId != matchingMember.ClassJobId)
            {
                assignment.MemberKey = matchingMember.Key;
                assignment.MemberName = matchingMember.Name;
                assignment.ClassJobId = matchingMember.ClassJobId;
                changed = true;
            }
        }

        if (changed)
        {
            SaveConfiguration();
        }
    }

    private PartyMemberInfo? FindUniqueCurrentPartyMemberByName(string memberName)
    {
        var normalizedName = PartySlotHelper.NormalizeMemberName(memberName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        var matches = CurrentParty
            .Where(member => string.Equals(PartySlotHelper.NormalizeMemberName(member.Name), normalizedName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private void ClearMatchingAssignments(string memberKey, string memberName)
    {
        foreach (var assignment in Configuration.PartySlots)
        {
            if (PartySlotHelper.AssignmentMatchesMemberIdentity(assignment, memberKey, memberName))
            {
                ClearAssignment(assignment);
            }
        }
    }

    private static void CopyAssignment(PartySlotAssignment target, PartySlotAssignment source)
    {
        target.MemberKey = source.MemberKey;
        target.MemberName = source.MemberName;
        target.ClassJobId = source.ClassJobId;
    }

    private static void ClearAssignment(PartySlotAssignment assignment)
    {
        assignment.MemberKey = string.Empty;
        assignment.MemberName = string.Empty;
        assignment.ClassJobId = 0;
    }

    private void RefreshLocalSlot()
    {
        var localContentId = PlayerState.ContentId;
        var localEntityId = ObjectTable.LocalPlayer?.EntityId ?? 0;
        var localMember = CurrentParty.FirstOrDefault(member =>
            (localContentId != 0 && member.ContentId == localContentId) ||
            (localEntityId != 0 && member.EntityId == localEntityId));
        LocalSlot = PartySlotHelper.FindSlotForMember(Configuration.PartySlots, localMember);
    }

    private void RefreshPhaseState(DateTime now)
    {
        if (!IsInDmu)
        {
            ResetPhaseState();
            wasInCombat = false;
            return;
        }

        var inCombat = IsInCombat;
        if (inCombat && !wasInCombat)
        {
            SetPhase(DmuPhase.P1, now, "Combat start");
        }
        wasInCombat = inCombat;

        if (!inCombat && CurrentPhaseState is not null)
        {
            return;
        }

        var livePhase = DetectLivePhase();
        if (livePhase is not DmuPhase.Unknown &&
            (CurrentPhaseState is null || livePhase > CurrentPhaseState.Phase))
        {
            SetPhase(livePhase, now, "Live phase signal");
        }

        SyncFromVisibleCasts(now);
    }

    private DmuPhase DetectLivePhase()
    {
        if (!IsInDmu)
        {
            return DmuPhase.Unknown;
        }

        var sawP3Boss = false;
        var sawP4Signal = false;
        var sawP5Signal = false;
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is not IBattleNpc battleNpc)
            {
                continue;
            }

            var name = battleNpc.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (name.Contains("Ultima Kefka", StringComparison.OrdinalIgnoreCase))
            {
                sawP5Signal = true;
            }

            if (name.Contains("Neo Exdeath", StringComparison.OrdinalIgnoreCase))
            {
                sawP4Signal = true;
            }

            if (name.Contains("Chaos", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Exdeath", StringComparison.OrdinalIgnoreCase))
            {
                sawP3Boss = true;
            }
        }

        if (CurrentPartyHasAnyStatus(5543, 5544, 5545, 5546, 5547, 5548, 4887, 4888, 454, 5464))
        {
            sawP4Signal = true;
        }

        if (sawP5Signal)
        {
            return DmuPhase.P5;
        }

        if (sawP4Signal)
        {
            return DmuPhase.P4;
        }

        return sawP3Boss ? DmuPhase.P3 : DmuPhase.Unknown;
    }

    private bool CurrentPartyHasAnyStatus(params uint[] statusIds)
    {
        var statusSet = statusIds.ToHashSet();
        foreach (var member in PartyList)
        {
            foreach (var status in member.Statuses)
            {
                if (statusSet.Contains(status.StatusId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void SyncFromVisibleCasts(DateTime now)
    {
        var phase = CurrentPhaseState?.Phase ?? DmuPhase.P1;
        var phaseElapsed = CurrentPhaseState?.ElapsedSeconds(now) ?? 0.0f;
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is not IBattleNpc battleNpc ||
                battleNpc is not IBattleChara battleChara ||
                !battleChara.IsCasting ||
                battleChara.CastActionId == 0)
            {
                continue;
            }

            var syncEvent = CurrentPhaseState is null
                ? DmuMitigationData.FindForwardSyncEvent(DmuPhase.P1, battleChara.CastActionId)
                : DmuMitigationData.FindSyncEvent(phase, battleChara.CastActionId, phaseElapsed) ??
                    DmuMitigationData.FindForwardSyncEvent(GetNextPhase(phase), battleChara.CastActionId);
            if (syncEvent is null)
            {
                continue;
            }

            var observedPhaseElapsed = Math.Max(0.0f, syncEvent.PhaseTimeSeconds - battleChara.CurrentCastTime);
            if (CurrentPhaseState is null || syncEvent.Phase != phase)
            {
                CurrentPhaseState = new PhaseState(
                    syncEvent.Phase,
                    now.AddSeconds(-observedPhaseElapsed),
                    $"Synced to {syncEvent.Name}");
                return;
            }

            var drift = MathF.Abs(observedPhaseElapsed - phaseElapsed);
            if (drift <= SyncDriftToleranceSeconds)
            {
                continue;
            }

            CurrentPhaseState = new PhaseState(
                syncEvent.Phase,
                now.AddSeconds(-observedPhaseElapsed),
                $"Synced to {syncEvent.Name}");
            return;
        }
    }

    private void SetPhase(DmuPhase phase, DateTime now, string label)
    {
        CurrentPhaseState = new PhaseState(phase, now, label);
    }

    private void ResetPhaseState()
    {
        CurrentPhaseState = null;
    }

    private void EnsureSlotList()
    {
        var orderedSlots = PartySlotHelper.OrderedSlots;
        var configuredSlots = Configuration.PartySlots ?? [];
        Configuration.PartySlots = configuredSlots
            .OfType<PartySlotAssignment>()
            .Where(entry => orderedSlots.Contains(entry.Slot))
            .GroupBy(entry => entry.Slot)
            .Select(group => group.First())
            .ToList();

        foreach (var slot in PartySlotHelper.OrderedSlots)
        {
            if (Configuration.PartySlots.All(entry => entry.Slot != slot))
            {
                Configuration.PartySlots.Add(new PartySlotAssignment { Slot = slot });
            }
        }

        Configuration.PartySlots = Configuration.PartySlots
            .Where(entry => orderedSlots.Contains(entry.Slot))
            .OrderBy(entry => GetSlotOrder(entry.Slot))
            .ToList();
    }

    private static int GetSlotOrder(PartySlot slot)
    {
        for (var index = 0; index < PartySlotHelper.OrderedSlots.Count; index++)
        {
            if (PartySlotHelper.OrderedSlots[index] == slot)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static DmuPhase GetNextPhase(DmuPhase phase)
    {
        return phase switch
        {
            DmuPhase.Unknown => DmuPhase.P1,
            DmuPhase.P1 => DmuPhase.P2,
            DmuPhase.P2 => DmuPhase.P3,
            DmuPhase.P3 => DmuPhase.P4,
            DmuPhase.P4 => DmuPhase.P5,
            _ => DmuPhase.P5,
        };
    }
}
