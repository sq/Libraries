/*
* TclNumArgsException.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclNumArgsException.java,v 1.3 2003/01/12 02:44:28 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This exception is used to report wrong number of arguments in Tcl scripts.</summary>
	
	public class TclNumArgsException:TclException
	{
		
		/// <summary> Creates a TclException with the appropiate Tcl error
		/// message for having the wring number of arguments to a Tcl command.
		/// <p>
		/// Example: <pre>
		/// 
		/// if (argv.length != 3) {
		/// throw new TclNumArgsException(interp, 1, argv, "option name");
		/// }
		/// </pre>
		/// 
		/// </summary>
		/// <param name="interp">current Interpreter.
		/// </param>
		/// <param name="argc">the number of arguments to copy from the offending
		/// command to put into the error message.
		/// </param>
		/// <param name="argv">the arguments of the offending command.
		/// </param>
		/// <param name="message">extra message to appear in the error message that
		/// explains the proper usage of the command.
		/// </param>
		/// <exception cref=""> TclException is always thrown.
		/// </exception>
		
		public TclNumArgsException(Interp interp, int argc, TclObject[] argv, string message):base(TCL.CompletionCode.ERROR)
		{
			
			if (interp != null)
			{
				System.Text.StringBuilder buff = new System.Text.StringBuilder(50);
				buff.Append("wrong # args: should be \"");
				
				for (int i = 0; i < argc; i++)
				{
					if (argv[i].InternalRep is TclIndex)
					{
						buff.Append(argv[i].InternalRep.ToString());
					}
					else
					{
						buff.Append(argv[i].ToString());
					}
					if (i < (argc - 1))
					{
						buff.Append(" ");
					}
				}
				if (( message != null)  )
				{
					buff.Append(" " + message);
				}
				buff.Append("\"");
				interp.setResult(buff.ToString());
			}
		}
	}
}
