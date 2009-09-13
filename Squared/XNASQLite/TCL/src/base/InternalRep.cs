/*
* InternalRep.java
*
*	This file contains the abstract class declaration for the
*	internal representations of TclObjects.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: InternalRep.java,v 1.4 2000/10/29 06:00:42 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This is the interface for implementing internal representation of Tcl
	/// objects.  A class that implements InternalRep should define the
	/// following:
	/// 
	/// (1) the two abstract methods specified in this base class:
	/// dispose()
	/// duplicate()
	/// 
	/// (2) The method toString()
	/// 
	/// (3) class method(s) newInstance() if appropriate
	/// 
	/// (4) class method set<Type>FromAny() if appropriate
	/// 
	/// (5) class method get() if appropriate
	/// </summary>
	
	public interface InternalRep
		{
			void  dispose();
			InternalRep duplicate();
		} // end InternalRep
}
