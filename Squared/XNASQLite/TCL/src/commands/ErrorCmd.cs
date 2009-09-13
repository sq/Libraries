/*
* ErrorCmd.java --
*
*	Implements the "error" command.
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
* RCS @(#) $Id: ErrorCmd.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "error" command in Tcl.
	*/
	
	class ErrorCmd : Command
	{
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length < 2 || argv.Length > 4)
			{
				throw new TclNumArgsException(interp, 1, argv, "message ?errorInfo? ?errorCode?");
			}
			
			if (argv.Length >= 3)
			{
				
				string errorInfo = argv[2].ToString();
				
				if (!errorInfo.Equals(""))
				{
					interp.addErrorInfo(errorInfo);
					interp.errAlreadyLogged = true;
				}
			}
			
			if (argv.Length == 4)
			{
				interp.setErrorCode(argv[3]);
			}
			
			interp.setResult(argv[1]);
			throw new TclException(TCL.CompletionCode.ERROR);
		}
	} // end ErrorCmd
}
