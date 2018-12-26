struct ViewTransform {
    float2 Scale;
    float2 Position;

    float4x4 Projection;
    float4x4 ModelView;
};

uniform ViewTransform Viewport;