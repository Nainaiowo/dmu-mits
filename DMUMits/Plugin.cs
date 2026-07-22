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
    private sealed record VisibleSyncCast(uint ActionId, float CurrentCastTime, float TotalCastTime)
    {
        public float TimeToResolveSeconds()
        {
            return Math.Max(0.0f, TotalCastTime - CurrentCastTime);
        }
    }

    private const string MainCommandName = "/dmumits";
    private const string ShortCommandName = "/dmit";
    private static readonly TimeSpan SyncPollInterval = TimeSpan.FromSeconds(1);
    private const float PreviewLeadInSeconds = 14.0f;
    private const float PreviewLoopPaddingSeconds = 12.0f;
    private const uint P2WingsOfDestructionLeftActionId = 47821;
    private const uint P2WingsOfDestructionRightActionId = 47822;
    private const uint P2WingsOfDestructionBusterCastActionId = 50311;
    private const uint P2UltimateEmbraceActionId = 49740;
    private const float P2UltimateEmbraceCastSeconds = 5.0f;
    private const float P3TargetableAfterFinalP2EmbraceSeconds = 50.3f;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("DMUMits");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly DateTime previewStartedAtUtc = DateTime.UtcNow;
    private readonly HashSet<string> firedTimelineAnchorKeys = [];
    private DateTime lastSyncPollAtUtc = DateTime.MinValue;
    private int? lastPartySignature;
    private DateTime? pendingP3StartAtUtc;
    private bool sawLateP2Wings;
    private bool wasInCombat;

    public Configuration Configuration { get; }

    public MitigationSheetImportResult MitigationSheetImportResult { get; private set; } = MitigationSheetImportResult.Empty;

    public IReadOnlyList<PartyMemberInfo> CurrentParty { get; private set; } = [];

    public PhaseState? CurrentPhaseState { get; private set; }

    public PartySlot? LocalSlot { get; private set; }

    public bool IsInDmu => ClientState.TerritoryType == DmuMitigationData.DmuTerritoryId;

    public bool IsInCombat => Condition[ConditionFlag.InCombat];

    public bool IsPreviewActive => CurrentPhaseState is null && Configuration.PreviewWhenInactive;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        EnsureSlotList();
        RebuildImportedMitigationSheet();

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

    public void SetHelperWindowClickThrough(bool enabled)
    {
        Configuration.ClickThroughHelperWindow = enabled;
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

    public void SetMitigationSheetDefaultPhase(DmuPhase phase)
    {
        Configuration.MitigationSheetDefaultPhase = phase is >= DmuPhase.Unknown and <= DmuPhase.P5
            ? phase
            : DmuPhase.Unknown;
        RebuildImportedMitigationSheet();
        SaveConfiguration();
    }

    public void SetUseImportedMitigationSheet(bool active)
    {
        Configuration.UseImportedMitigationSheet = active &&
            !string.IsNullOrWhiteSpace(Configuration.ImportedMitigationSheetText);
        RebuildImportedMitigationSheet();
        SaveConfiguration();
    }

    public void ImportMitigationSheet(string rawSheetText)
    {
        Configuration.ImportedMitigationSheetText = rawSheetText ?? string.Empty;
        Configuration.UseImportedMitigationSheet = !string.IsNullOrWhiteSpace(Configuration.ImportedMitigationSheetText);
        RebuildImportedMitigationSheet();
        SaveConfiguration();
    }

    public void ClearImportedMitigationSheet()
    {
        Configuration.ImportedMitigationSheetText = string.Empty;
        Configuration.UseImportedMitigationSheet = false;
        RebuildImportedMitigationSheet();
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
        Configuration.PartySlots = PartySlotHelper.AutoAssign(CurrentParty).ToList();
        EnsureSlotList();
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

    public void SwapSlots(PartySlot sourceSlot, PartySlot targetSlot)
    {
        if (sourceSlot == targetSlot)
        {
            return;
        }

        EnsureSlotList();
        var source = Configuration.PartySlots.FirstOrDefault(slot => slot.Slot == sourceSlot);
        var target = Configuration.PartySlots.FirstOrDefault(slot => slot.Slot == targetSlot);
        if (source is null || target is null)
        {
            return;
        }

        (source.MemberKey, target.MemberKey) = (target.MemberKey, source.MemberKey);
        (source.MemberName, target.MemberName) = (target.MemberName, source.MemberName);
        (source.ClassJobId, target.ClassJobId) = (target.ClassJobId, source.ClassJobId);
        RefreshLocalSlot();
        SaveConfiguration();
    }

    public uint GetClassJobIdForSlot(PartySlot slot)
    {
        var assignment = Configuration.PartySlots.FirstOrDefault(assignment => assignment.Slot == slot);
        if (assignment?.ClassJobId > 0)
        {
            return assignment.ClassJobId;
        }

        return slot == LocalSlot
            ? ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0
            : 0;
    }

    public IReadOnlyList<UpcomingMitigationEvent> GetUpcomingEvents(DateTime now)
    {
        if (CurrentPhaseState is null)
        {
            return Configuration.PreviewWhenInactive
                ? GetPreviewUpcomingEvents(now, 8)
                : [];
        }

        return GetLiveUpcomingEvents(now, 8);
    }

    public float GetPreviewElapsedSeconds(DateTime now)
    {
        var events = DmuMitigationData.GetEventsForPhase(DmuPhase.P1);
        if (events.Count == 0)
        {
            return 0.0f;
        }

        var firstEventTime = events[0].PhaseTimeSeconds;
        var loopStart = MathF.Max(0.0f, firstEventTime - PreviewLeadInSeconds);
        var loopLength = GetPreviewLoopLength(events, loopStart);
        var elapsedInLoop = (float)(now - previewStartedAtUtc).TotalSeconds % loopLength;
        return loopStart + elapsedInLoop;
    }

    private IReadOnlyList<UpcomingMitigationEvent> GetLiveUpcomingEvents(DateTime now, int takeCount)
    {
        var phase = CurrentPhaseState?.Phase ?? DmuPhase.P1;
        var phaseElapsed = CurrentPhaseState?.ElapsedSeconds(now) ?? 0.0f;
        var slot = LocalSlot;
        var upcoming = GetUpcomingEventsForPhase(phase, phaseElapsed, slot)
            .Concat(GetNextPhaseVisualUpcomingEvents(now, phase, phaseElapsed, slot))
            .OrderBy(entry => entry.SecondsRemaining)
            .ToList();
        return MarkNextMitigation(upcoming, slot)
            .Take(takeCount)
            .ToList();
    }

    private IEnumerable<UpcomingMitigationEvent> GetUpcomingEventsForPhase(
        DmuPhase phase,
        float phaseElapsed,
        PartySlot? slot)
    {
        return DmuMitigationData.GetEventsForPhase(phase)
            .Select(entry => CreateUpcomingEvent(entry, entry.PhaseTimeSeconds - phaseElapsed, slot))
            .Where(entry => entry.SecondsRemaining >= -2.0f && entry.SecondsRemaining <= Configuration.LookAheadSeconds);
    }

    private IEnumerable<UpcomingMitigationEvent> GetNextPhaseVisualUpcomingEvents(
        DateTime now,
        DmuPhase phase,
        float phaseElapsed,
        PartySlot? slot)
    {
        if (DmuMitigationData.GetNextPhase(phase) is not { } nextPhase ||
            !TryGetNextPhaseStartsIn(now, phase, phaseElapsed, out var nextPhaseStartsIn))
        {
            return [];
        }

        return DmuMitigationData.GetEventsForPhase(nextPhase)
            .Select(entry => CreateUpcomingEvent(entry, nextPhaseStartsIn + entry.PhaseTimeSeconds, slot))
            .Where(entry => entry.SecondsRemaining >= -2.0f && entry.SecondsRemaining <= Configuration.LookAheadSeconds);
    }

    private bool TryGetNextPhaseStartsIn(DateTime now, DmuPhase phase, float phaseElapsed, out float secondsRemaining)
    {
        secondsRemaining = 0.0f;
        if (phase == DmuPhase.P2 && pendingP3StartAtUtc is { } scheduledP3Start)
        {
            secondsRemaining = (float)(scheduledP3Start - now).TotalSeconds;
            return true;
        }

        if (!DmuMitigationData.TryGetActNextPhaseStartElapsed(phase, out var nextPhaseStartElapsed))
        {
            return false;
        }

        secondsRemaining = nextPhaseStartElapsed - phaseElapsed;
        return true;
    }

    private UpcomingMitigationEvent CreateUpcomingEvent(
        DmuTimelineEvent entry,
        float secondsRemaining,
        PartySlot? slot)
    {
        return new UpcomingMitigationEvent(
            entry,
            secondsRemaining,
            slot is not null && HasVisibleMitigationForSlot(entry, slot.Value) && secondsRemaining <= Configuration.UseNowLeadSeconds,
            false);
    }

    private IReadOnlyList<UpcomingMitigationEvent> GetPreviewUpcomingEvents(DateTime now, int takeCount)
    {
        var events = DmuMitigationData.GetEventsForPhase(DmuPhase.P1);
        if (events.Count == 0)
        {
            return [];
        }

        var slot = LocalSlot;
        var firstEventTime = events[0].PhaseTimeSeconds;
        var loopStart = MathF.Max(0.0f, firstEventTime - PreviewLeadInSeconds);
        var phaseElapsed = GetPreviewElapsedSeconds(now);
        var loopLength = GetPreviewLoopLength(events, loopStart);

        var upcoming = events
            .Select(entry =>
            {
                var secondsRemaining = entry.PhaseTimeSeconds - phaseElapsed;
                while (secondsRemaining < -2.0f)
                {
                    secondsRemaining += loopLength;
                }

                return new UpcomingMitigationEvent(
                    entry,
                    secondsRemaining,
                    slot is not null && HasVisibleMitigationForSlot(entry, slot.Value) && secondsRemaining <= Configuration.UseNowLeadSeconds,
                    false);
            })
            .OrderBy(entry => entry.SecondsRemaining)
            .ToList();
        return MarkNextMitigation(upcoming, slot)
            .Take(takeCount)
            .ToList();
    }

    private IReadOnlyList<UpcomingMitigationEvent> MarkNextMitigation(
        IReadOnlyList<UpcomingMitigationEvent> events,
        PartySlot? slot)
    {
        if (events.Count == 0)
        {
            return events;
        }

        var nextMitIndex = -1;
        if (slot is not null)
        {
            for (var index = 0; index < events.Count; index++)
            {
                if (HasVisibleMitigationForSlot(events[index].Event, slot.Value))
                {
                    nextMitIndex = index;
                    break;
                }
            }
        }
        if (nextMitIndex < 0)
        {
            return events;
        }

        return events
            .Select((entry, index) => entry with { IsNext = index == nextMitIndex })
            .ToList();
    }

    private static float GetPreviewLoopLength(IReadOnlyList<DmuTimelineEvent> events, float loopStart)
    {
        var lastEventTime = events[^1].PhaseTimeSeconds;
        return MathF.Max(PreviewLeadInSeconds + PreviewLoopPaddingSeconds, lastEventTime + PreviewLoopPaddingSeconds - loopStart);
    }

    public string GetUseNowText(DateTime now)
    {
        var slot = LocalSlot;
        if (slot is null)
        {
            return "Set your party slot";
        }

        var highlighted = GetHighlightedMitigationEvent(now, slot.Value);
        if (highlighted is null)
        {
            return "No upcoming mit";
        }

        var text = DmuMitigationData.GetMitigationDisplayText(highlighted.Event, slot.Value, GetClassJobIdForSlot(slot.Value));
        return text;
    }

    public UpcomingMitigationEvent? GetHighlightedMitigationEvent(DateTime now)
    {
        var slot = LocalSlot;
        return slot is null
            ? null
            : GetHighlightedMitigationEvent(now, slot.Value);
    }

    public IReadOnlyList<MitigationNote> GetUseNowNotes(DateTime now)
    {
        var slot = LocalSlot;
        if (slot is null)
        {
            return [];
        }

        var highlighted = GetHighlightedMitigationEvent(now, slot.Value);
        return highlighted is null
            ? []
            : DmuMitigationData.GetMitigationNotes(highlighted.Event, slot.Value);
    }

    private UpcomingMitigationEvent? GetHighlightedMitigationEvent(DateTime now, PartySlot slot)
    {
        var upcoming = CurrentPhaseState is null
            ? Configuration.PreviewWhenInactive ? GetPreviewUpcomingEvents(now, int.MaxValue) : []
            : GetLiveUpcomingEvents(now, int.MaxValue);
        return upcoming.FirstOrDefault(entry => entry.IsUseNow && HasVisibleMitigationForSlot(entry.Event, slot)) ??
            upcoming.FirstOrDefault(entry => HasVisibleMitigationForSlot(entry.Event, slot));
    }

    private bool HasVisibleMitigationForSlot(DmuTimelineEvent entry, PartySlot slot)
    {
        return !string.IsNullOrWhiteSpace(
            DmuMitigationData.GetMitigationDisplayText(entry, slot, GetClassJobIdForSlot(slot)));
    }

    private void RebuildImportedMitigationSheet()
    {
        MitigationSheetImportResult = MitigationSheetImporter.Import(
            Configuration.ImportedMitigationSheetText,
            Configuration.MitigationSheetDefaultPhase,
            Configuration.UseImportedMitigationSheet);
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
        var changed = ReconcilePartyAssignments();
        changed |= FillOpenPartySlotsFromCurrentParty();
        if (changed)
        {
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

    private bool ReconcilePartyAssignments()
    {
        var changed = false;
        if (CurrentParty.Count == 0)
        {
            return false;
        }

        foreach (var assignment in Configuration.PartySlots
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.MemberName))
            .ToList())
        {
            var matchingMember = FindCurrentPartyMemberForAssignment(assignment);
            if (matchingMember is null)
            {
                ClearAssignment(assignment);
                changed = true;
                continue;
            }

            if (TryMoveAssignmentToDefaultSlot(assignment, matchingMember))
            {
                changed = true;
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

        changed |= ClearDuplicatePartyAssignments();
        return changed;
    }

    private bool FillOpenPartySlotsFromCurrentParty()
    {
        if (CurrentParty.Count == 0)
        {
            return false;
        }

        var changed = false;
        var assignedKeys = GetAssignedCurrentMemberKeys();
        foreach (var autoAssignment in PartySlotHelper.AutoAssign(CurrentParty)
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.MemberKey)))
        {
            if (assignedKeys.Contains(autoAssignment.MemberKey))
            {
                continue;
            }

            var target = Configuration.PartySlots.FirstOrDefault(assignment => assignment.Slot == autoAssignment.Slot);
            if (target is null || !IsAssignmentEmpty(target))
            {
                continue;
            }

            CopyAssignment(target, autoAssignment);
            assignedKeys.Add(autoAssignment.MemberKey);
            changed = true;
        }

        foreach (var member in CurrentParty.Where(member => !assignedKeys.Contains(member.Key)))
        {
            var target = PartySlotHelper.GetPreferredSlotsForMember(member)
                .Select(slot => Configuration.PartySlots.FirstOrDefault(assignment => assignment.Slot == slot))
                .FirstOrDefault(assignment => assignment is not null && IsAssignmentEmpty(assignment)) ??
                Configuration.PartySlots.FirstOrDefault(IsAssignmentEmpty);
            if (target is null)
            {
                break;
            }

            target.MemberKey = member.Key;
            target.MemberName = member.Name;
            target.ClassJobId = member.ClassJobId;
            assignedKeys.Add(member.Key);
            changed = true;
        }

        return changed;
    }

    private bool ClearDuplicatePartyAssignments()
    {
        var changed = false;
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in Configuration.PartySlots
            .Where(assignment => !IsAssignmentEmpty(assignment))
            .ToList())
        {
            var matchingMember = FindCurrentPartyMemberForAssignment(assignment);
            if (matchingMember is null)
            {
                ClearAssignment(assignment);
                changed = true;
                continue;
            }

            if (seenKeys.Add(matchingMember.Key))
            {
                continue;
            }

            ClearAssignment(assignment);
            changed = true;
        }

        return changed;
    }

    private HashSet<string> GetAssignedCurrentMemberKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in Configuration.PartySlots.Where(assignment => !IsAssignmentEmpty(assignment)))
        {
            var member = FindCurrentPartyMemberForAssignment(assignment);
            if (member is not null)
            {
                keys.Add(member.Key);
            }
        }

        return keys;
    }

    private PartyMemberInfo? FindCurrentPartyMemberForAssignment(PartySlotAssignment assignment)
    {
        return CurrentParty.FirstOrDefault(member => PartySlotHelper.AssignmentMatchesMember(assignment, member));
    }

    private bool TryMoveAssignmentToDefaultSlot(PartySlotAssignment assignment, PartyMemberInfo member)
    {
        if (assignment.ClassJobId == member.ClassJobId)
        {
            return false;
        }

        var defaultSlot = PartySlotHelper.GetDefaultSlotForJob(member.ClassJobId);
        if (defaultSlot is null || defaultSlot.Value == assignment.Slot)
        {
            return false;
        }

        var target = Configuration.PartySlots.FirstOrDefault(slot => slot.Slot == defaultSlot.Value);
        if (target is null ||
            !string.IsNullOrWhiteSpace(target.MemberKey) ||
            !string.IsNullOrWhiteSpace(target.MemberName))
        {
            return false;
        }

        target.MemberKey = member.Key;
        target.MemberName = member.Name;
        target.ClassJobId = member.ClassJobId;
        ClearAssignment(assignment);
        return true;
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

    private static bool IsAssignmentEmpty(PartySlotAssignment assignment)
    {
        return string.IsNullOrWhiteSpace(assignment.MemberKey) &&
            string.IsNullOrWhiteSpace(assignment.MemberName);
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
        if (inCombat && !wasInCombat && CurrentPhaseState is null)
        {
            firedTimelineAnchorKeys.Clear();
            ClearP2ToP3TransitionState();
            SetPhase(DmuPhase.P1, now, "Combat start");
        }
        wasInCombat = inCombat;

        if (!inCombat && CurrentPhaseState is null)
        {
            return;
        }

        var visibleCasts = GetVisibleSyncCasts();

        TrackP2ToP3Transition(now, visibleCasts);
        if (TryStartScheduledP3(now))
        {
            return;
        }

        if (!inCombat && CurrentPhaseState is not null)
        {
            return;
        }

        if (TryApplyTimelineCastSync(now, visibleCasts))
        {
            return;
        }

        if (CurrentPhaseState?.Phase == DmuPhase.P2 &&
            pendingP3StartAtUtc is { } scheduledP3Start &&
            now < scheduledP3Start)
        {
            return;
        }

        var livePhase = DetectLivePhase();
        if (livePhase is not DmuPhase.Unknown &&
            (CurrentPhaseState is null || livePhase > CurrentPhaseState.Phase))
        {
            SetPhase(livePhase, now, "Live phase signal");
        }

        TryApplyTimelineCastSync(now, visibleCasts);
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

    private void TrackP2ToP3Transition(DateTime now, IReadOnlyList<VisibleSyncCast> visibleCasts)
    {
        if (CurrentPhaseState?.Phase != DmuPhase.P2 || pendingP3StartAtUtc is not null)
        {
            return;
        }

        foreach (var visibleCast in visibleCasts)
        {
            if (IsLateP2WingsAction(visibleCast.ActionId))
            {
                sawLateP2Wings = true;
                continue;
            }

            if (!sawLateP2Wings || visibleCast.ActionId != P2UltimateEmbraceActionId)
            {
                continue;
            }

            ScheduleP3FromP2UltimateEmbrace(now, visibleCast);
            return;
        }
    }

    private void ScheduleP3FromP2UltimateEmbrace(DateTime now, VisibleSyncCast visibleCast)
    {
        var secondsUntilEmbraceResolves = visibleCast.TotalCastTime > 0.0f
            ? visibleCast.TimeToResolveSeconds()
            : Math.Max(0.0f, P2UltimateEmbraceCastSeconds - visibleCast.CurrentCastTime);
        pendingP3StartAtUtc = now.AddSeconds(secondsUntilEmbraceResolves + P3TargetableAfterFinalP2EmbraceSeconds);
    }

    private bool TryStartScheduledP3(DateTime now)
    {
        if (pendingP3StartAtUtc is not { } startedAt || now < startedAt)
        {
            return false;
        }

        CurrentPhaseState = new PhaseState(DmuPhase.P3, startedAt, "Synced from final Ultimate Embrace");
        ClearP2ToP3TransitionState();
        return true;
    }

    private static bool IsLateP2WingsAction(uint actionId)
    {
        return actionId is P2WingsOfDestructionLeftActionId
            or P2WingsOfDestructionRightActionId
            or P2WingsOfDestructionBusterCastActionId;
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

    private bool TryApplyTimelineCastSync(DateTime now, IReadOnlyList<VisibleSyncCast> visibleCasts)
    {
        foreach (var visibleCast in visibleCasts)
        {
            var syncPoint = FindTimelineCastAnchor(visibleCast, now);
            if (syncPoint is null)
            {
                continue;
            }

            ApplyTimelineCastSync(syncPoint, now, visibleCast);
            return true;
        }

        return false;
    }

    private DmuTimelineSyncPoint? FindTimelineCastAnchor(VisibleSyncCast visibleCast, DateTime now)
    {
        if (CurrentPhaseState is null)
        {
            return FindForwardTimelineCastAnchor(DmuPhase.Unknown, visibleCast, 0.0f, 0.0f);
        }

        var phaseElapsed = CurrentPhaseState.ElapsedSeconds(now);
        var castStartPhaseTime = Math.Max(0.0f, phaseElapsed - visibleCast.CurrentCastTime);
        var actionResolvePhaseTime = phaseElapsed + visibleCast.TimeToResolveSeconds();
        var currentPhaseCastStartAnchor = DmuMitigationData.FindVisibleCastSyncPoint(
            CurrentPhaseState.Phase,
            visibleCast.ActionId,
            DmuTimelineSyncKind.CastStart,
            castStartPhaseTime,
            firedTimelineAnchorKeys);
        var currentPhaseActionResolveAnchor = DmuMitigationData.FindVisibleCastSyncPoint(
            CurrentPhaseState.Phase,
            visibleCast.ActionId,
            DmuTimelineSyncKind.ActionResolve,
            actionResolvePhaseTime,
            firedTimelineAnchorKeys);

        if (currentPhaseCastStartAnchor is null)
        {
            return currentPhaseActionResolveAnchor ??
                FindForwardTimelineCastAnchor(CurrentPhaseState.Phase, visibleCast, castStartPhaseTime, actionResolvePhaseTime);
        }

        if (currentPhaseActionResolveAnchor is null)
        {
            return currentPhaseCastStartAnchor;
        }

        var castStartDifference = MathF.Abs(currentPhaseCastStartAnchor.PhaseTimeSeconds - castStartPhaseTime);
        var actionResolveDifference = MathF.Abs(currentPhaseActionResolveAnchor.PhaseTimeSeconds - actionResolvePhaseTime);
        return actionResolveDifference < castStartDifference
            ? currentPhaseActionResolveAnchor
            : currentPhaseCastStartAnchor;
    }

    private DmuTimelineSyncPoint? FindForwardTimelineCastAnchor(
        DmuPhase currentPhase,
        VisibleSyncCast visibleCast,
        float castStartPhaseTime,
        float actionResolvePhaseTime)
    {
        return DmuMitigationData.FindForwardVisibleCastPhaseAnchor(
            currentPhase,
            visibleCast.ActionId,
            DmuTimelineSyncKind.CastStart,
            castStartPhaseTime,
            firedTimelineAnchorKeys) ??
            DmuMitigationData.FindForwardVisibleCastPhaseAnchor(
                currentPhase,
                visibleCast.ActionId,
                DmuTimelineSyncKind.ActionResolve,
                actionResolvePhaseTime,
                firedTimelineAnchorKeys);
    }

    private IReadOnlyList<VisibleSyncCast> GetVisibleSyncCasts()
    {
        var visibleCasts = new List<VisibleSyncCast>();
        foreach (var gameObject in ObjectTable)
        {
            try
            {
                if (gameObject is IBattleNpc &&
                    gameObject is IBattleChara battleChara &&
                    battleChara.IsCasting &&
                    battleChara.CastActionId != 0)
                {
                    visibleCasts.Add(new VisibleSyncCast(
                        battleChara.CastActionId,
                        Math.Max(0.0f, battleChara.CurrentCastTime),
                        Math.Max(0.0f, battleChara.TotalCastTime)));
                }
            }
            catch (NullReferenceException)
            {
                // Actors can disappear during phase transitions; skip the stale object this tick.
            }
        }

        return visibleCasts;
    }

    private void ApplyTimelineCastSync(DmuTimelineSyncPoint syncPoint, DateTime now, VisibleSyncCast visibleCast)
    {
        var observedPhaseElapsed = syncPoint.Kind switch
        {
            DmuTimelineSyncKind.ActionResolve => Math.Max(0.0f, syncPoint.PhaseTimeSeconds - visibleCast.TimeToResolveSeconds()),
            _ => syncPoint.PhaseTimeSeconds + visibleCast.CurrentCastTime,
        };
        CurrentPhaseState = new PhaseState(
            syncPoint.Phase,
            now.AddSeconds(-observedPhaseElapsed),
            $"Synced to {syncPoint.Label}");
        firedTimelineAnchorKeys.Add(DmuMitigationData.GetSyncAnchorKey(syncPoint));
        if (IsFinalP2UltimateEmbraceSync(syncPoint))
        {
            ScheduleP3FromP2UltimateEmbrace(now, visibleCast);
        }

        if (syncPoint.Phase != DmuPhase.P2)
        {
            ClearP2ToP3TransitionState();
        }
    }

    private static bool IsFinalP2UltimateEmbraceSync(DmuTimelineSyncPoint syncPoint)
    {
        return syncPoint.Phase == DmuPhase.P2 &&
            syncPoint.ActionId == P2UltimateEmbraceActionId &&
            syncPoint.PhaseTimeSeconds > 100.0f;
    }

    private void SetPhase(DmuPhase phase, DateTime now, string label)
    {
        CurrentPhaseState = new PhaseState(phase, now, label);
        if (phase != DmuPhase.P2)
        {
            ClearP2ToP3TransitionState();
        }
    }

    private void ResetPhaseState()
    {
        CurrentPhaseState = null;
        firedTimelineAnchorKeys.Clear();
        ClearP2ToP3TransitionState();
    }

    private void ClearP2ToP3TransitionState()
    {
        pendingP3StartAtUtc = null;
        sawLateP2Wings = false;
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

}
