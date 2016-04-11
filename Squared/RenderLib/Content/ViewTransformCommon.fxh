struct ViewTransform {
    float2 Scale;
    float2 Position;

    float4x4 ProjectionMatrix;
    float4x4 ModelViewMatrix;
};

uniform ViewTransform Viewport;