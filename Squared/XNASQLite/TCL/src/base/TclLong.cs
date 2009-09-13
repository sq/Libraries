/*
* TclLong.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclLong.java,v 1.5 2000/10/29 06:00:42 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{

  /// <summary> This class implements the long object type in Tcl.</summary>

  public class TclLong : InternalRep
  {
    /// <summary> longernal representation of a long value.</summary>
    private long value;

    /// <summary> Construct a TclLong representation with the given long value.</summary>
    private TclLong( long i )
    {
      value = i;
    }

    /// <summary> Construct a TclLong representation with the initial value taken
    /// from the given string.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="str">string rep of the long.
    /// </param>
    /// <exception cref=""> TclException if the string is not a well-formed Tcl long
    /// value.
    /// </exception>
    private TclLong( Interp interp, string str )
    {
      value = Util.getLong( interp, str );
    }

    /// <summary> Returns a dupilcate of the current object.</summary>
    /// <param name="obj">the TclObject that contains this InternalRep.
    /// </param>
    public InternalRep duplicate()
    {
      return new TclLong( value );
    }

    /// <summary> Implement this no-op for the InternalRep longerface.</summary>

    public void dispose()
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
      return value.ToString();
    }

    /// <summary> Tcl_NewlongObj -> TclLong.newInstance
    /// 
    /// Creates a new instance of a TclObject with a TclLong longernal
    /// representation.
    /// 
    /// </summary>
    /// <param name="b">initial value of the long object.
    /// </param>
    /// <returns> the TclObject with the given long value.
    /// </returns>

    public static TclObject newInstance( long i )
    {
      return new TclObject( new TclLong( i ) );
    }

    /// <summary> SetlongFromAny -> TclLong.setlongFromAny
    /// 
    /// Called to convert the other object's longernal rep to this type.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="forIndex">true if this methid is called by getForIndex.
    /// </param>
    /// <param name="tobj">the TclObject to convert to use the
    /// representation provided by this class.
    /// </param>

    private static void setlongFromAny( Interp interp, TclObject tobj )
    {
      InternalRep rep = tobj.InternalRep;

      if ( rep is TclLong )
      {
        // Do nothing.
      }
      else if ( rep is TclBoolean )
      {
        bool b = TclBoolean.get( interp, tobj );
        if ( b )
        {
          tobj.InternalRep = new TclLong( 1 );
        }
        else
        {
          tobj.InternalRep = new TclLong( 0 );
        }
      }
      else
      {
        // (ToDo) other short-cuts
        tobj.InternalRep = new TclLong( interp, tobj.ToString() );
      }
    }

    /// <summary> Tcl_GetlongFromObj -> TclLong.get
    /// 
    /// Returns the long value of the object.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the object to operate on.
    /// </param>
    /// <returns> the long value of the object.
    /// </returns>

    public static long get( Interp interp, TclObject tobj )
    {
      setlongFromAny( interp, tobj );
      TclLong tlong = (TclLong)tobj.InternalRep;
      return tlong.value;
    }

    /// <summary> Changes the long value of the object.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the object to operate on.
    /// @paran i the new long value.
    /// </param>
    public static void set( TclObject tobj, long i )
    {
      tobj.invalidateStringRep();
      InternalRep rep = tobj.InternalRep;
      TclLong tlong;

      if ( rep is TclLong )
      {
        tlong = (TclLong)rep;
        tlong.value = i;
      }
      else
      {
        tobj.InternalRep = new TclLong( i );
      }
    }
  }
}
