using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EventHorizon;

internal static class CrowdYell
{
    public static unsafe void Send(string line)
    {
        var text = (line ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (!text.StartsWith("/yell ", StringComparison.OrdinalIgnoreCase))
            text = "/yell " + text;
        if (text.Length <= "/yell ".Length)
            throw new InvalidOperationException("There is nothing to announce.");
        if (text.Length > Hosting.ChatLines.YellLimit)
            throw new InvalidOperationException("That yell is longer than chat allows.");

        var ui = UIModule.Instance();
        if (ui == null)
            throw new InvalidOperationException("Log in before announcing.");

        using var message = new Utf8String();
        message.SetString(text);
        ui->ProcessChatBoxEntry(&message);
    }
}
