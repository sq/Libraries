/*
* Command.java
*
*	Interface for Commands that can be added to the Tcl Interpreter.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Command.java,v 1.3 1999/08/05 03:43:27 mo Exp $
*/
using System;
namespace tcl.lang
{
	
	/// <summary> The Command interface specifies the method that a new Tcl command
	/// must implement.  See the createCommand method of the Interp class
	/// to see how to add a new command to an interperter.
	/// </summary>
	
	public interface Command
		{
      TCL.CompletionCode cmdProc( Interp interp, TclObject[] objv ); // Tcl exceptions are thown for Tcl errors.
		}
}
