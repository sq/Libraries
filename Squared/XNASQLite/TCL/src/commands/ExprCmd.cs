/*
* ExprCmd.java
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
* RCS @(#) $Id: ExprCmd.java,v 1.2 1999/05/08 23:59:30 dejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "expr" command in Tcl.</summary>
	
	class ExprCmd : Command
	{
		/// <summary> Evaluates a Tcl expression. See Tcl user documentation for
		/// details.
		/// </summary>
		/// <exception cref=""> TclException If malformed expression.
		/// </exception>

    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
		{
			if (argv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, argv, "arg ?arg ...?");
			}
			
			if (argv.Length == 2)
			{
				
				interp.setResult(interp.expr.eval(interp, argv[1].ToString()));
			}
			else
			{
				System.Text.StringBuilder sbuf = new System.Text.StringBuilder();
				
				sbuf.Append(argv[1].ToString());
				for (int i = 2; i < argv.Length; i++)
				{
					sbuf.Append(' ');
					
					sbuf.Append(argv[i].ToString());
				}
				interp.setResult(interp.expr.eval(interp, sbuf.ToString()));
			}
      return TCL.CompletionCode.RETURN;
    }
	}
}
