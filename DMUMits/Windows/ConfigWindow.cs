using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Linq;
using System.Numerics;

namespace DMUMits.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private const string DragDropPayload = "DMUMitsSlot";

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

        if (ImGui.BeginTabItem("Timeline"))
        {
            DrawTimelineAudit();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawSettings()
    {
        if (ImGui.Button("Open DMU Mits"))
        {
            plugin.OpenMainWindow();
        }

        var showWindow = plugin.Configuration.ShowWindow;
        if (ImGui.Checkbox("Show window", ref showWindow))
        {
            plugin.SetShowWindow(showWindow);
        }

        var preview = plugin.Configuration.PreviewWhenInactive;
        if (ImGui.Checkbox("Preview when inactive", ref preview))
        {
            plugin.SetPreviewWhenInactive(preview);
        }

        var opacity = plugin.Configuration.WindowOpacity;
        if (ImGui.SliderFloat("Window opacity", ref opacity, 0.2f, 1.0f, "%.2f"))
        {
            plugin.SetWindowOpacity(opacity);
        }

        var fontScale = plugin.Configuration.FontScale;
        if (ImGui.SliderFloat("Font scale", ref fontScale, 0.75f, 1.75f, "%.2f"))
        {
            plugin.SetFontScale(fontScale);
        }

        var lookAhead = plugin.Configuration.LookAheadSeconds;
        if (ImGui.SliderFloat("Look ahead", ref lookAhead, 30.0f, 180.0f, "%.0fs"))
        {
            plugin.SetLookAheadSeconds(lookAhead);
        }

        var lead = plugin.Configuration.UseNowLeadSeconds;
        if (ImGui.SliderFloat("Use-now window", ref lead, 4.0f, 25.0f, "%.0fs"))
        {
            plugin.SetUseNowLeadSeconds(lead);
        }

        ImGui.Separator();
        DrawPartySlots();
    }

    private void DrawPartySlots()
    {
        ImGui.TextUnformatted("Party slots");
        ImGui.SameLine();
        if (ImGui.Button("Auto assign"))
        {
            plugin.AutoAssignPartySlots();
        }

        ImGui.TextDisabled("Drag players between slots or use the dropdowns.");

        if (!ImGui.BeginTable("##DMUMitsPartySlots", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 110.0f);
        ImGui.TableSetupColumn("Player");
        ImGui.TableHeadersRow();

        foreach (var assignment in plugin.Configuration.PartySlots)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(PartySlotHelper.GetDisplayName(assignment.Slot));
            ImGui.TableNextColumn();
            DrawSlotAssignment(assignment);
        }

        ImGui.EndTable();
    }

    private unsafe void DrawSlotAssignment(PartySlotAssignment assignment)
    {
        var selectedName = string.IsNullOrWhiteSpace(assignment.MemberName) ? "(empty)" : assignment.MemberName;
        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.BeginCombo($"##slot-{assignment.Slot}", selectedName))
        {
            if (ImGui.Selectable("(empty)", string.IsNullOrWhiteSpace(assignment.MemberKey)))
            {
                plugin.AssignSlot(assignment.Slot, null);
            }

            foreach (var member in plugin.CurrentParty)
            {
                var selected = string.Equals(member.Key, assignment.MemberKey, StringComparison.Ordinal);
                if (ImGui.Selectable(member.Name, selected))
                {
                    plugin.AssignSlot(assignment.Slot, member);
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(DragDropPayload, [(byte)assignment.Slot]);
            ImGui.TextUnformatted(selectedName);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(DragDropPayload);
            if (payload.Data != null && payload.DataSize == 1)
            {
                var sourceSlot = (PartySlot)(*(byte*)payload.Data);
                SwapSlots(sourceSlot, assignment.Slot);
            }

            ImGui.EndDragDropTarget();
        }
    }

    private void SwapSlots(PartySlot sourceSlot, PartySlot targetSlot)
    {
        if (sourceSlot == targetSlot)
        {
            return;
        }

        var source = plugin.Configuration.PartySlots.FirstOrDefault(slot => slot.Slot == sourceSlot);
        var target = plugin.Configuration.PartySlots.FirstOrDefault(slot => slot.Slot == targetSlot);
        if (source is null || target is null)
        {
            return;
        }

        (source.MemberKey, target.MemberKey) = (target.MemberKey, source.MemberKey);
        (source.MemberName, target.MemberName) = (target.MemberName, source.MemberName);
        (source.ClassJobId, target.ClassJobId) = (target.ClassJobId, source.ClassJobId);
        plugin.SaveConfiguration();
    }

    private void DrawTimelineAudit()
    {
        foreach (var phaseGroup in DmuMitigationData.Events.GroupBy(entry => entry.Phase))
        {
            var phaseName = DmuMitigationData.PhaseNames.TryGetValue(phaseGroup.Key, out var name)
                ? name
                : phaseGroup.Key.ToString();
            if (!ImGui.CollapsingHeader($"{phaseName} ({phaseGroup.Count()})"))
            {
                continue;
            }

            foreach (var entry in phaseGroup.OrderBy(entry => entry.PhaseTimeSeconds))
            {
                var minutes = (int)(entry.PhaseTimeSeconds / 60);
                var seconds = (int)(entry.PhaseTimeSeconds % 60);
                ImGui.TextUnformatted($"{minutes:0}:{seconds:00}  {entry.Name}");
            }
        }
    }
}
