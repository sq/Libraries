/*
* UpvarCmd.java
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
* RCS @(#) $Id: UpvarCmd.java,v 1.3 1999/07/12 02:38:53 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "upvar" command in Tcl.</summary>
	
	class UpvarCmd : Command
	{
		/// <summary> Tcl_UpvarObjCmd -> UpvarCmd.cmdProc
		/// 
		/// This procedure is invoked to process the "upvar" Tcl command.
		/// See the user documentation for details on what it does.
		/// </summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			CallFrame frame;
			string frameSpec, otherVarName, myVarName;
			int p;
			int objc = objv.Length, objv_index;
			int result;
			
			if (objv.Length < 3)
			{
				throw new TclNumArgsException(interp, 1, objv, "?level? otherVar localVar ?otherVar localVar ...?");
			}
			
			// Find the call frame containing each of the "other variables" to be
			// linked to. 
			
			
			frameSpec = objv[1].ToString();
			// Java does not support passing a reference by refernece so use an array
			CallFrame[] frameArr = new CallFrame[1];
			result = CallFrame.getFrame(interp, frameSpec, frameArr);
			frame = frameArr[0];
			objc -= (result + 1);
			if ((objc & 1) != 0)
			{
				throw new TclNumArgsException(interp, 1, objv, "?level? otherVar localVar ?otherVar localVar ...?");
			}
			objv_index = result + 1;
			
			
			// Iterate over each (other variable, local variable) pair.
			// Divide the other variable name into two parts, then call
			// MakeUpvar to do all the work of linking it to the local variable.
			
			for (; objc > 0; objc -= 2, objv_index += 2)
			{
				
				myVarName = objv[objv_index + 1].ToString();
				
				otherVarName = objv[objv_index].ToString();
				
				int otherLength = otherVarName.Length;
				p = otherVarName.IndexOf((System.Char) '(');
				if ((p != - 1) && (otherVarName[otherLength - 1] == ')'))
				{
					// This is an array variable name
					Var.makeUpvar(interp, frame, otherVarName.Substring(0, (p) - (0)), otherVarName.Substring(p + 1, (otherLength - 1) - (p + 1)), 0, myVarName, 0);
				}
				else
				{
					// This is a scalar variable name
					Var.makeUpvar(interp, frame, otherVarName, null, 0, myVarName, 0);
				}
			}
			interp.resetResult();
      return TCL.CompletionCode.RETURN;
    }
	}
}
