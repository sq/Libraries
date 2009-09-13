/*
* ProcCmd.java
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
* RCS @(#) $Id: ProcCmd.java,v 1.2 1999/08/03 03:04:19 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "proc" command in Tcl.</summary>
	
	class ProcCmd : Command
	{
		/// <summary> 
		/// Tcl_ProcObjCmd -> ProcCmd.cmdProc
		/// 
		/// Creates a new Tcl procedure.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="objv">command arguments.
		/// </param>
		/// <exception cref=""> TclException If incorrect number of arguments.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			Procedure proc;
			string fullName, procName;
			NamespaceCmd.Namespace ns, altNs, cxtNs;
			Command cmd;
			System.Text.StringBuilder ds;
			
			if (objv.Length != 4)
			{
				throw new TclNumArgsException(interp, 1, objv, "name args body");
			}
			
			// Determine the namespace where the procedure should reside. Unless
			// the command name includes namespace qualifiers, this will be the
			// current namespace.
			
			
			fullName = objv[1].ToString();
			
			// Java does not support passing an address so we pass
			// an array of size 1 and then assign arr[0] to the value
			NamespaceCmd.Namespace[] nsArr = new NamespaceCmd.Namespace[1];
			NamespaceCmd.Namespace[] altNsArr = new NamespaceCmd.Namespace[1];
			NamespaceCmd.Namespace[] cxtNsArr = new NamespaceCmd.Namespace[1];
			string[] procNameArr = new string[1];
			
			NamespaceCmd.getNamespaceForQualName(interp, fullName, null, 0, nsArr, altNsArr, cxtNsArr, procNameArr);
			
			// Get the values out of the arrays
			ns = nsArr[0];
			altNs = altNsArr[0];
			cxtNs = cxtNsArr[0];
			procName = procNameArr[0];
			
			if (ns == null)
			{
				throw new TclException(interp, "can't create procedure \"" + fullName + "\": unknown namespace");
			}
			if ((System.Object) procName == null)
			{
				throw new TclException(interp, "can't create procedure \"" + fullName + "\": bad procedure name");
			}
			// FIXME : could there be a problem with a command named ":command" ?
			if ((ns != NamespaceCmd.getGlobalNamespace(interp)) && ((System.Object) procName != null) && ((procName.Length > 0) && (procName[0] == ':')))
			{
				throw new TclException(interp, "can't create procedure \"" + procName + "\" in non-global namespace with name starting with \":\"");
			}
			
			//  Create the data structure to represent the procedure.
			
			proc = new Procedure(interp, ns, procName, objv[2], objv[3], interp.ScriptFile, interp.getArgLineNumber(3));
			
			// Now create a command for the procedure. This will initially be in
			// the current namespace unless the procedure's name included namespace
			// qualifiers. To create the new command in the right namespace, we
			// generate a fully qualified name for it.
			
			ds = new System.Text.StringBuilder();
			if (ns != NamespaceCmd.getGlobalNamespace(interp))
			{
				ds.Append(ns.fullName);
				ds.Append("::");
			}
			ds.Append(procName);
			
			interp.createCommand(ds.ToString(), proc);
			
			// Now initialize the new procedure's cmdPtr field. This will be used
			// later when the procedure is called to determine what namespace the
			// procedure will run in. This will be different than the current
			// namespace if the proc was renamed into a different namespace.
			
			// FIXME : we do not handle renaming into another namespace correctly yet!
			//procPtr->cmdPtr = (Command *) cmd;

      return TCL.CompletionCode.RETURN;
		}
	}
}
