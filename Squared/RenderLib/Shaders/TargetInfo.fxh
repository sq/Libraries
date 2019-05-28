#define ACCEPTS_VPOS in float2 __vpos__ : VPOS
#define RAW_VPOS __vpos__
#define GET_VPOS normalize_vpos(__vpos__)

uniform bool   __IsRenderTargetUpsideDown__;
uniform float2 __RenderTargetDimensions__;

float2 normalize_vpos (float2 __vpos__) {
    float2 result = __vpos__;
    if (__IsRenderTargetUpsideDown__) {
        result.x = floor(result.x);
        result.y = floor(__RenderTargetDimensions__.y - result.y);
    }
    return result;
}