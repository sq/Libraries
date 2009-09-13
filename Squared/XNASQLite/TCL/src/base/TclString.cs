/*
* TclList.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclString.java,v 1.5 2003/03/08 03:42:56 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	// This class implements the string object type in Tcl.
	
	public class TclString : InternalRep
	{
		/// <summary> Called to convert the other object's internal rep to string.
		/// 
		/// </summary>
		/// <param name="tobj">the TclObject to convert to use the TclString internal rep.
		/// </param>
		private static TclObject StringFromAny
		{
			set
			{
				InternalRep rep = value.InternalRep;
				
				if (!(rep is TclString))
				{
					// make sure that this object now has a valid string rep.
					
					value.ToString();
					
					// Change the type of the object to TclString.
					
					value.InternalRep = new TclString();
				}
			}
			
			/*
			* public static String get(TclObject tobj) {;}
			*
			* There is no "get" class method for TclString representations.
			* Use tobj.toString() instead.
			*/
			
		}
		
		// Used to perform "append" operations. After an append op,
		// sbuf.toString() will contain the latest value of the string and
		// tobj.stringRep will be set to null. This field is not private
		// since it will need to be accessed directly by Jacl's IO code.
		
		internal System.Text.StringBuilder sbuf;

    private TclString()
		{
			sbuf = null;
		}
		
		private TclString(System.Text.StringBuilder sb)
		{
			sbuf = sb;
		}
		
		/// <summary> Returns a dupilcate of the current object.</summary>
		/// <param name="obj">the TclObject that contains this internalRep.
		/// </param>
		
		public  InternalRep duplicate()
		{
			return new TclString();
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
			if (sbuf == null)
			{
				return "";
			}
			else
			{
				return sbuf.ToString();
			}
		}
		
		/// <summary> Create a new TclObject that has a string representation with
		/// the given string value.
		/// </summary>
		public static TclObject newInstance(string str)
		{
			return new TclObject(new TclString(), str);
		}
		
		/// <summary> Create a new TclObject that makes use of the given StringBuffer
		/// object. The passed in StringBuffer should not be modified after
		/// it is passed to this method.
		/// </summary>
		internal static TclObject newInstance(System.Text.StringBuilder sb)
		{
			return new TclObject(new TclString(sb));
		}
		
		internal static TclObject newInstance(System.Object o)
		{
			return newInstance(o.ToString());
		}
		
		/// <summary> Create a TclObject with an internal TclString representation
		/// whose initial value is a string with the single character.
		/// 
		/// </summary>
		/// <param name="c">initial value of the string.
		/// </param>
		
		internal static TclObject newInstance(char c)
		{
			char[] charArray = new char[1];
			charArray[0] = c;
			return newInstance(new string(charArray));
		}
		
		
		/// <summary> Appends a string to a TclObject object. This method is equivalent to
		/// Tcl_AppendToObj() in Tcl 8.0.
		/// 
		/// </summary>
		/// <param name="tobj">the TclObject to append a string to.
		/// </param>
		/// <param name="string">the string to append to the object.
		/// </param>
		public static void  append(TclObject tobj, string toAppend)
		{
			StringFromAny = tobj;
			
			TclString tstr = (TclString) tobj.InternalRep;
			if (tstr.sbuf == null)
			{
				tstr.sbuf = new System.Text.StringBuilder(tobj.ToString());
			}
			tobj.invalidateStringRep();
      tstr.sbuf.Append( toAppend );
		}
		
		/// <summary> Appends an array of characters to a TclObject Object.
		/// Tcl_AppendUnicodeToObj() in Tcl 8.0.
		/// 
		/// </summary>
		/// <param name="tobj">the TclObject to append a string to.
		/// </param>
		/// <param name="charArr">array of characters.
		/// </param>
		/// <param name="offset">index of first character to append.
		/// </param>
		/// <param name="length">number of characters to append.
		/// </param>
		public static void  append(TclObject tobj, char[] charArr, int offset, int length)
		{
			StringFromAny = tobj;
			
			TclString tstr = (TclString) tobj.InternalRep;
			if (tstr.sbuf == null)
			{
				tstr.sbuf = new System.Text.StringBuilder(tobj.ToString());
			}
			tobj.invalidateStringRep();
			tstr.sbuf.Append(charArr, offset, length);
		}
		
		/// <summary> Appends a TclObject to a TclObject. This method is equivalent to
		/// Tcl_AppendToObj() in Tcl 8.0.
		/// 
		/// The type of the TclObject will be a TclString that contains the
		/// string value:
		/// tobj.toString() + tobj2.toString();
		/// </summary>
		internal static void  append(TclObject tobj, TclObject tobj2)
		{
			append(tobj, tobj2.ToString());
		}
		
		/// <summary> This procedure clears out an existing TclObject so
		/// that it has a string representation of "".
		/// </summary>
		
		public static void  empty(TclObject tobj)
		{
			StringFromAny = tobj;
			
			TclString tstr = (TclString) tobj.InternalRep;
			if (tstr.sbuf == null)
			{
				tstr.sbuf = new System.Text.StringBuilder();
			}
			else
			{
				tstr.sbuf.Length = 0;
			}
			tobj.invalidateStringRep();
		}
	}
}
