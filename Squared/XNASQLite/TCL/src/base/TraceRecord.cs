/*
* TraceRecord.java --
*
*	This class is used internally by CallFrame to store one
*	variable trace.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TraceRecord.java,v 1.2 1999/07/28 03:27:36 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class is used internally by CallFrame to store one variable
	/// trace.
	/// </summary>
	
	class TraceRecord
	{
		
		/// <summary> Stores info about the conditions under which this trace should be
		/// triggered. Should be a combination of TCL.VarFlag.TRACE_READS, TCL.VarFlag.TRACE_WRITES
		/// or TCL.VarFlag.TRACE_UNSETS.
		/// </summary>
		
		internal TCL.VarFlag flags;
		
		/// <summary> Stores the trace procedure to invoke when a trace is fired.</summary>
		
		internal VarTrace trace;
	} // end TraceRecord
}
