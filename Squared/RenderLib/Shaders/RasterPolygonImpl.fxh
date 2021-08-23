void computeTLBR_Polygon (
    float2 radius, float outlineSize, float4 params,
    float vertexCount, float closed,
    out float2 tl, out float2 br
) {
    // FIXME
    tl = -999;
    br = 999;
}

void evaluatePolygon (
    int type, float2 radius, float outlineSize, float4 params,
    in float2 worldPosition, in float2 a, in float2 b, in float2 c, in bool simple,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float gradientWeight, inout float gradientAngle
) {
}