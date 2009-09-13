/*
* ReadCmd.java --
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: ReadCmd.java,v 1.8 2003/03/08 03:42:44 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "read" command in Tcl.</summary>
	
	class ReadCmd : Command
	{
		
		/// <summary> This procedure is invoked to process the "read" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			
			Channel chan; // The channel being operated on this 
			// method 
			int i = 1; // Index to the next arg in argv
			int toRead = 0; // Number of bytes or chars to read from channel
			int charactersRead; // Number of bytes or chars read from channel
			bool readAll = true; // If true read-all else toRead
			bool noNewline = false; // If true, strip the newline if there
			TclObject result;
			
			
			if ((argv.Length != 2) && (argv.Length != 3))
			{
				
				errorWrongNumArgs(interp, argv[0].ToString());
			}
			
			
			if (argv[i].ToString().Equals("-nonewline"))
			{
				noNewline = true;
				i++;
			}
			
			if (i == argv.Length)
			{
				
				errorWrongNumArgs(interp, argv[0].ToString());
			}
			
			
			chan = TclIO.getChannel(interp, argv[i].ToString());
			if (chan == null)
			{
				
				throw new TclException(interp, "can not find channel named \"" + argv[i].ToString() + "\"");
			}
			
			// Consumed channel name. 
			
			i++;
			
			// Compute how many bytes or chars to read, and see whether the final
			// noNewline should be dropped.
			
			if (i < argv.Length)
			{
				
				string arg = argv[i].ToString();
				
				if (System.Char.IsDigit(arg[0]))
				{
					toRead = TclInteger.get(interp, argv[i]);
					readAll = false;
				}
				else if (arg.Equals("nonewline"))
				{
					noNewline = true;
				}
				else
				{
					throw new TclException(interp, "bad argument \"" + arg + "\": should be \"nonewline\"");
				}
			}
			
			try
			{
				if ((System.Object) chan.Encoding == null)
				{
					result = TclByteArray.newInstance();
				}
				else
				{
					result = TclString.newInstance(new System.Text.StringBuilder(64));
				}
				if (readAll)
				{
					charactersRead = chan.read(interp, result, TclIO.READ_ALL, 0);
					
					// If -nonewline was specified, and we have not hit EOF
					// and the last char is a "\n", then remove it and return.
					
					if (noNewline)
					{
						
						string inStr = result.ToString();
						if ((charactersRead > 0) && (inStr[charactersRead - 1] == '\n'))
						{
							interp.setResult(inStr.Substring(0, ((charactersRead - 1)) - (0)));
              return TCL.CompletionCode.RETURN;
						}
					}
				}
				else
				{
					// FIXME: Bug here, the -nonewline flag must be respected
					// when reading a set number of bytes
					charactersRead = chan.read(interp, result, TclIO.READ_N_BYTES, toRead);
				}
				
				/*
				// FIXME: Port this -nonewline logic from the C code.
				if (charactersRead < 0) {
				Tcl_ResetResult(interp);
				Tcl_AppendResult(interp, "error reading \"", name, "\": ",
				Tcl_PosixError(interp), (char *) NULL);
				Tcl_DecrRefCount(resultPtr);
				return TCL_ERROR;
				}
				
				// If requested, remove the last newline in the channel if at EOF.
				
				if ((charactersRead > 0) && (newline != 0)) {
				char *result;
				int length;
				
				result = Tcl_GetStringFromObj(resultPtr, length);
				if (result[length - 1] == '\n') {
				Tcl_SetObjLength(resultPtr, length - 1);
				}
				}
				
				*/
				
				interp.setResult(result);
			}
			catch (System.IO.IOException e)
			{
				throw new TclRuntimeError("ReadCmd.cmdProc() Error: IOException when reading " + chan.ChanName);
			}
      return TCL.CompletionCode.RETURN;
    }
		
		/// <summary> A unique error msg is printed for read, therefore dont call this 
		/// instead of the standard TclNumArgsException().
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="cmd">the name of the command (extracted form argv[0] of cmdProc)
		/// </param>
		
		private void  errorWrongNumArgs(Interp interp, string cmd)
		{
			throw new TclException(interp, "wrong # args: should be \"" + "read channelId ?numChars?\" " + "or \"read ?-nonewline? channelId\"");
		}
	}
}
