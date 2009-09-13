/*
* LappendCmd.java
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
* Copyright (c) 1998-1999 by Scriptics Corporation.
* Copyright (c) 1999 Mo DeJong.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: LappendCmd.java,v 1.3 2003/01/09 02:15:39 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "lappend" command in Tcl.</summary>
	class LappendCmd : Command
	{
		/// <summary> 
		/// Tcl_LappendObjCmd -> LappendCmd.cmdProc
		/// 
		/// This procedure is invoked to process the "lappend" Tcl command.
		/// See the user documentation for details on what it does.
		/// </summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			TclObject varValue, newValue = null;
      int i;//int numElems, i, j;
			bool createdNewObj, createVar;
			
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "varName ?value value ...?");
			}
			if (objv.Length == 2)
			{
				try
				{
					newValue = interp.getVar(objv[1], 0);
				}
				catch (TclException e)
				{
					// The variable doesn't exist yet. Just create it with an empty
					// initial value.
					varValue = TclList.newInstance();
					
					try
					{
						newValue = interp.setVar(objv[1], varValue, 0);
					}
					finally
					{
						if (newValue == null)
							varValue.release(); // free unneeded object
					}
					
					interp.resetResult();
          return TCL.CompletionCode.RETURN;
				}
			}
			else
			{
				// We have arguments to append. We used to call Tcl_SetVar2 to
				// append each argument one at a time to ensure that traces were run
				// for each append step. We now append the arguments all at once
				// because it's faster. Note that a read trace and a write trace for
				// the variable will now each only be called once. Also, if the
				// variable's old value is unshared we modify it directly, otherwise
				// we create a new copy to modify: this is "copy on write".
				
				createdNewObj = false;
				createVar = true;
				
				try
				{
					varValue = interp.getVar(objv[1], 0);
				}
				catch (TclException e)
				{
					// We couldn't read the old value: either the var doesn't yet
					// exist or it's an array element. If it's new, we will try to
					// create it with Tcl_ObjSetVar2 below.
					
					// FIXME : not sure we even need this parse for anything!
					// If we do not need to parse could we at least speed it up a bit
					
					string varName;
					int nameBytes;
					
					
					varName = objv[1].ToString();
					nameBytes = varName.Length; // Number of Unicode chars in string
					
					for (i = 0; i < nameBytes; i++)
					{
						if (varName[i] == '(')
						{
							i = nameBytes - 1;
							if (varName[i] == ')')
							{
								// last char is ')' => array ref
								createVar = false;
							}
							break;
						}
					}
					varValue = TclList.newInstance();
					createdNewObj = true;
				}
				
				// We only take this branch when the catch branch was not run
				if (createdNewObj == false && varValue.Shared)
				{
					varValue = varValue.duplicate();
					createdNewObj = true;
				}
				
				// Insert the new elements at the end of the list.
				
				for (i = 2; i < objv.Length; i++)
				{
					TclList.append(interp, varValue, objv[i]);
				}
				
				// No need to call varValue.invalidateStringRep() since it
				// is called during the TclList.append operation.
				
				// Now store the list object back into the variable. If there is an
				// error setting the new value, decrement its ref count if it
				// was new and we didn't create the variable.
				
				try
				{
					
					newValue = interp.setVar(objv[1].ToString(), varValue, 0);
				}
				catch (TclException e)
				{
					if (createdNewObj && !createVar)
					{
						varValue.release(); // free unneeded obj
					}
					throw ;
				}
			}
			
			// Set the interpreter's object result to refer to the variable's value
			// object.
			
			interp.setResult(newValue);
      return TCL.CompletionCode.RETURN;
		}
	}
}
