/*
* BreakCmd.java
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
* RCS @(#) $Id: BreakCmd.java,v 1.1.1.1 1998/10/14 21:09:18 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "break" command in Tcl.</summary>
	
	class BreakCmd : Command
	{
		/// <summary> This procedure is invoked to process the "break" Tcl command.
		/// See the user documentation for details on what it does.
		/// </summary>
		/// <exception cref=""> TclException is always thrown.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 1)
			{
				throw new TclNumArgsException(interp, 1, argv, null);
			}
			throw new TclException(interp, null, TCL.CompletionCode.BREAK);
		}
	}
}
