void SHAPE_TYPE_NAME (
    RASTERSHAPE_FS_ARGS
) {
    RASTERSHAPE_PREPROCESS_COLORS

    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha;
    rasterShapeCommon(
        worldPosition,
        ab, cd, params, abs(_type.x),
        centerColor, edgeColor,
        tl, br,
        fill, fillAlpha, outlineAlpha
    );

    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, params.z);

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }

    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void SHAPE_TYPE_NAME_TEX (
    RASTERSHAPE_FS_ARGS
) {
    RASTERSHAPE_PREPROCESS_COLORS
        
    float2 tl, br;
    float4 fill;
    float  fillAlpha, outlineAlpha;
    rasterShapeCommon(
        worldPosition,
        ab, cd, params, abs(_type.x),
        centerColor, edgeColor,
        tl, br,
        fill, fillAlpha, outlineAlpha
    );

    result = texturedShapeCommon(
        worldPosition, texRgn,
        ab, cd, 
        fill, outlineColor,
        fillAlpha, outlineAlpha,
        params, tl, br
    );

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }

    result.rgb = ApplyDither(result.rgb, GET_VPOS);
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
