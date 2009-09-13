/*
* SplitCmd.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: SplitCmd.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "split" command in Tcl.</summary>
	
	class SplitCmd : Command
	{
		/// <summary> Default characters for splitting up strings.</summary>
		
		private static char[] defSplitChars = new char[]{' ', '\n', '\t', '\r'};
		
		/// <summary> This procedure is invoked to process the "split" Tcl
		/// command. See Tcl user documentation for details.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		/// <exception cref=""> TclException If incorrect number of arguments.
		/// </exception>

    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
		{
			char[] splitChars = null;
			string inString;
			
			if (argv.Length == 2)
			{
				splitChars = defSplitChars;
			}
			else if (argv.Length == 3)
			{
				
				splitChars = argv[2].ToString().ToCharArray();
			}
			else
			{
				throw new TclNumArgsException(interp, 1, argv, "string ?splitChars?");
			}
			
			
			inString = argv[1].ToString();
			int len = inString.Length;
			int num = splitChars.Length;
			
			/*
			* Handle the special case of splitting on every character.
			*/
			
			if (num == 0)
			{
				TclObject list = TclList.newInstance();
				
				list.preserve();
				try
				{
					for (int i = 0; i < len; i++)
					{
						TclList.append(interp, list, TclString.newInstance(inString[i]));
					}
					interp.setResult(list);
				}
				finally
				{
					list.release();
				}
        return TCL.CompletionCode.RETURN;
      }
			
			/*
			* Normal case: split on any of a given set of characters.
			* Discard instances of the split characters.
			*/
			TclObject list2 = TclList.newInstance();
			int elemStart = 0;
			
			list2.preserve();
			try
			{
				int i, j;
				for (i = 0; i < len; i++)
				{
					char c = inString[i];
					for (j = 0; j < num; j++)
					{
						if (c == splitChars[j])
						{
							TclList.append(interp, list2, TclString.newInstance(inString.Substring(elemStart, (i) - (elemStart))));
							elemStart = i + 1;
							break;
						}
					}
				}
				if (i != 0)
				{
					TclList.append(interp, list2, TclString.newInstance(inString.Substring(elemStart)));
				}
				interp.setResult(list2);
			}
			finally
			{
				list2.release();
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
