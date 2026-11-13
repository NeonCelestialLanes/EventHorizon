using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;

namespace EventHorizon.Ui;

internal sealed class ScriptFace : IDisposable
{
    private static readonly int[] Sizes = [16, 18, 20, 23, 26, 29, 33, 37, 42, 48, 54, 60];

    private readonly IFontHandle[] fonts;
    private IFontHandle? pushed;

    public ScriptFace(IDalamudPluginInterface plugin)
    {
        this.fonts = new IFontHandle[Sizes.Length];
        for (var index = 0; index < Sizes.Length; index++)
        {
            var size = Sizes[index];
            this.fonts[index] = plugin.UiBuilder.FontAtlas.NewDelegateFontHandle(builder => builder.OnPreBuild(toolkit =>
            {
                toolkit.Font = toolkit.AddDalamudDefaultFont(size);
            }));
        }
    }

    public bool Push(float uiScale, float scriptScale)
    {
        this.Pop();
        var target = 17f * Math.Clamp(uiScale, 0.8f, 1.6f) * Math.Clamp(scriptScale, 1f, 2.2f);
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < Sizes.Length; index++)
        {
            var distance = MathF.Abs(Sizes[index] - target);
            if (distance >= bestDistance)
                continue;
            best = index;
            bestDistance = distance;
        }

        var font = this.fonts[best];
        if (font is not { Available: true })
            return false;

        font.Push();
        this.pushed = font;
        return true;
    }

    public void Pop()
    {
        this.pushed?.Pop();
        this.pushed = null;
    }

    public void Dispose()
    {
        this.Pop();
        foreach (var font in this.fonts)
            font.Dispose();
    }
}
