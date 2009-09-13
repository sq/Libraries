/*
* StrtoulResult.java
*
*	Stores the result of the Util.strtoul() method.
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
* RCS @(#) $Id: StrtoulResult.java,v 1.2 1999/05/09 01:30:54 dejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class stores the result of the Util.strtoul() method.</summary>
	
	class StrtoulResult
	{
		
		// If the conversion is successful, errno = 0;
		//
		// If the number cannot be converted to a valid unsigned 32-bit integer,
		// contains the error code (TCL.INTEGER_RANGE or TCL.INVALID_INTEGER).
		
		internal int errno;
		
		// If errno is 0, points to the character right after the number
		
		internal int index;
		
		// If errno is 0, contains the value of the number.
		
		internal long value;
		
		internal StrtoulResult(long v, int i, int e)
		{
			value = v;
			index = i;
			errno = e;
		}
	} // end StrtoulResult
}
