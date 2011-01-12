/*
Copyright (c) 2008 Jon Watte

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

http://www.enchantedage.com/xna-thread-pool
*/

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Xna.Framework;

namespace KiloWatt.Runtime.Support
{
  /// <summary>
  /// Called when a given task is complete, or has errored out.
  /// </summary>
  /// <param name="task">The task that completed.</param>
  /// <param name="error">null on success, non-null on error</param>
  public delegate void TaskComplete(Task task, System.Exception error);

  /// <summary>
  /// The TaskFunction delegate is called within the worker thread to do work.
  /// </summary>
  public delegate void TaskFunction();

  /// <summary>
  /// You typically only create a single ThreadPoolComponent in your application, 
  /// and let all your threaded tasks run within this component. This allows for 
  /// ideal thread balancing. If you have multiple components, they will not know 
  /// how to share the CPU between them fairly.
  /// </summary>
  public class ThreadPoolComponent : GameComponent
  {
    /// <summary>
    /// Create the ThreadPoolComponent in your application constructor, and add it 
    /// to your Components collection. The ThreadPool will deliver any completed 
    /// tasks first in the update order.
    /// 
    /// On Xbox, creates 3 threads. On PC, creates one or more threads, depending 
    /// on the number of CPU cores. Always creates at least one thread. The thread 
    /// tasks are assumed to be computationally expensive, so more threads than 
    /// there are CPU cores is not recommended.
    /// </summary>
    /// <param name="game">Your game instance.</param>
    public ThreadPoolComponent(Game game)
      : base(game)
    {
#if XBOX360
      int[] HardwareThread = new int[] { 3, 4, 5 };
      nThreads_ = 3;
      for (int i = 0; i != nThreads_; ++i)
      {
        int hwt = HardwareThread[i];
        Thread t = new System.Threading.Thread(new ThreadStart(
            delegate () { 
              Thread.CurrentThread.SetProcessorAffinity(new int [] { hwt });
              this.ThreadFunc();
            }));
        t.Start();
      }
#else
      //  hoaky but reasonable way of getting the number of processors in .NET
      nThreads_ = System.Environment.ProcessorCount;
      for (int i = 0; i != nThreads_; ++i)
      {
        Thread t = new System.Threading.Thread(new ThreadStart(this.ThreadFunc));
        t.Start();
      }
#endif
      UpdateOrder = Int32.MinValue;
    }

    /// <summary>
    /// Disposing the ParallelThreadPool component will immediately deliver all work
    /// items with an object disposed exception.
    /// </summary>
    /// <param name="disposing"></param>
    protected override void Dispose(bool disposing)
    {
      if (disposed_)
        throw new System.ObjectDisposedException("ParallelThreadPool", "double dispose of ParallelThreadPool");
      disposed_ = true;
      lock (this)
      {
        //  mark all work items as completed with exception
        if (completeList_ == null)
          completeList_ = workList_;
        else
          completeListEnd_.next_ = workList_;
        completeListEnd_ = workListEnd_;
        while (workList_ != null)
        {
          workList_.error_ = new System.ObjectDisposedException("ParallelThreadPool");
          workList_ = workList_.next_;
        }
        workListEnd_ = null;
        //  unblock the threads
      }
      //  let some thread know their time has come
      workEvent_.Set();
      //  todo: wait for each thread
      //  deliver all completed items
      DeliverComplete();
      base.Dispose(disposing);
    }
    
    AutoResetEvent workEvent_ = new AutoResetEvent(false);

    public override void Update(GameTime gameTime)
    {
      //  avoid an unnecessary lock if there's nothing to do
      if (completeList_ != null)
        DeliverComplete();
      base.Update(gameTime);
    }

#region Public API
    /// <summary>
    /// Deliver all complete tasks. This is usually called for you, but can be 
    /// called by you if you know that some tasks have completed.
    /// </summary>
    public void DeliverComplete()
    {
      Worker w, z;
      lock (this)
      {
        w = completeList_;
        z = w;
        completeList_ = null;
        completeListEnd_ = null;
      }
      if (z != null)
      {
        while (w != null)
        {
          try
          {
            if (w.completion_ != null)
              w.completion_(w, w.error_);
          }
          catch (System.Exception x)
          {
            Console.WriteLine("Exception thrown within worker completion! {0}", x.Message);
            //  retain the un-delivered notifications; leak the worker records already delivered
            if (completeList_ == null)
              completeList_ = w.next_;
            else
              completeListEnd_.next_ = w.next_;
            completeListEnd_ = w.next_;
            throw new Exception("The thread pool user threw an exception on delivery.", x);
          }
          w = w.next_;
        }
        lock (this)
        {
          //  I could link in the entire chain in one swoop if I kept some 
          //  more state around, but this seems simpler.
          while (z != null)
          {
            w = z.next_;
            z.next_ = freeList_;
            freeList_ = z;
            z = w;
          }
        }
      }
    }

