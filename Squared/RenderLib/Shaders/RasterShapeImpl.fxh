void SHAPE_TYPE_NAME (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, false, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_TEX (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight);

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
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, false, true, GET_VPOS);

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
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight);

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
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, true,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = centerColor;

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, true, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_SIMPLE_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, true,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = centerColor;

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, true, true, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_SIMPLE ();
    }
}

technique SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE_SHADOWED
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_SIMPLE_SHADOWED ();
    }
}

#endif



#ifdef SHAPE_TYPE_TECHNIQUE_NAME_RAMP

Texture2D RampTexture        : register(t3);
sampler   RampTextureSampler : register(s3) {
    Texture   = (RampTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// FIXME: In FNA ramp textures + sphere lights creates very jagged shadows but in XNA it does not.
float4 SampleFromRamp (float value) {
    return tex2Dlod(RampTextureSampler, float4(value, 0, 0, 0));
}

void SHAPE_TYPE_NAME_RAMP (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = SampleFromRamp(gradientWeight);
    fillAlpha *= lerp(centerColor.a, edgeColor.a, gradientWeight);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, false, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_RAMP_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float  fillAlpha, outlineAlpha, shadowAlpha, gradientWeight;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = SampleFromRamp(gradientWeight);
    fillAlpha *= lerp(centerColor.a, edgeColor.a, gradientWeight);

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, false, true, GET_VPOS);

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