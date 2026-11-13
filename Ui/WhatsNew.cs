using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal sealed partial class MainWindow
{
    private bool whatsNewOpen;
    private bool noticesReady;
    private int whatsNewExpanded;

    public void ShowNews()
    {
        this.whatsNewOpen = true;
        this.whatsNewExpanded = 0;
    }

    private void ConsiderWhatsNew()
    {
        if (this.noticesReady)
            return;

        this.noticesReady = true;
        if (!string.Equals(this.app.Store.Config.SeenVersion, ReleaseNotes.CurrentVersion(), StringComparison.Ordinal))
            this.ShowNews();
    }

    private void MarkVersionSeen()
    {
        var current = ReleaseNotes.CurrentVersion();
        if (string.Equals(this.app.Store.Config.SeenVersion, current, StringComparison.Ordinal))
            return;

        this.app.Store.Config.SeenVersion = current;
        this.app.Store.MarkConfig();
        this.app.Store.SaveDirty();
    }

    private void DrawWhatsNew()
    {
        if (!this.whatsNewOpen)
            return;

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(520, 560), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 320), new Vector2(760, 860));
        ImGui.SetNextWindowFocus();
        var flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse;
        if (!ImGui.Begin("What's new###eh-news", ref this.whatsNewOpen, flags))
        {
            ImGui.End();
            if (!this.whatsNewOpen)
                this.MarkVersionSeen();
            return;
        }

        ImGui.SetWindowFontScale(this.app.Store.Config.UiScale);
        var version = ReleaseNotes.CurrentVersion();
        var headline = "Event Horizon " + version;
        foreach (var note in ReleaseNotes.Releases)
        {
            if (!string.Equals(note.Version, version, StringComparison.Ordinal))
                continue;

            headline = note.Title;
            break;
        }

        ImGui.TextDisabled("Event Horizon " + version);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextUnformatted(headline);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        for (var i = 0; i < ReleaseNotes.Releases.Length; i++)
        {
            var note = ReleaseNotes.Releases[i];
            var open = this.whatsNewExpanded == i;
            if (NewsRow("news" + i, note.Version + "    " + note.Title, open))
                this.whatsNewExpanded = open ? -1 : i;

            if (this.whatsNewExpanded != i)
            {
                ImGui.Spacing();
                continue;
            }

            ImGui.Spacing();
            foreach (var section in note.Sections)
            {
                ImGui.TextColored(ThemeHolder.Accent, section.Heading);
                foreach (var line in section.Lines)
                    NewsLine(line);
                ImGui.Spacing();
            }
        }

        ImGui.Spacing();
        if (Chrome.Button("Close##news"))
            this.whatsNewOpen = false;

        ImGui.SetWindowFontScale(1f);
        ImGui.End();
        if (!this.whatsNewOpen)
            this.MarkVersionSeen();
    }

    private static bool NewsRow(string id, string label, bool open)
    {
        var textSize = ImGui.CalcTextSize(label, false, Math.Max(48f, ImGui.GetContentRegionAvail().X - 36f));
        var width = ImGui.GetContentRegionAvail().X;
        var height = textSize.Y + 16f;
        var pressed = ImGui.InvisibleButton(id, new Vector2(width, height));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var fill = open ? ThemeHolder.Accent : ImGui.IsItemHovered() ? ThemeHolder.ButtonHover : ThemeHolder.Button;
        var ink = open ? Chrome.Contrast(ThemeHolder.Accent) : ThemeHolder.Text;
        Chrome.DrawPill(min, max, fill);
        var midY = min.Y + (height * 0.5f);
        var tri = min.X + 16f;
        var draw = ImGui.GetWindowDrawList();
        if (open)
        {
            draw.AddTriangleFilled(
                new Vector2(tri - 4f, midY - 2f),
                new Vector2(tri + 4f, midY - 2f),
                new Vector2(tri, midY + 3f),
                ImGui.GetColorU32(ink));
        }
        else
        {
            draw.AddTriangleFilled(
                new Vector2(tri - 2f, midY - 4f),
                new Vector2(tri - 2f, midY + 4f),
                new Vector2(tri + 3f, midY),
                ImGui.GetColorU32(ink));
        }

        draw.AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize(),
            new Vector2(min.X + 28f, min.Y + ((height - textSize.Y) * 0.5f)),
            ImGui.GetColorU32(ink),
            label,
            Math.Max(48f, width - 40f));
        return pressed;
    }

    private static void NewsLine(string text)
    {
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y + (ImGui.GetFontSize() * 0.42f);
        ImGui.GetWindowDrawList().AddCircleFilled(new Vector2(origin.X + 8f, y), 2.5f, ImGui.GetColorU32(ThemeHolder.Accent));
        ImGui.Indent(18f);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.Unindent(18f);
    }
}
