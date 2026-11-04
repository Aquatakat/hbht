namespace HBHT.Plugins.Records;

/// <summary>
/// A DB record's name, and an entity's portrait, body and sounds: how entities, spells, items and loot tables read alone.
/// </summary>
/// <param name="Portrait">The image beside what an entity says (<c>EntityDB::GetPortrait</c>). None for other records.</param>
/// <param name="Body">How an entity stands in the world. None for other records.</param>
/// <param name="Sounds">An entity's footsteps and voice (<see cref="EntityRecord.Sounds"/>). None for silence, and for other records.</param>
public sealed record DbRecord(RecordId Id, string Name, RecordId? Portrait = null, EntityBody? Body = null, BodySounds? Sounds = null);

/// <summary>
/// How an entity stands in the world, as a wanderer, a cut-scene's actor or a combatant
/// (<c>gEntityExtras</c>, <c>dailougEnt_info_array</c>; <c>EntityDB::GetBorg7</c>, <c>GetCollideRadius</c>,
/// <c>GetScale</c> and <c>GetHeight</c>).
/// </summary>
/// <param name="Actor">Its model. A record the tables leave out is a bat, as the code makes it.</param>
/// <param name="Radius">Its collision sphere's radius, before <paramref name="Scale"/>. Every one from the dialogue entity table has 0.55, which the code gives them.</param>
/// <param name="Scale">How much bigger than its actor's model it's drawn, and how much bigger its sphere is when bodies push each other.</param>
/// <param name="Height">About how tall its model is at a scale of 1, in scene units.</param>
/// <param name="Strike">
/// The share of its attack's animation at which the blow lands and the target starts to react (<c>EntityBody.StrikePoint</c>,
/// <c>0x8006C7FC</c>, which <c>ProcessPlayers</c> reads in combat); 0.4 for one from the dialogue entity table or one the tables leave out. A shot's, a throw's and the rest are in <paramref name="Strikes"/>.
/// </param>
/// <param name="Strikes">
/// The share of each animation request's animation (<c>aidyn:request/n</c>) at which what it does happens, for the requests the port
/// times by the body. None of these are the original's fields; the port makes them the entity's, for mods (the user's choice), and
/// the importer leaves them out, so each is the original's (<see cref="StrikeFor"/>):
/// <list type="bullet">
/// <item>a bow's shot (14) and a throw (15): where the arrow or what's thrown leaves the hand, 0.4 for every entity
/// (<c>EntityBody.ShotStrike</c>, <c>0x8006C884</c>);</item>
/// <item>the cast (5): where its spell's effect starts, its visual, the glow and the sky's dimming, and Teleportation's move; 0, the
/// cast's start (<c>Combatant.CastCurrentSpell</c>);</item>
/// <item>opening a container (11) and using a door or another teleporter (19), as it leads the party in trek: where the container
/// opens or the teleporter is used, its lock, trap or fade; 0, as A opens it at once (<c>MapObjectUse.Reach</c>, <c>0x80004654</c>,
/// uses the object and then asks for the animation).</item>
/// </list>
/// Any other attack's is <paramref name="Strike"/>.
/// </param>
/// <param name="WeaponsInModel">
/// Its weapon, arrow or flask isn't drawn in the hand in combat, its model having its weapons already
/// (<c>Combatant.HasNoWeaponAttachment</c>, <c>0x80066C5C</c>, the entity list's places at <c>0x800D2A0C</c>): Shamsuk, the Gorgon,
/// the Chaos Lieutenant, the Harpy, the Marquis, the Minotaur, the Minotaur Lord and Niesen. A shield or a bow is drawn all the same.
/// </param>
/// <param name="BowHand">
/// Which of its actor's locators hold a bow and its arrow (<c>EntityBody.Hand</c>, <c>0x8006C8BC</c>, a switch on the entity list's
/// place): the other way round from the rest for the bandit bosses and woodsmen, Becan and Keelin.
/// </param>
/// <param name="StopReach">
/// Under Modern movement, the port's, as it leads the party in trek: the furthest the rest of its step carries it once the stick is
/// let go, before its scale. The original has no such field, and plays the whole step (docs/systems/movement.md#modern-movement).
/// </param>
/// <param name="StopPace">Under Modern movement, how many times as fast the rest of its step plays once the stick is let go.</param>
/// <param name="Stride">Under Modern movement, the ticks its speed is the mean root motion of, or 1 for none.</param>
/// <param name="SetOffTurn">
/// Under Modern movement, how many degrees a tick it turns toward the stick as it sets off from standing, until its animation's
/// root first moves it. The original's is 10, as on the move.
/// </param>
/// <param name="Alpha">
/// How opaque its body is in combat, 0 to 255 (the body's <c>+0x70C</c>, which <c>EntityBody.ShadowAlpha</c>, <c>0x8006C798</c>, gives
/// <c>Combat.CreateBody</c>, <c>0x8005C474</c>): it caps the body's own draw and its shadow's. The original makes it 127 for the entity list's
/// places <c>0xAF</c> to <c>0xB5</c> and <c>0xC0</c>, and 255 for every other. A freed body fades out from it.
/// </param>
public sealed record EntityBody(RecordId Actor, float Radius, float Scale, float Height, float Strike = EntityBody.DefaultStrike,
    IReadOnlyDictionary<RecordId, float>? Strikes = null,
    float StopReach = EntityBody.DefaultStopReach, float StopPace = EntityBody.DefaultStopPace, int Stride = EntityBody.DefaultStride,
    float SetOffTurn = EntityBody.DefaultSetOffTurn, byte Alpha = EntityBody.DefaultAlpha, bool WeaponsInModel = false,
    BowHand BowHand = BowHand.Usual)
{
    /// <summary>A body's alpha for every entity the original doesn't make translucent.</summary>
    public const byte DefaultAlpha = 255;

    /// <summary>The strike's share for a record the tables leave out (<c>0x800D2CF8</c>).</summary>
    public const float DefaultStrike = 0.4f;

    /// <summary>The requests whose share a body's <see cref="Strikes"/> may give besides an attack's: the shot and the throw, the cast, and opening a container and using a teleporter.</summary>
    public const int ShotRequest = 14, ThrowRequest = 15, CastRequest = 5, OpenContainerRequest = 11, UseTeleporterRequest = 19;

    /// <summary>
    /// The share of <paramref name="request"/>'s animation at which what it does happens: the body's own (<see cref="Strikes"/>), else
    /// the original's, which is 0.4 for a shot or a throw, 0 for a cast, a container or a teleporter, and <see cref="Strike"/> for any
    /// other.
    /// </summary>
    public float StrikeFor(int request)
    {
        if (Strikes is { Count: > 0 } strikes && strikes.TryGetValue(Requests.Id(request), out var share))
            return share;
        return request switch
        {
            ShotRequest or ThrowRequest => DefaultShotStrike,
            CastRequest => DefaultCastStrike,
            OpenContainerRequest or UseTeleporterRequest => DefaultOpenStrike,
            _ => Strike,
        };
    }

    /// <summary>Every entity's share for a shot or a throw in the original (<c>0x800D2CFC</c>).</summary>
    public const float DefaultShotStrike = 0.4f;

    /// <summary>Every entity's share for a cast's effect in the original: the cast's start.</summary>
    public const float DefaultCastStrike = 0;

    /// <summary>Every entity's share for opening a container or using a teleporter in the original: the press's tick.</summary>
    public const float DefaultOpenStrike = 0;

    /// <summary>
    /// What the importer gives every entity for Modern movement, the user's tuning to Alaron's run (Oct 30, 2026): 0.3 is about a
    /// quarter of its step of 1.2, and 10 ticks about half its loop of 19.
    /// </summary>
    public const float DefaultStopReach = 0.3f, DefaultStopPace = 3, DefaultSetOffTurn = 20;

    /// <inheritdoc cref="DefaultStopReach"/>
    public const int DefaultStride = 10;
}

/// <summary>Which of an actor's locators hold a bow and its arrow (<c>EntityBody.Hand</c>, <c>0x8006C8BC</c>).</summary>
public enum BowHand
{
    /// <summary>The bow at locator 1 and the arrow at 0.</summary>
    Usual,
    /// <summary>The bow at locator 0 and the arrow at 1.</summary>
    Other,
}
