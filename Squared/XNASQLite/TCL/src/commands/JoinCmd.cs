/*
* JoinCmd.java
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
* RCS @(#) $Id: JoinCmd.java,v 1.1.1.1 1998/10/14 21:09:18 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "join" command in Tcl.</summary>
	class JoinCmd : Command
	{
		
		/// <summary> See Tcl user documentation for details.</summary>
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			string sep = null;
			
			if (argv.Length == 2)
			{
				sep = null;
			}
			else if (argv.Length == 3)
			{
				
				sep = argv[2].ToString();
			}
			else
			{
				throw new TclNumArgsException(interp, 1, argv, "list ?joinString?");
			}
			TclObject list = argv[1];
			int size = TclList.getLength(interp, list);
			
			if (size == 0)
			{
				interp.resetResult();
        return TCL.CompletionCode.RETURN;
			}
			
			
			System.Text.StringBuilder sbuf = new System.Text.StringBuilder(TclList.index(interp, list, 0).ToString());
			
			for (int i = 1; i < size; i++)
			{
				if ((System.Object) sep == null)
				{
					sbuf.Append(' ');
				}
				else
				{
					sbuf.Append(sep);
				}
				
				sbuf.Append(TclList.index(interp, list, i).ToString());
			}
			interp.setResult(sbuf.ToString());
      return TCL.CompletionCode.RETURN;
    }
	}
}
