using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using EventHorizon.Hosting;
using EventHorizon.Models;

namespace EventHorizon;

internal sealed class VenueTeleport
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICallGateSubscriber<string, object> execute;
    private readonly ICallGateSubscriber<bool> busy;

    public VenueTeleport(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        this.execute = pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        this.busy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
    }

    public void Go(HorizonApp app, VenueInfo venue)
    {
        if (!VenueAddress.TryCommand(venue, out var command, out var where, out var error))
        {
            app.Fail(error);
            return;
        }

        if (!this.IsLoaded())
        {
            app.Fail("Install and enable Lifestream, then press Teleport again.");
            return;
        }

        try
        {
            if (this.busy.InvokeFunc())
            {
                app.Fail("Lifestream is already traveling.");
                return;
            }

            this.execute.InvokeAction(command);
            app.Note("Teleporting to " + where + ".");
        }
        catch (IpcNotReadyError)
        {
            app.Fail("Lifestream isn't ready yet. Wait a moment, then try again.");
        }
        catch (IpcError)
        {
            app.Fail("Lifestream didn't take that trip. Check the world, district, ward, and plot.");
        }
    }

    private bool IsLoaded()
    {
        foreach (var plugin in this.pluginInterface.InstalledPlugins)
        {
            if (plugin.InternalName == "Lifestream" && plugin.IsLoaded)
                return true;
        }

        return false;
    }
}
