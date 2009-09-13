/*
* GlobalCmd.java
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
* RCS @(#) $Id: GlobalCmd.java,v 1.2 1999/08/03 02:55:41 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "global" command in Tcl.</summary>
	
	class GlobalCmd : Command
	{
		/// <summary> See Tcl user documentation for details.</summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "varName ?varName ...?");
			}
			
			//  If we are not executing inside a Tcl procedure, just return.
			
			if ((interp.varFrame == null) || !interp.varFrame.isProcCallFrame)
			{
        return TCL.CompletionCode.RETURN;
      }
			
			for (int i = 1; i < objv.Length; i++)
			{
				
				// Make a local variable linked to its counterpart in the global ::
				// namespace.
				
				TclObject obj = objv[i];
				
				string varName = obj.ToString();
				
				// The variable name might have a scope qualifier, but the name for
				// the local "link" variable must be the simple name at the tail.
				
				int tail = varName.Length;
				
				tail -= 1; // tail should start on the last index of the string
				
				while ((tail > 0) && ((varName[tail] != ':') || (varName[tail - 1] != ':')))
				{
					tail--;
				}
				if (varName[tail] == ':')
				{
					tail++;
				}
				
				// Link to the variable "varName" in the global :: namespace.
				
				Var.makeUpvar(interp, null, varName, null, TCL.VarFlag.GLOBAL_ONLY, varName.Substring(tail), 0);
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
