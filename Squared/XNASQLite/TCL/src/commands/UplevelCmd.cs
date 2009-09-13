/*
* UplevelCmd.java --
*
*	Implements the "uplevel" command.
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
* RCS @(#) $Id: UplevelCmd.java,v 1.3 1999/07/12 02:38:53 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "uplevel" command in Tcl.
	*/
	
	class UplevelCmd : Command
	{
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			string optLevel;
			int result;
			CallFrame savedVarFrame, frame;
			int objc = objv.Length;
			int objv_index;
			TclObject cmd;
			
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "?level? command ?arg ...?");
			}
			
			// Find the level to use for executing the command.
			
			
			optLevel = objv[1].ToString();
			// Java does not support passing a reference by refernece so use an array
			CallFrame[] frameArr = new CallFrame[1];
			result = CallFrame.getFrame(interp, optLevel, frameArr);
			frame = frameArr[0];
			
			objc -= (result + 1);
			if (objc == 0)
			{
				throw new TclNumArgsException(interp, 1, objv, "?level? command ?arg ...?");
			}
			objv_index = (result + 1);
			
			// Modify the interpreter state to execute in the given frame.
			
			savedVarFrame = interp.varFrame;
			interp.varFrame = frame;
			
			// Execute the residual arguments as a command.
			
			if (objc == 1)
			{
				cmd = objv[objv_index];
			}
			else
			{
				cmd = TclString.newInstance(Util.concat(objv_index, objv.Length - 1, objv));
			}
			cmd.preserve();
			
			try
			{
				interp.eval(cmd, 0);
			}
			catch (TclException e)
			{
				if (e.getCompletionCode() == TCL.CompletionCode.ERROR)
				{
					interp.addErrorInfo("\n    (\"uplevel\" body line " + interp.errorLine + ")");
				}
				throw ;
			}
			finally
			{
				interp.varFrame = savedVarFrame;
				cmd.release();
			}
      return TCL.CompletionCode.RETURN;
    }
	} // end UplevelCmd
}
