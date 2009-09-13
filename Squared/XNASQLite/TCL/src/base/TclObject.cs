/*
* TclObject.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclObject.java,v 1.9 2003/01/09 02:15:40 mdejong Exp $
*
*/
using System;
using System.Text;

namespace tcl.lang
{
	
	/// <summary> This class implements the basic notion of an "object" in Tcl. The
	/// fundamental representation of an object is its string value. However,
	/// an object can also have an internal representation, which is a "cached"
	/// reprsentation of this object in another form. The type of the internal
	/// rep of Tcl objects can mutate. This class provides the storage of the
	/// string rep and the internal rep, as well as the facilities for mutating
	/// the internal rep.
	/// </summary>
	
	public class TclObject
	{
		/// <summary> Returns the handle to the current internal rep. This method should be
		/// called only by an InternalRep implementation.
		/// the handle to the current internal rep.
		/// Change the internal rep of the object. The old internal rep
		/// will be deallocated as a result. This method should be
		/// called only by an InternalRep implementation.
		/// </summary>
		/// <param name="rep">the new internal rep.
		/// </param>
		public InternalRep InternalRep
		{
			get
			{
				disposedCheck();
				return internalRep;
			}
			
			set
			{
				disposedCheck();
				if (value == null)
				{
					throw new TclRuntimeError("null InternalRep");
				}
				if (value == internalRep)
				{
					return ;
				}
				
				// In the special case where the internal representation is a CObject,
				// we want to call the special interface to convert the underlying
				// native object into a reference to the Java TclObject.  Note that
				// this test will always fail if we are not using the native
				// implementation. Also note that the makeReference method
				// will do nothing in the case where the Tcl_Obj inside the
				// CObject was originally allocated in Java. When converting
				// to a CObject we need to break the link made earlier.
				
				if ((internalRep is CObject) && !(value is CObject))
				{
					// We must ensure that the string rep is copied into Java
					// before we lose the reference to the underlying CObject.
					// Otherwise we will lose the original string information
					// when the backpointer is lost.
					
					if ((System.Object) stringRep == null)
					{
						stringRep = internalRep.ToString();
					}
					((CObject) internalRep).makeReference(this);
				}
				
				//System.out.println("TclObject setInternalRep for \"" + stringRep + "\"");
				//System.out.println("from \"" + internalRep.getClass().getName() +
				//    "\" to \"" + rep.getClass().getName() + "\"");
				internalRep.dispose();
				internalRep = value;
			}
			
		}
		/// <summary> Returns true if the TclObject is shared, false otherwise.</summary>
		/// <returns> true if the TclObject is shared, false otherwise.
		/// </returns>
		public bool Shared
		{
			get
			{
				disposedCheck();
				return (refCount > 1);
			}
			
		}
		/// <summary> Returns the refCount of this object.
		/// 
		/// </summary>
		/// <returns> refCount.
		/// </returns>
		public int RefCount
		{
			get
			{
				return refCount;
			}
			
		}
		/// <summary> Returns the Tcl_Obj* objPtr member for a CObject or TclList.
		/// This method is only called from Tcl Blend.
		/// </summary>
		internal long CObjectPtr
		{
			
			
			get
			{
				if (internalRep is CObject)
				{
					return ((CObject) internalRep).CObjectPtr;
				}
				else
				{
					return 0;
				}
			}
			
		}
		/// <summary> Returns 2 if the internal rep is a TclList.
		/// Returns 1 if the internal rep is a CObject.
		/// Otherwise returns 0.
		/// This method provides an optimization over
		/// invoking getInternalRep() and two instanceof
		/// checks via JNI. It is only used by Tcl Blend.
		/// </summary>
		internal int CObjectInst
		{
			
			
			get
			{
				if (internalRep is CObject)
				{
					if (internalRep is TclList)
						return 2;
					else
						return 1;
				}
				else
				{
					return 0;
				}
			}
			
		}
   
    // Internal representation of the object.
		
		protected internal InternalRep internalRep;
		
		// Reference count of this object. When 0 the object will be deallocated.
		
		protected internal int refCount;
		
		// String  representation of the object.
		
		protected internal string stringRep;
		
		/// <summary> Creates a TclObject with the given InternalRep. This method should be
		/// called only by an InternalRep implementation.
		/// 
		/// </summary>
		/// <param name="rep">the initial InternalRep for this object.
		/// </param>
		public TclObject(InternalRep rep)
		{
			if (rep == null)
			{
				throw new TclRuntimeError("null InternalRep");
			}
			internalRep = rep;
			stringRep = null;
			refCount = 0;
		}
		
		/// <summary> Creates a TclObject with the given InternalRep and stringRep.
		/// This constructor is used by the TclString class only. No other place
		/// should call this constructor.
		/// 
		/// </summary>
		/// <param name="rep">the initial InternalRep for this object.
		/// </param>
		/// <param name="s">the initial string rep for this object.
		/// </param>
		protected internal TclObject(TclString rep, string s)
		{
			if (rep == null)
			{
				throw new TclRuntimeError("null InternalRep");
			}
			internalRep = rep;
			stringRep = s;
			refCount = 0;
		}
		
		/// <summary> Returns the string representation of the object.
		/// 
		/// </summary>
		/// <returns> the string representation of the object.
		/// </returns>
		public override string ToString()
		{
			disposedCheck();
			if ((System.Object) stringRep == null)
			{
        stringRep = internalRep.ToString().Replace( "Infinity", "inf" );
			}
			return stringRep;
		}

