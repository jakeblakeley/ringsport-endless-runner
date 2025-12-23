#ifndef ARC_EFFECT_INCLUDED
#define ARC_EFFECT_INCLUDED

// Arc effect function for float precision
// All parameters passed as inputs for Shader Graph Custom Function compatibility
//
// Inputs:
//   PositionWS - World space vertex position
//   PlayerPosition - Player world position (from global _PlayerPosition)
//   ArcStrength - Displacement intensity, typically 0-50 (from global _ArcStrength)
//   ArcDistance - Distance for max effect, typically 10-100 (from global _ArcDistance)
//
// Output:
//   ModifiedPositionWS - Position with arc displacement applied
void ArcEffect_float(
    float3 PositionWS,
    float3 PlayerPosition,
    float ArcStrength,
    float ArcDistance,
    out float3 ModifiedPositionWS)
{
    // Calculate horizontal distance from player (XZ plane only)
    float2 offset = PositionWS.xz - PlayerPosition.xz;
    float dist = length(offset);

    // Normalize distance by arc distance parameter
    float arcBlend = saturate(dist / ArcDistance);

    // Apply quadratic falloff for smooth, natural curve
    float arcAmount = arcBlend * arcBlend * ArcStrength;

    // Offset vertex downward (creates horizon dip effect)
    ModifiedPositionWS = PositionWS;
    ModifiedPositionWS.y -= arcAmount;
}

// Arc effect function for half precision (mobile optimization)
void ArcEffect_half(
    half3 PositionWS,
    half3 PlayerPosition,
    half ArcStrength,
    half ArcDistance,
    out half3 ModifiedPositionWS)
{
    // Calculate horizontal distance from player (XZ plane only)
    half2 offset = PositionWS.xz - PlayerPosition.xz;
    half dist = length(offset);

    // Normalize distance by arc distance parameter
    half arcBlend = saturate(dist / ArcDistance);

    // Apply quadratic falloff for smooth, natural curve
    half arcAmount = arcBlend * arcBlend * ArcStrength;

    // Offset vertex downward (creates horizon dip effect)
    ModifiedPositionWS = PositionWS;
    ModifiedPositionWS.y -= arcAmount;
}

#endif // ARC_EFFECT_INCLUDED