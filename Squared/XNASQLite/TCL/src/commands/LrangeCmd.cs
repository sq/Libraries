/*
* LrangeCmd.java
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
* RCS @(#) $Id: LrangeCmd.java,v 1.2 2000/03/17 23:31:30 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "lrange" command in Tcl.</summary>
	
	class LrangeCmd : Command
	{
		/// <summary> See Tcl user documentation for details.</summary>
		/// <exception cref=""> TclException If incorrect number of arguments.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 4)
			{
				throw new TclNumArgsException(interp, 1, argv, "list first last");
			}
			
			int size = TclList.getLength(interp, argv[1]);
			int first;
			int last;
			
			first = Util.getIntForIndex(interp, argv[2], size - 1);
			last = Util.getIntForIndex(interp, argv[3], size - 1);
			
			if (last < 0)
			{
				interp.resetResult();
        return TCL.CompletionCode.RETURN;
			}
			if (first >= size)
			{
				interp.resetResult();
        return TCL.CompletionCode.RETURN;
			}
			if (first <= 0 && last >= size)
			{
				interp.setResult(argv[1]);
        return TCL.CompletionCode.RETURN;
			}
			
			if (first < 0)
			{
				first = 0;
			}
			if (first >= size)
			{
				first = size - 1;
			}
			if (last < 0)
			{
				last = 0;
			}
			if (last >= size)
			{
				last = size - 1;
			}
			if (first > last)
			{
				interp.resetResult();
        return TCL.CompletionCode.RETURN;
			}
			
			TclObject list = TclList.newInstance();
			
			list.preserve();
			try
			{
				for (int i = first; i <= last; i++)
				{
					TclList.append(interp, list, TclList.index(interp, argv[1], i));
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
