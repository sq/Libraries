/*
* PwdCmd.java
*
*	This file contains the Jacl implementation of the built-in Tcl "pwd"
*	command.
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
* RCS @(#) $Id: PwdCmd.java,v 1.2 1999/05/09 01:12:14 dejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "pwd" command in Tcl.
	*/
	
	class PwdCmd : Command
	{
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 1)
			{
				throw new TclNumArgsException(interp, 1, argv, null);
			}
			
			// Get the name of the working dir.
			
			string dirName = interp.getWorkingDir().ToString();
			
			// Java File Object methods use backslashes on Windows.
			// Convert them to forward slashes before returning the dirName to Tcl.
			
			if (JACL.PLATFORM == JACL.PLATFORM_WINDOWS)
			{
				dirName = dirName.Replace('\\', '/');
			}
			
			interp.setResult(dirName);
      return TCL.CompletionCode.RETURN;
    }
	} // end PwdCmd class
}
