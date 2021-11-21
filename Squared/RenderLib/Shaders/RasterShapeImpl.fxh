void SHAPE_TYPE_NAME (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight.x);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, false, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_TEX (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight.x);

    result = texturedShapeCommon(
        worldPositionTypeAndWorldSpace.xy, texRgn,
        ab, cd, 
        fill, outlineColor,
        fillAlpha, outlineAlpha, shadowAlpha,
        params, params2, tl, br, false, GET_VPOS
    );

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight.x);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, false, true, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME ();
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_TEX
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_TEX ();
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_SHADOWED
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_SHADOWED ();
    }
}



#ifdef SHAPE_TYPE_TECHNIQUE_NAME_TEX_SHADOWED

void SHAPE_TYPE_NAME_TEX_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight.x);

    result = texturedShapeCommon(
        worldPositionTypeAndWorldSpace.xy, texRgn,
        ab, cd, 
        fill, outlineColor,
        fillAlpha, outlineAlpha, shadowAlpha,
        params, params2, tl, br, true, GET_VPOS
    );

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_TEX_SHADOWED
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_TEX_SHADOWED ();
    }
}

#endif



#ifdef SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE

void SHAPE_TYPE_NAME_SIMPLE (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, true,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = centerColor;

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, true, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_SIMPLE_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, true,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = centerColor;

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, true, true, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader_Simple ();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_SIMPLE ();
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE_SHADOWED
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader_Simple ();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_SIMPLE_SHADOWED ();
    }
}

#endif



#ifdef SHAPE_TYPE_TECHNIQUE_NAME_RAMP

uniform float2 RampUVOffset;

Texture2D RampTexture        : register(t3);
sampler   RampTextureSampler : register(s3) {
    Texture   = (RampTexture);
    AddressU  = CLAMP;
    AddressV  = WRAP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// FIXME: In FNA ramp textures + sphere lights creates very jagged shadows but in XNA it does not.
float4 SampleFromRamp (float x) {
    return tex2Dlod(RampTextureSampler, float4(x, 0, 0, 0));
}

float4 SampleFromRamp2 (float2 xy) {
    return tex2Dlod(RampTextureSampler, float4(xy, 0, 0));
}

void SHAPE_TYPE_NAME_RAMP (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = SampleFromRamp2(gradientWeight + RampUVOffset);
    fillAlpha *= lerp(centerColor.a, edgeColor.a, gradientWeight.x);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, true, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_RAMP_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = SampleFromRamp2(gradientWeight + RampUVOffset);
    fillAlpha *= lerp(centerColor.a, edgeColor.a, gradientWeight.x);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, true, true, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_RAMP
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_RAMP ();
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_RAMP_SHADOWED
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_RAMP_SHADOWED ();
    }
}

#endif