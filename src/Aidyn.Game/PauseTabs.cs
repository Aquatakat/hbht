using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// A tab of the pause menu as <see cref="PauseMenu"/> lays it out: made once, on the model each opening builds, faded
/// into its parchment beside the tab showing, and stepping only while it's the tab showing.
/// </summary>
internal interface IPauseTab
{
    /// <summary>How far its widgets are faded into the parchment, as it's beside the tab showing (ADR 0014).</summary>
    float Veil { set; }

    /// <summary>Whether the menu can slide from it to the next tab: not while a screen, popup or the journal is up.</summary>
    bool CanLeave { get; }

    /// <summary>Whether it's the tab showing, which alone steps and takes the mouse.</summary>
    void Activate(bool showing);
}

/// <summary>A pause menu tab drawn from a model that each opening of the menu builds afresh.</summary>
internal interface IPauseTab<in TModel> : IPauseTab
{
    /// <summary>The model, built again as the menu opens.</summary>
    TModel Model { set; }
}

public partial class PartyTabView : IPauseTab<IPartyWindow>
{
    IPartyWindow IPauseTab<IPartyWindow>.Model { set => Window = value; }

    bool IPauseTab.CanLeave => !Window.HasPopup;

    void IPauseTab.Activate(bool showing)
    {
        ProcessMode = showing ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        MouseFilter = showing ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
    }
}

public partial class SystemTabView : IPauseTab<SystemMenu>
{
    SystemMenu IPauseTab<SystemMenu>.Model { set => Menu = value; }

    bool IPauseTab.CanLeave => Menu.OnList;

    void IPauseTab.Activate(bool showing)
    {
        ProcessMode = showing ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        MouseFilter = showing ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
    }
}

/// <remarks>The World tab steps from the menu's own frame (<see cref="WorldMenu.Step"/>), so showing changes nothing here.</remarks>
public partial class WorldTabView : IPauseTab<WorldMenu>
{
    WorldMenu IPauseTab<WorldMenu>.Model { set => Menu = value; }

    bool IPauseTab.CanLeave => Menu.Message is null && Menu.Journal is null;

    void IPauseTab.Activate(bool showing)
    {
    }
}
