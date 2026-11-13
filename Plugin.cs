using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EventHorizon.Persistence;
using EventHorizon.Ui;

namespace EventHorizon;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/eventhorizon";
    private const string Alias = "/eh";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly HorizonApp app;
    private readonly WindowSystem windows = new("EventHorizon");
    private readonly MainWindow main;
    private readonly LiveHostWindow host;
    private readonly CompactHostWindow compact;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IChatGui chat, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chat = chat;
        this.log = log;
        this.app = new HorizonApp(pluginInterface.GetPluginConfigDirectory(), new DalamudLog(log), pluginInterface.AssemblyLocation.DirectoryName);
        this.app.Start();
        this.app.ScriptFace = new ScriptFace(pluginInterface);
        this.app.Travel = new VenueTeleport(pluginInterface);
        this.app.Sync = new PlannerSync(this.app.Store);

        this.main = new MainWindow(this.app);
        this.host = new LiveHostWindow(this.app);
        this.compact = new CompactHostWindow(this.app);
        this.windows.AddWindow(this.main);
        this.windows.AddWindow(this.host);
        this.windows.AddWindow(this.compact);

        this.AddCommand(CommandName, "Open or close Event Horizon.");
        this.AddCommand(Alias, "Open Event Horizon. /eh host, next, reload, games, schedule.");

        pluginInterface.UiBuilder.Draw += this.DrawUi;
        pluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
        pluginInterface.UiBuilder.OpenConfigUi += this.OpenMain;
        log.Information("Event Horizon is ready. Type {Command} to open it.", Alias);
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenMain;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        this.pluginInterface.UiBuilder.Draw -= this.DrawUi;
        this.RemoveCommand(CommandName);
        this.RemoveCommand(Alias);
        try
        {
            this.app.Save();
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Event Horizon could not save while closing.");
        }

        this.windows.RemoveAllWindows();
        this.app.Sync?.Dispose();
        this.app.ScriptFace?.Dispose();
    }

    private void AddCommand(string name, string help)
    {
        if (this.commandManager.Commands.ContainsKey(name))
            this.commandManager.RemoveHandler(name);

        this.commandManager.AddHandler(name, new CommandInfo(this.OnCommand) { HelpMessage = help });
    }

    private void RemoveCommand(string name)
    {
        if (this.commandManager.Commands.ContainsKey(name))
            this.commandManager.RemoveHandler(name);
    }

    private void OnCommand(string command, string args)
    {
        var result = this.app.Execute(args);
        this.Apply(result.Request);
        if (!string.IsNullOrWhiteSpace(result.Message))
            this.chat.Print("[Event Horizon] " + result.Message);
    }

    private void OpenMain()
    {
        this.main.IsOpen = true;
        this.main.BringToFront();
    }

    private void Apply(UiRequest request)
    {
        switch (request)
        {
            case UiRequest.ToggleMain:
                this.main.IsOpen = !this.main.IsOpen;
                if (this.main.IsOpen)
                    this.main.BringToFront();
                break;
            case UiRequest.OpenMain:
                this.OpenMain();
                break;
            case UiRequest.OpenHost:
                this.OpenMain();
                this.host.IsOpen = true;
                this.host.BringToFront();
                break;
        }
    }

    private void DrawUi()
    {
        try
        {
            this.app.Flush();
            this.app.Sync?.Pump();
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Event Horizon could not save.");
        }

        try
        {
            this.app.WatchCue(DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Event Horizon could not check the schedule.");
        }

        this.app.Theme.Push(this.app.Store.Config);
        try
        {
            this.windows.Draw();
            this.main.Dialogs.Draw();
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Event Horizon could not draw.");
            this.app.Fail("The window hit an error. Details are in the plugin log.");
        }
        finally
        {
            this.app.Theme.Pop();
        }

        if (this.app.Session.RequestHost)
        {
            this.app.Session.RequestHost = false;
            this.host.IsOpen = true;
            this.host.BringToFront();
        }

        if (this.app.Session.RequestCompact)
        {
            this.app.Session.RequestCompact = false;
            this.compact.IsOpen = true;
            this.compact.BringToFront();
        }
    }
}

internal sealed class DalamudLog(IPluginLog log) : ILog
{
    public void Info(string message) => log.Information("{Message}", message);

    public void Error(Exception exception, string message) => log.Error(exception, "{Message}", message);
}
