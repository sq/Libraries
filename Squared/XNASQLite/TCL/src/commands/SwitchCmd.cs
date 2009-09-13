/*
* SwitchCmd.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: SwitchCmd.java,v 1.2 1999/05/09 01:32:03 dejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "switch" command in Tcl.</summary>
	
	class SwitchCmd : Command
	{
		
		private static readonly string[] validCmds = new string[]{"-exact", "-glob", "-regexp", "--"};
		private const int EXACT = 0;
		private const int GLOB = 1;
		private const int REGEXP = 2;
		private const int LAST = 3;
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			int i, mode, body;
			bool matched;
			string inString;
			TclObject[] switchArgv = null;
			
			mode = EXACT;
			for (i = 1; i < argv.Length; i++)
			{
				
				if (!argv[i].ToString().StartsWith("-"))
				{
					break;
				}
				int opt = TclIndex.get(interp, argv[i], validCmds, "option", 1);
				if (opt == LAST)
				{
					i++;
					break;
				}
				else if (opt > LAST)
				{
					throw new TclException(interp, "SwitchCmd.cmdProc: bad option " + opt + " index to validCmds");
				}
				else
				{
					mode = opt;
				}
			}
			
			if (argv.Length - i < 2)
			{
				throw new TclNumArgsException(interp, 1, argv, "?switches? string pattern body ... ?default body?");
			}
			
			inString = argv[i].ToString();
			i++;
			
			// If all of the pattern/command pairs are lumped into a single
			// argument, split them out again.
			
			if (argv.Length - i == 1)
			{
				switchArgv = TclList.getElements(interp, argv[i]);
				i = 0;
			}
			else
			{
				switchArgv = argv;
			}
			
			for (; i < switchArgv.Length; i += 2)
			{
				if (i == (switchArgv.Length - 1))
				{
					throw new TclException(interp, "extra switch pattern with no body");
				}
				
				// See if the pattern matches the string.
				
				matched = false;
				
				string pattern = switchArgv[i].ToString();
				
				if ((i == switchArgv.Length - 2) && pattern.Equals("default"))
				{
					matched = true;
				}
				else
				{
					switch (mode)
					{
						
						case EXACT: 
							matched = inString.Equals(pattern);
							break;
						
						case GLOB: 
							matched = Util.stringMatch(inString, pattern);
							break;
						
						case REGEXP: 
							matched = Util.regExpMatch(interp, inString, switchArgv[i]);
							break;
						}
				}
				if (!matched)
				{
					continue;
				}
				
				// We've got a match.  Find a body to execute, skipping bodies
				// that are "-".
				
				for (body = i + 1; ; body += 2)
				{
					if (body >= switchArgv.Length)
					{
						
						throw new TclException(interp, "no body specified for pattern \"" + switchArgv[i] + "\"");
					}
					
					if (!switchArgv[body].ToString().Equals("-"))
					{
						break;
					}
				}
				
				try
				{
					interp.eval(switchArgv[body], 0);
          return TCL.CompletionCode.RETURN;
        }
				catch (TclException e)
				{
					if (e.getCompletionCode() == TCL.CompletionCode.ERROR)
					{
						
						interp.addErrorInfo("\n    (\"" + switchArgv[i] + "\" arm line " + interp.errorLine + ")");
					}
					throw ;
				}
			}
			
			// Nothing matched:  return nothing.
      return TCL.CompletionCode.RETURN;
    }
	} // end SwitchCmd
}
