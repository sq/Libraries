/*
* VwaitCmd.java --
*
*	This file implements the Tcl "vwait" command.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: VwaitCmd.java,v 1.2 1999/08/03 03:22:47 mo Exp $
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "vwait" command in Tcl.
	*/
	
	class VwaitCmd : Command
	{
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 2)
			{
				throw new TclNumArgsException(interp, 1, argv, "name");
			}
			
			VwaitTrace trace = new VwaitTrace();
			Var.traceVar(interp, argv[1], TCL.VarFlag.GLOBAL_ONLY | TCL.VarFlag.TRACE_WRITES | TCL.VarFlag.TRACE_UNSETS, trace);
			
			int foundEvent = 1;
			while (!trace.done && (foundEvent != 0))
			{
				foundEvent = interp.getNotifier().doOneEvent(TCL.ALL_EVENTS);
			}
			
			Var.untraceVar(interp, argv[1], TCL.VarFlag.GLOBAL_ONLY | TCL.VarFlag.TRACE_WRITES | TCL.VarFlag.TRACE_UNSETS, trace);
			
			// Clear out the interpreter's result, since it may have been set
			// by event handlers.
			
			interp.resetResult();
			
			if (foundEvent == 0)
			{
				
				throw new TclException(interp, "can't wait for variable \"" + argv[1] + "\":  would wait forever");
			}
      return TCL.CompletionCode.RETURN;
    }
	} // end VwaitCmd
	
	class VwaitTrace : VarTrace
	{
		
		/*
		* TraceCmd.cmdProc continuously watches this variable across calls to
		* doOneEvent(). It returns immediately when done is set to true.
		*/
		
		internal bool done = false;
		
		public  void  traceProc(Interp interp, string part1, string part2, TCL.VarFlag flags)
		// Mode flags: Should only be TCL.VarFlag.TRACE_WRITES.
		{
			done = true;
		}
	} // end VwaitTrace
}
