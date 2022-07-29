void computeTLBR_Bezier (
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    tl = min(a, c);
    br = max(a, c);

    if (any(b < tl) || any(b > br))
    {
        float2 t = clamp((a - b) / (a - 2.0*b + c), 0.0, 1.0);
        float2 s = 1.0 - t;
        float2 q = s*s*a + 2.0*s*t*b + t*t*c;
        tl = min(tl, q);
        br = max(br, q);
    }
}

float2 closestPointOnLine2(float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    float d = dot(ab, ab);
    if (abs(d) < 0.001)
        d = 0.001;
    t = dot(pt - a, ab) / d;
    return a + t * ab;
}

float2 closestPointOnLineSegment2(float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    float d = dot(ab, ab);
    if (abs(d) < 0.001)
        d = 0.001;
    t = saturate(dot(pt - a, ab) / d);
    return a + t * ab;
}

// Assumes y is 0
bool quadraticBezierTFromY (
    in float y0, in float y1, in float y2, 
    out float t1, out float t2
) {
    float divisor = (y0 - (2 * y1) + y2);
    if (abs(divisor) <= 0.001) {
        t1 = t2 = 0;
        return false;
    }
    float rhs = sqrt(-(y0 * y2) + (y1 * y1));
    t1 = ((y0 - y1) + rhs) / divisor;
    t2 = ((y0 - y1) - rhs) / divisor;
    return true;
}

float2 evaluateBezierAtT (
    in float2 a, in float2 b, in float2 c, in float t
) {
    float2 ab = lerp(a, b, t),
        bc = lerp(b, c, t);
    return lerp(ab, bc, t);
}

void pickClosestT (
    inout float cd, inout float ct, in float d, in float t
) {
    if (d < cd) {
        ct = t;
        cd = d;
    }
}

// Assumes worldPosition is 0 relative to the control points
float distanceSquaredToBezierAtT (
    in float2 a, in float2 b, in float2 c, in float t
) {
    float2 pt = evaluateBezierAtT(a, b, c, t);
    return abs(dot(pt, pt));
}

void pickClosestTOnBezierForAxis (
    in float2 a, in float2 b, in float2 c, in float2 mask,
    inout float cd, inout float ct
) {
    // For a given x or y value on the bezier there are two candidate T values that are closest,
    //  so we compute both and then pick the closest of the two. If the divisor is too close to zero
    //  we will have failed to compute any valid T values, so we bail out
    float t1, t2;
    float2 _a = a * mask, _b = b * mask, _c = c * mask;
    if (!quadraticBezierTFromY(_a.x+_a.y, _b.x+_b.y, _c.x+_c.y, t1, t2))
        return;
    float d1 = distanceSquaredToBezierAtT(a, b, c, t1);
    if ((t1 > 0) && (t1 < 1))
        pickClosestT(cd, ct, d1, t1);
    float d2 = distanceSquaredToBezierAtT(a, b, c, t2);
    if ((t2 > 0) && (t2 < 1))
        pickClosestT(cd, ct, d2, t2);
}

float lengthOfBezier (in float2 a, in float2 b, in float2 c) {
    float2 v = 2 * (b - a),
        w = c - (2*b) + a;

    float uu = 4 * dot(w, w);
    if (uu < 0.0001) {
        float2 ca = (c - a);
        return sqrt(dot(ca, ca));
    }

    float vv = 4 * dot(v, w),
        ww = dot(v, v),
        t1 = 2*sqrt(uu*(uu + vv + ww)),
        t2 = 2*uu+vv,
        t3 = vv*vv - 4*uu*ww,
        t4 = 2*sqrt(uu*ww);

    return ((t1*t2 - t3*log(t2+t1) -(vv*t4 - t3*log(vv+t4))) / (8*pow(uu, 1.5)));
}