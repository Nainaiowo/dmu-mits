using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Linq;
using System.Numerics;

namespace DMUMits.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private static readonly Vector4 GreenColor = new(0.32f, 1.0f, 0.46f, 1.0f);
    private static readonly Vector4 GoldColor = new(1.0f, 0.75f, 0.18f, 1.0f);
    private static readonly Vector4 MutedColor = new(0.70f, 0.74f, 0.78f, 1.0f);
    private static readonly Vector4 BarBackColor = new(0.10f, 0.12f, 0.14f, 0.88f);
    private static readonly Vector4 BarFillColor = new(0.21f, 0.47f, 0.62f, 0.88f);
    private static readonly Vector4 BarBorderColor = new(1.0f, 1.0f, 1.0f, 0.16f);
    private const float BarHeight = 24.0f;
    private const float BarGap = 5.0f;

    public MainWindow(Plugin plugin) : base("DMU Mits###DMUMitsMain")
    {
        this.plugin = plugin;
        Size = new Vector2(360, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = false;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse;
        if (plugin.Configuration.LockHelperWindow)
        {
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        }

        if (plugin.Configuration.ClickThroughHelperWindow)
        {
            Flags |= ImGuiWindowFlags.NoInputs;
        }
    }

    public override void Draw()
    {
        BgAlpha = Math.Clamp(plugin.Configuration.WindowOpacity, 0.0f, 1.0f);
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.FontScale, 0.75f, 1.75f));

        var now = DateTime.UtcNow;
        DrawHeader(now);
        ImGui.Spacing();
        DrawSlotPrompt();
        if (plugin.LocalSlot is null)
        {
            ImGui.Spacing();
        }

        DrawUpcomingBars(now);
    }

    private void DrawHeader(DateTime now)
    {
        var phaseState = plugin.CurrentPhaseState;
        var phaseName = phaseState is null
            ? "Preview"
            : GetPhaseName(phaseState.Phase);
        ImGui.TextUnformatted("DMU Mits");
        ImGui.SameLine();
        var statusText = phaseState switch
        {
            not null => $"{phaseName}  {FormatTime(phaseState.ElapsedSeconds(now))}",
            null when plugin.IsPreviewActive => $"{phaseName}  {FormatTime(plugin.GetPreviewElapsedSeconds(now))}",
            _ => plugin.IsInDmu ? "Waiting for pull" : "Waiting for DMU",
        };
        ImGui.TextColored(MutedColor, statusText);

        if (phaseState is not null && !string.IsNullOrWhiteSpace(phaseState.SyncLabel))
        {
            ImGui.TextColored(MutedColor, phaseState.SyncLabel);
        }
    }

    private void DrawSlotPrompt()
    {
        if (plugin.LocalSlot is not null)
        {
            return;
        }

        ImGui.TextColored(MutedColor, "Set your party slot");
    }

    private void DrawUpcomingBars(DateTime now)
    {
        var upcoming = plugin.GetUpcomingEvents(now);
        if (upcoming.Count == 0)
        {
            ImGui.TextDisabled(plugin.IsInDmu ? "No upcoming mitigation entries." : "Open settings or enter DMU.");
            return;
        }

        var slot = plugin.LocalSlot;
        foreach (var entry in upcoming)
        {
            DrawMechanicBar(entry, slot);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + BarGap);
        }
    }

    private void DrawMechanicBar(UpcomingMitigationEvent entry, PartySlot? slot)
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(120.0f, ImGui.GetContentRegionAvail().X);
        var end = start + new Vector2(width, BarHeight);
        var fillRatio = Math.Clamp(entry.SecondsRemaining / Math.Max(1.0f, plugin.Configuration.LookAheadSeconds), 0.0f, 1.0f);
        var fillColor = entry.IsUseNow
            ? GreenColor
            : entry.IsNext ? GoldColor : BarFillColor;
        var labelColor = entry.IsUseNow
            ? GreenColor
            : entry.IsNext ? GoldColor : new Vector4(0.94f, 0.96f, 0.98f, 1.0f);

        drawList.AddRectFilled(start, end, ImGui.GetColorU32(BarBackColor), 4.0f);
        drawList.AddRectFilled(start, new Vector2(start.X + width * fillRatio, end.Y), ImGui.GetColorU32(fillColor with { W = 0.45f }), 4.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(BarBorderColor), 4.0f);

        var timeText = entry.SecondsRemaining < 0.0f ? "now" : $"{MathF.Ceiling(entry.SecondsRemaining):0}s";
        var timeSize = ImGui.CalcTextSize(timeText);
        var namePosition = start + new Vector2(8.0f, 4.0f);
        var timePosition = new Vector2(end.X - timeSize.X - 8.0f, start.Y + 4.0f);
        drawList.AddText(namePosition, ImGui.GetColorU32(labelColor), entry.Event.Name);
        drawList.AddText(timePosition, ImGui.GetColorU32(labelColor), timeText);

        ImGui.Dummy(new Vector2(width, BarHeight));
        var barHovered = ImGui.IsItemHovered();
        var mitigationHovered = false;
        if (slot is not null && entry.Event.HasMitigationFor(slot.Value))
        {
            mitigationHovered = DrawMechanicBarMitigation(entry, slot.Value, width);
        }

        if (slot is not null && entry.Event.HasMitigationFor(slot.Value) && (barHovered || mitigationHovered))
        {
            var notes = DmuMitigationData.GetMitigationNotes(entry.Event, slot.Value);

            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32.0f);
            ImGui.TextUnformatted(DmuMitigationData.GetMitigationDisplayText(entry.Event, slot.Value));
            if (notes.Count > 0)
            {
                ImGui.Separator();
                foreach (var note in notes)
                {
                    ImGui.TextWrapped($"{DmuMitigationData.GetMitigationNoteMarker(note.Number)} {note.DetailText}");
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.Event.Extras))
            {
                ImGui.Separator();
                ImGui.TextWrapped(entry.Event.Extras);
            }
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private static bool DrawMechanicBarMitigation(UpcomingMitigationEvent entry, PartySlot slot, float width)
    {
        var mitigationText = DmuMitigationData.GetMitigationDisplayText(entry.Event, slot);
        if (string.IsNullOrWhiteSpace(mitigationText))
        {
            return false;
        }

        var hovered = false;
        var textColor = entry.IsUseNow
            ? GreenColor
            : entry.IsNext ? GoldColor : MutedColor;
        const float leftPadding = 8.0f;
        var cursorX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(cursorX + leftPadding);
        ImGui.PushTextWrapPos(cursorX + MathF.Max(80.0f, width - leftPadding));
        ImGui.TextColored(textColor, mitigationText);
        hovered |= ImGui.IsItemHovered();
        ImGui.PopTextWrapPos();

        if (!entry.IsUseNow && !entry.IsNext)
        {
            return hovered;
        }

        foreach (var note in DmuMitigationData.GetMitigationNotes(entry.Event, slot))
        {
            ImGui.SetCursorPosX(cursorX + leftPadding);
            ImGui.PushTextWrapPos(cursorX + MathF.Max(80.0f, width - leftPadding));
            ImGui.TextColored(MutedColor, $"{DmuMitigationData.GetMitigationNoteMarker(note.Number)} {note.ShortText}");
            hovered |= ImGui.IsItemHovered();
            ImGui.PopTextWrapPos();
        }

        return hovered;
    }

    private static string FormatTime(float seconds)
    {
        var total = Math.Max(0, (int)MathF.Floor(seconds));
        return $"{total / 60:0}:{total % 60:00}";
    }

    private static string GetPhaseName(DmuPhase phase)
    {
        return DmuMitigationData.PhaseNames.TryGetValue(phase, out var name)
            ? name
            : phase.ToString();
    }
}
