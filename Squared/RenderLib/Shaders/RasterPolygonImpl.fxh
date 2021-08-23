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
    float2 radius, float outlineSize, float4 params,
    in float2 worldPosition, in float vertexCount, in float closed, in bool simple,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float gradientWeight, inout float gradientAngle
) {
    // FIXME
    distance = 0;
    gradientWeight = 0;
}