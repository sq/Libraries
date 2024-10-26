// If the radius is too small when doing a soft stroke it won't look right.
#define MinFalloffRadius 0.5
// Higher values produce smooth strokes for more parameters, but prevent smooth strokes from being especially thin
#define SoftRadiusBias 0.33

float IMPL_NAME (
    in float3 localRadiuses, in float2 worldPosition, IMPL_INPUTS, 
    in float4 seed, in float4 taperRanges, in float4 biases,
    in float distanceTraveled, in float totalLength, in float stepOffset,
    in float2 vpos, in float4 colorA, in float4 colorB,
    inout float4 result, inout float fragmentStack
) {
    float2 a = ab.xy, b = ab.zw, ba = b - a,
        atlasScale = float2(1.0 / NozzleParams.x, 1.0 / NozzleParams.y);

    if (totalLength <= 0)
        totalLength = CALCULATE_LENGTH;

    const float threshold = (1 / 512.0);

    // Locate the closest point on the line segment. This will be the center of our search area
    // We search outwards from the center in both directions to find splats that may overlap us
    float maxSize = Constants2.w,
        // FIXME: A spacing of 1.0 still produces overlap
        stepPx = max(maxSize * Constants2.z, 0.05),
        l = max(CALCULATE_LENGTH, 0.01), centerT, splatCount = ceil(l / stepPx),
        globalSplatCount = ceil(totalLength / stepPx),
        localToGlobalLength = l / totalLength;

    // Expand brush maximum size for shadow but only after computing step
    maxSize += localRadiuses.z;

    // new Vector4(TaperIn.Value, TaperOut.Value, StartOffset.Value, EndOffset.Value);
    taperRanges.zw *= totalLength;

    float taperedL = max(totalLength - taperRanges.z - taperRanges.w, 0.01),
        stepT = 1.0 / splatCount, globalStepT = 1.0 / globalSplatCount,
        angleRadians = atan2(ba.y, ba.x),
        // FIXME: 360deg -> 1.0
        angleFactor = angleRadians,
        // For AngleFromDirection
        angleBias = NozzleParams.w * atan2(ba.y, -ba.x);

    float2 closestPoint = GET_CLOSEST_POINT(worldPosition, centerT);
    if (EARLY_REJECT)
        return ceil(l / stepPx);
    
    // FIXME: offset first splat by (distanceTraveled - floor(distanceTraveled / stepPx) * stepPx)

    float centerD = centerT * l,
        startD = max(0, centerD - SEARCH_DISTANCE),
        endD = min(centerD + SEARCH_DISTANCE, l),
        firstIteration = floor(startD / stepPx),
        lastIteration = ceil(endD / stepPx),
        brushCount = NozzleParams.x * NozzleParams.y,
        colorTOffset = distanceTraveled / totalLength,
        stack = 0;

    for (float i = firstIteration; i <= lastIteration; i += 1.0) {
        float globalI = i + stepOffset;
        float4 noise1, noise2;
        // TODO: Use variants for this
        if (UsesNoise) {
            float4 seedUv = float4(seed.x + (globalI * 2 * seed.z), seed.y + (globalI * seed.w), 0, 0);
            noise1 = tex2Dlod(NoiseSampler, seedUv);
            seedUv.x += seed.z;
            noise2 = tex2Dlod(NoiseSampler, seedUv);
        } else {
            noise1 = 0;
            noise2 = 0;
        }

        float t = i * stepT,
            d = t * l,
            globalD = d + distanceTraveled,
            taper = computeTaper(taperedL, globalD, taperRanges);
        float localRadius = abs(Constants1.x) * saturate(1 + lerp(localRadiuses.x, localRadiuses.y, t));
        float sizePx = evaluateDynamics2((localRadius + biases.x) * maxSize, maxSize, SizeDynamics, float4(taper, i, noise1.x, angleFactor), Constants1.x < 0, maxSize);
        if (sizePx <= 0)
            continue;

        // biases are: (Size, Flow, Hardness, Color)
        float splatAngleFactor = evaluateDynamics(Constants1.y, AngleDynamics, float4(taper, globalI, noise1.y, angleFactor), 1),
            splatAngle = (splatAngleFactor * PI * 2) + angleBias,
            flow = evaluateDynamics(Constants1.z + biases.y, FlowDynamics, float4(taper, globalI, noise1.z, angleFactor), 2),
            brushIndex = evaluateDynamics2(abs(Constants1.w), brushCount, BrushIndexDynamics, float4(taper, globalI, noise1.w, angleFactor), true, brushCount),
            hardness = evaluateDynamics(Constants2.x + biases.z, HardnessDynamics, float4(taper, globalI, noise2.x, angleFactor), 1.0),
            // HACK: Increment here is scaled by t instead of i
            // FIXME: centerT for polygons
            colorT = COLOR_PER_SPLAT ? (globalI * globalStepT) : (globalD / totalLength),
            colorFactor = evaluateDynamics(Constants2.y + biases.w, ColorDynamics, float4(taper, colorT, noise2.y, angleFactor), 1.0);

        bool outOfRange = (globalD < taperRanges.z) || (globalD > (totalLength - taperRanges.w));

        float r = i * sizePx, radius = sizePx * 0.5;
        float2 center = CALCULATE_CENTER(t), texCoordScale = atlasScale / sizePx,
            shapeFactor = float2(Constants3.y, 1);
        float distance = length((center - worldPosition));

        float2x2 rotationMatrix = make2DRotation(-splatAngle);
        float2 posSplatRotated = (worldPosition - center),
            posSplatDerotated = mul(posSplatRotated, rotationMatrix) * shapeFactor,
            posSplatDecentered = (posSplatDerotated + radius);

        float g = length(posSplatDerotated), discardDistance = (hardness < 1) ? radius + SoftRadiusBias : radius;
        float4 color = 1;

        if (Textured) {
            [branch]
            if (
                outOfRange ||
                any(abs(posSplatDerotated) > radius)
            ) {
                continue;
            } else {
                brushIndex = floor(brushIndex) % brushCount;
                float splatIndexY = floor(brushIndex / NozzleParams.x), splatIndexX = brushIndex - (splatIndexY * NozzleParams.x);
                float2 texCoord = (posSplatDecentered * texCoordScale) + (atlasScale * float2(splatIndexX, splatIndexY));
                float lod = max(log2(NozzleParams.z / sizePx) - 0.35, 0);

#ifdef VISUALIZE_TEXCOORDS
                // HACK: Diagnostic view of splat rects
                color = float4(texCoord.x, texCoord.y, lod / 6, 1);
#else
                float4 texel = tex2Dlod(NozzleSampler, float4(texCoord.xy, 0, lod));
                if (BlendInLinearSpace)
                    texel = pSRGBToPLinear(texel);
                color = texel;
#endif
            }

            // Ramp the alpha channel more or less aggressively
            if ((color.a < 1) && (color.a > 0) && (hardness != 0.5)) {
                float softness = (1 - saturate(hardness)) * 2 - 1.0;
                float e = softness > 0
                    ? lerp(1, 3, softness)
                    : lerp(1, 0.25, -softness);
                float softA = pow(color.a, e);
                color *= (softA / color.a);
            }
        } else {
            // HACK: We do the early-out more conservatively because of the need to compute
            //  partial coverage at circle edges, so an exact rejection is not right
            [branch]
            if (outOfRange || (g >= discardDistance))
                continue;
            
            // reduce maximum coverage as sizePx goes below 1.0
            float r = min(radius, 0.5) * 2;

            [branch]
            if (abs(distance - radius) <= 3.333) {
                // HACK: Approximate pixel coverage calculation near edges / for tiny circles
                // FIXME: Does this work for shapefactor != 1.0? Probably not
                r *= approxPixelCoverage(worldPosition, center, radius);
            }

            if (hardness < 1) {
                float falloff = max((1 - hardness) * radius, MinFalloffRadius);
                // HACK
                g -= (hardness * radius);
                g /= falloff;
                g = saturate(g + 0.05);
                g = sin(g * PI * 0.5);
                g = pow(g, Constants3.x);
                r *= 1 - g;
            }
            
            flow *= r;
        }
        
        // FIXME: Doing ramp sampling here instead of at the end hits a bug in fxc and everything breaks.
        if (Ramped) {
            stack += flow;
        } else {
            // TODO: Interpolate based on outer edges instead of center points,
            //  so that we don't get a nasty hard edge at the end
            color *= lerp(
                colorA, colorB, colorFactor
            );
            
            result = over(color, flow, result, 1);
        }
    }

    // The best we can do :( At least it looks pretty good for lines.
    if (Ramped && (stack > fragmentStack)) {
        fragmentStack = max(stack, fragmentStack);
        // FIXME
        float rampTaper = computeTaper(taperedL, centerD + distanceTraveled, taperRanges), rampNoise = 0, rampAngle = 0;
        float rampV = evaluateDynamics(Constants2.y + biases.w, ColorDynamics, float4(rampTaper, (centerD + distanceTraveled) / totalLength, rampNoise, rampAngle), 
        1.0);
        float4 stackColor = tex2Dlod(RampSampler, float4(fragmentStack, rampV, 0, 0));
        if (BlendInLinearSpace)
            stackColor = pSRGBToPLinear_Accurate(stackColor);
        stackColor *= lerp(
            colorA, colorB, rampV
        );
        result = stackColor;
    }
    
    return ceil(l / stepPx);
}