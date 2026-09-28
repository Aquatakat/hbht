namespace Aidyn.Plugins.Records;

/// <summary>
/// An animated model and its animation state machine (<c>aidyn:actor/0xHHHH</c>, a Borg7). Every
/// state's animation animates <see cref="Scene"/>, whose asset holds them as glTF animations named
/// <c>borg6 &lt;index&gt;</c>.
/// </summary>
/// <param name="RootTransform">
/// The scene transform drawn with its position zeroed. Its motion moves the actor instead.
/// </param>
/// <param name="Head">A head drawn on the actor, which it turns to look at things nearby.</param>
public sealed record ActorRecord(RecordId Id, RecordId Scene, int RootTransform, int InitialState, IReadOnlyList<ActorState> States,
    ActorHead? Head = null);

/// <summary>
/// A head scene drawn at one of the actor's locators. Only Alaron's actor has one: his body's scene
/// has only a placeholder at the neck (<c>Actor::ChangeAppearance</c>, <c>some_borg5</c>).
/// </summary>
/// <param name="Locator">The locator of the actor's scene it sits on, and faces along.</param>
public sealed record ActorHead(RecordId Scene, int Locator);

/// <param name="Branches">Tried in order when the animation ends, or when the request changes.</param>
public sealed record ActorState(RecordId Animation, IReadOnlyList<ActorBranch> Branches);

/// <summary>
/// A way out of a state: taken for <see cref="Request"/> (255 is any) with a chance in 100.
/// </summary>
/// <param name="Interrupts">Rolled for as soon as the request changes, not only when the animation ends.</param>
public sealed record ActorBranch(int Request, int Chance, bool Interrupts, int State);