    /// <summary>
    /// Add a task to the thread queue. When a thread is available, it will 
    /// dequeue this task and run it. Once complete, the task will be marked 
    /// complete, but your application won't be called back until the next 
    /// time Update() is called (so that callbacks are from the main thread).
    /// </summary>
    /// <param name="function">The function to call within the thread.</param>
    /// <param name="completion">The callback to report results to, or null. If 
    /// you care about which particular task has completed, use a different instance 
    /// for this delegate per task (typically, a delegate on the task itself).</param>
    /// <param name="ctx">A previously allocated TaskContext, to allow for waiting 
    /// on the task, or null. It cannot have been already used.</param>
    /// <returns>A Task identifier for the operation in question. Note: because
    /// of the threaded behavior, the task may have already completed when it 
    /// is returned. However, if you AddTask() from the main thread, the completion 
    /// function will not yet have been called.</returns>
    public Task AddTask(TaskFunction function, TaskComplete completion, TaskContext ctx)
    {
      if (function == null)
        throw new System.ArgumentNullException("function");
      Worker w;
      lock (this)
      {
        if (disposed_)
          throw new System.ObjectDisposedException("ParallelThreadPool");
        qDepth_++;
        w = NewWorker(function, completion);
        if (ctx != null)
          ctx.Init(w);
        if (workList_ == null)
          workList_ = w;
        else
          workListEnd_.next_ = w;
        workListEnd_ = w;
      }
      workEvent_.Set();
      return w;
    }

    public TaskContext NewTaskContext()
    {
      lock (this)
      {
        if (taskContextList_ == null)
          taskContextList_ = new TaskContext(this);
        TaskContext ret = taskContextList_;
        taskContextList_ = ret.next_;
        ret.next_ = null;
        return ret;
      }
    }
    
    internal void Reclaim(TaskContext ctx)
    {
      lock (this)
      {
        ctx.next_ = taskContextList_;
        taskContextList_ = ctx;
      }
    }

    TaskContext taskContextList_;

    public class TaskContext : IDisposable
    {
      internal TaskContext(ThreadPoolComponent tpc)
      {
        pool_ = tpc;
      }

      public void Dispose()
      {
        if (w_ == null)
          throw new ObjectDisposedException("TaskContext.Dispose()");
        w_.context_ = null;
        w_ = null;
        pool_.Reclaim(this);
      }

      internal void Init(Worker w)
      {
        w_ = w;
        w_.context_ = this;
        event_.Reset();
      }

      /// <summary>
      /// Wait will wait for the given task to complete, and then dispose 
      /// the context. After Wait() returns, you should do nothing else to 
      /// the context.
      /// </summary>
      public void Wait()
      {
        if (w_ == null)
          throw new ObjectDisposedException("TaskContext.Wait()");
        w_ = null;
        event_.WaitOne();
        pool_.Reclaim(this);
      }

      public void Complete()
      {
        event_.Set();
      }

      internal ThreadPoolComponent pool_;
      internal TaskContext next_;
      internal ManualResetEvent event_ = new ManualResetEvent(false);
      internal Worker w_;
    }
#endregion

#region Implementation details

    void WorkOne()
    {
      Worker w = null;
      workEvent_.WaitOne();
      if (disposed_)
      {
        workEvent_.Set();   //  tell the next guy through
        return;
      }
      lock (this)
      {
        w = workList_;
        if (w != null)
        {
          workList_ = w.next_;
          if (workList_ == null)
            workListEnd_ = null;
          else
            workEvent_.Set();   //  tell the next guy through
          w.next_ = null;
        }
        else
          return;
      }
      try
      {
        w.function_();
      }
      catch (System.Exception x)
      {
        w.error_ = x;
      }
      lock (this)
      {
        if (disposed_ && w.error_ == null)
          w.error_ = new System.ObjectDisposedException("ParallelThreadPool");
        if (completeList_ == null)
          completeList_ = w;
        else
          completeListEnd_.next_ = w;
        completeListEnd_ = w;
        --qDepth_;
        if (w.context_ != null)
          w.context_.Complete();
      }
    }

    void ThreadFunc()
    {
      while (!disposed_)
        WorkOne();
    }

    Worker NewWorker(TaskFunction tf, TaskComplete tc)
    {
      if (freeList_ == null)
        freeList_ = new Worker(null, null);
      Worker ret = freeList_;
      freeList_ = ret.next_;
      ret.function_ = tf;
      ret.completion_ = tc;
      ret.context_ = null;
      ret.error_ = null;
      ret.next_ = null;
      return ret;
    }

    Worker freeList_;
    Worker workList_;
    Worker workListEnd_;
    Worker completeList_;
    Worker completeListEnd_;
    volatile bool disposed_;
    volatile int qDepth_;
    int nThreads_;

    internal class Worker : Task
    {
      internal Worker(TaskFunction function, TaskComplete completion)
      {
        function_ = function;
        completion_ = completion;
        error_ = null;
      }
      internal TaskContext context_;
      internal TaskFunction function_;
      internal TaskComplete completion_;
      internal System.Exception error_;
      internal Worker next_;
    }
#endregion
  }

  public interface Task
  {
  }
}
