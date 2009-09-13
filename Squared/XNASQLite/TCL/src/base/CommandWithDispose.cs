/*
* CommandWithDispose.java --
*
*	Interface for Commands that need to know when they are deleted
*	from an interpreter.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: CommandWithDispose.java,v 1.2 1999/07/28 03:41:13 mo Exp $
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This interface is implemented by Commands that need to know when
	/// they are deleted from an interpreter.  Most commands do not need
	/// to know when they are deleted in Java because Java will garbage
	/// collect any allocations made by the command.  However, sometimes
	/// a command may hold onto resources that must be explicitly released.
	/// This interface allows those commands to be notified when they are
	/// being deleted from the interpreter.
	/// </summary>
	
	public interface CommandWithDispose : Command
		{
			void  disposeCmd(); // The disposeCmd method is called when the
			// interp is removing the Tcl command.
		}
}
