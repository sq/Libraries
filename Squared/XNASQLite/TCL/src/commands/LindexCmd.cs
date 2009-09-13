/*
* LindexCmd.java - -
*
*	Implements the built-in "lindex" Tcl command.
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
* RCS @(#) $Id: LindexCmd.java,v 1.2 2000/03/17 23:31:30 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "lindex" command in Tcl.
	*/
	
	class LindexCmd : Command
	{
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 3)
			{
				throw new TclNumArgsException(interp, 1, argv, "list index");
			}
			
			int size = TclList.getLength(interp, argv[1]);
			int index = Util.getIntForIndex(interp, argv[2], size - 1);
			TclObject element = TclList.index(interp, argv[1], index);
			
			if (element != null)
			{
				interp.setResult(element);
			}
			else
			{
				interp.resetResult();
			}
      return TCL.CompletionCode.RETURN;
    }
	} // end 
}
