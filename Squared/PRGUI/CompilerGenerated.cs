using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Input;
using Squared.Util;

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        private struct TraverseChildrenEnumerable : IEnumerable<TraversalInfo>, IEnumerable, IEnumerator<TraversalInfo>, IDisposable, IEnumerator
        {
	        // Token: 0x06000903 RID: 2307 RVA: 0x0002BF1C File Offset: 0x0002A11C
	        public void Dispose()
	        {
		        int num = this.state;
		        if (num == -3 || num == 3)
		        {
			        try
			        {
			        }
			        finally
			        {
				        this._m__Finally1();
			        }
		        }
	        }

	        // Token: 0x06000904 RID: 2308 RVA: 0x0002BF54 File Offset: 0x0002A154
	        public bool MoveNext()
	        {
		        try
		        {
			        int num = this.state;
			        UIContext uicontext = this.context;
			        switch (num)
			        {
			        case 0:
			        {
				        this.state = -1;
				        if (this.collection.Count <= 0)
				        {
					        return false;
				        }
				        this._i_5__2 = ((this.settings.Direction > 0) ? 0 : (this.collection.Count - 1));
				        this._tabOrdered_5__3 = this.collection.InTabOrder(uicontext.FrameIndex, false);
				        IControlContainer controlContainer = this.collection.Host as IControlContainer;
				        if (!this.settings.StartWithDefault || ((controlContainer != null) ? controlContainer.DefaultFocusTarget : null) == null)
				        {
					        goto IL_165;
				        }
				        this._dft_5__4 = controlContainer.DefaultFocusTarget;
				        if (Control.IsEqualOrAncestor(this._dft_5__4, this.collection.Host))
				        {
					        Control control = uicontext.FindFocusableChildOfDefaultFocusTarget(this._dft_5__4, this.settings);
					        if (control != null)
					        {
						        UIContext.TraversalInfo traversalInfo = uicontext.Traverse_MakeInfo(control);
						        if (this.settings.Predicate == null || this.settings.Predicate(traversalInfo.Control))
						        {
							        this._2__current = traversalInfo;
							        this.state = 1;
							        return true;
						        }
					        }
				        }
				        break;
			        }
			        case 1:
				        this.state = -1;
				        break;
			        case 2:
				        this.state = -1;
				        goto IL_1EA;
			        case 3:
				        this.state = -3;
				        goto IL_278;
			        default:
				        return false;
			        }
			        int num2 = this._tabOrdered_5__3.IndexOf(this._dft_5__4);
			        if (num2 >= 0)
			        {
				        this._i_5__2 = num2;
			        }
			        this._dft_5__4 = null;
			        IL_165:
			        if (this._i_5__2 < 0 || this._i_5__2 >= this._tabOrdered_5__3.Count)
			        {
				        return false;
			        }
			        Control control2 = this._tabOrdered_5__3[this._i_5__2];
			        this._info_5__5 = uicontext.Traverse_MakeInfo(control2);
			        if (this.settings.Predicate == null || this.settings.Predicate(control2))
			        {
				        this._2__current = this._info_5__5;
				        this.state = 2;
				        return true;
			        }
			        IL_1EA:
			        if (this._info_5__5.IsProxy && this.settings.FollowProxies)
			        {
				        return false;
			        }
			        if (!uicontext.Traverse_CanDescend(ref this._info_5__5, ref this.settings))
			        {
				        goto IL_292;
			        }
			        this._7__wrap5 = uicontext.TraverseChildren(this._info_5__5.Container.Children, ref settings).GetEnumerator();
			        this.state = -3;
			        IL_278:
			        if (this._7__wrap5.MoveNext())
			        {
				        UIContext.TraversalInfo traversalInfo2 = this._7__wrap5.Current;
				        this._2__current = traversalInfo2;
				        this.state = 3;
				        return true;
			        }
			        this._m__Finally1();
			        this._7__wrap5 = null;
			        IL_292:
			        this._i_5__2 += this.settings.Direction;
			        this._info_5__5 = default(UIContext.TraversalInfo);
			        goto IL_165;
		        }
		        catch
		        {
			        Dispose();
			        throw;
		        }
		        bool result;
		        return result;
	        }

	        // Token: 0x06000905 RID: 2309 RVA: 0x0002C244 File Offset: 0x0002A444
	        private void _m__Finally1()
	        {
		        this.state = -1;
		        if (this._7__wrap5 != null)
		        {
			        this._7__wrap5.Dispose();
		        }
	        }

	        // Token: 0x17000243 RID: 579
	        // (get) Token: 0x06000906 RID: 2310 RVA: 0x0002C260 File Offset: 0x0002A460
	        public UIContext.TraversalInfo Current
	        {
		        get
		        {
			        return this._2__current;
		        }
	        }

	        // Token: 0x06000907 RID: 2311 RVA: 0x0002C268 File Offset: 0x0002A468
	        void IEnumerator.Reset()
	        {
		        throw new NotSupportedException();
	        }

	        // Token: 0x17000244 RID: 580
	        // (get) Token: 0x06000908 RID: 2312 RVA: 0x0002C26F File Offset: 0x0002A46F
	        object IEnumerator.Current
	        {
		        get
		        {
			        return this._2__current;
		        }
	        }

            public TraverseChildrenEnumerable GetEnumerator () {
                this.state = 0;
                return this;
            }

            public IEnumerable<Control> Controls {
                get {
                    // FIXME: Optimize this
                    return this.Select(ti => ti.Control);
                }
            }

            public TraversalInfo FirstOrDefault () {
                this.state = 0;
                var ok = MoveNext();
                var result = ok ? _2__current : default;
                Dispose();
                return result;
            }

	        // Token: 0x06000909 RID: 2313 RVA: 0x0002C27C File Offset: 0x0002A47C
	        IEnumerator<UIContext.TraversalInfo> IEnumerable<UIContext.TraversalInfo>.GetEnumerator()
	        {
                return GetEnumerator();
	        }

	        // Token: 0x0600090A RID: 2314 RVA: 0x0002C2D7 File Offset: 0x0002A4D7
	        IEnumerator IEnumerable.GetEnumerator()
	        {
		        return GetEnumerator();
	        }

	        // Token: 0x040004EB RID: 1259
	        public int state;

	        // Token: 0x040004EC RID: 1260
	        private UIContext.TraversalInfo _2__current;

	        // Token: 0x040004EF RID: 1263
	        public ControlCollection collection;

	        // Token: 0x040004F1 RID: 1265
	        public UIContext.TraverseSettings settings;

	        // Token: 0x040004F2 RID: 1266
	        public UIContext context;

	        // Token: 0x040004F3 RID: 1267
	        private int _i_5__2;

	        // Token: 0x040004F4 RID: 1268
	        private List<Control> _tabOrdered_5__3;

	        // Token: 0x040004F5 RID: 1269
	        private Control _dft_5__4;

	        // Token: 0x040004F6 RID: 1270
	        private UIContext.TraversalInfo _info_5__5;

	        // Token: 0x040004F7 RID: 1271
	        private IEnumerator<UIContext.TraversalInfo> _7__wrap5;
        }

        // Token: 0x0200009A RID: 154
        private struct SearchForSiblingsEnumerable : IEnumerable<UIContext.TraversalInfo>, IEnumerable, IEnumerator<UIContext.TraversalInfo>, IDisposable, IEnumerator
        {
	        // Token: 0x0600090C RID: 2316 RVA: 0x0002C2FC File Offset: 0x0002A4FC
	        public void Dispose()
	        {
		        int num = this.state;
		        if (num == -3 || num == 2)
		        {
			        try
			        {
			        }
			        finally
			        {
				        this.__m__Finally1();
			        }
		        }
	        }

	        // Token: 0x0600090D RID: 2317 RVA: 0x0002C334 File Offset: 0x0002A534
	        public bool MoveNext()
	        {
		        bool result;
		        try
		        {
			        int num = this.state;
			        UIContext uicontext = this.context;
			        Control host;
			        bool flag;
			        switch (num)
			        {
			        case 0:
			        {
				        this.state = -1;
				        if (this.startingPosition == null)
				        {
					        throw new ArgumentNullException("startingPosition");
				        }
				        this._visitedProxyTargets_5__2 = default(DenseList<Control>);
				        this._descendSettings_5__3 = this.settings;
				        this._descendSettings_5__3.AllowAscend = false;
				        ControlCollection controlCollection;
				        if ((controlCollection = this.collection) == null)
				        {
					        IControlContainer controlContainer = this.startingPosition as IControlContainer;
					        controlCollection = ((controlContainer != null) ? controlContainer.Children : null);
				        }
				        this._currentCollection_5__4 = controlCollection;
				        if (this._currentCollection_5__4 == null)
				        {
					        Control control;
					        IControlContainer controlContainer2;
					        if (!this.startingPosition.TryGetParent(out control) || (controlContainer2 = (control as IControlContainer)) == null)
					        {
						        return false;
					        }
					        this._currentCollection_5__4 = controlContainer2.Children;
				        }
				        host = this.startingPosition;
				        flag = false;
				        break;
			        }
			        case 1:
				        this.state = -1;
				        goto IL_212;
			        case 2:
				        this.state = -3;
				        goto IL_2D4;
			        default:
				        return false;
			        }
			        IL_CD:
			        this._tabOrdered_5__5 = this._currentCollection_5__4.InTabOrder(uicontext.FrameIndex, false);
			        int num2 = flag ? 0 : this.settings.Direction;
			        this._index_5__6 = this._tabOrdered_5__5.IndexOf(host);
			        this._i_5__7 = this._index_5__6 + num2;
			        this._proxyTarget_5__8 = null;
			        IL_121:
			        if (this._i_5__7 < 0 || this._i_5__7 >= this._tabOrdered_5__5.Count)
			        {
				        goto IL_330;
			        }
			        Control control2 = this._tabOrdered_5__5[this._i_5__7];
			        this._info_5__9 = uicontext.Traverse_MakeInfo(control2);
			        if (this._info_5__9.IsProxy && this.settings.FollowProxies && this._info_5__9.Control.Enabled && this._visitedProxyTargets_5__2.IndexOf(this._info_5__9.RedirectTarget) < 0)
			        {
				        this._proxyTarget_5__8 = this._info_5__9.RedirectTarget;
				        this._visitedProxyTargets_5__2.Add(this._proxyTarget_5__8);
				        goto IL_330;
			        }
			        if (this.settings.Predicate == null || this.settings.Predicate(control2))
			        {
				        this.__2__current = this._info_5__9;
				        this.state = 1;
				        return true;
			        }
			        IL_212:
			        if (!uicontext.Traverse_CanDescend(ref this._info_5__9, ref this.settings))
			        {
				        goto IL_2F9;
			        }
			        this.__7__wrap9 = uicontext.TraverseChildren(this._info_5__9.Container.Children, ref _descendSettings_5__3).GetEnumerator();
			        this.state = -3;
			        IL_2D4:
			        if (this.__7__wrap9.MoveNext())
			        {
				        UIContext.TraversalInfo traversalInfo = this.__7__wrap9.Current;
				        if (!traversalInfo.IsProxy || !this.settings.FollowProxies || this._visitedProxyTargets_5__2.IndexOf(traversalInfo.RedirectTarget) >= 0)
				        {
					        this.__2__current = traversalInfo;
					        this.state = 2;
					        return true;
				        }
				        this._proxyTarget_5__8 = this._info_5__9.RedirectTarget;
				        this._visitedProxyTargets_5__2.Add(this._proxyTarget_5__8);
			        }
			        this.__m__Finally1();
			        this.__7__wrap9 = null;
			        if (this._proxyTarget_5__8 != null)
			        {
				        goto IL_330;
			        }
			        IL_2F9:
			        this._i_5__7 += this.settings.Direction;
			        if (this._i_5__7 != this._index_5__6)
			        {
				        this._info_5__9 = default(UIContext.TraversalInfo);
				        goto IL_121;
			        }
			        IL_330:
			        if (this._proxyTarget_5__8 != null)
			        {
				        host = this._proxyTarget_5__8;
				        flag = true;
				        this.settings.DidFollowProxy[0] = true;
			        }
			        else
			        {
				        host = this._currentCollection_5__4.Host;
				        flag = false;
				        if (!this.settings.AllowAscend)
				        {
					        goto IL_3A4;
				        }
			        }
			        Control control3;
			        IControlContainer controlContainer3;
			        if (host.TryGetParent(out control3) && (controlContainer3 = (control3 as IControlContainer)) != null)
			        {
				        this._currentCollection_5__4 = controlContainer3.Children;
				        this._tabOrdered_5__5 = null;
				        this._proxyTarget_5__8 = null;
				        goto IL_CD;
			        }
			        IL_3A4:
			        result = false;
		        }
		        catch
		        {
			        Dispose();
			        throw;
		        }
		        return result;
	        }

	        // Token: 0x0600090E RID: 2318 RVA: 0x0002C710 File Offset: 0x0002A910
	        private void __m__Finally1()
	        {
		        this.state = -1;
		        if (this.__7__wrap9 != null)
		        {
			        this.__7__wrap9.Dispose();
		        }
	        }

	        // Token: 0x17000245 RID: 581
	        // (get) Token: 0x0600090F RID: 2319 RVA: 0x0002C72C File Offset: 0x0002A92C
	        public UIContext.TraversalInfo Current
	        {
		        get
		        {
			        return this.__2__current;
		        }
	        }

	        // Token: 0x06000910 RID: 2320 RVA: 0x0002C268 File Offset: 0x0002A468
	        void IEnumerator.Reset()
	        {
		        throw new NotSupportedException();
	        }

	        // Token: 0x17000246 RID: 582
	        // (get) Token: 0x06000911 RID: 2321 RVA: 0x0002C734 File Offset: 0x0002A934
	        object IEnumerator.Current
	        {
		        get
		        {
			        return this.__2__current;
		        }
	        }

	        // Token: 0x06000912 RID: 2322 RVA: 0x0002C744 File Offset: 0x0002A944
	        public SearchForSiblingsEnumerable GetEnumerator()
	        {
			    this.state = 0;
                return this;
	        }

	        // Token: 0x06000913 RID: 2323 RVA: 0x0002C7AB File Offset: 0x0002A9AB
	        IEnumerator IEnumerable.GetEnumerator()
	        {
		        return GetEnumerator();
	        }

            IEnumerator<TraversalInfo> IEnumerable<TraversalInfo>.GetEnumerator () {
                return GetEnumerator();
            }

            public TraversalInfo FirstOrDefault () {
                this.state = 0;
                var ok = MoveNext();
                var result = ok ? __2__current : default;
                Dispose();
                return result;
            }

            // Token: 0x040004F8 RID: 1272
            public int state;

	        // Token: 0x040004F9 RID: 1273
	        private UIContext.TraversalInfo __2__current;

	        // Token: 0x040004FC RID: 1276
	        public Control startingPosition;

	        // Token: 0x040004FE RID: 1278
	        public UIContext.TraverseSettings settings;

	        // Token: 0x04000500 RID: 1280
	        public ControlCollection collection;

	        // Token: 0x04000501 RID: 1281
	        public UIContext context;

	        // Token: 0x04000502 RID: 1282
	        private DenseList<Control> _visitedProxyTargets_5__2;

	        // Token: 0x04000503 RID: 1283
	        private UIContext.TraverseSettings _descendSettings_5__3;

	        // Token: 0x04000504 RID: 1284
	        private ControlCollection _currentCollection_5__4;

	        // Token: 0x04000505 RID: 1285
	        private List<Control> _tabOrdered_5__5;

	        // Token: 0x04000506 RID: 1286
	        private int _index_5__6;

	        // Token: 0x04000507 RID: 1287
	        private int _i_5__7;

	        // Token: 0x04000508 RID: 1288
	        private Control _proxyTarget_5__8;

	        // Token: 0x04000509 RID: 1289
	        private UIContext.TraversalInfo _info_5__9;

	        // Token: 0x0400050A RID: 1290
	        private IEnumerator<UIContext.TraversalInfo> __7__wrap9;
        }

    }
}