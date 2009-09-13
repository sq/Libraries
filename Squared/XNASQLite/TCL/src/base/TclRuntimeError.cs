/*
* TclRuntimeError.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclRuntimeError.java,v 1.1.1.1 1998/10/14 21:09:14 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> Signals that a unrecoverable run-time error in the interpreter.
	/// Similar to the panic() function in C.
	/// </summary>
	public class TclRuntimeError:System.SystemException
	{
		/// <summary> Constructs a TclRuntimeError with the specified detail
		/// message.
		/// 
		/// </summary>
		/// <param name="s">the detail message.
		/// </param>
		public TclRuntimeError(string s):base(s)
		{
		}
		public TclRuntimeError(string s,Exception inner):base(s,inner)
		{
		}
	}
}
