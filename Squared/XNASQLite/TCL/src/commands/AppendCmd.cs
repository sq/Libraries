/*
* AppendCmd.java --
*
*	Implements the built-in "append" Tcl command.
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
* RCS @(#) $Id: AppendCmd.java,v 1.2 1999/07/28 01:59:49 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "append" command in Tcl.
	*/
	
	class AppendCmd : Command
	{
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			TclObject varValue = null;
			
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "varName ?value value ...?");
			}
			else if (objv.Length == 2)
			{
        interp.resetResult(); 
        interp.setResult( interp.getVar( objv[1], 0 ) );
			}
			else
			{
				for (int i = 2; i < objv.Length; i++)
				{
					varValue = interp.setVar(objv[1], objv[i], TCL.VarFlag.APPEND_VALUE);
				}
				
				if (varValue != null)
				{
          interp.resetResult(); 
          interp.setResult( varValue );
				}
				else
				{
					interp.resetResult();
				}
			}
      return TCL.CompletionCode.RETURN;
    }
	} // end AppendCmd
}
