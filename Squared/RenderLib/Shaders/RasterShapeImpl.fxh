void SHAPE_TYPE_NAME (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_TEX (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, false,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

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
    float4 fill;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, true, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_TEX_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, false,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

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

technique SHAPE_TYPE_TECHNIQUE_NAME_TEX_SHADOWED
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 SHAPE_TYPE_NAME_TEX_SHADOWED ();
    }
}

#ifdef SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE

void SHAPE_TYPE_NAME_SIMPLE (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false, true,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, false, GET_VPOS);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}

void SHAPE_TYPE_NAME_SIMPLE_SHADOWED (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, true, true,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, true, GET_VPOS);

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
