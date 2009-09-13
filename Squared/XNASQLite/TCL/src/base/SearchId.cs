/*
* SearchId.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: SearchId.java,v 1.1.1.1 1998/10/14 21:09:20 cvsadmin Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> SearchId is used only by the ArrayVar class.  When searchstart is
	/// called on an Tcl array, a SearchId is created that contains the
	/// Enumerated list of all the array keys; a String that uniquely
	/// identifies the searchId for the Tcl array, and an index that is
	/// used when to generate other unique strings.
	/// </summary>
	public sealed class SearchId
	{
		/// <summary> Return the Enumeration for the SearchId object.  This is 
		/// used in the ArrayCmd class for the anymore, donesearch, 
		/// and nextelement functions.
		/// 
		/// </summary>
		/// <param name="">none
		/// </param>
		/// <returns> The Enumeration for the SearchId object
		/// </returns>
		private IDictionaryEnumerator Enum
		{
			get
			{
				return enum_Renamed;
			}
			
		}
		/// <summary> Return the integer value of the index.  Used in ArrayVar to
		/// generate the next unique SearchId string.
		/// 
		/// </summary>
		/// <param name="">none
		/// </param>
		/// <returns>h  The integer value of the index
		/// </returns>
		internal int Index
		{
			get
			{
				return index;
			}
			
		}
		private bool hasMore = true;
		internal bool HasMore 
		{
			get 
			{
				return hasMore;
			}
		}
		private DictionaryEntry entry;
		internal DictionaryEntry nextEntry() {
			DictionaryEntry cEntry = entry;
			hasMore = enum_Renamed.MoveNext();
			if (hasMore)
				entry = enum_Renamed.Entry;
			return cEntry;
		}

		
		/// <summary> An Enumeration that stores the list of keys for
		/// the ArrayVar.
		/// </summary>
		private IDictionaryEnumerator enum_Renamed;
		
		/// <summary> The unique searchId string</summary>
		private string str;
		
		/// <summary> Unique index used for generating unique searchId strings</summary>
		private int index;
		
		/// <summary> A SearchId is only created from an ArrayVar object.  The ArrayVar 
		/// constructs a new SearchId object by passing it's current keys 
		/// stored as an enumeration, a unique string that ArrayVar creates, 
		/// and an index value used for future SearchId objects.
		/// 
		/// </summary>
		/// <param name="e">initial Enumeration
		/// </param>
		/// <param name="s">String as the unique identifier for the searchId
		/// </param>
		/// <param name="e">index value for this object
		/// </param>
		internal SearchId(IDictionaryEnumerator e, string s, int i)
		{
			enum_Renamed = e;
			str = s;
			index = i;
			hasMore = enum_Renamed.MoveNext();
			if (hasMore)
				entry = enum_Renamed.Entry;
		}
		
		/// <summary> Return the str that is the unique identifier of the SearchId</summary>
		public override string ToString()
		{
			return str;
		}
		
		/// <summary> Tests for equality based on the value of str</summary>
		/// <param name="">none
		/// </param>
		/// <returns> boolean based on the equality of the string
		/// </returns>
		internal bool equals(string s)
		{
			return str.Equals(s);
		}
	}
}
