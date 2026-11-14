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

        if (!string.IsNullOrWhiteSpace(sync.Error))
            ImGui.TextColored(ThemeHolder.Red, sync.Error);

        var shown = sync.Code is null ? "" : sync.Code.Length == 6 ? sync.Code[..3] + "-" + sync.Code[3..] : sync.Code;
        if (sync.Sharing && sync.Code is not null)
        {
            Chrome.CodeWell(shown);
            ImGui.TextDisabled(sync.Status);
            if (Chrome.Button("Copy code##sync"))
            {
                ImGui.SetClipboardText(shown);
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
        }
        else
        {
            if (Chrome.Accent("Create sync code"))
                sync.StartHost();
            Chrome.CodeWell("No code yet", ThemeHolder.Muted);
        }

        if (!(sync.Sharing && sync.Code is not null))
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(sync.Status) ? "No code yet" : sync.Status);

        ImGui.Spacing();
        ImGui.TextDisabled("Join with a code");
        var joinText = sync.JoinCode ?? "";
        ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - 96f));
        if (ImGui.InputText("##join-code", ref joinText, 64))
            sync.JoinCode = joinText;
        ImGui.SameLine();
        if (Chrome.Button("Join##sync"))
            sync.Join(sync.JoinCode ?? "");
    }
}
