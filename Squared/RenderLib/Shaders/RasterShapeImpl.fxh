void SHAPE_TYPE_NAME (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha, shadowAlpha;
    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, false,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, GET_VPOS);

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
        worldPositionTypeAndWorldSpace, false,
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
        params, params2, tl, br, GET_VPOS
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
        worldPositionTypeAndWorldSpace, true,
        ab, cd, params, params2,
        centerColor, edgeColor, GET_VPOS,
        tl, br,
        fill, fillAlpha, outlineAlpha, shadowAlpha
    );

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, GET_VPOS);

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
        worldPositionTypeAndWorldSpace, true,
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
        params, params2, tl, br, GET_VPOS
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
