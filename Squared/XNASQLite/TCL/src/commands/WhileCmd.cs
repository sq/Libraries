/*
* WhileCmd.java
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
* RCS @(#) $Id: WhileCmd.java,v 1.1.1.1 1998/10/14 21:09:20 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "while" command in Tcl.</summary>
	
	class WhileCmd : Command
	{
		/// <summary> This procedure is invoked to process the "while" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		/// <exception cref=""> TclException if script causes error.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 3)
			{
				throw new TclNumArgsException(interp, 1, argv, "test command");
			}
			
			string test = argv[1].ToString();
			TclObject command = argv[2];
			
			{
				while (interp.expr.evalBoolean(interp, test))
				{
					try
					{
						interp.eval(command, 0);
					}
					catch (TclException e)
					{
						switch (e.getCompletionCode())
						{
							
							case TCL.CompletionCode.BREAK: 
																goto loop_brk;
							
							
							case TCL.CompletionCode.CONTINUE: 
								continue;
							
							
							case TCL.CompletionCode.ERROR: 
								interp.addErrorInfo("\n    (\"while\" body line " + interp.errorLine + ")");
								throw ;
							
							
							default: 
								throw ;
							
						}
					}
				}
			}
			
loop_brk: ;
			
			
			interp.resetResult();
		return TCL.CompletionCode.RETURN;
    }
	}
}
