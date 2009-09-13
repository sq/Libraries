/*
* RenameCmd.java
*
* Copyright (c) 1999 Mo DeJong.
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: RenameCmd.java,v 1.2 1999/08/03 03:07:54 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "rename" command in Tcl.</summary>
	
	class RenameCmd : Command
	{
		/// <summary>----------------------------------------------------------------------
		/// 
		/// Tcl_RenameObjCmd -> RenameCmd.cmdProc
		/// 
		/// This procedure is invoked to process the "rename" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// Results:
		/// A standard Tcl object result.
		/// 
		/// Side effects:
		/// See the user documentation.
		/// 
		/// ----------------------------------------------------------------------
		/// </summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			string oldName, newName;
			
			if (objv.Length != 3)
			{
				throw new TclNumArgsException(interp, 1, objv, "oldName newName");
			}
			
			
			oldName = objv[1].ToString();
			
			newName = objv[2].ToString();
			
			interp.renameCommand(oldName, newName);
      return TCL.CompletionCode.RETURN;
		}
	}
}
