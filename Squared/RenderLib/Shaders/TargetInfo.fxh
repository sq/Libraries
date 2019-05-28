#define ACCEPTS_VPOS in float2 __vpos__ : VPOS
#define RAW_VPOS __vpos__.xy
#define GET_VPOS normalize_vpos(__vpos__)

uniform bool   __IsRenderTargetUpsideDown__;
uniform float2 __RenderTargetDimensions__;

float2 normalize_vpos (float2 __vpos__) {
    float2 result = RAW_VPOS;
    if (__IsRenderTargetUpsideDown__)
        result.y = __RenderTargetDimensions__.y - result.y;
    return floor(result);
}

float2 GetRenderTargetSize () {
    return __RenderTargetDimensions__;
}