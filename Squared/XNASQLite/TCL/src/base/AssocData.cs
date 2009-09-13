/*
* AssocData.java --
*
*	The API for registering named data objects in the Tcl
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
* RCS @(#) $Id: AssocData.java,v 1.2 1999/05/11 23:10:03 dejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This interface is the API for registering named data objects in the
	/// Tcl interpreter.
	/// </summary>
	
	public interface AssocData
		{
			
			void  disposeAssocData(Interp interp); // The interpreter in which this AssocData
			// instance is registered in.
		}
}
