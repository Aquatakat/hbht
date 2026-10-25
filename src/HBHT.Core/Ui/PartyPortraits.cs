using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

/// <summary>
/// The party's portraits at the trek HUD's bottom centre (<c>Portraits::Draw</c>, <c>DrawParty</c>, <c>0x8002583c</c> in
/// 1.0): four glowing frames, each with its member's portrait at half alpha, sliding up from below the screen while
/// they're up and down out of it while they're down.
/// </summary>
public sealed class PartyPortraits
{
    /// <summary>The portrait's frame (<c>BORG8_PortraitHighlight</c>), the conversation box's too.</summary>
    public static readonly RecordId Frame = HudSprite.Id(0x00DF);

    /// <summary>The frames' tops when down, out of the screen, and when up; they move 4 a frame between.</summary>
    public const int Down = 240, Up = 184, Step = 4;

    /// <summary>The frames' left edges, 38 apart; each portrait is 6 inside its frame, both ways.</summary>
    public static readonly int[] FrameX = [84, 122, 160, 198];

    public const float FrameScale = 0.8f, PortraitScale = 0.75f;
    public const int PortraitInset = 6;

    /// <summary>Whether they're up, or on their way up.</summary>
    public bool Shown { get; set; } = true;

    /// <summary>The frames' top (<c>screen_bottom</c>), which starts out of the screen when the game starts.</summary>
    public int Top { get; private set; } = Down;

    /// <summary>A frame: they move 4 toward where they're going (once a rendered frame, whatever its ticks).</summary>
    public void StepFrame()
    {
        if (!Shown && Top < Down)
            Top += Step;
        else if (Shown && Top >= Up + 2)
            Top -= Step;
    }

    /// <summary>
    /// What they draw: all four frames, then each member's portrait in party order, the screen at
    /// <paramref name="brightness"/>. An empty slot keeps its frame; a dead member looks the same.
    /// </summary>
    /// <param name="portraits">Each slot's portrait, or null for an empty slot or a member with none.</param>
    public IEnumerable<HudSprite> Draw(IReadOnlyList<RecordId?> portraits, float brightness)
    {
        if (Top >= Down)
            yield break;
        var glow = HudSprite.Times(new Rgba(255, 255, 255, 128), brightness);
        foreach (var x in FrameX)
            yield return new HudSprite(Frame, x, Top, FrameScale, glow);
        // The original never sets the portraits' blue, which keeps the frames' blend and is dimmed again.
        var portrait = glow with { B = ColorBytes.Single(glow.B * brightness) };
        for (var i = 0; i < Math.Min(portraits.Count, FrameX.Length); i++)
        {
            if (portraits[i] is { } image)
                yield return new HudSprite(image, FrameX[i] + PortraitInset, Top + PortraitInset, PortraitScale, portrait);
        }
    }
}
