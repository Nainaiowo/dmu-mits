using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Linq;
using System.Numerics;

namespace DMUMits.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private const string DragDropPayload = "DMUMitsSlot";
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

        if (ImGui.BeginTabItem("Timeline"))
        {
            DrawTimelineAudit();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawSettings()
    {
        ImGui.TextUnformatted("Helper window");
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

        ImGui.Separator();

        var preview = plugin.Configuration.PreviewWhenInactive;
        if (ImGui.Checkbox("Preview when inactive", ref preview))
        {
            plugin.SetPreviewWhenInactive(preview);
        }

        var opacity = plugin.Configuration.WindowOpacity;
        if (ImGui.SliderFloat("Window opacity", ref opacity, 0.0f, 1.0f, "%.2f"))
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

        ImGui.TextDisabled("Works outside DMU. Saved players stay assigned if they leave party.");

        if (!ImGui.BeginTable("##DMUMitsPartySlots", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 110.0f);
        ImGui.TableSetupColumn("Player");
        ImGui.TableHeadersRow();

        foreach (var assignment in plugin.Configuration.PartySlots.Where(assignment => assignment is not null))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(PartySlotHelper.GetDisplayName(assignment.Slot));
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
                SwapSlots(sourceSlot, assignment.Slot);
            }

            ImGui.EndDragDropTarget();
        }
    }

    private void SwapSlots(PartySlot sourceSlot, PartySlot targetSlot)
    {
        draggingSlot = null;

        if (sourceSlot == targetSlot)
        {
            return;
        }

        var source = plugin.Configuration.PartySlots.FirstOrDefault(slot => slot is not null && slot.Slot == sourceSlot);
        var target = plugin.Configuration.PartySlots.FirstOrDefault(slot => slot is not null && slot.Slot == targetSlot);
        if (source is null || target is null)
        {
            return;
        }

        (source.MemberKey, target.MemberKey) = (target.MemberKey, source.MemberKey);
        (source.MemberName, target.MemberName) = (target.MemberName, source.MemberName);
        (source.ClassJobId, target.ClassJobId) = (target.ClassJobId, source.ClassJobId);
        plugin.SaveConfiguration();
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
