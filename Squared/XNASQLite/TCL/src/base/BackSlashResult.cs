/*
* BackSlashResult.java
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
* RCS @(#) $Id: BackSlashResult.java,v 1.1.1.1 1998/10/14 21:09:19 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	class BackSlashResult
	{
		internal char c;
		internal int nextIndex;
		internal bool isWordSep;
		internal BackSlashResult(char ch, int w)
		{
			c = ch;
			nextIndex = w;
			isWordSep = false;
		}
		internal BackSlashResult(char ch, int w, bool b)
		{
			c = ch;
			nextIndex = w;
			isWordSep = b;
		}
	}
}
