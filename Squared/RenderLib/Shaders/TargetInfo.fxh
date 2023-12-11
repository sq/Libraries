#if !DEFINED_RTD
#define DEFINED_RTD 1

#define ACCEPTS_VPOS in float2 __vpos__ : VPOS
#define RAW_VPOS __vpos__.xy

uniform const float2 __RenderTargetDimensions__;

#if FNA
#define GET_VPOS normalize_vpos(__vpos__)

float2 normalize_vpos (float2 __vpos__) {
    float2 result = RAW_VPOS;
    if (__RenderTargetDimensions__.y < 0)
        result.y = -__RenderTargetDimensions__.y - result.y;
    return floor(result);
}
#else
#define GET_VPOS __vpos__
#endif

float2 GetRenderTargetSize () {
    return __RenderTargetDimensions__;
}

#define GET_VPOS_FRAC (GET_VPOS / GetRenderTargetSize())
#define GET_VPOS_SCALED (GET_VPOS_FRAC * GetViewportProjectionInputDomain())
#endif