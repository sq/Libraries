#ifdef INCLUDE_COMPOSITES
uniform const int CompositeCount;
uniform const float4 Composites[8];

void evaluateComposites (
    in float2 worldPosition, bool expandBox,
    inout float distance, inout float2 tl, inout float2 br
) {
    for (int i = 0, c = min(CompositeCount, 4); i < c; i++) {
        int type = abs((int)Composites[i].x), mode = abs((int)Composites[i].y);
        float2 center = Composites[i + 1].xy, size = abs(Composites[i + 1].zw),
            localTl = center - size, localBr = center + size,
            localPos = worldPosition - center;

        float compositeDistance;
        switch (type) {
            case 0:
                compositeDistance = sdEllipse(localPos, size);
                break;
            case 1:
                compositeDistance = sdBox(localPos, size);
                break;
            case 2:
                localPos = rotate2D(localPos, -0.25 * PI);
                compositeDistance = sdBox(localPos, size);
                break;
        }

        bool needExpand = false;
        switch (mode) {
            case 0: // union
                distance = min(distance, compositeDistance);
                needExpand = true;
                break;
            case 1: // subtract
                distance = max(distance, -compositeDistance);
                break;
            case 2: // xor
                distance = max(min(distance, compositeDistance), -max(distance, compositeDistance));
                needExpand = true;
                break;
            case 3: // intersection
                distance = max(distance, compositeDistance);
                break;
        }

        [flatten]
        if (expandBox && needExpand) {
            tl = min(localTl, tl);
            br = max(localBr, br);
        }
    }
}

void computeTLBR_Composite (
    bool expandBox,
    inout float2 tl, inout float2 br
) {
    float dummy = 0;

    evaluateComposites(0, expandBox, dummy, tl, br);
}
#else
#define CompositeCount 0

void computeTLBR_Composite (
    bool expandBox, in float2 tl, in float2 br
) {    
}

void evaluateComposites (
    in float2 worldPosition, bool expandBox,
    in float distance, in float2 tl, in float2 br
) {
}
#endif
