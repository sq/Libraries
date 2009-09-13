/*
* LreplaceCmd.java
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
* RCS @(#) $Id: LreplaceCmd.java,v 1.5 2003/01/09 02:15:39 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "lreplace" command in Tcl.</summary>
	
	class LreplaceCmd : Command
	{
		/// <summary> See Tcl user documentation for details.</summary>
		/// <exception cref=""> TclException If incorrect number of arguments.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length < 4)
			{
				throw new TclNumArgsException(interp, 1, argv, "list first last ?element element ...?");
			}
			int size = TclList.getLength(interp, argv[1]);
			int first = Util.getIntForIndex(interp, argv[2], size - 1);
			int last = Util.getIntForIndex(interp, argv[3], size - 1);
			int numToDelete;
			
			if (first < 0)
			{
				first = 0;
			}
			
			// Complain if the user asked for a start element that is greater
			// than the list length. This won't ever trigger for the "end*"
			// case as that will be properly constrained by getIntForIndex
			// because we use size-1 (to allow for replacing the last elem).
			
			if ((first >= size) && (size > 0))
			{
				
				throw new TclException(interp, "list doesn't contain element " + argv[2]);
			}
			if (last >= size)
			{
				last = size - 1;
			}
			if (first <= last)
			{
				numToDelete = (last - first + 1);
			}
			else
			{
				numToDelete = 0;
			}
			
			TclObject list = argv[1];
			bool isDuplicate = false;
			
			// If the list object is unshared we can modify it directly. Otherwise
			// we create a copy to modify: this is "copy on write".
			
			if (list.Shared)
			{
				list = list.duplicate();
				isDuplicate = true;
			}
			
			try
			{
				TclList.replace(interp, list, first, numToDelete, argv, 4, argv.Length - 1);
				interp.setResult(list);
			}
			catch (TclException e)
			{
				if (isDuplicate)
				{
					list.release();
				}
				throw ;
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
