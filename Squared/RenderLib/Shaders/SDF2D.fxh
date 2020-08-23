// A bunch of the distance formulas in here are thanks to inigo quilez
// http://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
// http://iquilezles.org/www/articles/distfunctions/distfunctions.htm

float sdBox(in float2 p, in float2 b) {
    float2 d = abs(p) - b;
    return length(max(d, 0.0001)) + min(max(d.x, d.y), 0.0001);
}

float sdEllipse(in float2 p, in float2 ab) {
    p = abs(p);
    if (p.x > p.y) {
        p = p.yx; ab = ab.yx;
    }
    float l = ab.y*ab.y - ab.x*ab.x;
    float m = ab.x*p.x / l;
    float m2 = m*m;
    float n = ab.y*p.y / l;
    float n2 = n*n;
    float c = (m2 + n2 - 1.0) / 3.0;
    float c3 = c*c*c;
    float q = c3 + m2*n2*2.0;
    float d = c3 + m2*n2;
    float g = m + m*n2;
    float co;
    if (d<0.0) {
        float h = acos(q / c3) / 3.0;
        float s = cos(h);
        float t = sin(h)*sqrt(3.0);
        float rx = sqrt(-c*(s + t + 2.0) + m2);
        float ry = sqrt(-c*(s - t + 2.0) + m2);
        co = (ry + sign(l)*rx + abs(g) / (rx*ry) - m) / 2.0;
    }
    else {
        float h = 2.0*m*n*sqrt(d);
        float s = sign(q + h)*pow(abs(q + h), 1.0 / 3.0);
        float u = sign(q - h)*pow(abs(q - h), 1.0 / 3.0);
        float rx = -s - u - c*4.0 + 2.0*m2;
        float ry = (s - u)*sqrt(3.0);
        float rm = sqrt(rx*rx + ry*ry);
        co = (ry / sqrt(rm - rx) + 2.0*g / rm - m) / 2.0;
    }
    float2 r = ab * float2(co, sqrt(1.0 - co*co));
    return length(r - p) * sign(p.y - r.y);
}

#ifdef INCLUDE_BEZIER
float sdBezier(in float2 pos, in float2 A, in float2 B, in float2 C) {
    float2 a = B - A;
    float2 b = A - 2.0*B + C;
    float2 c = a * 2.0;
    float2 d = A - pos;
    float dotB = dot(b, b);
    if (abs(dotB) < 0.0001)
        dotB = 0.0001 * sign(dotB);
    float kk = 1.0 / dotB;
    float kx = kk * dot(a, b);
    float ky = kk * (2.0*dot(a, a) + dot(d, b)) / 3.0;
    float kz = kk * dot(d, a);
    float res = 0.0;
    float p = ky - kx*kx;
    float p3 = p*p*p;
    float q = kx*(2.0*kx*kx - 3.0*ky) + kz;
    float h = q*q + 4.0*p3;
    if (h >= 0.0)
    {
        h = sqrt(h);
        float2 x = (float2(h, -h) - q) / 2.0;
        float2 uv = sign(x)*pow(abs(x), 1.0 / 3.0);
        float t = uv.x + uv.y - kx;
        t = clamp(t, 0.0, 1.0);
        float2 qos = d + (c + b*t)*t;
        res = dot(qos, qos);
    }
    else
    {
        float z = sqrt(-p);
        float pz2 = (p*z*2.0);
        if (abs(pz2) < 0.0001)
            pz2 = 0.0001 * sign(pz2);
        float v = acos(q / pz2) / 3.0;
        float m = cos(v);
        float n = sin(v)*1.732050808;
        float3 t = float3(m + m, -n - m, n - m) * z - kx;
        t = clamp(t, 0.0, 1.0);
        float2 qos = d + (c + b*t.x)*t.x;
        res = dot(qos, qos);
        qos = d + (c + b*t.y)*t.y;
        res = min(res, dot(qos, qos));
        qos = d + (c + b*t.z)*t.z;
        res = min(res, dot(qos, qos));
    }
    return sqrt(res);
}
#endif

float sdTriangle(in float2 p, in float2 p0, in float2 p1, in float2 p2) {
    float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
    float2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
    float2 pq0 = v0 - e0*clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
    float2 pq1 = v1 - e1*clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
    float2 pq2 = v2 - e2*clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
    float s = sign(e0.x*e2.y - e0.y*e2.x);
    float2 d = min(min(float2(dot(pq0, pq0), s*(v0.x*e0.y - v0.y*e0.x)),
        float2(dot(pq1, pq1), s*(v1.x*e1.y - v1.y*e1.x))),
        float2(dot(pq2, pq2), s*(v2.x*e2.y - v2.y*e2.x)));
    return -sqrt(d.x)*sign(d.y);
}

float sdArc (in float2 p, in float2 sca, in float2 scb, in float ra, float rb) {
    p = mul(p, float2x2(sca.x, sca.y, -sca.y, sca.x));
    p.x = abs(p.x);
    float k = (scb.y*p.x>scb.x*p.y) ? dot(p.xy, scb) : length(p.xy);
    return sqrt(dot(p, p) + ra*ra - 2.0*ra*k) - rb;
}