using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Render {
    public class DisposalQueue {
        private List<List<IDisposable>> SpareLists = new List<List<IDisposable>>();
        private List<IDisposable> CurrentList = new List<IDisposable>();

        public List<IDisposable> FreezeCurrentList () {
            List<IDisposable> newList = null;
            lock (SpareLists) {
                if (SpareLists.Count > 0) {
                    newList = SpareLists[SpareLists.Count - 1];
                    SpareLists.RemoveAt(SpareLists.Count - 1);
                }
            }
            if (newList == null)
                newList = new List<IDisposable>();
            return Interlocked.Exchange(ref CurrentList, newList);
        }

        public void DisposeListContents (List<IDisposable> list) {
            lock (list) {
                foreach (var item in list)
                    item.Dispose();

                list.Clear();
            }

            lock (SpareLists)
                if (SpareLists.Count < 4)
                    SpareLists.Add(list);
        }

        public void Enqueue (IDisposable resource) {
            var cl = CurrentList;
            lock (cl)
                cl.Add(resource);
        }
    }
}
