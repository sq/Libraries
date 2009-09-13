/*
* ParseResult.java
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
* RCS @(#) $Id: ParseResult.java,v 1.3 2003/01/09 02:15:39 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class stores a single word that's generated inside the Tcl parser
	/// inside the Interp class.
	/// </summary>
	public class ParseResult
	{
		
		/// <summary> The value of a parse operation. For calls to Interp.intEval(),
		/// this variable is the same as interp.m_result. The ref count
		/// has been incremented, so the user will need to explicitly
		/// invoke release() to drop the ref.
		/// </summary>
		public TclObject value;
		
		/// <summary> Points to the next character to be parsed.</summary>
		public int nextIndex;
		
		/// <summary> Create an empty parsed word.</summary>
		internal ParseResult()
		{
			value = TclString.newInstance("");
			value.preserve();
		}
		
		internal ParseResult(string s, int ni)
		{
			value = TclString.newInstance(s);
			value.preserve();
			nextIndex = ni;
		}
		
		/// <summary> Assume that the caller has already preserve()'ed the TclObject.</summary>
		internal ParseResult(TclObject o, int ni)
		{
			value = o;
			nextIndex = ni;
		}
		
		internal ParseResult(System.Text.StringBuilder sbuf, int ni)
		{
			value = TclString.newInstance(sbuf.ToString());
			value.preserve();
			nextIndex = ni;
		}
		
		public void release()
		{
			value.release();
		}
	}
}
