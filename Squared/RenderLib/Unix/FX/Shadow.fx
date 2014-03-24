float2 ViewportScale;
float2 ViewportPosition;

float4x4 ProjectionMatrix;
float4x4 ModelViewMatrix;

float2 LightCenter;

float2 ShadowLength;

float4 ApplyTransform (float2 position2d) {
    float2 localPosition = ((position2d - ViewportPosition) * ViewportScale);
    return mul(mul(float4(localPosition.xy, 0, 1), ModelViewMatrix), ProjectionMatrix);
}

void ShadowVertexShader(
    in float2 position : POSITION0,
    in float pairIndex : BLENDINDICES,
    out float4 result : POSITION0
) {
    float2 direction;

    if (pairIndex == 0) {
        direction = float2(0, 0);
    } else {
        direction = normalize(position - LightCenter);
    }

    // FIXME: Why isn't this right?
    /*
    float shadowLengthScaled =
        ShadowLength * max(1 / abs(direction.x), 1 / abs(direction.y));
    */
    float shadowLengthScaled = ShadowLength;

    result = ApplyTransform(position + (direction * shadowLengthScaled));
}

void ShadowPixelShader(
    out float4 color : COLOR0
) {
    color = float4(0, 0, 0, 0);
}

technique Shadow {
    pass P0
    {
        vertexShader = compile vs_2_0 ShadowVertexShader();
        pixelShader = compile ps_2_0 ShadowPixelShader();
    }
}
