using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class DueBanner
{
    public static void Draw(HorizonApp app)
    {
        if (string.IsNullOrWhiteSpace(app.Session.DueCueId))
            return;

        var pulse = 0.35f + (0.65f * MathF.Abs(MathF.Sin((float)ImGui.GetTime() * 5.5f)));
        var fill = ThemeService.With(ThemeHolder.Gold, 0.16f + (0.28f * pulse));
        var edge = ThemeService.With(pulse > 0.7f ? ThemeHolder.Pink : ThemeHolder.Gold, 0.95f);
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(240f, ImGui.GetContentRegionAvail().X);
        const float height = 78f;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.GetColorU32(fill), 14f);
        draw.AddRect(origin, origin + new Vector2(width, height), ImGui.GetColorU32(edge), 14f, ImDrawFlags.None, 2f + (pulse * 2f));
        ImGui.Dummy(new Vector2(width, height));
        ImGui.SetCursorScreenPos(origin + new Vector2(16f, 12f));
        ImGui.TextColored(ThemeHolder.Gold, "Time for the next " + (app.Session.DueCueKind ?? "cue"));
        ImGui.SameLine();
        ImGui.TextUnformatted(app.Session.DueCueTitle ?? "");
        ImGui.SetCursorScreenPos(origin + new Vector2(16f, 40f));
        if (Chrome.Accent("Go to it"))
            app.OpenDueCue();
        ImGui.SameLine();
        if (Chrome.Button("Quiet"))
            app.QuietCue();
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, height + 8f));
    }
}
