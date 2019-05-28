struct ViewTransform {
    float4x4 Projection;
    float4x4 ModelView;
    float4 ScaleAndPosition;
};

uniform ViewTransform Viewport;

float4x4 GetViewportProjectionMatrix () {
    return Viewport.Projection;
}

float4x4 GetViewportModelViewMatrix () {
    return Viewport.ModelView;
}

float2 GetViewportScale () {
    return Viewport.ScaleAndPosition.xy;
}

float2 GetViewportPosition () {
    return Viewport.ScaleAndPosition.zw;
}