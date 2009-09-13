/*
* TclByteArray.java
*
*	This class contains the implementation of the Jacl binary data object.
*
* Copyright (c) 1999 Christian Krone.
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclByteArray.java,v 1.4 2003/03/08 02:05:06 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the binary data object type in Tcl.</summary>
	public class TclByteArray : InternalRep
	{
		
		/// <summary> The number of bytes used in the byte array.
		/// The following structure is the internal rep for a ByteArray object.
		/// Keeps track of how much memory has been used. This can be different from
		/// how much has been allocated for the byte array to enable growing and
		/// shrinking of the ByteArray object with fewer allocations.  
		/// </summary>
		private int used;
    
    /// <summary> Internal representation of the binary data.</summary>
		private byte[] bytes;
		
		/// <summary> Create a new empty Tcl binary data.</summary>
		private TclByteArray()
		{
			used = 0;
			bytes = new byte[0];
		}
		
		/// <summary> Create a new Tcl binary data.</summary>
		private TclByteArray(byte[] b)
		{
			used = b.Length;
			bytes = new byte[used];
			Array.Copy(b, 0, bytes, 0, used);
		}
		
		/// <summary> Create a new Tcl binary data.</summary>
		private TclByteArray(byte[] b, int position, int length)
		{
			used = length;
			bytes = new byte[used];
			Array.Copy(b, position, bytes, 0, used);
		}
		
		/// <summary> Create a new Tcl binary data.</summary>
		private TclByteArray(char[] c)
		{
			used = c.Length;
			bytes = new byte[used];
			for (int ix = 0; ix < used; ix++)
			{
				bytes[ix] = (byte) c[ix];
			}
		}
		
		/// <summary> Returns a duplicate of the current object.
		/// 
		/// </summary>
		/// <param name="obj">the TclObject that contains this internalRep.
		/// </param>
		public  InternalRep duplicate()
		{
			return new TclByteArray(bytes, 0, used);
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
			char[] c = new char[used];
			for (int ix = 0; ix < used; ix++)
			{
				c[ix] = (char) (bytes[ix] & 0xff);
			}
			return new string(c);
		}
		
		/// <summary> Creates a new instance of a TclObject with a TclByteArray internal
		/// rep.
		/// 
		/// </summary>
		/// <returns> the TclObject with the given byte array value.
		/// </returns>
		
		public static TclObject newInstance(byte[] b, int position, int length)
		{
			return new TclObject(new TclByteArray(b, position, length));
		}
		
		/// <summary> Creates a new instance of a TclObject with a TclByteArray internal
		/// rep.
		/// 
		/// </summary>
		/// <returns> the TclObject with the given byte array value.
		/// </returns>
		
		public static TclObject newInstance(byte[] b)
		{
			return new TclObject(new TclByteArray(b));
		}
		
		/// <summary> Creates a new instance of a TclObject with an empty TclByteArray
		/// internal rep.
		/// 
		/// </summary>
		/// <returns> the TclObject with the empty byte array value.
		/// </returns>
		
		public static TclObject newInstance()
		{
			return new TclObject(new TclByteArray());
		}
		
		/// <summary> Called to convert the other object's internal rep to a ByteArray.
		/// 
		/// </summary>
		/// <param name="interp">current interpreter.
		/// </param>
		/// <param name="tobj">the TclObject to convert to use the ByteArray internal rep.
		/// </param>
		/// <exception cref=""> TclException if the object doesn't contain a valid ByteArray.
		/// </exception>
		internal static void  setByteArrayFromAny(Interp interp, TclObject tobj)
		{
			InternalRep rep = tobj.InternalRep;
			
			if (!(rep is TclByteArray))
			{
				
				char[] c = tobj.ToString().ToCharArray();
				tobj.InternalRep = new TclByteArray(c);
			}
		}
		
		/// <summary> 
		/// This method changes the length of the byte array for this
		/// object.  Once the caller has set the length of the array, it
		/// is acceptable to directly modify the bytes in the array up until
		/// Tcl_GetStringFromObj() has been called on this object.
		/// 
		/// Results:
		/// The new byte array of the specified length.
		/// 
		/// Side effects:
		/// Allocates enough memory for an array of bytes of the requested
		/// size.  When growing the array, the old array is copied to the
		/// new array; new bytes are undefined.  When shrinking, the
		/// old array is truncated to the specified length.
		/// </summary>
		
		public static byte[] setLength(Interp interp, TclObject tobj, int length)
		{
			if (tobj.Shared)
			{
				throw new TclRuntimeError("TclByteArray.setLength() called with shared object");
			}
			setByteArrayFromAny(interp, tobj);
			TclByteArray tbyteArray = (TclByteArray) tobj.InternalRep;
			
			if (length > tbyteArray.bytes.Length)
			{
				byte[] newBytes = new byte[length];
				Array.Copy(tbyteArray.bytes, 0, newBytes, 0, tbyteArray.used);
				tbyteArray.bytes = newBytes;
			}
			tobj.invalidateStringRep();
			tbyteArray.used = length;
			return tbyteArray.bytes;
		}
		
		/// <summary> Queries the length of the byte array. If tobj is not a byte array
		/// object, an attempt will be made to convert it to a byte array.
		/// 
		/// </summary>
		/// <param name="interp">current interpreter.
		/// </param>
		/// <param name="tobj">the TclObject to use as a byte array.
		/// </param>
		/// <returns> the length of the byte array.
		/// </returns>
		/// <exception cref=""> TclException if tobj is not a valid byte array.
		/// </exception>
		public static int getLength(Interp interp, TclObject tobj)
		{
			setByteArrayFromAny(interp, tobj);
			
			TclByteArray tbyteArray = (TclByteArray) tobj.InternalRep;
			return tbyteArray.used;
		}
		
		/// <summary> Returns the bytes of a ByteArray object.  If tobj is not a ByteArray
		/// object, an attempt will be made to convert it to a ByteArray. <p>
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="tobj">the byte array object.
		/// </param>
		/// <returns> a byte array.
		/// </returns>
		/// <exception cref=""> TclException if tobj is not a valid ByteArray.
		/// </exception>
		public static byte[] getBytes(Interp interp, TclObject tobj)
		{
			setByteArrayFromAny(interp, tobj);
			TclByteArray tbyteArray = (TclByteArray) tobj.InternalRep;
			return tbyteArray.bytes;
		}
	}
}
