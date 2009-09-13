/*
* TclIndex.java
*
*	This file implements objects of type "index".  This object type
*	is used to lookup a keyword in a table of valid values and cache
*	the index of the matching entry.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclIndex.java,v 1.5 2003/01/10 01:35:58 mdejong Exp $
*/
using System;
namespace tcl.lang
{
	
	public class TclIndex : InternalRep
	{
		
		/// <summary> The variable slots for this object.</summary>
		private int index;
    
    /// <summary> Table of valid options.</summary>
		
		private string[] table;
		
		/// <summary> Construct a TclIndex representation with the given index & table.</summary>
		private TclIndex(int i, string[] tab)
		{
			index = i;
			table = tab;
		}
		
		/// <summary> Returns a dupilcate of the current object.</summary>
		/// <param name="obj">the TclObject that contains this internalRep.
		/// </param>
		public  InternalRep duplicate()
		{
			return new TclIndex(index, table);
		}
		
		/// <summary> Implement this no-op for the InternalRep interface.</summary>
		
		public  void  dispose()
		{
		}
		
		/// <summary> Called to query the string representation of the Tcl object. This
		/// method is called only by TclObject.toString() when
		/// TclObject.stringRep is null.
		/// 
		/// </summary>
		/// <returns> the string representation of the Tcl object.
		/// </returns>
		public override string ToString()
		{
			return table[index];
		}
		
		/// <summary> Tcl_GetIndexFromObj -> get
		/// 
		/// Gets the index into the table of the object.  Generate an error
		/// it it doesn't occur.  This also converts the object to an index
		/// which should catch the lookup for speed improvement.
		/// 
		/// </summary>
		/// <param name="interp">the interperter or null
		/// </param>
		/// <param name="tobj">the object to operate on.
		/// @paran table the list of commands
		/// @paran msg used as part of any error messages
		/// @paran flags may be TCL.EXACT.
		/// </param>
		
		public static int get(Interp interp, TclObject tobj, string[] table, string msg, int flags)
		{
			InternalRep rep = tobj.InternalRep;
			
			if (rep is TclIndex)
			{
				if (((TclIndex) rep).table == table)
				{
					return ((TclIndex) rep).index;
				}
			}
			
			string str = tobj.ToString();
			int strLen = str.Length;
			int tableLen = table.Length;
			int index = - 1;
			int numAbbrev = 0;
			
			{
				if (strLen > 0)
				{
					
					for (int i = 0; i < tableLen; i++)
					{
						string option = table[i];
						
						if (((flags & TCL.EXACT) == TCL.EXACT) && (option.Length != strLen))
						{
							continue;
						}
						if (option.Equals(str))
						{
							// Found an exact match already. Return it.
							
							index = i;
							goto checking_brk;
						}
						if (option.StartsWith(str))
						{
							numAbbrev++;
							index = i;
						}
					}
				}
				if (numAbbrev != 1)
				{
					System.Text.StringBuilder sbuf = new System.Text.StringBuilder();
					if (numAbbrev > 1)
					{
						sbuf.Append("ambiguous ");
					}
					else
					{
						sbuf.Append("bad ");
					}
					sbuf.Append(msg);
					sbuf.Append(" \"");
					sbuf.Append(str);
					sbuf.Append("\"");
					sbuf.Append(": must be ");
					sbuf.Append(table[0]);
					for (int i = 1; i < tableLen; i++)
					{
						if (i == (tableLen - 1))
						{
							sbuf.Append((i > 1)?", or ":" or ");
						}
						else
						{
							sbuf.Append(", ");
						}
						sbuf.Append(table[i]);
					}
					throw new TclException(interp, sbuf.ToString());
				}
			}
checking_brk: ;
			// Create a new index object.
			tobj.InternalRep = new TclIndex(index, table);
			return index;
		}
	}
}
