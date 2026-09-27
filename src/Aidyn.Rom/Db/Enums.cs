namespace Aidyn.Rom.Db;

public enum Aspect : byte
{
    None = 0,
    Lunar = 1,
    Solar = 2,
}

/// <summary>Damage and resistance elements.</summary>
public enum Element : byte
{
    None,
    Earth,
    Solar,
    Physical,
    Necromancy,
    Fire,
    Lunar,
    Naming,
    Water,
    Magic,
    Air,
    Star,
    Elemental,
    Chaos,
    Cutting,
    Smashing,
    Holy,
}

public enum MagicSchool : byte
{
    /// <summary>On an entity, Chaos means every school.</summary>
    Chaos,
    Elemental,
    Naming,
    Necromancy,
    None,
    Star,
}

/// <summary>Weapon classes, which are also the weapon skills.</summary>
public enum WeaponClass : byte
{
    Bite,
    Breath,
    Claw,
    Hafted,
    Missile,
    Pole,
    Spit,
    Sting,
    Sword,
    Thrown,
    Tusk,
}

/// <summary>How many targets a spell takes.</summary>
public enum SpellTargetCount : byte
{
    Self,
    One,
    All,
    /// <summary>Grows with the spell's rank.</summary>
    ByRank,
}

/// <summary>Who a spell can target.</summary>
public enum SpellTargetType : byte
{
    /// <summary>Only usable outside combat.</summary>
    Field,
    /// <summary>Party members, or charmed enemies.</summary>
    Party,
    Enemy,
    Area,
    All,
}

/// <summary>The reagent a spell consumes.</summary>
public enum Reagent : byte
{
    None,
    Spice,
    Herb,
    Gemstone,
}
