struct ViewTransform {
    float4x4 Projection;
    float4x4 ModelView;
    float2 Scale;
    float2 Position;
};

// FIXME: FNA HACKS
uniform float4x4 ViewportProjection;
uniform float4x4 ViewportModelView;

uniform ViewTransform Viewport;

float4x4 GetViewportProjectionMatrix () {
    // FIXME: FNA HACKS
    return ViewportProjection;
}

float4x4 GetViewportModelViewMatrix () {
    // FIXME: FNA HACKS
    return ViewportModelView;
}

float2 GetViewportScale () {
    return Viewport.Scale;
}

float2 GetViewportPosition () {
    return Viewport.Position;
}