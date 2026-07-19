using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace DMUMits.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private static readonly Vector4 TextColor = new(0.93f, 0.96f, 0.98f, 1.0f);
    private static readonly Vector4 MutedColor = new(0.68f, 0.75f, 0.79f, 1.0f);
    private static readonly Vector4 PanelColor = new(0.07f, 0.09f, 0.11f, 0.84f);
    private static readonly Vector4 RowColor = new(0.09f, 0.12f, 0.14f, 0.88f);
    private static readonly Vector4 RowFillColor = new(0.18f, 0.36f, 0.44f, 0.34f);
    private static readonly Vector4 BorderColor = new(0.34f, 0.70f, 0.78f, 0.28f);
    private static readonly Vector4 AccentColor = new(0.33f, 0.86f, 0.91f, 1.0f);
    private static readonly Vector4 UseNowColor = new(0.36f, 1.0f, 0.52f, 1.0f);
    private static readonly Vector4 NextColor = new(1.0f, 0.74f, 0.22f, 1.0f);
    private static readonly Vector4 WarningDimColor = new(0.95f, 0.22f, 0.20f, 1.0f);
    private static readonly Vector4 WarningBrightColor = new(1.0f, 0.52f, 0.46f, 1.0f);
    private const float BarGap = 7.0f;
    private const float SidePadding = 8.0f;

    public MainWindow(Plugin plugin) : base("DMU Mits###DMUMitsMain")
    {
        this.plugin = plugin;
        Size = new Vector2(380, 320);
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
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8.0f);
        DrawSlotPrompt();
        DrawUpcomingBars(now);
    }

    private void DrawHeader(DateTime now)
    {
        var phaseState = plugin.CurrentPhaseState;
        var phaseName = phaseState is null
            ? "Preview"
            : GetPhaseName(phaseState.Phase);
        var statusText = phaseState switch
        {
            not null => $"{phaseName}  {FormatTime(phaseState.ElapsedSeconds(now))}",
            null when plugin.IsPreviewActive => $"{phaseName}  {FormatTime(plugin.GetPreviewElapsedSeconds(now))}",
            _ => plugin.IsInDmu ? "Waiting for pull" : "Waiting for DMU",
        };

        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(180.0f, ImGui.GetContentRegionAvail().X);
        var height = MathF.Max(42.0f, (ImGui.GetTextLineHeight() * 2.0f) + 12.0f);
        var end = start + new Vector2(width, height);
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(PanelColor), 6.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(BorderColor), 6.0f);
        drawList.AddRectFilled(start, new Vector2(start.X + 3.0f, end.Y), ImGui.GetColorU32(AccentColor), 6.0f);

        var slotText = plugin.LocalSlot is { } slot
            ? $"{PartySlotHelper.GetDisplayName(slot)} {MitigationTextResolver.GetJobAbbreviation(plugin.GetClassJobIdForSlot(slot))}".Trim()
            : "No slot";
        var slotSize = ImGui.CalcTextSize(slotText) + new Vector2(14.0f, 6.0f);
        var slotStart = new Vector2(end.X - slotSize.X - 8.0f, start.Y + 8.0f);
        var showSlotBadge = slotSize.X + 132.0f <= width;
        var textStart = start + new Vector2(10.0f, 6.0f);
        var textEndX = showSlotBadge ? slotStart.X - 6.0f : end.X - 8.0f;
        drawList.PushClipRect(textStart, new Vector2(MathF.Max(textStart.X + 20.0f, textEndX), end.Y), true);
        drawList.AddText(textStart, ImGui.GetColorU32(TextColor), "DMU Mits");
        drawList.AddText(textStart + new Vector2(0.0f, ImGui.GetTextLineHeight() + 3.0f), ImGui.GetColorU32(MutedColor), FitText(statusText, MathF.Max(20.0f, textEndX - textStart.X)));
        drawList.PopClipRect();

        if (showSlotBadge)
        {
            drawList.AddRectFilled(slotStart, slotStart + slotSize, ImGui.GetColorU32(new Vector4(0.02f, 0.04f, 0.05f, 0.72f)), 4.0f);
            drawList.AddRect(slotStart, slotStart + slotSize, ImGui.GetColorU32(BorderColor), 4.0f);
            drawList.AddText(slotStart + new Vector2(7.0f, 3.0f), ImGui.GetColorU32(plugin.LocalSlot is null ? MutedColor : AccentColor), slotText);
        }

        ImGui.Dummy(new Vector2(width, height));

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

        ImGui.TextColored(NextColor, "Set your party slot in settings.");
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4.0f);
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
        var width = MathF.Max(80.0f, ImGui.GetContentRegionAvail().X);
        var height = MathF.Max(28.0f, ImGui.GetTextLineHeight() + 12.0f);
        var end = start + new Vector2(width, height);
        var fillRatio = Math.Clamp(entry.SecondsRemaining / Math.Max(1.0f, plugin.Configuration.LookAheadSeconds), 0.0f, 1.0f);
        var accentColor = GetStateColor(entry);

        drawList.AddRectFilled(start, end, ImGui.GetColorU32(RowColor), 5.0f);
        drawList.AddRectFilled(start, new Vector2(start.X + width * fillRatio, end.Y), ImGui.GetColorU32(RowFillColor), 5.0f);
        drawList.AddRectFilled(start, new Vector2(start.X + 3.0f, end.Y), ImGui.GetColorU32(accentColor), 5.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(BorderColor), 5.0f);

        var timeText = entry.SecondsRemaining < 0.0f ? "now" : $"{MathF.Ceiling(entry.SecondsRemaining):0}s";
        var timeSize = ImGui.CalcTextSize(timeText);
        var timePosition = new Vector2(end.X - timeSize.X - SidePadding, start.Y + ((height - timeSize.Y) * 0.5f));
        var nameMaxWidth = MathF.Max(20.0f, width - timeSize.X - 28.0f);
        var nameText = FitText(GetMechanicDisplayName(entry), nameMaxWidth);
        drawList.PushClipRect(start, end, true);
        drawList.AddText(start + new Vector2(SidePadding + 4.0f, (height - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(GetStateTextColor(entry)), nameText);
        drawList.AddText(timePosition, ImGui.GetColorU32(GetStateTextColor(entry)), timeText);
        drawList.PopClipRect();

        ImGui.Dummy(new Vector2(width, height));
        var barHovered = ImGui.IsItemHovered();
        var mitigationHovered = false;
        MitigationCooldownWarning? warning = null;
        uint classJobId = 0;
        if (slot is not null)
        {
            classJobId = plugin.GetClassJobIdForSlot(slot.Value);
            mitigationHovered = DrawMechanicBarMitigation(entry, slot.Value, classJobId, width, out warning);
        }

        if (slot is not null && (barHovered || mitigationHovered))
        {
            DrawMitigationTooltip(entry, slot.Value, classJobId, warning);
        }
    }

    private bool DrawMechanicBarMitigation(
        UpcomingMitigationEvent entry,
        PartySlot slot,
        uint classJobId,
        float width,
        out MitigationCooldownWarning? warning)
    {
        var mitigationText = DmuMitigationData.GetMitigationDisplayText(entry.Event, slot, classJobId);
        warning = CooldownTracker.GetWarning(mitigationText, entry.SecondsRemaining);
        if (string.IsNullOrWhiteSpace(mitigationText))
        {
            return false;
        }

        var hovered = false;
        var textColor = warning is not null
            ? GetBlinkingWarningColor()
            : GetMitigationTextColor(entry);
        var iconSize = MathF.Max(18.0f, ImGui.GetTextLineHeight() + 2.0f);
        var cursorX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(cursorX + SidePadding);
        var iconId = MitigationActionCatalog.ResolveIconId(mitigationText);
        DrawActionIcon(iconId, iconSize);
        hovered |= ImGui.IsItemHovered();
        ImGui.SameLine();
        ImGui.PushTextWrapPos(cursorX + MathF.Max(80.0f, width - SidePadding));
        ImGui.TextColored(textColor, mitigationText);
        hovered |= ImGui.IsItemHovered();
        ImGui.PopTextWrapPos();

        if (!entry.IsUseNow && !entry.IsNext)
        {
            return hovered;
        }

        foreach (var note in DmuMitigationData.GetMitigationNotes(entry.Event, slot))
        {
            ImGui.SetCursorPosX(cursorX + SidePadding + iconSize + ImGui.GetStyle().ItemSpacing.X);
            ImGui.PushTextWrapPos(cursorX + MathF.Max(80.0f, width - SidePadding));
            ImGui.TextColored(MutedColor, $"{DmuMitigationData.GetMitigationNoteMarker(note.Number)} {note.ShortText}");
            hovered |= ImGui.IsItemHovered();
            ImGui.PopTextWrapPos();
        }

        return hovered;
    }

    private static void DrawActionIcon(uint iconId, float iconSize)
    {
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(iconSize));
            return;
        }

        try
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            var wrap = texture.GetWrapOrDefault();
            if (wrap is not null)
            {
                ImGui.Image(wrap.Handle, new Vector2(iconSize));
                return;
            }
        }
        catch
        {
        }

        ImGui.Dummy(new Vector2(iconSize));
    }

    private void DrawMitigationTooltip(
        UpcomingMitigationEvent entry,
        PartySlot slot,
        uint classJobId,
        MitigationCooldownWarning? warning)
    {
        var mitigationText = DmuMitigationData.GetMitigationDisplayText(entry.Event, slot, classJobId);
        var notes = DmuMitigationData.GetMitigationNotes(entry.Event, slot);
        if (string.IsNullOrWhiteSpace(mitigationText) &&
            notes.Count == 0 &&
            string.IsNullOrWhiteSpace(entry.Event.Extras) &&
            warning is null)
        {
            return;
        }

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32.0f);
        if (!string.IsNullOrWhiteSpace(mitigationText))
        {
            ImGui.TextUnformatted(mitigationText);
        }

        if (warning is not null)
        {
            ImGui.Separator();
            ImGui.TextColored(WarningBrightColor, $"Cooldown: {warning.ActionName} ready in {FormatSeconds(warning.ReadyInSeconds)}");
        }

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

    private static Vector4 GetStateColor(UpcomingMitigationEvent entry)
    {
        return entry.IsUseNow
            ? UseNowColor
            : entry.IsNext ? NextColor : AccentColor;
    }

    private static Vector4 GetStateTextColor(UpcomingMitigationEvent entry)
    {
        return entry.IsUseNow || entry.IsNext ? GetStateColor(entry) : TextColor;
    }

    private static Vector4 GetMitigationTextColor(UpcomingMitigationEvent entry)
    {
        return entry.IsUseNow
            ? UseNowColor
            : entry.IsNext ? NextColor : MutedColor;
    }

    private static Vector4 GetBlinkingWarningColor()
    {
        var pulse = (MathF.Sin((float)ImGui.GetTime() * 8.0f) + 1.0f) * 0.5f;
        return Vector4.Lerp(WarningDimColor, WarningBrightColor, pulse);
    }

    private static string FitText(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        const string suffix = "...";
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = string.Concat(text.AsSpan(0, length), suffix);
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
            {
                return candidate;
            }
        }

        return suffix;
    }

    private static string FormatTime(float seconds)
    {
        var total = Math.Max(0, (int)MathF.Floor(seconds));
        return $"{total / 60:0}:{total % 60:00}";
    }

    private static string FormatSeconds(float seconds)
    {
        var total = Math.Max(0, (int)MathF.Ceiling(seconds));
        return total >= 60
            ? $"{total / 60:0}:{total % 60:00}"
            : $"{total:0}s";
    }

    private static string GetPhaseName(DmuPhase phase)
    {
        return DmuMitigationData.PhaseNames.TryGetValue(phase, out var name)
            ? name
            : phase.ToString();
    }

    private string GetMechanicDisplayName(UpcomingMitigationEvent entry)
    {
        return plugin.CurrentPhaseState is { } phaseState && entry.Event.Phase != phaseState.Phase
            ? $"{GetPhaseTag(entry.Event.Phase)}: {entry.Event.Name}"
            : entry.Event.Name;
    }

    private static string GetPhaseTag(DmuPhase phase)
    {
        return phase switch
        {
            DmuPhase.P1 => "P1",
            DmuPhase.P2 => "P2",
            DmuPhase.P3 => "P3",
            DmuPhase.P4 => "P4",
            DmuPhase.P5 => "P5",
            _ => phase.ToString(),
        };
    }
}
