/*
* TclException.java --
*
*	This file defines the TclException class used by Tcl to report
*	generic script-level errors and exceptions.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclException.java,v 1.2 2000/04/03 14:09:11 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* TclException is used to interrupt the Tcl script currently being
	* interpreted by the Tcl Interpreter. Usually, a TclException is thrown
	* to indicate a script level error, e.g.:
	*
	*	- A syntax error occurred in a script.
	*	- A unknown variable is referenced.
	*	- A unknown command is executed.
	*	- A command is passed incorrected.
	*
	* A TclException can also be thrown by Tcl control structure commands such
	* as "return" and "continue" to change the flow of control in
	* a Tcl script.
	*
	* A TclException is accompanied by two pieces of information: the error
	* message and the completion code. The error message is a string stored in
	* the interpreter result. After a TclException is thrown and caught, the
	* error message can be queried by Interp.getResult().
	*
	* The completion code indicates why the TclException is generated. It is
	* stored in the compCode field of this class.
	*/
	
	public class TclException:System.Exception
	{
		
		/*
		* Stores the completion code of a TclException.
		*/
		
		private TCL.CompletionCode compCode;
		
		/*
		* An index that indicates where an error occurs inside a Tcl
		* string. This is used to add the offending command into the stack
		* trace.
		*
		* A negative value means the location of the index is unknown.
		*
		* Currently this field is used only by the Jacl interpreter.
		*/
		
		protected internal int errIndex;
		
		protected internal TclException(Interp interp, string msg, TCL.CompletionCode ccode, int idx):base(msg)
		{
			if (ccode == TCL.CompletionCode.OK)
			{
				throw new TclRuntimeError("The reserved completion code TCL.CompletionCode.OK (0) cannot be used " + "in TclException");
			}
			compCode = ccode;
			errIndex = idx;
			
			if (interp != null && (System.Object) msg != null)
			{
				interp.setResult(msg);
			}
		}
		public TclException(TCL.CompletionCode ccode):base()
		{
			if (ccode == TCL.CompletionCode.OK)
			{
				throw new TclRuntimeError("The reserved completion code TCL.CompletionCode.OK (0) cannot be used");
			}
			compCode = ccode;
			errIndex = - 1;
		}
		public TclException(Interp interp, string msg):this(interp, msg, TCL.CompletionCode.ERROR, - 1)
		{
		}
		public TclException(Interp interp, string msg, TCL.CompletionCode ccode):this(interp, msg, ccode, - 1)
		{
		}
		public TCL.CompletionCode getCompletionCode()
		{
			return compCode;
		}
		internal void  setCompletionCode(TCL.CompletionCode ccode)
		// New completion code. 
		{
			if (ccode == TCL.CompletionCode.OK)
			{
				throw new TclRuntimeError("The reserved completion code TCL.CompletionCode.OK (0) cannot be used");
			}
			compCode = ccode;
		}
	} // end TclException
}
