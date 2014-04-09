using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Squared.Task.Http {
    public partial class HttpServer {
        public class EndPointList : IEnumerable<EndPoint> {
            public readonly HttpServer Owner;
            private readonly HashSet<EndPoint> EndPoints = new HashSet<EndPoint>();

            internal EndPointList (HttpServer owner) {
                Owner = owner;
            }

            private void CheckInvariant () {
                if ((Owner == null) || Owner.IsDisposed)
                    throw new ObjectDisposedException("Owner");

                if (Owner.IsListening)
                    throw new InvalidOperationException("Endpoint list may not be modified while server is listening");
            }

            public void Add (EndPoint endPoint) {
                CheckInvariant();

                EndPoints.Add(endPoint);
            }

            public void Add (params EndPoint[] endPoints) {
                CheckInvariant();

                foreach (var ep in endPoints)
                    EndPoints.Add(ep);
            }

            public bool Remove (EndPoint endPoint) {
                CheckInvariant();

                return EndPoints.Remove(endPoint);
            }

            public EndPoint[] ToArray () {
                return EndPoints.ToArray();
            }

            public int Count {
                get {
                    return EndPoints.Count;
                }
            }

            public IEnumerator<EndPoint> GetEnumerator () {
                return EndPoints.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
                return EndPoints.GetEnumerator();
            }
        }
    }
}
