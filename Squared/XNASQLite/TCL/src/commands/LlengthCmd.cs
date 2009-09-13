/*
* LlengthCmd.java
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
* RCS @(#) $Id: LlengthCmd.java,v 1.1.1.1 1998/10/14 21:09:21 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "llength" command in Tcl.</summary>
	
	class LlengthCmd : Command
	{
		/// <summary> See Tcl user documentation for details.</summary>
		/// <exception cref=""> TclException If incorrect number of arguments.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 2)
			{
				throw new TclNumArgsException(interp, 1, argv, "list");
			}
			interp.setResult(TclInteger.newInstance(TclList.getLength(interp, argv[1])));
      return TCL.CompletionCode.RETURN;
    }
	}
}
