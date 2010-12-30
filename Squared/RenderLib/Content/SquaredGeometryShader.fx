shared float2 ViewportPosition;
shared float2 ViewportScale;

shared float4x4 ProjectionMatrix;

shared texture BasicTexture;

uniform const sampler TextureSampler = sampler_state {
    Texture = (BasicTexture);
    
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};

void ScreenSpaceVertexShader(
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    out float4 result : POSITION0
) {
    result = mul(float4(position.xy, 0, 1), ProjectionMatrix);
}

void WorldSpaceVertexShader(
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    out float4 result : POSITION0
) {
    position -= ViewportPosition;
    position *= ViewportScale;
    result = mul(float4(position.xy, 0, 1), ProjectionMatrix);
}

void ScreenSpaceTexturedVertexShader(
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    inout float2 texCoord : TEXCOORD0,
    out float4 result : POSITION0
) {
    result = mul(float4(position.xy, 0, 1), ProjectionMatrix);
}

void WorldSpaceTexturedVertexShader(
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    inout float2 texCoord : TEXCOORD0,
    out float4 result : POSITION0
) {
    position -= ViewportPosition;
    position *= ViewportScale;
    result = mul(float4(position.xy, 0, 1), ProjectionMatrix);
}

void VertexColorPixelShader(
    inout float4 color : COLOR0
) {
}

void TexturedPixelShader(
    inout float4 color : COLOR0,
    in float2 texCoord : TEXCOORD0
) {
    color *= tex2D(TextureSampler, texCoord);
}

technique ScreenSpaceUntextured
{
    pass P0
    {
        vertexShader = compile vs_1_1 ScreenSpaceVertexShader();
        pixelShader = compile ps_2_0 VertexColorPixelShader();
    }
}

technique WorldSpaceUntextured
{
    pass P0
    {
        vertexShader = compile vs_1_1 WorldSpaceVertexShader();
        pixelShader = compile ps_2_0 VertexColorPixelShader();
    }
}

technique ScreenSpaceTextured
{
    pass P0
    {
        vertexShader = compile vs_1_1 ScreenSpaceTexturedVertexShader();
        pixelShader = compile ps_2_0 TexturedPixelShader();
    }
}

technique WorldSpaceTextured
{
    pass P0
    {
        vertexShader = compile vs_1_1 WorldSpaceTexturedVertexShader();
        pixelShader = compile ps_2_0 TexturedPixelShader();
    }
}