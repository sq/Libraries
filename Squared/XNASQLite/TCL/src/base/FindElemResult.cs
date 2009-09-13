/*
* FindElemResult.java --
*
*	Result returned by Util.findElement().
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
* RCS @(#) $Id: FindElemResult.java,v 1.1.1.1 1998/10/14 21:09:21 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* Result returned by Util.findElement().
	*/
	
	class FindElemResult
	{
		
		/*
		* The end of the element in the original string -- the index of the
		* character immediately behind the element.
		*/
		
		internal int elemEnd;
		
		/*
		* The element itself.
		*/
		
		internal string elem;
		
		internal FindElemResult(int i, string s)
		{
			elemEnd = i;
			elem = s;
		}
	} // end FindElemResult
}
