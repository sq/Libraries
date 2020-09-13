using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiscTests {
    class Program {
        static void Main (string[] args) {
            const int blockSize = 24, frameCount = 4;
            const int blockSizeSquared = blockSize * blockSize;

            var medianList = new List<float>();

            for (float i = -4; i <= 260; i += 0.5f) {
                float value = i / 255f;
                double sum = 0, min = 99999, max = -99999;

                medianList.Clear();
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++) {
                    for (int y = 0; y < blockSize; y++) {
                        for (int x = 0; x < blockSize; x++) {
                            var sample = ApplyDither(value, x, y, frameIndex) * 255f;
                            min = Math.Min(min, sample);
                            max = Math.Max(max, sample);
                            sum += sample;
                            medianList.Add(sample);
                        }
                    }
                }

                medianList.Sort();
                var median = medianList[medianList.Count / 2];

                var average = sum / (blockSizeSquared * frameCount);
                Console.WriteLine($"{i:000.0} mean={average:000.0000} median={median:000} min={min:000} max={max:000}");
            }

            if (Debugger.IsAttached)
                Console.ReadLine();
        }

        static float GetBandSize () {
            return 1;
        }

        static float[] k0 = new float[] { 2 / 17f, 7 / 17f, 23 / 17f };

        static float dot (float aX, float aY, float aZ, float bX, float bY, float bZ) {
			return aX * bX + aY * bY + aZ * bZ;
        }

        static float frac (float f) {
            return (float)(f - Math.Floor(f));
        }

        static float step (float x, float y) {
            if (x >= y)
                return 1;
            else
                return 0;
        }

        static float lerp (float a, float b, float t) {
            return a + ((b - a) * t);
        }

        static float Dither17 (float vposX, float vposY, float frameIndexMod4) {
            float ret = dot(vposX, vposY, frameIndexMod4, k0[0], k0[1], k0[2]);
            return frac(ret);
        }

        static float DitheringGetUnit () {
            // pow2 - 1
            return 255;
        }

        static float DitheringGetInvUnit () {
            return 1f / DitheringGetUnit();
        }

        static float ApplyDither (float value, float vposX, float vposY, int frameIndex) {
            float threshold = Dither17(vposX, vposY, (frameIndex % 4) + 0.5f);
            threshold *= GetBandSize();

            const float strengthOffset = 0.05f;
            const float offset = 0f / 255f;

            float value8 = (value + offset) * DitheringGetUnit();
            float a = (float)Math.Floor(value8), b = (float)Math.Ceiling(value8);
            float error = b - value8;
            float mask = step(Math.Abs(error), threshold);
            float result = lerp(a, b, mask);

            float strength = 1;
            return lerp(value, result * DitheringGetInvUnit(), strength);
        }
    }
}
