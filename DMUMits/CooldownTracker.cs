using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Linq;

namespace DMUMits;

public sealed record MitigationCooldownWarning(string ActionName, float ReadyInSeconds);

public static class CooldownTracker
{
    public static MitigationCooldownWarning? GetWarning(string mitigationText, float secondsUntilEvent)
    {
        try
        {
            var warningWindow = MathF.Max(0.0f, secondsUntilEvent) + 0.5f;
            return MitigationActionCatalog.FindActions(mitigationText, includeCarryover: false)
                .Select(action => new
                {
                    action.Name,
                    Remaining = GetRecastRemaining(action.ActionId),
                })
                .Where(action => action.Remaining is not null && action.Remaining.Value > warningWindow)
                .OrderByDescending(action => action.Remaining!.Value - warningWindow)
                .Select(action => new MitigationCooldownWarning(action.Name, action.Remaining!.Value))
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to read mitigation cooldown.");
            return null;
        }
    }

    private static unsafe float? GetRecastRemaining(uint actionId)
    {
        var actionManager = ActionManager.Instance();
        if (actionManager is null)
        {
            return null;
        }

        var adjustedActionId = actionManager->GetAdjustedActionId(actionId);
        var total = actionManager->GetRecastTime(ActionType.Action, adjustedActionId);
        if (total <= 0.0f)
        {
            return 0.0f;
        }

        var elapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, adjustedActionId);
        var maxCharges = ActionManager.GetMaxCharges(adjustedActionId, 0);
        if (maxCharges > 1)
        {
            var perCharge = total / maxCharges;
            return perCharge > 0.0f && elapsed < perCharge
                ? MathF.Max(0.0f, perCharge - elapsed)
                : 0.0f;
        }

        return MathF.Max(0.0f, total - elapsed);
    }
}
