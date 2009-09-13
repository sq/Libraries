/*
* ListCmd.java
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
* RCS @(#) $Id: ListCmd.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "list" command in Tcl.</summary>
	class ListCmd : Command
	{
		
		/// <summary> See Tcl user documentation for details.</summary>
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			TclObject list = TclList.newInstance();
			
			list.preserve();
			try
			{
				for (int i = 1; i < argv.Length; i++)
				{
					TclList.append(interp, list, argv[i]);
				}
				interp.setResult(list);
			}
			finally
			{
				list.release();
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
