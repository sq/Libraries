/*
* ForCmd.java
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
* RCS @(#) $Id: ForCmd.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "for" command in Tcl.</summary>
	
	class ForCmd : Command
	{
		/*
		* This procedure is invoked to process the "for" Tcl command.
		* See the user documentation for details on what it does.
		*
		* @param interp the current interpreter.
		* @param argv command arguments.
		* @exception TclException if script causes error.
		*/
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			if (argv.Length != 5)
			{
				throw new TclNumArgsException(interp, 1, argv, "start test next command");
			}
			
			TclObject start = argv[1];
			
			string test = argv[2].ToString();
			TclObject next = argv[3];
			TclObject command = argv[4];
			
			bool done = false;
			try
			{
				interp.eval(start, 0);
			}
			catch (TclException e)
			{
				interp.addErrorInfo("\n    (\"for\" initial command)");
				throw ;
			}
			
			while (!done)
			{
				if (!interp.expr.evalBoolean(interp, test))
				{
					break;
				}
				
				try
				{
					interp.eval(command, 0);
				}
				catch (TclException e)
				{
					switch (e.getCompletionCode())
					{
						
						case TCL.CompletionCode.BREAK: 
							done = true;
							break;
						
						
						case TCL.CompletionCode.CONTINUE: 
							break;
						
						
						case TCL.CompletionCode.ERROR: 
							interp.addErrorInfo("\n    (\"for\" body line " + interp.errorLine + ")");
							throw ;
						
						
						default: 
							throw ;
						
					}
				}
				
				if (!done)
				{
					try
					{
						interp.eval(next, 0);
					}
					catch (TclException e)
					{
						switch (e.getCompletionCode())
						{
							
							case TCL.CompletionCode.BREAK: 
								done = true;
								break;
							
							
							case TCL.CompletionCode.CONTINUE: 
								break;
							
							
							default: 
								interp.addErrorInfo("\n    (\"for\" loop-end command)");
								throw ;
							
						}
					}
				}
			}
			
			interp.resetResult();
      return TCL.CompletionCode.RETURN;
    }
	}
}
