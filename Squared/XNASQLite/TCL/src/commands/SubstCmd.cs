/*
* SubstCmd.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: SubstCmd.java,v 1.3 2003/01/09 02:15:39 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "subst" command in Tcl.</summary>
	
	class SubstCmd : Command
	{
		private static readonly string[] validCmds = new string[]{"-nobackslashes", "-nocommands", "-novariables"};
		
		internal const int OPT_NOBACKSLASHES = 0;
		internal const int OPT_NOCOMMANDS = 1;
		internal const int OPT_NOVARS = 2;
		
		/// <summary> This procedure is invoked to process the "subst" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		/// <exception cref=""> TclException if wrong # of args or invalid argument(s).
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			int currentObjIndex, len, i;
			int objc = argv.Length - 1;
			bool doBackslashes = true;
			bool doCmds = true;
			bool doVars = true;
			System.Text.StringBuilder result = new System.Text.StringBuilder();
			string s;
			char c;
			
			for (currentObjIndex = 1; currentObjIndex < objc; currentObjIndex++)
			{
				
				if (!argv[currentObjIndex].ToString().StartsWith("-"))
				{
					break;
				}
				int opt = TclIndex.get(interp, argv[currentObjIndex], validCmds, "switch", 0);
				switch (opt)
				{
					
					case OPT_NOBACKSLASHES: 
						doBackslashes = false;
						break;
					
					case OPT_NOCOMMANDS: 
						doCmds = false;
						break;
					
					case OPT_NOVARS: 
						doVars = false;
						break;
					
					default: 
						throw new TclException(interp, "SubstCmd.cmdProc: bad option " + opt + " index to cmds");
					
				}
			}
			if (currentObjIndex != objc)
			{
				throw new TclNumArgsException(interp, currentObjIndex, argv, "?-nobackslashes? ?-nocommands? ?-novariables? string");
			}
			
			/*
			* Scan through the string one character at a time, performing
			* command, variable, and backslash substitutions.
			*/
			
			
			s = argv[currentObjIndex].ToString();
			len = s.Length;
			i = 0;
			while (i < len)
			{
				c = s[i];
				
				if ((c == '[') && doCmds)
				{
					ParseResult res;
					try
					{
						interp.evalFlags = Parser.TCL_BRACKET_TERM;
						interp.eval(s.Substring(i + 1, (len) - (i + 1)));
						TclObject interp_result = interp.getResult();
						interp_result.preserve();
						res = new ParseResult(interp_result, i + interp.termOffset);
					}
					catch (TclException e)
					{
						i = e.errIndex + 1;
						throw ;
					}
					i = res.nextIndex + 2;
					
					result.Append(res.value.ToString());
					res.release();
				}
				else if (c == '\r')
				{
					/*
					* (ToDo) may not be portable on Mac
					*/
					
					i++;
				}
				else if ((c == '$') && doVars)
				{
					ParseResult vres = Parser.parseVar(interp, s.Substring(i, (len) - (i)));
					i += vres.nextIndex;
					
					result.Append(vres.value.ToString());
					vres.release();
				}
				else if ((c == '\\') && doBackslashes)
				{
					BackSlashResult bs = tcl.lang.Interp.backslash(s, i, len);
					i = bs.nextIndex;
					if (bs.isWordSep)
					{
						break;
					}
					else
					{
						result.Append(bs.c);
					}
				}
				else
				{
					result.Append(c);
					i++;
				}
			}
			
			interp.setResult(result.ToString());
      return TCL.CompletionCode.RETURN;
    }
	}
}
