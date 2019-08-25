struct ViewTransform {
    float4x4 Projection;
    float4x4 ModelView;
    float4 ScaleAndPosition;
    float4 InputAndOutputZRanges;
};

uniform ViewTransform Viewport;

float4x4 GetViewportProjectionMatrix () {
    return Viewport.Projection;
}

float4x4 GetViewportModelViewMatrix () {
    return Viewport.ModelView;
}

float2 GetViewportScale () {
    return Viewport.ScaleAndPosition.xy;
}

float2 GetViewportPosition () {
    return Viewport.ScaleAndPosition.zw;
}

float ScaleZIntoViewTransformSpace (
    in float worldSpaceZ
) {
    float2 inputRange = Viewport.InputAndOutputZRanges.xy + float2(0, 1);
    float zDivisor = max(
        abs(inputRange.y - inputRange.x), 0.5
    ) * ((inputRange.y >= inputRange.x) ? 1 : -1);
    float viewSpaceZ = (worldSpaceZ - inputRange.x) / zDivisor;
    float2 outputRange = Viewport.InputAndOutputZRanges.zw + float2(0, 1);
    viewSpaceZ = outputRange.x + (viewSpaceZ * (outputRange.y - outputRange.x));

    return clamp(viewSpaceZ, outputRange.x, outputRange.y);
}
