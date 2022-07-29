// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#define Textured true
#include "RasterStrokeLineImpl.fxh"

technique RasterStrokeLineTextured
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterStrokeLineSegmentVertexShader();
        pixelShader = compile ps_3_0 RasterStrokeLineSegmentFragmentShader();
    }
}
