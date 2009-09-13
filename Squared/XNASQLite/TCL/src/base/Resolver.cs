/// <summary> Resolver.java
/// 
/// Interface for resolvers that can be added to
/// the Tcl Interpreter or to a namespace.
/// 
/// Copyright (c) 1997 Sun Microsystems, Inc.
/// Copyright (c) 2001 Christian Krone
/// 
/// See the file "license.terms" for information on usage and
/// redistribution of this file, and for a DISCLAIMER OF ALL
/// WARRANTIES.
/// 
///  Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
/// $Header$
/// RCS: @(#) $Id: Resolver.java,v 1.1 2001/05/05 22:38:13 mdejong Exp $
/// </summary>
using System;
namespace tcl.lang
{
	
	/// <summary> The Resolver interface specifies the methods that a new Tcl resolver
	/// must implement.  See the addInterpResolver method of the Interp class
	/// to see how to add a new resolver to an interperter or the
	/// setNamespaceResolver of the NamespaceCmd class.
	/// </summary>
	
	public interface Resolver
		{
			
			WrappedCommand resolveCmd(Interp interp, string name, NamespaceCmd.Namespace context, TCL.VarFlag flags); // Tcl exceptions are thrown for Tcl errors.
			
			Var resolveVar(Interp interp, string name, NamespaceCmd.Namespace context, TCL.VarFlag flags); // Tcl exceptions are thrown for Tcl errors.
		} // end Resolver
}
