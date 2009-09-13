/*
* ReturnCmd.java --
*
*	This file implements the Tcl "return" command.
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
* RCS @(#) $Id: ReturnCmd.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "return" command in Tcl.
	*/
	
	class ReturnCmd : Command
	{

    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
		{
			interp.errorCode = null;
			interp.errorInfo = null;
			TCL.CompletionCode returnCode;
			int i;
			
			/*
			* Note: returnCode is the value given by the -code option. Don't
			* confuse this value with the compCode variable of the
			* TclException thrown by this method, which is always TCL.CompletionCode.RETURN.
			*/
			
			returnCode = TCL.CompletionCode.OK;
			for (i = 1; i < argv.Length - 1; i += 2)
			{
				
				if (argv[i].ToString().Equals("-code"))
				{
					
					if (argv[i + 1].ToString().Equals("ok"))
					{
						returnCode = TCL.CompletionCode.OK;
					}
					else
					{
						
						if (argv[i + 1].ToString().Equals("error"))
						{
							returnCode = TCL.CompletionCode.ERROR;
						}
						else
						{
							
							if (argv[i + 1].ToString().Equals("return"))
							{
								returnCode = TCL.CompletionCode.RETURN;
							}
							else
							{
								
								if (argv[i + 1].ToString().Equals("break"))
								{
									returnCode = TCL.CompletionCode.BREAK;
								}
								else
								{
									
									if (argv[i + 1].ToString().Equals("continue"))
									{
										returnCode = TCL.CompletionCode.CONTINUE;
									}
									else
									{
										try
										{
											returnCode = (TCL.CompletionCode)TclInteger.get(interp, argv[i + 1]);
										}
										catch (TclException e)
										{
											
											throw new TclException(interp, "bad completion code \"" + argv[i + 1] + "\": must be ok, error, return, break, " + "continue, or an integer");
										}
									}
								}
							}
						}
					}
				}
				else
				{
					
					if (argv[i].ToString().Equals("-errorcode"))
					{
						
						interp.errorCode = argv[i + 1].ToString();
					}
					else
					{
						
						if (argv[i].ToString().Equals("-errorinfo"))
						{
							
							interp.errorInfo = argv[i + 1].ToString();
						}
						else
						{
							
							throw new TclException(interp, "bad option \"" + argv[i] + "\": must be -code, -errorcode, or -errorinfo");
						}
					}
				}
			}
			if (i != argv.Length)
			{
				interp.setResult(argv[argv.Length - 1]);
			}
			
			interp.returnCode = returnCode;
      throw new TclException( TCL.CompletionCode.RETURN );
    }
	} // end ReturnCmd
}
