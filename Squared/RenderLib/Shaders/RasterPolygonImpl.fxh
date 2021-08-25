#define NODE_LINE 0
#define NODE_BEZIER 1

float4 get (int offset) {
    int y = (int)floor(offset / MAX_VERTEX_BUFFER_WIDTH);
    float2 uvi = float2(
        offset - (y * MAX_VERTEX_BUFFER_WIDTH), y
    );
    return tex2Dlod(VertexDataSampler, float4(uvi * PolygonVertexBufferInvSize, 0, 0));
}

void computeTLBR_Bezier (
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
);

void evaluateLineSegment (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float gradientWeight
);

float sdBezier (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c
);

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
    float2 prev = 0;

    for (int i = 0; i < count; i++) {
        float3 xyt = get(offset).xyz;
        int nodeType = (int)xyt.z;
        float2 pos = xyt.xy;
        offset++;
        if (nodeType == NODE_BEZIER) {
            float4 controlPoints = get(offset);
            offset++;
            float2 btl, bbr;
            computeTLBR_Bezier(prev, controlPoints.xy, pos, btl, bbr);
            tl = min(btl, tl);
            br = max(bbr, br);
        } else {
            tl = min(pos, tl);
            br = max(pos, br);
        }
        prev = pos;
    }

    if (br.x < tl.x)
        tl.x = br.x = -9999;
    if (br.y < tl.y)
        tl.y = br.y = -9999;

    tl -= radius.x + outlineSize;
    br += radius.x + outlineSize;
}

float doesCrossLine (float2 a, float2 b, float2 pos) {
    return sign((b.y-a.y) * (pos.x-a.x) - (b.x-a.x) * (pos.y-a.y));
}

// Determine which side we're on (using barycentric parameterization)
float signBezier(float2 A, float2 B, float2 C, float2 p)
{ 
    float2 a = C - A, b = B - A, c = p - A;
    float2 bary = float2(c.x*b.y-b.x*c.y,a.x*c.y-c.x*a.y) / (a.x*b.y-b.x*a.y);
    float2 d = float2(bary.y * 0.5, 0.0) + 1.0 - bary.x - bary.y;
    return lerp(
        sign(d.x * d.x - d.y), 
        lerp(-1.0, 1.0, step(doesCrossLine(A, B, p) * doesCrossLine(B, C, p), 0.0)),
        step((d.x - d.y), 0.0)
    ) * doesCrossLine(A, C, B);
}

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
        if (nodeType == NODE_BEZIER)
            offset++;

        if (closed) {
            // FIXME: Bezier

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
            float temp, temp2;
            if (nodeType == NODE_BEZIER) {
                float4 controlPoints = get(offset - 1);
                float2 a = prev, b = controlPoints.xy, c = pos; 
                temp = sdBezier(worldPosition, a, b, c) - radius.x;
                temp2 = 1 - saturate(-temp / radius.x);

                // FIXME: TLBR
                // computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br);
            } else {
                int temp3 = gradientType;
                evaluateLineSegment(
                    worldPosition, prev, pos, 0,
                    radius, temp, temp3, temp2
                );
            }

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

    tl -= radius.x;
    br += radius.x;

    if (closed)
        distance = (s * sqrt(d)) - radius.x;
}