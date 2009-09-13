/*
* RegsubCmd.java
*
* 	This contains the Jacl implementation of the built-in Tcl
*	"regsub" command.
*
* Copyright (c) 1997-1999 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: RegsubCmd.java,v 1.4 2000/02/23 22:07:23 mo Exp $
*/
using System;
using Regexp = sunlabs.brazil.util.regexp.Regexp;
using Regsub = sunlabs.brazil.util.regexp.Regsub;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "regsub" command in Tcl.</summary>
	
	class RegsubCmd : Command
	{
		
		private static readonly string[] validOpts = new string[]{"-all", "-nocase", "--"};
		private const int OPT_ALL = 0;
		private const int OPT_NOCASE = 1;
		private const int OPT_LAST = 2;
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			bool all = false;
			bool nocase = false;
			
			try
			{
				int i = 1;
				
				while (argv[i].ToString().StartsWith("-"))
				{
					int index = TclIndex.get(interp, argv[i], validOpts, "switch", 0);
					i++;
					switch (index)
					{
						
						case OPT_ALL:  {
								all = true;
								break;
							}
						
						case OPT_NOCASE:  {
								nocase = true;
								break;
							}
						
						case OPT_LAST:  {
																goto opts_brk;
							}
						}
				}
				
opts_brk: ;
				
				
				TclObject exp = argv[i++];
				
				string inString = argv[i++].ToString();
				
				string subSpec = argv[i++].ToString();
				
				string varName = argv[i++].ToString();
				if (i != argv.Length)
				{
					throw new System.IndexOutOfRangeException();
				}
				
				Regexp r = TclRegexp.compile(interp, exp, nocase);
				
				int count = 0;
				string result;
				
				if (all == false)
				{
					result = r.sub(inString, subSpec);
					if ((System.Object) result == null)
					{
						result = inString;
					}
					else
					{
						count++;
					}
				}
				else
				{
					System.Text.StringBuilder sb = new System.Text.StringBuilder();
					Regsub s = new Regsub(r, inString);
					while (s.nextMatch())
					{
						count++;
						sb.Append(s.skipped());
						Regexp.applySubspec(s, subSpec, sb);
					}
					sb.Append(s.rest());
					result = sb.ToString();
				}
				
				TclObject obj = TclString.newInstance(result);
				try
				{
					interp.setVar(varName, obj, 0);
				}
				catch (TclException e)
				{
					throw new TclException(interp, "couldn't set variable \"" + varName + "\"");
				}
				interp.setResult(count);
			}
			catch (System.IndexOutOfRangeException e)
			{
				throw new TclNumArgsException(interp, 1, argv, "?switches? exp string subSpec varName");
			}
      return TCL.CompletionCode.RETURN;
    }
	} // end RegsubCmd
}
