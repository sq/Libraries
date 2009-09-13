/*
* ExprValue.java
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
* RCS @(#) $Id: ExprValue.java,v 1.2 1999/05/09 00:03:00 dejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> Describes an expression value, which can be either an integer (the
	/// usual case), a double-precision floating-point value, or a string.
	/// A given number has only one value at a time.
	/// </summary>
	
	class ExprValue
	{
		internal const int ERROR = 0;
		internal const int INT = 1;
		internal const int DOUBLE = 2;
		internal const int STRING = 3;
		
		/// <summary> Integer value, if any.</summary>
		internal long intValue;
		
		/// <summary> Floating-point value, if any.</summary>
		internal double doubleValue;
		
		/// <summary> Used to hold a string value, if any.</summary>
		internal string stringValue;
		
		/// <summary> Type of value: INT, DOUBLE, or STRING.</summary>
		internal int type;
		
		/// <summary> Constructors.</summary>
		internal ExprValue()
		{
			type = ERROR;
		}
		
		internal ExprValue(long i)
		{
			intValue = i;
			type = INT;
		}
		
		internal ExprValue(double d)
		{
			doubleValue = d;
			type = DOUBLE;
		}
		
		internal ExprValue(string s)
		{
			stringValue = s;
			type = STRING;
		}
	}
}
