using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

#if XBOX
using KiloWatt.Runtime.Support;
using InvokerFunction = KiloWatt.Runtime.Support.TaskFunction;
#else
using System.Threading.Tasks;
using InvokerFunction = System.Action;
#endif

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

#if XBOX
        internal readonly ThreadPoolComponent ThreadPool;
        internal readonly AutoResetEvent[] Signals;
#endif

        public ParallelInvoker (Game game, ParallelInvokerTarget<T> function, bool multiThreaded) {
            Function = function;

            if (multiThreaded) {
#if XBOX
                // ThreadPoolComponent always uses 3 threads on XBox 360.
                ThreadCount = 3;
#else
                // Try to pick a good number of threads based on the current CPU count.
                // Too many and we'll get bogged down in scheduling overhead. Too few and we won't benefit from parallelism.
                ThreadCount = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
#endif
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

#if XBOX
            Signals = new AutoResetEvent[ThreadCount];
            for (int i = 0; i < ThreadCount; i++)
                Signals[i] = new AutoResetEvent(false);

            foreach (var component in game.Components) {
                ThreadPool = component as ThreadPoolComponent;
                if (ThreadPool != null)
                    break;
            }

            if (ThreadPool == null)
                throw new InvalidOperationException("You must have a ThreadPoolComponent to use a ParallelInvoker");
#endif
        }

        protected void InvokeInternal (int threadIndex) {
            Function(threadIndex, ThreadCount, UserData);
#if XBOX
            Signals[threadIndex].Set();
#endif
        }

        public void Invoke () {
            if (ThreadCount == 1) {
                Delegates[0]();
            } else {
#if XBOX
                foreach (var d in Delegates)
                    ThreadPool.AddTask(d, null, null);

                WaitHandle.WaitAll(Signals);
#else
                Parallel.Invoke(Delegates);
#endif
            }
        }
    }
}
