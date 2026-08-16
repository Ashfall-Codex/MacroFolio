using Dalamud.Configuration;

namespace MacroFolio;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string UiLanguage { get; set; } = string.Empty;
    public string LastTestBuildWarningVersionSeen { get; set; } = string.Empty;
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
