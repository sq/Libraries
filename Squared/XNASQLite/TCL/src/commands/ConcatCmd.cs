/*
* ConcatCmd.java
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
* RCS @(#) $Id: ConcatCmd.java,v 1.1.1.1 1998/10/14 21:09:18 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "concat" command in Tcl.</summary>
	class ConcatCmd : Command
	{
		
		/// <summary> See Tcl user documentation for details.</summary>
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			interp.setResult(Util.concat(1, argv.Length, argv));
      return TCL.CompletionCode.RETURN;
    }
	}
}
