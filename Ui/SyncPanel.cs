using Dalamud.Bindings.ImGui;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class SyncPanel
{
    public static void Draw(HorizonApp app)
    {
        var sync = app.Sync;
        if (sync is null)
            return;

        Chrome.Section("Live sync");
        Chrome.Muted("Create a code and give it to the other planners, the same way Pose Kit does. Anyone who enters it sees this event as you change it, including the live show. Same network uses the short code. If they are somewhere else, send the longer address. Chat is never posted.");
        if (!string.IsNullOrWhiteSpace(sync.Error))
            ImGui.TextColored(ThemeHolder.Red, sync.Error);

        if (sync.Sharing && sync.Code is not null)
        {
            ImGui.TextUnformatted(sync.Code.Length == 6 ? sync.Code[..3] + "-" + sync.Code[3..] : sync.Code);
            ImGui.TextDisabled(sync.Status);
            if (Chrome.Button("Copy code##sync"))
            {
                ImGui.SetClipboardText(sync.Code.Length == 6 ? sync.Code[..3] + "-" + sync.Code[3..] : sync.Code);
                app.Note("Copied the sync code.");
            }

            if (sync.IsHost && sync.Direct is not null)
            {
                ImGui.SameLine();
                if (Chrome.Button("Copy address##sync"))
                {
                    ImGui.SetClipboardText(sync.Direct);
                    app.Note("Copied the address for a planner who is not on your network.");
                }
            }

            ImGui.SameLine();
            if (Chrome.Button("Stop sharing##sync"))
                sync.Stop();
            return;
        }

        if (Chrome.Accent("Create sync code"))
            sync.StartHost();
        var join = sync.JoinCode;
        if (Chrome.Field("Join with a code", join, value => sync.JoinCode = value ?? ""))
            _ = join;
        if (Chrome.Button("Join##sync"))
            sync.Join(sync.JoinCode);
        ImGui.TextDisabled(sync.Status);
    }
}
