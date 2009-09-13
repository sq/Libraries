/*
* TimeCmd.java
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TimeCmd.java,v 1.1.1.1 1998/10/14 21:09:18 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "time" command in Tcl.</summary>
	
	class TimeCmd : Command
	{
		/// <summary> See Tcl user documentation for details.</summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if ((argv.Length < 2) || (argv.Length > 3))
			{
				throw new TclNumArgsException(interp, 1, argv, "script ?count?");
			}
			
			int count;
			if (argv.Length == 2)
			{
				count = 1;
			}
			else
			{
				count = TclInteger.get(interp, argv[2]);
			}
			
			long startTime = System.DateTime.Now.Ticks;
			for (int i = 0; i < count; i++)
			{
				interp.eval(argv[1], 0);
			}
			long endTime = System.DateTime.Now.Ticks;
			long uSecs = (((endTime - startTime) / 10) / count);
			if (uSecs == 1)
			{
				interp.setResult(TclString.newInstance("1 microsecond per iteration"));
			}
			else
			{
				interp.setResult(TclString.newInstance(uSecs + " microseconds per iteration"));
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
