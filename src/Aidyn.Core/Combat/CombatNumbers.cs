using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>
/// The numbers over heads (<c>CombatStatIndicator</c>, <c>combatstatindicator.cpp</c>, <c>0x8008A000</c>–<c>0x8008A8D4</c>): a pool of
/// 16, each a number of up to three digits put together from the digit textures, billboarded where it was shown and held there
/// for 120 ticks. Damage is yellowish white and rises, grows and fades out; healing is blue and, as the code reads, sinks, shrinks
/// and fades in. Nothing is shown for a miss, a critical or a death. The runtime draws them.
/// </summary>
public sealed class CombatNumbers
{
    /// <summary>The pool's size.</summary>
    public const int Pool = 16;

    /// <summary>How long one shows (<c>CombatStatIndicator.Frame</c>, <c>0x8008A630</c>).</summary>
    public const int LifeTicks = 120;

    /// <summary>
    /// The digit textures 0–9 (<c>0x800E31C0</c>), Borg1 archive indexes (<c>aidyn:texture/…</c>), 16 × 16 and upside down.
    /// </summary>
    public static readonly IReadOnlyList<int> DigitTextures = [22, 17, 21, 20, 15, 14, 19, 18, 13, 16];

    private readonly List<CombatNumber> _shown = [];

    /// <summary>The numbers showing, oldest first.</summary>
    public IReadOnlyList<CombatNumber> Shown => _shown;

    /// <summary>
    /// Shows <paramref name="amount"/> over a body (<c>CombatStatIndicator.Add</c>, <c>0x8008A128</c>): at its X and Z, as high as
    /// its head (its feet plus its entity's height), half a unit higher for healing. The amount is a byte; the oldest goes when the
    /// pool is full.
    /// </summary>
    public void Add(Combatant over, int amount, bool healing)
    {
        amount = (byte)amount;
        if (amount == 0)
            return;
        if (_shown.Count == Pool)
            _shown.RemoveAt(0);
        var body = over.Body;
        var at = body.Sphere.Position;
        at.Y += -body.ScaledRadius + over.Look.Height * over.Look.Scale + (healing ? 0.5f : 0);
        _shown.Add(new CombatNumber(over.Slot, at, amount, healing));
    }

    /// <summary>A tick: each ages, and goes at <see cref="LifeTicks"/>.</summary>
    public void Tick()
    {
        foreach (var number in _shown)
            number.Ticks++;
        _shown.RemoveAll(n => n.Ticks >= LifeTicks);
    }

    /// <summary>Takes a body's away as it goes (<c>0x8008AA68</c>, from <c>Combat.FreeBody</c>).</summary>
    public void Remove(int slot) => _shown.RemoveAll(n => n.Slot == slot);
}

/// <summary>One number over a head: whose body it's over, where, how much, and whether it's healing.</summary>
public sealed class CombatNumber(int slot, Vector3 at, int amount, bool healing)
{
    public int Slot { get; } = slot;

    /// <summary>Where it was shown: its centre at the start.</summary>
    public Vector3 At { get; } = at;

    public int Amount { get; } = amount;

    public bool Healing { get; } = healing;

    /// <summary>Ticks it has shown.</summary>
    public int Ticks { get; internal set; }

    /// <summary>Its colour, damage's (255, 255, 128) or healing's (128, 128, 255).</summary>
    public Rgba Color => Healing ? new Rgba(128, 128, 255, Alpha) : new Rgba(255, 255, 128, Alpha);

    /// <summary>Its digits, most significant first.</summary>
    public string Digits => Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The pulse of its life: sin(t / 120 × 90°), negative for healing.</summary>
    private float S => MathF.Sin(Ticks / (float)CombatNumbers.LifeTicks * 1.5708f) * (Healing ? -1 : 1);

    /// <summary>
    /// Its centre's height now, and its width and height, at <paramref name="scale"/>, its distance from the camera × 0.15 so it
    /// keeps its size on the screen (<c>0x8008A630</c>): it rises half a scale and grows by a scale across and 0.4 up, from 0.5 ×
    /// 0.2 for damage and 1.5 × 0.6 for healing.
    /// </summary>
    public (float Y, float Width, float Height) Place(float scale)
    {
        var s = S;
        var (w, h) = Healing ? (1.5f, 0.6f) : (0.5f, 0.2f);
        return (At.Y + s * 0.5f * scale, w + s * scale, h + s * 0.4f * scale);
    }

    /// <summary>The scale for a camera at <paramref name="listener"/> (<c>Camera.Listener</c>).</summary>
    public float Scale(Vector3 listener) => Vector3.Distance(At, listener) * 0.15f;

    /// <summary>Its alpha: damage's from 255 down to 0, healing's from 0 up.</summary>
    public byte Alpha => (byte)((Healing ? 0 : 255) - (sbyte)(int)(S * 255));
}
