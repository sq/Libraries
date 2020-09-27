using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render;

namespace Framework {
    public static class PerformanceStats {
        private static readonly StringBuilder StringBuilder = new StringBuilder();
        private static string _CachedString = null;

        public const int SampleCount = 200;
        private static readonly List<double> WaitSamples = new List<double>(),
            BeginDrawSamples = new List<double>(),
            DrawSamples = new List<double>(),
            BeforePresentSamples = new List<double>(),
            EndDrawSamples = new List<double>();

        private static int LastBatchCount, LastPrimitiveCount;

        private static void PushSample (List<double> list, double sample) {
            if (list.Count == SampleCount)
                list.RemoveAt(0);

            list.Add(sample);
        }

        private static double GetAverage (List<double> list) {
            if (list.Count == 0)
                return 0;

            return list.Average();
        }

        private static void GenerateText (int primCountOffset) {
            StringBuilder.Clear();

            var drawAverage = GetAverage(DrawSamples);
            var beginAverage = GetAverage(BeginDrawSamples);
            var bpAverage = GetAverage(BeforePresentSamples);
            var endAverage = GetAverage(EndDrawSamples);
            var waitAverage = GetAverage(WaitSamples);

            var totalAverage = drawAverage + beginAverage + endAverage + waitAverage;
            var fpsAverage = 1000.0 / totalAverage;

            StringBuilder.AppendFormat("ms/f {0,7:000.00}\r\n", totalAverage);
            StringBuilder.AppendFormat("FPS ~{0,7:000.00}\r\n", fpsAverage);
            StringBuilder.AppendFormat("batch {0,7:0000}\r\n", LastBatchCount);
            StringBuilder.AppendFormat("prim ~{0,7:0000000}\r\n", LastPrimitiveCount + primCountOffset);
        }

        public static void Record (FrameTiming timing) {
            PushSample(WaitSamples, timing.Wait.TotalMilliseconds);
            PushSample(BeginDrawSamples, timing.BeginDraw.TotalMilliseconds);
            PushSample(DrawSamples, timing.Draw.TotalMilliseconds);
            PushSample(BeforePresentSamples, timing.BeforePresent.TotalMilliseconds);
            PushSample(EndDrawSamples, timing.EndDraw.TotalMilliseconds);
            LastBatchCount = timing.BatchCount;
            LastPrimitiveCount = timing.PrimitiveCount;
        }

        public static StringBuilder GetText (int primCountOffset = 0) {
            GenerateText(primCountOffset);
            return StringBuilder;
        }
    }
}
