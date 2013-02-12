using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;

namespace Squared.Render {
    public interface IBatchCombiner {
        bool CanCombine (Batch lhs, Batch rhs);
        Batch Combine (Batch lhs, Batch rhs);
    }

    public class BatchTypeSorter : IComparer<Batch> {
        public int Compare (Batch x, Batch y) {
            if (x == null)
                return (x == y) ? 0 : -1;
            else if (y == null)
                return 1;

            var typeResult = x.TypeId.CompareTo(y.TypeId);
            if (typeResult == 0)
                return x.Layer.CompareTo(y.Layer);
            else
                return typeResult;
        }
    }

    public static class BatchCombiner {
        public static readonly Comparison<Batch> BatchTypeSorter = new BatchTypeSorter().Compare;
        public static readonly List<IBatchCombiner> Combiners = new List<IBatchCombiner>();

        /// <summary>
        /// Scans over a list of batches and applies batch combiners to reduce the total number of batches sent to the GPU and
        ///  improve batch preparation efficiency. Batches eliminated by combination are replaced with null.
        /// </summary>
        /// <param name="batches">The list of batches to perform a combination pass over.</param>
        /// <returns>The number of batches eliminated.</returns>
        public static int CombineBatches (UnorderedList<Batch> batches) {
            batches.Timsort(BatchTypeSorter);

            int i = 0, j = i + 1, l = batches.Count, eliminatedCount = 0;

            Batch a, b;
            Type aType, bType;

            var _batches = batches.GetBuffer();

            while ((i < l) && (j < l)) {
                a = _batches[i];

                if (a == null) {
                    i += 1;
                    j = i + 1;
                    continue;
                }
                aType = a.GetType();

                b = _batches[j];

                if (b == null) {
                    j += 1;
                    continue;
                }
                bType = b.GetType();

                if ((aType != bType) || (a.Layer != b.Layer)) {
                    i = j;
                    j = i + 1;
                } else {
                    bool combined = false;

                    foreach (var combiner in Combiners) {
                        if (combined = combiner.CanCombine(a, b)) {
                            _batches[i] = _batches[j] = null;
                            _batches[i] = combiner.Combine(a, b);
                            _batches[i].Container = a.Container;

                            if ((a != _batches[i]) && (a.ReleaseAfterDraw))
                                a.ReleaseResources();
                            if (b.ReleaseAfterDraw)
                                b.ReleaseResources();

                            eliminatedCount += 1;
                            break;
                        }
                    }

                    j += 1;
                }
            }

            if (false && (eliminatedCount > 0))
                Console.WriteLine("Eliminated {0:0000} of {1:0000} batch(es)", eliminatedCount, batches.Count);

            return eliminatedCount;
        }
    }
}
