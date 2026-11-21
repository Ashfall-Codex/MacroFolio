using System.Reflection;

namespace MacroFolio;

internal static class Constants
{
    public const string PluginName = "MacroFolio";
    public const string CommandName = "/macrofolio";
    public static readonly string[] CommandAliases = { "/mfolio" };

    public const string Author = "Keda";
    public const string GitHubUrl = "https://github.com/Ashfall-Codex/MacroFolio";
    public static string PluginVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
