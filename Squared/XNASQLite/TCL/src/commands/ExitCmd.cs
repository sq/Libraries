/*
* ExitCmd.java
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
* RCS @(#) $Id: ExitCmd.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "exit" command in Tcl.</summary>
	class ExitCmd : Command
	{
		
		/// <summary> See Tcl user documentation for details.</summary>
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			int code;
			
			if (argv.Length > 2)
			{
				throw new TclNumArgsException(interp, 1, argv, "?returnCode?");
			}
			if (argv.Length == 2)
			{
				code = TclInteger.get(interp, argv[1]);
			}
			else
			{
				code = 0;
			}
			System.Environment.Exit(code);
      return TCL.CompletionCode.RETURN;
    }
	}
}
