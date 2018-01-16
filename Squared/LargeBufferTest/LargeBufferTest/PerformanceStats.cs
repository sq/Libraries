using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render;

namespace LargeBufferTest {
    public static class PerformanceStats {
        private static readonly StringBuilder StringBuilder = new StringBuilder();
        private static string _CachedString = null;

        public const int SampleCount = 90;
        public static readonly List<double> WaitSamples = new List<double>(),
            BeginDrawSamples = new List<double>(),
            DrawSamples = new List<double>(),
            BeforePresentSamples = new List<double>(),
            EndDrawSamples = new List<double>();

        public static void PushSample (List<double> list, double sample) {
            if (list.Count == SampleCount)
                list.RemoveAt(0);

            list.Add(sample);
        }

        public static double GetAverage (List<double> list) {
            if (list.Count == 0)
                return 0;

            return list.Average();
        }

        private static string GenerateText (MultithreadedGame game, int primCountOffset) {
            StringBuilder.Clear();

            var drawAverage = GetAverage(DrawSamples);
            var beginAverage = GetAverage(BeginDrawSamples);
            var bpAverage = GetAverage(BeforePresentSamples);
            var endAverage = GetAverage(EndDrawSamples);
            var waitAverage = GetAverage(WaitSamples);

            var totalAverage = drawAverage + beginAverage + endAverage + waitAverage;
            var fpsAverage = 1000.0 / totalAverage;

            StringBuilder.AppendFormat("wait  {0,7:0.00}\r\n", waitAverage);
            StringBuilder.AppendFormat("bp    {0,7:0.00}\r\n", bpAverage);
            StringBuilder.AppendFormat("ms/f  {0,7:0.00}\r\n", totalAverage);
            StringBuilder.AppendFormat("FPS  ~{0,7:0.00}\r\n", fpsAverage);

            return StringBuilder.ToString();
        }

        public static void Record (MultithreadedGame game) {
            PushSample(WaitSamples, game.PreviousFrameTiming.Wait.TotalMilliseconds);
            PushSample(BeginDrawSamples, game.PreviousFrameTiming.BeginDraw.TotalMilliseconds);
            PushSample(DrawSamples, game.PreviousFrameTiming.Draw.TotalMilliseconds);
            PushSample(BeforePresentSamples, game.PreviousFrameTiming.BeforePresent.TotalMilliseconds);
            PushSample(EndDrawSamples, game.PreviousFrameTiming.EndDraw.TotalMilliseconds);

            _CachedString = null;
        }

        public static string GetText (MultithreadedGame game, int primCountOffset = 0) {
            if (_CachedString == null)
                _CachedString = GenerateText(game, primCountOffset);

            return _CachedString;
        }
    }
}
