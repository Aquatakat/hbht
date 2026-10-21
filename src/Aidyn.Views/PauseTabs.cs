using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Views;

/// <summary>
/// A tab of the pause menu as the runtime's <c>PauseMenuView</c> lays it out: made once, on the model each opening builds, faded
/// into its parchment beside the tab showing, and stepping only while it's the tab showing.
/// </summary>
/// <remarks>
/// Public, and here rather than with the pause menu in the runtime, because the Party tab is one and the editor shows it
/// too (ADR 0022). The runtime's System and World tabs implement it there.
/// </remarks>
public interface IPauseTab
{
    /// <summary>How far its widgets are faded into the parchment, as it's beside the tab showing (ADR 0014).</summary>
    float Veil { set; }

    /// <summary>Whether it's the tab showing, which alone steps and takes the mouse.</summary>
    void Activate(bool showing);
}

/// <summary>A pause menu tab drawn from a model that each opening of the menu builds afresh.</summary>
public interface IPauseTab<in TModel> : IPauseTab
{
    /// <summary>The model, built again as the menu opens.</summary>
    TModel Model { set; }
}

public partial class PartyTabView : IPauseTab<IPartyWindow>
{
    IPartyWindow IPauseTab<IPartyWindow>.Model { set => Window = value; }

    void IPauseTab.Activate(bool showing)
    {
        ProcessMode = showing ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        MouseFilter = showing ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
    }
}
