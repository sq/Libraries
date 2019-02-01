struct ViewTransform {
    float4x4 Projection;
    float4x4 ModelView;
    float2 Scale;
    float2 Position;
};

uniform float4x4 ViewportProjection;
uniform float4x4 ViewportModelView;

uniform ViewTransform Viewport;

float4x4 GetViewportProjectionMatrix () {
    return ViewportProjection;
}

float4x4 GetViewportModelViewMatrix () {
    return ViewportModelView;
}

float2 GetViewportScale () {
    return float2(1, 1);
    return Viewport.Scale;
}

float2 GetViewportPosition () {
    return float2(0, 0);
    return Viewport.Position;
}