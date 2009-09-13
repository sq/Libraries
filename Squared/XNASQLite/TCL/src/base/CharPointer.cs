/* 
* CharPointer.java --
*
*	Used in the Parser, this class implements the functionality
* 	of a C character pointer.  CharPointers referencing the same
*	script share a reference to one array, while maintaining there
* 	own current index into the array.
*
* Copyright (c) 1997 by Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and redistribution
* of this file, and for a DISCLAIMER OF ALL WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: CharPointer.java,v 1.4 1999/08/05 03:33:44 mo Exp $
*/
using System;

namespace tcl.lang
{
	public class CharPointer
	{
		
		// A string of characters.
		
		public char[] array;
		
		// The current index into the array.
		
		public int index;
		internal CharPointer()
		{
			this.array = null;
			this.index = - 1;
		}
		internal CharPointer(CharPointer c)
		{
			this.array = c.array;
			this.index = c.index;
		}
		public CharPointer(string str)
		{
			int len = str.Length;
			this.array = new char[len + 1];
			SupportClass.GetCharsFromString(str, 0, len, ref this.array, 0);
			this.array[len] = '\x0000';
			this.index = 0;
		}
		internal  char charAt()
		{
			return (array[index]);
		}
		internal  char charAt(int x)
		{
			return (array[index + x]);
		}
		public int length()
		{
			return (array.Length - 1);
		}
		public override string ToString()
		{
			return new string(array, 0, array.Length - 1);
		}
	} // end CharPointer
}
