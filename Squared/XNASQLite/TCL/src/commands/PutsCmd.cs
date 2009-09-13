/*
* PutsCmd.java
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
* RCS @(#) $Id: PutsCmd.java,v 1.6 2002/01/21 06:34:26 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "puts" command in Tcl.</summary>
	
	class PutsCmd : Command
	{
		/// <summary> Prints the given string to a channel. See Tcl user
		/// documentation for details.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			
			Channel chan; // The channel being operated on this method
			string channelId; // String containing the key to chanTable
			string arg; // Argv[i] converted to a string
			int i = 1; // Index to the next arg in argv
			bool newline = true;
			// Indicates to print a newline in result
			
			
			if ((argv.Length >= 2) && (argv[1].ToString().Equals("-nonewline")))
			{
				newline = false;
				i++;
			}
			if ((i < argv.Length - 3) || (i >= argv.Length))
			{
				throw new TclNumArgsException(interp, 1, argv, "?-nonewline? ?channelId? string");
			}
			
			// The code below provides backwards compatibility with an old
			// form of the command that is no longer recommended or documented.
			
			if (i == (argv.Length - 3))
			{
				
				arg = argv[i + 2].ToString();
				if (!arg.Equals("nonewline"))
				{
					throw new TclException(interp, "bad argument \"" + arg + "\": should be \"nonewline\"");
				}
				newline = false;
			}
			
			if (i == (argv.Length - 1))
			{
				channelId = "stdout";
			}
			else
			{
				
				channelId = argv[i].ToString();
				i++;
			}
			
			if (i != (argv.Length - 1))
			{
				throw new TclNumArgsException(interp, 1, argv, "?-nonewline? ?channelId? string");
			}
			
			chan = TclIO.getChannel(interp, channelId);
			if (chan == null)
			{
				throw new TclException(interp, "can not find channel named \"" + channelId + "\"");
			}
			
			try
			{
				if (newline)
				{
					chan.write(interp, argv[i]);
					chan.write(interp, "\n");
				}
				else
				{
					chan.write(interp, argv[i]);
				}
			}
			catch (System.IO.IOException e)
			{
				throw new TclRuntimeError("PutsCmd.cmdProc() Error: IOException when putting " + chan.ChanName);
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
