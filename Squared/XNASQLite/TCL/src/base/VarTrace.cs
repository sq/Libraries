/*
* VarTrace.java --
*
*	Interface for creating variable traces.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: VarTrace.java,v 1.1.1.1 1998/10/14 21:09:14 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This interface is used to make variable traces. To make a variable
	* trace, write a class that implements the VarTrace and call
	* Interp.traceVar with an instance of that class.
	* 
	*/
	
	public interface VarTrace
		{
			
			void  traceProc(Interp interp, string part1, string part2, TCL.VarFlag flags); // The traceProc may throw a TclException
			// to indicate an error during the trace.
		} // end VarTrace
}
