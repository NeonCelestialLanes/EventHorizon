using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;

namespace EventHorizon.Ui;

internal static class ChatCopy
{
    public static void Button(HorizonApp app, string key, string? text, string label, string empty) =>
        Button(app, key, ChatLines.Yells(text), label, empty);

    public static void Button(HorizonApp app, string key, IReadOnlyList<string> pieces, string label, string empty)
    {
        if (pieces.Count == 0)
        {
            if (Chrome.Button(label + "##" + key))
                app.Fail(empty);
            return;
        }

        var id = key + "\n" + string.Join('\n', pieces);
        if (!app.Session.CopyNext.TryGetValue(id, out var next) || next < 0 || next >= pieces.Count)
            next = 0;

        var caption = pieces.Count == 1 ? label : label + " " + (next + 1);
        if (!Chrome.Button(caption + "##" + key))
            return;

        ImGui.SetClipboardText(pieces[next]);
        var number = next + 1;
        var last = number >= pieces.Count;
        app.Session.CopyNext[id] = last ? 0 : next + 1;
        app.Note(pieces.Count == 1
            ? "Copied."
            : last
                ? "Copied " + number + ". The next press starts over."
                : "Copied " + number + ".");
    }
}
