struct ViewTransform {
    float4x4 Projection;
    float4x4 ModelView;
    float4 ScaleAndPosition;
    float4 InputAndOutputZRanges;
};

uniform const ViewTransform Viewport;

float4x4 GetViewportProjectionMatrix () {
    return Viewport.Projection;
}

float4x4 GetViewportModelViewMatrix () {
    return Viewport.ModelView;
}

float2 GetViewportProjectionInputDomain () {
    // Take a regular ortho projection matrix and figure out its w/h
    return float2(
        1 / Viewport.Projection._11 * 2,
        (1 / Viewport.Projection._22) * -2
    );
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
    );
    float scaledWorldSpaceZ = (worldSpaceZ - inputRange.x) / zDivisor;
    float2 outputRange = Viewport.InputAndOutputZRanges.zw + float2(0, 1);

    float viewSpaceZ = outputRange.x + (scaledWorldSpaceZ * (outputRange.y - outputRange.x));

    return clamp(viewSpaceZ, outputRange.x, outputRange.y);
}
