float IMPL_NAME (
    in float3 localRadiuses, in float2 worldPosition, IMPL_INPUTS, 
    in float4 seed, in float4 taperRanges, in float4 biases,
    in float distanceTraveled, in float totalLength, in float stepOffset,
    in float2 vpos, in float4 colorA, in float4 colorB,
    inout float4 result
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
        globalSplatCount = ceil(totalLength / stepPx);

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

    float centerD = centerT * l,
        startD = max(0, centerD - SEARCH_DISTANCE),
        endD = min(centerD + SEARCH_DISTANCE, l),
        firstIteration = floor(startD / stepPx), 
        lastIteration = ceil(endD / stepPx),
        brushCount = NozzleParams.x * NozzleParams.y;

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
            colorT = COLOR_PER_SPLAT ? (globalI * globalStepT) : centerT,
            colorFactor = evaluateDynamics(Constants2.y + biases.w, ColorDynamics, float4(taper, colorT, noise2.y, angleFactor), 1.0);

        bool outOfRange = (globalD < taperRanges.z) || (globalD > (totalLength - taperRanges.w));

        float r = i * sizePx, radius = sizePx * 0.5;
        float2 center = CALCULATE_CENTER(t), texCoordScale = atlasScale / sizePx;
        float distance = length(center - worldPosition);

        float2x2 rotationMatrix = make2DRotation(-splatAngle);
        float2 posSplatRotated = (worldPosition - center),
            posSplatDerotated = mul(posSplatRotated, rotationMatrix),
            posSplatDecentered = (posSplatDerotated + radius);

        float g = length(posSplatDerotated), discardDistance = (hardness < 1) ? radius + 1 : radius;
        float4 color;

        if (Textured) {
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
            if (outOfRange || (g >= discardDistance))
                continue;
            
            float r;

            PREFER_BRANCH
            // FIXME: radius - 1 might be too conservative
            if ((distance >= (radius - 1)) || (radius <= 2)) {
                // HACK: Approximate pixel coverage calculation near edges / for tiny circles
                r = approxPixelCoverage(worldPosition, center, radius);
            } else {
                r = 1;
            }

            if (hardness < 1) {
                float falloff = max((1 - hardness) * radius, 1.05);
                // HACK
                g -= (hardness * radius);
                g /= falloff;
                g = saturate(g + 0.05);
                g = sin(g * PI * 0.5);
                r *= 1 - g;
            }
            
            color = r;
        }

        // FIXME: Doing ramp sampling here instead of at the end hits a bug in fxc and everything breaks.
        if (!Ramped)
            // TODO: Interpolate based on outer edges instead of center points,
            //  so that we don't get a nasty hard edge at the end
            color *= lerp(
                colorA, colorB, colorFactor
            );

        // HACK: Avoid accumulating error.
        if (color.a > 0)
            result = over(color, flow, result, 1);
    }

    // The best we can do :( At least it looks pretty good for lines.
    if (Ramped) {
        // FIXME
        float rampTaper = computeTaper(taperedL, centerD, taperRanges), rampNoise = 0, rampAngle = 0;
        float rampV = evaluateDynamics(Constants2.y + biases.w, ColorDynamics, float4(rampTaper, centerT, rampNoise, rampAngle), 1.0);
        result = tex2Dlod(RampSampler, float4(result.r, rampV, 0, 0));
        result = pSRGBToPLinear_Accurate(result);
        result *= lerp(
            colorA, colorB, rampV
        );
    }
    
    return ceil(l / stepPx);
}