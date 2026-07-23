using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DMUMits.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private const string DragDropPayload = "DMUMitsSlot";
    private static readonly Vector4 AccentColor = new(0.33f, 0.86f, 0.91f, 1.0f);
    private static readonly Vector4 MutedColor = new(0.68f, 0.75f, 0.79f, 1.0f);
    private static readonly Vector4 WarningColor = new(1.0f, 0.74f, 0.22f, 1.0f);
    private static readonly Vector4 PlaceholderBorderColor = new(1.0f, 1.0f, 1.0f, 0.22f);
    private static readonly Vector4 PlaceholderTextColor = new(0.70f, 0.74f, 0.78f, 1.0f);
    private PartySlot? draggingSlot;

    public ConfigWindow(Plugin plugin) : base("DMU Mits Settings###DMUMitsConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(560, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##DMUMitsSettingsTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            DrawSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Mit Sheet"))
        {
            DrawMitigationSheetDetails();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Timeline"))
        {
            DrawTimelineAudit();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawSettings()
    {
        DrawMitigationPlanChoice();

        ImGui.Spacing();
        DrawSectionHeader("Helper", "Window behavior and preview controls.");
        var showWindow = plugin.Configuration.ShowWindow;
        if (ImGui.Checkbox("Show helper", ref showWindow))
        {
            plugin.SetShowWindow(showWindow);
        }

        var lockHelper = plugin.LockAndClickThroughHelperWindow;
        if (ImGui.Checkbox("Lock and Clickthrough helper window", ref lockHelper))
        {
            plugin.SetLockAndClickThroughHelperWindow(lockHelper);
        }

        ImGui.TextDisabled(lockHelper
            ? "Disable to move, resize, or hover helper details."
            : "Move and resize the helper window, then lock it here.");

        var preview = plugin.Configuration.PreviewWhenInactive;
        if (ImGui.Checkbox("Preview when inactive", ref preview))
        {
            plugin.SetPreviewWhenInactive(preview);
        }

        ImGui.Spacing();
        DrawSectionHeader("Timing", "How far ahead the helper watches and when it highlights your mit.");
        var opacity = plugin.Configuration.WindowOpacity;
        var fontScale = plugin.Configuration.FontScale;
        var lookAhead = plugin.Configuration.LookAheadSeconds;
        var lead = plugin.Configuration.UseNowLeadSeconds;

        if (ImGui.BeginTable("##DMUMitsTimingControls", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 120.0f);
            ImGui.TableSetupColumn("Control");
            DrawSliderRow("Window opacity", ref opacity, 0.0f, 1.0f, "%.2f", plugin.SetWindowOpacity);
            DrawSliderRow("Font scale", ref fontScale, 0.75f, 1.75f, "%.2f", plugin.SetFontScale);
            DrawSliderRow("Look ahead", ref lookAhead, 30.0f, 180.0f, "%.0fs", plugin.SetLookAheadSeconds);
            DrawSliderRow("Use-now window", ref lead, 4.0f, 25.0f, "%.0fs", plugin.SetUseNowLeadSeconds);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        DrawPartySlots();
    }

    private void DrawMitigationPlanChoice()
    {
        DrawSectionHeader("Mitigation plan", "Choose which built-in sheet is shown on the ACT timeline.");

        ImGui.Spacing();
        DrawMitigationPlanDropdown("Active plan");
    }

    private void DrawMitigationPlanDropdown(string label)
    {
        var sheets = Enum.GetValues<DmuMitigationSheet>();
        var labels = sheets.Select(DmuMitigationData.GetMitigationSheetName).ToArray();
        var index = Array.IndexOf(sheets, plugin.Configuration.MitigationSheet);
        if (index < 0)
        {
            index = 0;
        }

        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.Combo(label, ref index, labels, labels.Length))
        {
            plugin.SetMitigationSheet(sheets[index]);
        }
    }

    private void DrawMitigationSheetDetails()
    {
        DrawSectionHeader("Mitigation sheet", "Current sheet status and mapping notes.");
        ImGui.TextWrapped("Timings stay anchored to the ACT timeline. Sheet rows are baked into the plugin and mapped to specific mechanics.");

        ImGui.Spacing();
        var selectedSheet = plugin.Configuration.MitigationSheet;
        ImGui.TextColored(AccentColor, $"{DmuMitigationData.GetMitigationSheetName(selectedSheet)} active.");
        ImGui.TextDisabled($"{DmuMitigationData.GetMitigationSheetRowCount(selectedSheet)} mapped mitigation rows.");

        if (selectedSheet == DmuMitigationSheet.Lpdu)
        {
            ImGui.TextWrapped("LPDU uses its own phase sheets. Rows with different names, such as Hyperdrive and later P3 Thunder sets, are mapped explicitly.");
            ImGui.TextColored(WarningColor, "Fake-melee extras are shown when D2 is assigned to a caster or physical ranged job.");
        }
        else
        {
            ImGui.TextWrapped("The original built-in Ikuya Mitty sheet remains available as the default source.");
        }
    }

    private void DrawPartySlots()
    {
        DrawSectionHeader("Party assignment", "Drag players between slots or rebuild from the current party.");
        if (ImGui.Button("Rebuild from party"))
        {
            plugin.AutoAssignPartySlots();
        }

        ImGui.TextDisabled("Works outside DMU. Current party members fill empty slots automatically.");

        DrawSlotGroup("Tanks", [PartySlot.MT, PartySlot.OT]);
        DrawSlotGroup("Healers", [PartySlot.WHM, PartySlot.AST, PartySlot.SCH, PartySlot.SGE]);
        DrawSlotGroup("Damage", [PartySlot.D1, PartySlot.D2, PartySlot.D3, PartySlot.D4]);

        var unassigned = plugin.CurrentParty
            .Where(member => !plugin.Configuration.PartySlots.Any(assignment => PartySlotHelper.AssignmentMatchesMember(assignment, member)))
            .Select(member => member.Name)
            .ToList();
        if (unassigned.Count > 0)
        {
            ImGui.TextColored(WarningColor, $"Unassigned: {string.Join(", ", unassigned)}");
        }
    }

    private void DrawSlotGroup(string label, IReadOnlyList<PartySlot> slots)
    {
        ImGui.Spacing();
        ImGui.TextColored(MutedColor, label);
        if (!ImGui.BeginTable($"##DMUMitsPartySlots{label}", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 90.0f);
        ImGui.TableSetupColumn("Player");

        foreach (var slot in slots)
        {
            var assignment = plugin.Configuration.PartySlots.FirstOrDefault(assignment => assignment is not null && assignment.Slot == slot);
            if (assignment is null)
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var slotName = PartySlotHelper.GetDisplayName(slot);
            if (plugin.LocalSlot == slot)
            {
                ImGui.TextColored(AccentColor, slotName);
            }
            else
            {
                ImGui.TextUnformatted(slotName);
            }

            ImGui.TableNextColumn();
            DrawSlotAssignment(assignment);
        }

        ImGui.EndTable();
    }

    private unsafe void DrawSlotAssignment(PartySlotAssignment? assignment)
    {
        if (assignment is null)
        {
            return;
        }

        var selectedName = string.IsNullOrWhiteSpace(assignment.MemberName) ? "(empty)" : assignment.MemberName;
        var iconSize = MathF.Max(16.0f, ImGui.GetTextLineHeight());
        DrawClassJobIcon(assignment.ClassJobId, iconSize, selectedName);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.BeginCombo($"##slot-{assignment.Slot}", selectedName))
        {
            if (ImGui.Selectable("(empty)", string.IsNullOrWhiteSpace(assignment.MemberKey)))
            {
                plugin.AssignSlot(assignment.Slot, null);
            }

            foreach (var member in plugin.CurrentParty)
            {
                if (member is null)
                {
                    continue;
                }

                var selected = PartySlotHelper.AssignmentMatchesMember(assignment, member);
                DrawClassJobIcon(member.ClassJobId, iconSize, member.Name);
                var iconClicked = ImGui.IsItemClicked();
                ImGui.SameLine();
                if (ImGui.Selectable($"{member.Name}##{member.Key}", selected) || iconClicked)
                {
                    plugin.AssignSlot(assignment.Slot, member);
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.BeginDragDropSource())
        {
            draggingSlot = assignment.Slot;
            ImGui.SetDragDropPayload(DragDropPayload, [1]);
            ImGui.TextUnformatted(selectedName);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(DragDropPayload);
            if (payload.Handle != null && draggingSlot is { } sourceSlot)
            {
                draggingSlot = null;
                plugin.SwapSlots(sourceSlot, assignment.Slot);
            }

            ImGui.EndDragDropTarget();
        }
    }

    private static void DrawClassJobIcon(uint classJobId, float iconSize, string tooltip)
    {
        var iconId = GetClassJobIconId(classJobId);
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(iconSize));
            return;
        }

        if (iconId != 0)
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                var wrap = texture.GetWrapOrDefault();
                if (wrap is not null)
                {
                    ImGui.Image(wrap.Handle, new Vector2(iconSize));
                    if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(tooltip))
                    {
                        ImGui.SetTooltip(tooltip);
                    }

                    return;
                }
            }
            catch
            {
            }
        }

        DrawClassJobPlaceholder(iconSize, tooltip);
    }

    private static void DrawClassJobPlaceholder(float iconSize, string tooltip)
    {
        var size = new Vector2(iconSize);
        var start = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(
            start,
            start + size,
            ImGui.GetColorU32(PlaceholderBorderColor),
            3.0f);

        if (!string.IsNullOrWhiteSpace(tooltip))
        {
            var text = "?";
            var textSize = ImGui.CalcTextSize(text);
            drawList.AddText(
                start + new Vector2(MathF.Max(0.0f, (size.X - textSize.X) * 0.5f), MathF.Max(0.0f, (size.Y - textSize.Y) * 0.5f)),
                ImGui.GetColorU32(PlaceholderTextColor),
                text);
        }

        if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(tooltip))
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static uint GetClassJobIconId(uint classJobId)
    {
        return classJobId == 0 ? 0 : 62100u + classJobId;
    }

    private void DrawTimelineAudit()
    {
        var localSlot = plugin.LocalSlot;
        var localClassJobId = localSlot is null ? 0 : plugin.GetClassJobIdForSlot(localSlot.Value);
        foreach (var phaseGroup in DmuMitigationData.Events.GroupBy(entry => entry.Phase))
        {
            var phaseName = DmuMitigationData.PhaseNames.TryGetValue(phaseGroup.Key, out var name)
                ? name
                : phaseGroup.Key.ToString();
            if (!ImGui.CollapsingHeader($"{phaseName} ({phaseGroup.Count()})"))
            {
                continue;
            }

            if (!ImGui.BeginTable($"##DMUMitsTimeline{phaseGroup.Key}", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
            {
                continue;
            }

            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 54.0f);
            ImGui.TableSetupColumn("Mechanic");
            ImGui.TableSetupColumn("Your mit");
            ImGui.TableSetupColumn("Slots", ImGuiTableColumnFlags.WidthFixed, 96.0f);
            ImGui.TableSetupColumn("Sync", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("Extra", ImGuiTableColumnFlags.WidthFixed, 64.0f);
            ImGui.TableHeadersRow();

            foreach (var entry in phaseGroup.OrderBy(entry => entry.PhaseTimeSeconds))
            {
                var minutes = (int)(entry.PhaseTimeSeconds / 60);
                var seconds = (int)(entry.PhaseTimeSeconds % 60);
                var mitigations = DmuMitigationData.GetMitigations(entry);
                var localMitigation = localSlot is null
                    ? string.Empty
                    : DmuMitigationData.GetMitigationDisplayText(entry, localSlot.Value, localClassJobId);
                var mitSlots = mitigations.Count == 0
                    ? string.Empty
                    : string.Join(", ", mitigations.Keys.Select(slot => slot.ToString()));
                var syncIds = entry.SyncActionIds.Count == 0
                    ? string.Empty
                    : string.Join(", ", entry.SyncActionIds.Select(id => $"0x{id:X}"));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{minutes:0}:{seconds:00}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Name);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(localMitigation);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(mitSlots);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(syncIds);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(entry.Extras);
            }

            ImGui.EndTable();
        }
    }

    private static void DrawSectionHeader(string title, string subtitle)
    {
        ImGui.TextColored(AccentColor, title);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextColored(MutedColor, subtitle);
        }
    }

    private static void DrawSliderRow(string label, ref float value, float min, float max, string format, Action<float> setter)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.SliderFloat($"##{label}", ref value, min, max, format))
        {
            setter(value);
        }
    }
}
