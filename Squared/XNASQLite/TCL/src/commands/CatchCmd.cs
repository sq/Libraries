/*
* CatchCmd.java
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
* RCS @(#) $Id: CatchCmd.java,v 1.2 2000/08/20 06:08:42 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "catch" command in Tcl.</summary>
	
	class CatchCmd : Command
	{
		/// <summary> This procedure is invoked to process the "catch" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		/// <exception cref=""> TclException if wrong number of arguments.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 2 && argv.Length != 3)
			{
				throw new TclNumArgsException(interp, 1, argv, "command ?varName?");
			}
			
			TclObject result;
			TCL.CompletionCode code = TCL.CompletionCode.OK;
			
			try
			{
				interp.eval(argv[1], 0);
			}
			catch (TclException e)
			{
				code = e.getCompletionCode();
			}

      result = interp.getResult();

      if ( argv.Length == 3 )
			{
				try
				{
					interp.setVar(argv[2], result, 0);
				}
				catch (TclException e)
				{
					throw new TclException(interp, "couldn't save command result in variable");
				}
			}
			
			interp.resetResult();
			interp.setResult(TclInteger.newInstance((int)code));
      return TCL.CompletionCode.RETURN;
    }
	}
}
