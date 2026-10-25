namespace HBHT.Plugins.Records;

/// <summary>
/// How a trapped container's explosion is drawn (<c>aidyn:explosion/0</c>, the only one; <c>Containers.Explode</c>,
/// <c>0x8000ffe4</c> in 1.0): its particles' texture, the particle texture slot (2) the code names, looked up in the engine's
/// particle list (<c>partTextInds</c>). Its fires, burst and lights are the engine's (<c>HBHT.Core.Particles.Explosion</c>,
/// docs/systems/map-objects.md#containers).
/// </summary>
/// <param name="Texture">What its fires' flames and its burst's debris are drawn with, or null to draw none.</param>
public sealed record ExplosionRecord(RecordId Id, RecordId? Texture);
