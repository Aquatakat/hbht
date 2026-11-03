namespace HBHT.Core.Ui;

/// <summary>
/// A menu's question over choices as it fades, 90 a menu update as a message does (<see cref="MenuOverlay.MessageStep"/>):
/// in as it's asked, and out as it's answered, drawn on after it's gone from <see cref="Up"/> (<see cref="Leaving"/>).
/// </summary>
/// <remarks>
/// 1.0's questions don't fade: the question over choices (<c>WidgetChoiceDia</c>, <c>ChoicePopup.Create</c>,
/// <c>0x800457F4</c>) and the drop question (<c>DollBarterConfirm</c>) are drawn whole from their first frame and go at the
/// press. The port fades them, the user's choice, as every other feathered box fades. Presses reach a question as it fades
/// in, as they do the conversation box, and an answered one takes none: what was under it takes them at once.
/// </remarks>
public sealed class QuestionFade<T> where T : class
{
    private PopupFade? _fade;
    private PopupFade? _leavingFade;

    /// <summary>The question up, which takes the presses, or none.</summary>
    public T? Up { get; private set; }

    /// <summary>The question up's alpha.</summary>
    public int Alpha => _fade?.Alpha ?? WidgetFade.Opaque;

    /// <summary>An answered question fading out, only drawn, or none.</summary>
    public T? Leaving { get; private set; }

    /// <summary>The answered question's alpha.</summary>
    public int LeavingAlpha => _leavingFade?.Alpha ?? 0;

    /// <summary>Whether either is fading, and so is drawn anew each update.</summary>
    public bool Fading => (_fade is not null && !_fade.IsOpen) || Leaving is not null;

    /// <summary>Puts a question up, fading in from nothing.</summary>
    public void Ask(T question)
    {
        Up = question;
        _fade = new PopupFade(MenuOverlay.MessageStep);
    }

    /// <summary>Takes the question down, answered or not: it fades out from where it is.</summary>
    public void Close()
    {
        if (Up is null)
            return;
        (Leaving, _leavingFade) = (Up, _fade);
        _leavingFade!.Close();
        (Up, _fade) = (null, null);
    }

    /// <summary>Steps both fades by a menu update, dropping the answered question once it's gone.</summary>
    public void Step()
    {
        _fade?.Advance();
        _leavingFade?.Advance();
        if (_leavingFade?.IsGone == true)
            (Leaving, _leavingFade) = (null, null);
    }
}
