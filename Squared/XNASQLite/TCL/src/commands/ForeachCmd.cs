/*
* ForeachCmd.java
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
* RCS @(#) $Id: ForeachCmd.java,v 1.4 1999/08/07 06:44:04 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "Foreach" command in Tcl.</summary>
	
	class ForeachCmd : Command
	{
		/// <summary> Tcl_ForeachObjCmd -> ForeachCmd.cmdProc
		/// 
		/// This procedure is invoked to process the "foreach" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="objv">command arguments.
		/// </param>
		/// <exception cref=""> TclException if script causes error.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			if (objv.Length < 4 || (objv.Length % 2) != 0)
			{
				throw new TclNumArgsException(interp, 1, objv, "varList list ?varList list ...? command");
			}
			
			// foreach {n1 n2} {1 2 3 4} {n3} {1 2} {puts $n1-$n2-$n3}
			//	name[0] = {n1 n2}	value[0] = {1 2 3 4}
			//	name[1] = {n3}		value[0] = {1 2}
			
			TclObject[] name = new TclObject[(objv.Length - 2) / 2];
			TclObject[] value = new TclObject[(objv.Length - 2) / 2];
			
			int c, i, j, base_;
			int maxIter = 0;
			TclObject command = objv[objv.Length - 1];
			bool done = false;
			
			for (i = 0; i < objv.Length - 2; i += 2)
			{
				int x = i / 2;
				name[x] = objv[i + 1];
				value[x] = objv[i + 2];
				
				int nSize = TclList.getLength(interp, name[x]);
				int vSize = TclList.getLength(interp, value[x]);
				
				if (nSize == 0)
				{
					throw new TclException(interp, "foreach varlist is empty");
				}
				
				int iter = (vSize + nSize - 1) / nSize;
				if (maxIter < iter)
				{
					maxIter = iter;
				}
			}
			
			for (c = 0; !done && c < maxIter; c++)
			{
				// Set up the variables
				
				for (i = 0; i < objv.Length - 2; i += 2)
				{
					int x = i / 2;
					int nSize = TclList.getLength(interp, name[x]);
					base_ = nSize * c;
					for (j = 0; j < nSize; j++)
					{
						// Test and see if the name variable is an array.
						
						
						Var[] result = Var.lookupVar(interp, name[x].ToString(), null, 0, null, false, false);
						Var var = null;
						
						if (result != null)
						{
							if (result[1] != null)
							{
								var = result[1];
							}
							else
							{
								var = result[0];
							}
						}
						
						try
						{
							if (base_ + j >= TclList.getLength(interp, value[x]))
							{
								interp.setVar(TclList.index(interp, name[x], j), TclString.newInstance(""), 0);
							}
							else
							{
								interp.setVar(TclList.index(interp, name[x], j), TclList.index(interp, value[x], base_ + j), 0);
							}
						}
						catch (TclException e)
						{
							
							throw new TclException(interp, "couldn't set loop variable: \"" + TclList.index(interp, name[x], j) + "\"");
						}
					}
				}
				
				// Execute the script
				
				try
				{
					interp.eval(command, 0);
				}
				catch (TclException e)
				{
					switch (e.getCompletionCode())
					{
						
						case TCL.CompletionCode.BREAK: 
							done = true;
							break;
						
						
						case TCL.CompletionCode.CONTINUE: 
							continue;
						
						
						case TCL.CompletionCode.ERROR: 
							interp.addErrorInfo("\n    (\"foreach\" body line " + interp.errorLine + ")");
							throw ;
						
						
						default: 
							throw ;
						
					}
				}
			}
			
			interp.resetResult();
      return TCL.CompletionCode.RETURN;
    }
	}
}
