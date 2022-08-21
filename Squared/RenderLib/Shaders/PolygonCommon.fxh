Texture2D VertexDataTexture : register(t2);

#define MAX_VERTEX_BUFFER_WIDTH 1024

uniform const float2 PolygonVertexBufferInvSize;
sampler VertexDataSampler : register(s2) {
    Texture = (VertexDataTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

#define NODE_LINE 0
#define NODE_BEZIER 1
#define NODE_START 2

float4 getPolyVertex (int offset) {
    int y = (int)floor(offset / MAX_VERTEX_BUFFER_WIDTH);
    float2 uvi = float2(
        offset - (y * MAX_VERTEX_BUFFER_WIDTH), y
    );
    return tex2Dlod(VertexDataSampler, float4(uvi * PolygonVertexBufferInvSize, 0, 0));
}
