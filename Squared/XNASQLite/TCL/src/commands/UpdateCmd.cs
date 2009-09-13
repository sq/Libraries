/*
* UpdateCmd.java --
*
*	Implements the "update" command.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: UpdateCmd.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "update" command in Tcl.
	*/
	
	class UpdateCmd : Command
	{
		
		/*
		* Valid command options.
		*/
		
		private static readonly string[] validOpts = new string[]{"idletasks"};
		
		internal const int OPT_IDLETASKS = 0;
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			int flags;
			
			if (argv.Length == 1)
			{
				flags = TCL.ALL_EVENTS | TCL.DONT_WAIT;
			}
			else if (argv.Length == 2)
			{
				TclIndex.get(interp, argv[1], validOpts, "option", 0);
				
				/*
				* Since we just have one valid option, if the above call returns
				* without an exception, we've got "idletasks" (or abreviations).
				*/
				
				flags = TCL.IDLE_EVENTS | TCL.DONT_WAIT;
			}
			else
			{
				throw new TclNumArgsException(interp, 1, argv, "?idletasks?");
			}
			
			while (interp.getNotifier().doOneEvent(flags) != 0)
			{
				/* Empty loop body */
			}
			
			/*
			* Must clear the interpreter's result because event handlers could
			* have executed commands.
			*/
			
			interp.resetResult();
      return TCL.CompletionCode.RETURN;
    }
	} // end UpdateCmd
}
