struct ViewTransform {
    float4x4 Projection;
    float4x4 ModelView;
    float2 Scale;
    float2 Position;
};

uniform ViewTransform Viewport;

float4x4 GetViewportProjectionMatrix () {
    return Viewport.Projection;
}

float4x4 GetViewportModelViewMatrix () {
    return Viewport.ModelView;
}

float2 GetViewportScale () {
    return Viewport.Scale;
}

float2 GetViewportPosition () {
    return Viewport.Position;
}

#define vpos (__vpos__)