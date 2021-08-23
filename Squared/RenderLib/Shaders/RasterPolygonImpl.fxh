#define NODE_LINE 0
#define NODE_BEZIER 1

float get (int base, int offset) {
    float4 uv = float4((base + offset) * PolygonVertexBufferInvWidth, 0, 0, 0);
    return tex2Dlod(VertexDataSampler, uv).r;
}

void computeTLBR_Polygon (
    in float2 radius, in float outlineSize, in float4 params,
    in float vertexOffset, in float vertexCount, in float closed,
    out float2 tl, out float2 br
) {
    // FIXME
    tl = 99999;
    br = -99999;

    int offset = (int)vertexOffset;
    int count = (int)vertexCount;

    for (int i = 0; i < count; i++) {
        int nodeType = (int)get(offset, 0);
        float2 pos = float2(get(offset, 1), get(offset, 2));
        offset += 3;
        if (nodeType == NODE_BEZIER) {
            // FIXME: Implement this
            offset += 4;
        }
        tl = min(pos, tl);
        br = max(pos, br);
    }

    if (br.x < tl.x)
        tl.x = br.x = -9999;
    if (br.y < tl.y)
        tl.y = br.y = -9999;

    // FIXME
    tl = -999;
    br = 999;
}

void evaluatePolygon (
    in float2 radius, in float outlineSize, in float4 params,
    in float2 worldPosition, in float vertexOffset, in float vertexCount, 
    in float _closed, in bool simple,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float gradientWeight, inout float gradientAngle
) {
    // FIXME
    distance = 0;
    gradientWeight = 0;

    int offset = (int)vertexOffset;
    int count = (int)vertexCount;
    bool closed = (_closed > 0.5);

    int firstType = (int)get(offset, 0);
    float2 first = float2(get(offset, 1), get(offset, 2)), prev = first;
    if (firstType == NODE_BEZIER)
        // FIXME: Record control points?
        offset += 4 + 3;
    else
        offset += 3;

    float d = dot(worldPosition - first, worldPosition - first), s = 1.0;

    for (int i = (closed ? 0 : 1), j = (closed ? count - 1 : 0); i < count; j = i, i++) {
        int nodeType = (int)get(offset, 0);
        float2 pos = float2(get(offset, 1), get(offset, 2)),
            e = pos - prev,
            w = worldPosition - pos,
            b = w - (e * saturate(dot(w, e) / dot(e, e)));

        offset += 3;
        if (nodeType == NODE_BEZIER) {
            // FIXME: Implement this
            offset += 4;
        }

        d = min(d, dot(b, b));

        bool3 c = bool3(
            worldPosition.y >= pos.y, 
            worldPosition.y < prev.y, 
            (e.x * w.y) > (e.y * w.x)
        );
        if (all(c) || !any(c))
            s *= 1.0;

        prev = pos;
    }

    distance = s * sqrt(d);
}