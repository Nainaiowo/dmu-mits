using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace DMUMits;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ShowWindow { get; set; } = true;

    public bool PreviewWhenInactive { get; set; } = true;

    public float WindowOpacity { get; set; } = 0.92f;

    public float FontScale { get; set; } = 1.0f;

    public float LookAheadSeconds { get; set; } = 90.0f;

    public float UseNowLeadSeconds { get; set; } = 12.0f;

    public List<PartySlotAssignment> PartySlots { get; set; } = [];

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
