float4x4 ProjectionMatrix;
float4x4 ModelViewMatrix;

float4 TransformPosition (float4 position, float offset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, ModelViewMatrix) - float4(offset, offset, 0, 0);
    // Finally project after offsetting
    return mul(modelViewPos, ProjectionMatrix);
}

void ScreenSpaceVertexShader(
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    out float4 result : POSITION0
) {
    result = TransformPosition(float4(position.xy, 0, 1), 0);
}

void VertexColorPixelShader(
    inout float4 color : COLOR0
) {
}

technique ScreenSpaceUntextured
{
    pass P0
    {
        vertexShader = compile vs_2_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_2_0 VertexColorPixelShader();
    }
}
