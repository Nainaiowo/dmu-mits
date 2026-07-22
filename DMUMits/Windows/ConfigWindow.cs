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
    private string mitigationSheetBuffer;

    public ConfigWindow(Plugin plugin) : base("DMU Mits Settings###DMUMitsConfig")
    {
        this.plugin = plugin;
        mitigationSheetBuffer = plugin.Configuration.ImportedMitigationSheetText;
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
            DrawMitigationSheetImport();
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
        DrawSectionHeader("Helper", "Window behavior and preview controls.");
        var showWindow = plugin.Configuration.ShowWindow;
        if (ImGui.Checkbox("Show helper", ref showWindow))
        {
            plugin.SetShowWindow(showWindow);
        }

        var lockHelper = plugin.Configuration.LockHelperWindow;
        if (ImGui.Checkbox("Lock helper window", ref lockHelper))
        {
            plugin.SetHelperWindowLocked(lockHelper);
        }

        var clickThrough = plugin.Configuration.ClickThroughHelperWindow;
        if (ImGui.Checkbox("Clickthrough helper window", ref clickThrough))
        {
            plugin.SetHelperWindowClickThrough(clickThrough);
        }

        ImGui.TextDisabled(lockHelper
            ? "Unlock to move or resize the helper window."
            : "Move and resize the helper window, then lock it here.");
        if (clickThrough)
        {
            ImGui.TextDisabled("Disable clickthrough to move, resize, or hover helper details.");
        }

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

    private void DrawMitigationSheetImport()
    {
        DrawSectionHeader("Mitigation sheet", "Paste copied Google Sheets or Excel cells. ACT timeline timing stays authoritative.");
        ImGui.TextWrapped("Rows are matched by phase, mechanic name, and top-to-bottom order. Unmatched rows are listed here so wrong calls do not silently appear.");

        ImGui.Spacing();
        DrawMitigationSheetDefaultPhaseSelector();
        DrawMitigationSheetActiveSelector();
        ImGui.Spacing();
        ImGui.InputTextMultiline("##DMUMitsMitSheetImport", ref mitigationSheetBuffer, 262144, new Vector2(-1.0f, 180.0f));

        if (ImGui.Button("Paste clipboard"))
        {
            mitigationSheetBuffer = ImGui.GetClipboardText() ?? string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Import sheet"))
        {
            plugin.ImportMitigationSheet(mitigationSheetBuffer);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear imported sheet"))
        {
            mitigationSheetBuffer = string.Empty;
            plugin.ClearImportedMitigationSheet();
        }

        DrawMitigationSheetImportSummary();
    }

    private void DrawMitigationSheetDefaultPhaseSelector()
    {
        var labels = new[]
        {
            "Require phase in pasted rows",
            "P1 Kefka",
            "P2 Forsaken Kefka",
            "P3 Chaos & Exdeath",
            "P4 Kefka Says",
            "P5 Ultima Kefka",
        };
        var phase = plugin.Configuration.MitigationSheetDefaultPhase;
        var index = phase switch
        {
            DmuPhase.P1 => 1,
            DmuPhase.P2 => 2,
            DmuPhase.P3 => 3,
            DmuPhase.P4 => 4,
            DmuPhase.P5 => 5,
            _ => 0,
        };

        ImGui.SetNextItemWidth(-1.0f);
        if (!ImGui.Combo("Rows without a phase", ref index, labels, labels.Length))
        {
            return;
        }

        var selectedPhase = index switch
        {
            1 => DmuPhase.P1,
            2 => DmuPhase.P2,
            3 => DmuPhase.P3,
            4 => DmuPhase.P4,
            5 => DmuPhase.P5,
            _ => DmuPhase.Unknown,
        };
        plugin.SetMitigationSheetDefaultPhase(selectedPhase);
    }

    private void DrawMitigationSheetActiveSelector()
    {
        var active = plugin.Configuration.UseImportedMitigationSheet;
        var hasImportedText = !string.IsNullOrWhiteSpace(plugin.Configuration.ImportedMitigationSheetText);
        ImGui.BeginDisabled(!hasImportedText);
        if (ImGui.Checkbox("Use imported sheet instead of built-in fallback", ref active))
        {
            plugin.SetUseImportedMitigationSheet(active);
        }

        ImGui.EndDisabled();
        ImGui.TextDisabled(hasImportedText
            ? "Turn this off to use the built-in Ikuya fallback sheet again."
            : "Built-in Ikuya fallback sheet is active until you import a sheet.");
    }

    private void DrawMitigationSheetImportSummary()
    {
        var result = plugin.MitigationSheetImportResult;
        ImGui.Spacing();
        if (!plugin.Configuration.UseImportedMitigationSheet)
        {
            ImGui.TextColored(AccentColor, "Built-in fallback mit sheet is active.");
        }
        else if (result.ParsedRows == 0)
        {
            ImGui.TextColored(WarningColor, "No imported mitigation sheet is active. The helper will show timeline mechanics without mit calls.");
        }
        else
        {
            ImGui.TextColored(AccentColor, $"Imported {result.MatchedRows}/{result.ParsedRows} mitigation rows.");
        }

        if (result.Warnings.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(WarningColor, "Import warnings");
            foreach (var warning in result.Warnings.Take(8))
            {
                ImGui.BulletText(warning);
            }

            if (result.Warnings.Count > 8)
            {
                ImGui.TextDisabled($"+ {result.Warnings.Count - 8} more");
            }
        }

        if (result.UnmatchedMechanics.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(WarningColor, $"Unmatched rows ({result.UnmatchedRows})");
            foreach (var mechanic in result.UnmatchedMechanics.Take(12))
            {
                ImGui.BulletText(mechanic);
            }

            if (result.UnmatchedMechanics.Count > 12)
            {
                ImGui.TextDisabled($"+ {result.UnmatchedMechanics.Count - 12} more");
            }
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
