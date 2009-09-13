/*
* RegexpCmd.java --
*
* 	This file contains the Jacl implementation of the built-in Tcl
*	"regexp" command. 
*
* Copyright (c) 1997-1999 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: RegexpCmd.java,v 1.3 2000/02/23 22:07:23 mo Exp $
*/
using System;
using Regexp = sunlabs.brazil.util.regexp.Regexp;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "regexp" command in Tcl.</summary>
	
	class RegexpCmd : Command
	{
		
		private static readonly string[] validOpts = new string[]{"-indices", "-nocase", "--"};
		private const int OPT_INDICES = 0;
		private const int OPT_NOCASE = 1;
		private const int OPT_LAST = 2;
		internal static void  init(Interp interp)
		// Current interpreter. 
		{
			interp.createCommand("regexp", new tcl.lang.RegexpCmd());
			interp.createCommand("regsub", new tcl.lang.RegsubCmd());
		}
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			bool nocase = false;
			bool indices = false;
			
			try
			{
				int i = 1;
				
				while (argv[i].ToString().StartsWith("-"))
				{
					int index = TclIndex.get(interp, argv[i], validOpts, "switch", 0);
					i++;
					switch (index)
					{
						
						case OPT_INDICES:  {
								indices = true;
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


        TclObject exp = TclString.newInstance( argv[i++].ToString().Replace( "\\d", "[0-9]" ) );
				
				string inString = argv[i++].ToString();
				
				int matches = argv.Length - i;
				
				Regexp r = TclRegexp.compile(interp, exp, nocase);
				
				int[] args = new int[matches * 2];
				bool matched = r.match(inString, args);
				if (matched)
				{
					for (int match = 0; i < argv.Length; i++)
					{
						TclObject obj;
						
						int start = args[match++];
						int end = args[match++];
						if (indices)
						{
							if (end >= 0)
							{
								end--;
							}
							obj = TclList.newInstance();
							TclList.append(interp, obj, TclInteger.newInstance(start));
							TclList.append(interp, obj, TclInteger.newInstance(end));
						}
						else
						{
							string range = (start >= 0)?inString.Substring(start, (end) - (start)):"";
							obj = TclString.newInstance(range);
						}
						try
						{
							
							interp.setVar(argv[i].ToString(), obj, 0);
						}
						catch (TclException e)
						{
							
							throw new TclException(interp, "couldn't set variable \"" + argv[i] + "\"");
						}
					}
				}
				interp.setResult(matched);
			}
			catch (System.IndexOutOfRangeException e)
			{
				throw new TclNumArgsException(interp, 1, argv, "?switches? exp string ?matchVar? ?subMatchVar subMatchVar ...?");
			}
      return TCL.CompletionCode.RETURN;
    }
	} // end RegexpCmd
}
