/*
* CObject.java --
*
*	A stub class that represents objects created by the NativeTcl
*	interpreter.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: CObject.java,v 1.2 2000/10/29 06:00:41 mdejong Exp $
*/
using System;
namespace tcl.lang
{
	
	/*
	* This is a stub class used in Jacl to represent objects created in
	* the Tcl Blend interpreter. Actually CObjects will never appear inside
	* Jacl. However, since TclObject (which is shared between the Tcl Blend
	* and Jacl implementations) makes some references to CObject, we include
	* a stub class here to make the compiler happy.
	*
	* None of the methods in this implementation will ever be called.
	*/
	
	class CObject : InternalRep
	{
		
		public  void  dispose()
		{
			throw new TclRuntimeError("This shouldn't be called");
		}
		
		public  InternalRep duplicate()
		{
			throw new TclRuntimeError("This shouldn't be called");
		}
		
		internal void  makeReference(TclObject tobj)
		{
			throw new TclRuntimeError("This shouldn't be called");
		}
		
		public override string ToString()
		{
			throw new TclRuntimeError("This shouldn't be called");
		}
    
    public long CObjectPtr;
		public void decrRefCount() 
		{
		}
		public void incrRefCount() 
		{
		}
	} // end CObject
}
