#define NODE_LINE 0
#define NODE_BEZIER 1

float4 get (int offset) {
    int y = (int)floor(offset / MAX_VERTEX_BUFFER_WIDTH);
    float2 uvi = float2(
        offset - (y * MAX_VERTEX_BUFFER_WIDTH), y
    );
    return tex2Dlod(VertexDataSampler, float4(uvi * PolygonVertexBufferInvSize, 0, 0));
}

void computeTLBR_Polygon (
    in float2 radius, in float outlineSize, in float4 params,
    in float vertexOffset, in float vertexCount, in float _closed,
    out float2 tl, out float2 br
) {
    tl = 99999;
    br = -99999;

    bool closed = (_closed > 0.5);
    int offset = (int)vertexOffset;
    int count = (int)vertexCount;

    for (int i = 0; i < count; i++) {
        float3 xyt = get(offset).xyz;
        int nodeType = (int)xyt.z;
        float2 pos = xyt.xy;
        offset++;
        if (nodeType == NODE_BEZIER) {
            float4 controlPoints = get(offset);
            // FIXME: Implement this
            offset++;
        }
        tl = min(pos, tl);
        br = max(pos, br);
    }

    if (br.x < tl.x)
        tl.x = br.x = -9999;
    if (br.y < tl.y)
        tl.y = br.y = -9999;

    tl -= radius.x + outlineSize;
    br += radius.x + outlineSize;
}

void evaluateLineSegment (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float gradientWeight
);

void evaluatePolygon (
    in float2 radius, in float outlineSize, in float4 params,
    in float2 worldPosition, in float vertexOffset, in float vertexCount, 
    in float _closed, in bool simple,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float gradientWeight, inout float gradientAngle
) {
    // FIXME
    distance = 9999;
    gradientWeight = 0;
    tl = 99999;
    br = -99999;

    bool closed = (_closed > 0.5), annular = params.y > 0;
    int offset = (int)vertexOffset;
    int count = (int)vertexCount;

    // FIXME
    if (closed && (gradientType == GRADIENT_TYPE_Natural))
        gradientType = GRADIENT_TYPE_Radial;

    float4 first = get(offset), prev = first;
    if (((int)first.z) == NODE_BEZIER)
        // FIXME: Record control points?
        offset += 2;
    else
        offset += 1;

    float d = dot(worldPosition - first.xy, worldPosition - first.xy), s = 1.0,
        gdist = 99999;

    for (int i = 0, limit = closed ? count : count - 1; i < limit; i++) {
        float4 xyt = get(offset);
        int nodeType = (int)xyt.z;
        float2 pos = (i >= (count - 1)) ? first : xyt.xy;

        offset++;
        if (nodeType == NODE_BEZIER) {
            float4 controlPoints = get(offset);
            // FIXME: Implement this
            offset++;
        }

        if (closed) {
            float2 e = prev - pos,
                w = worldPosition - pos,
                b = w - (e * saturate(dot(w, e) / dot(e, e)));

            d = min(d, dot(b, b));

            bool3 c = bool3(
                worldPosition.y >= pos.y, 
                worldPosition.y < prev.y, 
                (e.x * w.y) > (e.y * w.x)
            );
            if (all(c) || !any(c))
                s *= -1.0;
        } else {
            // FIXME: Bezier
            int temp3 = gradientType;
            float temp, temp2;
            evaluateLineSegment(
                worldPosition, prev, pos, 0,
                radius, temp, temp3, temp2
            );
            distance = min(distance, temp);

            if (gradientType == GRADIENT_TYPE_Along) {
                if (((gdist > 0) && (temp < gdist)) || (temp < 0)) {
                    float scale = 1.0 / (vertexCount - 1);
                    gradientWeight = (i * scale) + (temp2 * scale);
                    gdist = temp;
                }
            } else if (temp < gdist) {
                gradientWeight = temp2;
                gdist = temp;
            }
        }

        prev = xyt;
        tl = min(tl, pos);
        br = max(br, pos);
    }

    if (closed)
        distance = s * sqrt(d);
}