    /// <summary> Returns the UTF8 byte representation of the object.
    /// 
    /// </summary>
    /// <returns> the string representation of the object.
    /// </returns>
    public byte[] ToBytes()
    {
      disposedCheck();
      if ( (System.Object)stringRep == null )
      {
        stringRep = internalRep.ToString();
      }
      return Encoding.UTF8.GetBytes(stringRep);
    }
    /// <summary> Sets the string representation of the object to null.  Next
		/// time when toString() is called, getInternalRep().toString() will
		/// be called. This method should be called ONLY when an InternalRep
		/// is about to modify the value of a TclObject.
		/// 
		/// </summary>
		/// <exception cref=""> TclRuntimeError if object is not exclusively owned.
		/// </exception>
		public void  invalidateStringRep()
		{
			disposedCheck();
			if (refCount > 1)
			{
				throw new TclRuntimeError("string representation of object \"" + ToString() + "\" cannot be invalidated: refCount = " + refCount);
			}
			stringRep = null;
		}
		
		/// <summary> Tcl_DuplicateObj -> duplicate
		/// 
		/// Duplicate a TclObject, this method provides the preferred
		/// means to deal with modification of a shared TclObject.
		/// It should be invoked in conjunction with isShared instead
		/// of using the deprecated takeExclusive method.
		/// 
		/// Example:
		/// 
		/// if (tobj.isShared()) {
		/// tobj = tobj.duplicate();
		/// }
		/// TclString.append(tobj, "hello");
		/// 
		/// </summary>
		/// <returns> an TclObject with a refCount of 0.
		/// </returns>
		
		public TclObject duplicate()
		{
			disposedCheck();
			if (internalRep is TclString)
			{
				if ((System.Object) stringRep == null)
				{
					stringRep = internalRep.ToString();
				}
			}
			TclObject newObj = new TclObject(internalRep.duplicate());
      newObj.typePtr = this.typePtr;
      newObj.stringRep = this.stringRep;
			newObj.refCount = 0;
			return newObj;
		}
		
		/// <deprecated> The takeExclusive method has been deprecated
		/// in favor of the new duplicate() method. The takeExclusive
		/// method would modify the ref count of the original object
		/// and return an object with a ref count of 1 instead of 0.
		/// These two behaviors lead to lots of useless duplication
		/// of objects that could be modified directly.
		/// </deprecated>
		
		public TclObject takeExclusive()
		{
			disposedCheck();
			if (refCount == 1)
			{
				return this;
			}
			else if (refCount > 1)
			{
				if (internalRep is TclString)
				{
					if ((System.Object) stringRep == null)
					{
						stringRep = internalRep.ToString();
					}
				}
				TclObject newObj = new TclObject(internalRep.duplicate());
				newObj.stringRep = this.stringRep;
				newObj.refCount = 1;
				refCount--;
				return newObj;
			}
			else
			{
				throw new TclRuntimeError("takeExclusive() called on object \"" + ToString() + "\" with: refCount = 0");
			}
		}
		
		/// <summary> Tcl_IncrRefCount -> preserve
		/// 
		/// Increments the refCount to indicate the caller's intent to
		/// preserve the value of this object. Each preserve() call must be matched
		/// by a corresponding release() call.
		/// 
		/// </summary>
		/// <exception cref=""> TclRuntimeError if the object has already been deallocated.
		/// </exception>
		public void  preserve()
		{
			disposedCheck();
			if (internalRep is CObject)
			{
				((CObject) internalRep).incrRefCount();
			}
			_preserve();
		}
		
		/// <summary> _preserve
		/// 
		/// Private implementation of preserve() method.
		/// This method will be invoked from Native code
		/// to change the TclObject's ref count without
		/// effecting the ref count of a CObject.
		/// </summary>
		private void  _preserve()
		{
			refCount++;
		}
		
		/// <summary> Tcl_DecrRefCount -> release
		/// 
		/// Decrements the refCount to indicate that the caller is no longer
		/// interested in the value of this object. If the refCount reaches 0,
		/// the obejct will be deallocated.
		/// </summary>
		public void  release()
		{
			disposedCheck();
			if (internalRep is CObject)
			{
				((CObject) internalRep).decrRefCount();
			}
			_release();
		}
		
		/// <summary> _release
		/// 
		/// Private implementation of preserve() method.
		/// This method will be invoked from Native code
		/// to change the TclObject's ref count without
		/// effecting the ref count of a CObject.
		/// </summary>
		private void  _release()
		{
			refCount--;
			if (refCount <= 0)
			{
				internalRep.dispose();
				
				// Setting these to null will ensure that any attempt to use
				// this object will result in a Java NullPointerException.
				
				internalRep = null;
				stringRep = null;
			}
		}
		
		/// <summary> Raise a TclRuntimeError if this TclObject has been
		/// disposed of before the last ref was released.
		/// </summary>
		
		private void  disposedCheck()
		{
			if (internalRep == null)
			{
				throw new TclRuntimeError("TclObject has been deallocated");
			}
		}

    protected internal string _typePtr;
    /// <summary> Return string describing type.</summary>
    public string typePtr
    {
      get { return _typePtr; }
      set { _typePtr = value; }
    }

  }
}
