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

        var motion = app.Store.Config.Animations;
        var pulse = motion ? 0.35f + (0.65f * MathF.Abs(MathF.Sin((float)ImGui.GetTime() * 5.5f))) : 1f;
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(240f, ImGui.GetContentRegionAvail().X);
        var button = Chrome.ButtonHeight();
        var height = Math.Max(56f, button + 16f);
        var draw = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(width, height);
        var left = ImGui.GetColorU32(ThemeService.With(ThemeHolder.Cyan, 0.10f + (motion ? 0.12f * pulse : 0.12f)));
        var right = ImGui.GetColorU32(ThemeService.With(ThemeHolder.Violet, 0.10f + (motion ? 0.12f * pulse : 0.12f)));
        draw.AddRectFilledMultiColor(origin, end, left, right, right, left);
        draw.AddRect(origin, end, ImGui.GetColorU32(ThemeService.With(ThemeHolder.Cyan, motion ? 0.45f + (0.4f * pulse) : 0.7f)), 12f, ImDrawFlags.None, motion ? 1f + pulse : 1f);
        ImGui.Dummy(new Vector2(width, height));

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttons = Chrome.ButtonWidth("Go to it") + Chrome.ButtonWidth("Quiet") + spacing;
        ImGui.SetCursorScreenPos(origin + new Vector2(16f, (height - ImGui.GetTextLineHeight()) * 0.5f));
        ImGui.TextUnformatted("Next block is due soon:");
        ImGui.SameLine();
        ImGui.TextColored(ThemeHolder.Cyan, app.Session.DueCueTitle ?? "");
        ImGui.SetCursorScreenPos(new Vector2(end.X - buttons - 12f, origin.Y + ((height - button) * 0.5f)));
        if (Chrome.Accent("Go to it"))
            app.OpenDueCue();
        ImGui.SameLine();
        if (Chrome.Button("Quiet"))
            app.QuietCue();
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, height));
        ImGui.Dummy(new Vector2(1f, 8f));
    }
}
