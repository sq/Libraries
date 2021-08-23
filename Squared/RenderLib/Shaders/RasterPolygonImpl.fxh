#define NODE_LINE 0
#define NODE_BEZIER 1

float4 get (int offset) {
    float4 uv = float4(offset * PolygonVertexBufferInvWidth, 0, 0, 0);
    return tex2Dlod(VertexDataSampler, uv);
}

void computeTLBR_Polygon (
    in float2 radius, in float outlineSize, in float4 params,
    in float vertexOffset, in float vertexCount, in float _closed,
    out float2 tl, out float2 br
) {
    // FIXME
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

    tl -= outlineSize;
    br += outlineSize;
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

    bool closed = (_closed > 0.5);
    int offset = (int)vertexOffset;
    int count = (int)vertexCount;

    float4 first = get(offset), prev = first;
    if (((int)first.z) == NODE_BEZIER)
        // FIXME: Record control points?
        offset += 2;
    else
        offset += 1;

    float d = dot(worldPosition - first.xy, worldPosition - first.xy), s = 1.0;

/*
float sdPolygon( in vec2[N] v, in vec2 p )
{
    float d = dot(p-v[0],p-v[0]);
    float s = 1.0;
    for( int i=0, j=N-1; i<N; j=i, i++ )
    {
        vec2 e = v[j] - v[i];
        vec2 w =    p - v[i];
        vec2 b = w - e*clamp( dot(w,e)/dot(e,e), 0.0, 1.0 );
        d = min( d, dot(b,b) );
        bvec3 c = bvec3(p.y>=v[i].y,p.y<v[j].y,e.x*w.y>e.y*w.x);
        if( all(c) || all(not(c)) ) s*=-1.0;  
    }
    return s*sqrt(d);
}
*/

    for (int i = 0, limit = closed ? count : count - 1; i < limit; i++) {
        float4 xyt = get(offset);
        int nodeType = (int)xyt.z;
        float2 pos = (i >= (count - 1)) ? first : xyt.xy,
            e = prev - pos,
            w = worldPosition - pos,
            b = w - (e * saturate(dot(w, e) / dot(e, e)));

        offset++;
        if (nodeType == NODE_BEZIER) {
            float4 controlPoints = get(offset);
            // FIXME: Implement this
            offset++;
        }

        d = min(d, dot(b, b));

        bool3 c = bool3(
            worldPosition.y >= pos.y, 
            worldPosition.y < prev.y, 
            (e.x * w.y) > (e.y * w.x)
        );
        if (all(c) || !any(c))
            s *= -1.0;

        prev = xyt;
    }

    distance = s * sqrt(d);
}