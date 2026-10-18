namespace Aidyn.Core.Ui;

/// <summary>
/// The menus' widget fade, one step of it (<c>MenuPopup.FadeIn</c>, <c>0x80032840</c>, <c>FadeOut</c>, <c>0x800328ac</c>):
/// a widget's alpha goes up <see cref="Step"/> a frame while it's under <see cref="Opaque"/> − <see cref="Step"/>, and then
/// to 255, and down <see cref="Step"/> a frame until it's that or less, and then to 0. So in it's 60, 120, 180, 240 and
/// 255, and out from 255 it's 195, 135, 75, 15 and 0. The popups (<see cref="PopupFade"/>), the journal, the
/// scripts' backdrop page and the System tab's pages fade alike, and the journal's arrows by a smaller step.
/// </summary>
/// <remarks>
/// It steps an alpha, and each owner decides when it starts and when it's done: a popup is gone at 0, the journal and the
/// backdrop page are closed at 0, and the System tab's outgoing page is dropped at 0. <c>ScreenFade</c> and the audio's
/// fades work differently in the original, and aren't this.
/// </remarks>
public static class WidgetFade
{
    /// <summary>How much the alpha changes a frame: 60 in five frames.</summary>
    public const int Step = 60;

    public const int Opaque = 255;

    /// <summary>The alpha one step more of fading in: <paramref name="step"/> more, until that would reach 255.</summary>
    public static int In(int alpha, int step = Step) => alpha < Opaque - step ? alpha + step : Opaque;

    /// <summary>The alpha one step more of fading out: <paramref name="step"/> less, to 0 once it's that or less.</summary>
    public static int Out(int alpha, int step = Step) => alpha <= step ? 0 : alpha - step;
}

/// <summary>
/// The menu's tweens (<c>Tween.CreateShort</c> at <c>0x800cc880</c>, <c>Tween.CreateByte</c> <c>0x800ccb88</c> in 1.0): a
/// value moved from where it is to an end over so many frames, a step a frame, on a curve, and truncated to a
/// whole number each step. <see cref="MenuSlide{T}"/> counts the frames, and these are the curves.
/// </summary>
public static class MenuTween
{
    /// <summary>
    /// The share of the way a tween has gone after <paramref name="frame"/> of <paramref name="frames"/>, on its curve
    /// (<c>Tween.SineInOut</c>, <c>0x800e6590</c>): 0.5 × sin(π t − π/2) + 0.5, which starts and ends slowly.
    /// </summary>
    public static float Ease(int frame, int frames)
    {
        var t = Math.Clamp((float)frame / frames, 0, 1);
        return 0.5f * MathF.Sin(MathF.PI * t - MathF.PI / 2) + 0.5f;
    }

    /// <summary>
    /// The share of the way a tween on the ease-out curve has gone (<c>MenuTween.EaseOut</c>, <c>0x800e65c0</c>):
    /// sin(π t / 2), which starts quickly and ends slowly.
    /// </summary>
    public static float EaseOut(int frame, int frames) => MathF.Sin(MathF.PI / 2 * Math.Clamp((float)frame / frames, 0, 1));

    /// <summary>A value that far from <paramref name="from"/> to <paramref name="to"/>, truncated as the original's are.</summary>
    public static int Value(int from, int to, float ease) => (int)(from + (to - from) * ease);
}

/// <summary>
/// A change under way that the menu draws as it was and as it is, the share of the way it's gone eased by a curve of
/// <see cref="MenuTween"/>: a list sliding out as another slides in, a save sliding out as the next slides in, or the
/// Party tab's header changing over to another member. The owner steps it a frame, and drops it once it's finished.
/// </summary>
/// <remarks>
/// A tween steps its frames and then finishes on the next, so the widget is busy one frame longer than it moves.
/// </remarks>
/// <typeparam name="T">What it changed from: the list or the slot shown before, or whatever the view needs of the old state.</typeparam>
/// <param name="from">What was shown before the change.</param>
/// <param name="direction">Which way the stick moved it: 1 right, −1 left, or 0 if the change has none.</param>
/// <param name="frames">The frames it takes to move.</param>
/// <param name="curve">The curve of <see cref="MenuTween"/> it goes on, from a frame and the frames.</param>
public sealed class MenuSlide<T>(T from, int direction, int frames, Func<int, int, float> curve)
{
    public T From { get; } = from;

    public int Direction { get; } = direction;

    /// <summary>The frames it has gone: 0 as it starts.</summary>
    public int Frame { get; private set; }

    /// <summary>How far it has gone, eased, from 0 to 1.</summary>
    public float Change => curve(Frame, frames);

    /// <summary>Steps it a frame, and says whether it's still under way: false once it has finished, a frame after it moved its last.</summary>
    public bool Step() => ++Frame <= frames;
}
