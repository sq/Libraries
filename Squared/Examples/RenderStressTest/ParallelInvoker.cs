using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InvokerFunction = System.Action;

namespace RenderStressTest {
    public delegate void ParallelInvokerTarget<T> (int threadId, int threadCount, T userData)
        where T : class;

    // Abstracts out the annoying lack of Parallel.Invoke on XBox 360 and also
    //  attempts to prevent the creation of garbage on both platforms
    public class ParallelInvoker<T>
        where T : class, new() {

        public readonly ParallelInvokerTarget<T> Function;
        public readonly int ThreadCount;
        public readonly T UserData;

        internal readonly InvokerFunction[] Delegates;

        public ParallelInvoker (Game game, ParallelInvokerTarget<T> function, bool multiThreaded) {
            Function = function;

            if (multiThreaded) {
                // Try to pick a good number of threads based on the current CPU count.
                // Too many and we'll get bogged down in scheduling overhead. Too few and we won't benefit from parallelism.
                ThreadCount = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
            } else {
                ThreadCount = 1;
            }

            UserData = new T();

            Delegates = new InvokerFunction[ThreadCount];
            for (int i = 0; i < ThreadCount; i++) {
                int j = i;
                Delegates[i] = 
                    () => InvokeInternal(j);
            }
        }

        protected void InvokeInternal (int threadIndex) {
            Function(threadIndex, ThreadCount, UserData);
        }

        public void Invoke () {
            if (ThreadCount == 1) {
                Delegates[0]();
            } else {
                Parallel.Invoke(Delegates);
            }
        }
    }
}
