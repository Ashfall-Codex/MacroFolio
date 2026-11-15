using Dalamud.Game.Command;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using MacroFolio.Localization;
using MacroFolio.Services;
using MacroFolio.UI;

namespace MacroFolio;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => Constants.PluginName;

    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    internal static IPluginLog Log { get; private set; } = null!;
    internal static ITextureProvider TextureProvider { get; private set; } = null!;
    internal static IObjectTable ObjectTable { get; private set; } = null!;
    internal static IDataManager DataManager { get; private set; } = null!;
    internal static Configuration Config { get; private set; } = null!;
    internal static FileDialogManager FileDialogManager { get; } = new();

    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IAddonLifecycle addonLifecycle;
    private int reapplyCountdown = -1;

    public Configuration Configuration { get; }
    public readonly WindowSystem WindowSystem = new(Constants.PluginName);

    private readonly MacroLibrary library;
    private readonly ChatSenderService chatSender;
    private readonly EmoteRecordingService recordingService;
    private readonly MainWindow mainWindow;
    private readonly MacroCreateWizard createWizard;
    private readonly MacroImportWindow importWindow;
    private readonly IconPickerWindow iconPicker;
    private readonly TestBuildWarningWindow testBuildWarning;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog pluginLog,
        ITextureProvider textureProvider,
        IObjectTable objectTable,
        IDataManager dataManager,
        IGameInteropProvider gameInteropProvider,
        IFramework framework,
        IClientState clientState,
        IAddonLifecycle addonLifecycle)
    {
        PluginInterface = pluginInterface;
        Log = pluginLog;
        TextureProvider = textureProvider;
        ObjectTable = objectTable;
        DataManager = dataManager;
        this.framework = framework;
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.addonLifecycle = addonLifecycle;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config = Configuration;

        Loc.SetLanguage(ResolveUiLanguage(Configuration.UiLanguage, clientState.ClientLanguage));

        MacroFolioTheme.AttachConfiguration(Configuration);
        MacroFolioTheme.Backdrop = new BackdropBlur(pluginInterface.UiBuilder);

        var macroService = new MacroService();
        library = new MacroLibrary(macroService);
        var plancheLibrary = new PlancheLibrary();
        var brouillonLibrary = new BrouillonLibrary();
        var texteLibrary = new TexteLibrary();
        chatSender = new ChatSenderService();
        recordingService = new EmoteRecordingService(gameInteropProvider);
        iconPicker = new IconPickerWindow();
        createWizard = new MacroCreateWizard(library, iconPicker);
        importWindow = new MacroImportWindow(library, iconPicker);
        mainWindow = new MainWindow(library, plancheLibrary, brouillonLibrary, texteLibrary, chatSender, recordingService, createWizard, importWindow, iconPicker);
        testBuildWarning = new TestBuildWarningWindow(Configuration);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(createWizard);
        WindowSystem.AddWindow(importWindow);
        WindowSystem.AddWindow(iconPicker);
        WindowSystem.AddWindow(testBuildWarning);

        commandManager.AddHandler(Constants.CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open MacroFolio.",
        });

        foreach (var alias in Constants.CommandAliases)
        {
            commandManager.AddHandler(alias, new CommandInfo(OnCommand)
            {
                HelpMessage = $"Alias for {Constants.CommandName}.",
                ShowInHelp = true,
            });
        }

        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;
        framework.Update += OnFrameworkUpdate;
        clientState.Login += OnLogin;

        // When the native Macro window closes, the game re-saves the edited set
        // and truncates any line past its native length limit. Re-apply our full
        // cached content right after so hotbar shortcuts keep the complete macro.
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Macro", OnMacroAddonClose);

        // Already logged in when the plugin loads (e.g. dev reload): queue a re-apply.
        if (clientState.IsLoggedIn)
            reapplyCountdown = ReapplyDelayFrames;
    }

    // Configured UI language wins; otherwise mirror the game client's language.
    private static string ResolveUiLanguage(string configured, ClientLanguage clientLanguage)
    {
        if (!string.IsNullOrEmpty(configured)) return configured;
        return clientLanguage == ClientLanguage.French ? Loc.FrenchCode : Loc.EnglishCode;
    }

    // Frames to wait before re-applying, so the game finishes its own write first.
    private const int ReapplyDelayFrames = 120;
    private const int ReapplyAfterMacroCloseFrames = 10;

    private void OnLogin() => reapplyCountdown = ReapplyDelayFrames;

    private void OnMacroAddonClose(AddonEvent type, AddonArgs args)
        => reapplyCountdown = ReapplyAfterMacroCloseFrames;

    private void OnFrameworkUpdate(IFramework fw)
    {
        var dt = (float)fw.UpdateDelta.TotalSeconds;
        recordingService.Tick(dt);
        chatSender.Tick(dt);
        library.TickPreview(dt);

        if (reapplyCountdown >= 0 && --reapplyCountdown < 0)
        {
            try { library.ReconcileWithGame(); }
            catch (Exception ex) { Log.Warning(ex, "Failed to reconcile macros with game."); }
        }
    }

    private void DrawUI()
    {
        if (MacroFolioTheme.Backdrop is { } backdrop)
            backdrop.Strength = MacroFolioTheme.BlurStrength;
        WindowSystem.Draw();
        MacroFolioTheme.Backdrop?.ReleaseIfIdle();
        FileDialogManager.Draw();
    }

    private void OpenMainUi() => mainWindow.IsOpen = true;

    private void OnCommand(string command, string args) => mainWindow.Toggle();

    public void Dispose()
    {
        // Dispose the native hook FIRST and isolate every step: if any later
        // unsubscribe throws, the hook must still have been freed (otherwise
        // Dalamud reports a leaked hook).
        Safe(() => recordingService.Dispose());

        Safe(() => PluginInterface.UiBuilder.Draw -= DrawUI);
        Safe(() => PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi);
        Safe(() => PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi);
        Safe(() => framework.Update -= OnFrameworkUpdate);
        Safe(() => clientState.Login -= OnLogin);
        Safe(() => addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "Macro", OnMacroAddonClose));

        Safe(() => WindowSystem.RemoveAllWindows());
        Safe(() => mainWindow.Dispose());
        Safe(() => createWizard.Dispose());
        Safe(() => importWindow.Dispose());
        Safe(() => iconPicker.Dispose());
        Safe(() => testBuildWarning.Dispose());
        Safe(() =>
        {
            MacroFolioTheme.Backdrop?.Dispose();
            MacroFolioTheme.Backdrop = null;
        });

        Safe(() => commandManager.RemoveHandler(Constants.CommandName));
        foreach (var alias in Constants.CommandAliases)
            Safe(() => commandManager.RemoveHandler(alias));
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Warning(ex, "MacroFolio dispose step failed."); }
    }
}
