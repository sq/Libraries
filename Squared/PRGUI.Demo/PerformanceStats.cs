using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render;
using Squared.Util;

namespace Framework {
    public static class PerformanceStats {
        private static readonly StringBuilder StringBuilder = new StringBuilder();
        private static string _CachedString = null;

        public const int SampleCount = 200;
        private static readonly List<double> UpdateSamples = new List<double>(),
            WaitSamples = new List<double>(),
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

        private static void Analyze (List<double> list, out double average, out double max) {
            average = max = 0;

            var sum = 0.0;
            foreach (var d in list) {
                max = Math.Max(max, d);
                sum += d;
            }

            if (list.Count > 0)
                average = sum / list.Count;
        }

        private static void GenerateText (int primCountOffset) {
            StringBuilder.Clear();

            double updateAverage, drawAverage, beginAverage, bpAverage, endAverage, waitAverage;
            double updateMax, drawMax, beginMax, bpMax, endMax, waitMax;
            Analyze(UpdateSamples, out updateAverage, out updateMax);
            Analyze(DrawSamples, out drawAverage, out drawMax);
            Analyze(BeginDrawSamples, out beginAverage, out beginMax);
            Analyze(BeforePresentSamples, out bpAverage, out bpMax);
            Analyze(EndDrawSamples, out endAverage, out endMax);
            Analyze(WaitSamples, out waitAverage, out waitMax);

            var totalAverage = updateAverage + drawAverage + beginAverage + endAverage + waitAverage;
            var totalMax = updateAverage + drawMax + beginMax + endMax + waitMax;
            var fpsAverage = 1000.0 / totalAverage;

            StringBuilder.Append("ms/f ");
            StringBuilder.Append(Math.Round(totalAverage, 3, MidpointRounding.AwayFromZero));
            StringBuilder.AppendLine();
            StringBuilder.Append("max ");
            StringBuilder.Append(Math.Round(totalMax, 3, MidpointRounding.AwayFromZero));
            StringBuilder.AppendLine();
            StringBuilder.Append("upd% ");
            StringBuilder.Append(Math.Round(
                (updateAverage / totalAverage) * 100, 1, MidpointRounding.AwayFromZero
            ));
            StringBuilder.AppendLine();
            StringBuilder.Append("FPS ~");
            StringBuilder.Append(Math.Round(
                fpsAverage, 1, MidpointRounding.AwayFromZero
            ));
            StringBuilder.AppendLine();
            StringBuilder.Append(LastBatchCount);
            StringBuilder.AppendLine(" batches");
        }

        public static void Record (FrameTiming timing, long updateElapsedTicks) {
            PushSample(UpdateSamples, (double)updateElapsedTicks / (double)Time.MillisecondInTicks);
            PushSample(WaitSamples, timing.Wait.TotalMilliseconds);
            PushSample(BeginDrawSamples, timing.BeginDraw.TotalMilliseconds);
            PushSample(DrawSamples, timing.BuildFrame.TotalMilliseconds);
            PushSample(BeforePresentSamples, timing.BeforePresent.TotalMilliseconds);
            PushSample(EndDrawSamples, timing.SyncEndDraw.TotalMilliseconds);
            LastBatchCount = timing.BatchCount;
            LastPrimitiveCount = timing.PrimitiveCount;
        }

        public static StringBuilder GetText (int primCountOffset = 0) {
            GenerateText(primCountOffset);
            return StringBuilder;
        }
    }
}
