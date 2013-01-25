using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

            var xType = x.GetType();
            var yType = y.GetType();

            if (xType == yType)
                return 0;
            else
                return xType.GetHashCode().CompareTo(yType.GetHashCode());
        }
    }

    public static class BatchCombiner {
        public static readonly IComparer<Batch> BatchTypeSorter = new BatchTypeSorter();
        public static readonly List<IBatchCombiner> Combiners = new List<IBatchCombiner>();

        /// <summary>
        /// Scans over a list of batches and applies batch combiners to reduce the total number of batches sent to the GPU and
        ///  improve batch preparation efficiency. Batches eliminated by combination are replaced with null.
        /// </summary>
        /// <param name="batches">The list of batches to perform a combination pass over.</param>
        /// <returns>The number of batches eliminated.</returns>
        public static int CombineBatches (List<Batch> batches) {
            batches.Sort(BatchTypeSorter);

            int i = 0, j = i + 1, l = batches.Count, eliminatedCount = 0;

            Batch a, b;
            Type aType, bType;

            while ((i < l) && (j < l)) {
                a = batches[i];

                if (a == null) {
                    i += 1;
                    j = i + 1;
                    continue;
                }
                aType = a.GetType();

                b = batches[j];

                if (b == null) {
                    j += 1;
                    continue;
                }
                bType = b.GetType();

                if (aType != bType) {
                    i = j;
                    j = i + 1;
                } else {
                    bool combined = false;

                    foreach (var combiner in Combiners) {
                        if (combined = combiner.CanCombine(a, b)) {
                            batches[i] = batches[j] = null;
                            batches[i] = combiner.Combine(a, b);
                            eliminatedCount += 1;
                            break;
                        }
                    }

                    j += 1;
                }
            }

            return eliminatedCount;
        }
    }
}
