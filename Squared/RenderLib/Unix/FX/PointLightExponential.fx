float2 ViewportScale;
float2 ViewportPosition;

float4x4 ProjectionMatrix;
float4x4 ModelViewMatrix;

float4 LightNeutralColor;

float4 ApplyTransform (float2 position2d) {
    float2 localPosition = ((position2d - ViewportPosition) * ViewportScale);
    return mul(mul(float4(localPosition.xy, 0, 1), ModelViewMatrix), ProjectionMatrix);
}

void PointLightVertexShader(
    in float2 position : POSITION0,
    inout float4 color : COLOR0,
    inout float2 lightCenter : TEXCOORD0,
    inout float2 ramp : TEXCOORD1,
    out float2 worldPosition : TEXCOORD2,
    out float4 result : POSITION0
) {
    worldPosition = position;
    result = ApplyTransform(position);
}

void PointLightPixelShaderExponential(
    in float2 worldPosition: TEXCOORD2,
    in float2 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    out float4 result : COLOR0
) {
    float distance = length(worldPosition - lightCenter) - ramp.x;
    float distanceOpacity = 1 - clamp(distance / (ramp.y - ramp.x), 0, 1);
    distanceOpacity *= distanceOpacity;

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

technique PointLightExponential {
    pass P0
    {
        vertexShader = compile vs_2_0 PointLightVertexShader();
        pixelShader = compile ps_2_0 PointLightPixelShaderExponential();
    }
}
