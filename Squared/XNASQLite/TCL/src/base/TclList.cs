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
* RCS @(#) $Id: TclList.java,v 1.5 2003/01/09 02:15:39 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{

  /// <summary> This class implements the list object type in Tcl.</summary>
  public class TclList : InternalRep
  {

    /// <summary> Internal representation of a list value.</summary>
    private ArrayList vector;

    /// <summary> Create a new empty Tcl List.</summary>
    private TclList()
    {
      vector = new ArrayList( 10 );
    }

    /// <summary> Create a new empty Tcl List, with the vector pre-allocated to
    /// the given size.
    /// 
    /// </summary>
    /// <param name="size">the number of slots pre-allocated in the vector.
    /// </param>
    private TclList( int size )
    {
      vector = new ArrayList( size );
    }

    /// <summary> Called to free any storage for the type's internal rep.</summary>
    /// <param name="obj">the TclObject that contains this internalRep.
    /// </param>
    public void dispose()
    {
      int size = vector.Count;
      for ( int i = 0 ; i < size ; i++ )
      {
        ( (TclObject)vector[i] ).release();
      }
    }

    /// <summary> DupListInternalRep -> duplicate
    /// 
    /// Returns a dupilcate of the current object.
    /// 
    /// </summary>
    /// <param name="obj">the TclObject that contains this internalRep.
    /// </param>
    public InternalRep duplicate()
    {
      int size = vector.Count;
      TclList newList = new TclList( size );

      for ( int i = 0 ; i < size ; i++ )
      {
        TclObject tobj = (TclObject)vector[i];
        tobj.preserve();
        newList.vector.Add( tobj );
      }

      return newList;
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
      System.Text.StringBuilder sbuf = new System.Text.StringBuilder();
      int size = vector.Count;

      try
      {
        for ( int i = 0 ; i < size ; i++ )
        {
          Object elm = vector[i];
          if ( elm != null )
          {

            Util.appendElement( null, sbuf, elm.ToString() );
          }
          else
          {
            Util.appendElement( null, sbuf, "" );
          }
        }
      }
      catch ( TclException e )
      {
        throw new TclRuntimeError( "unexpected TclException: " + e.Message, e );
      }

      return sbuf.ToString();
    }

    /// <summary> Creates a new instance of a TclObject with a TclList internal
    /// rep.
    /// 
    /// </summary>
    /// <returns> the TclObject with the given list value.
    /// </returns>

    public static TclObject newInstance()
    {
      return new TclObject( new TclList() );
    }

    /// <summary> Called to convert the other object's internal rep to list.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the TclObject to convert to use the List internal rep.
    /// </param>
    /// <exception cref=""> TclException if the object doesn't contain a valid list.
    /// </exception>
    internal static void setListFromAny( Interp interp, TclObject tobj )
    {
      InternalRep rep = tobj.InternalRep;

      if ( !( rep is TclList ) )
      {
        TclList tlist = new TclList();

        splitList( interp, tlist.vector, tobj.ToString() );
        tobj.InternalRep = tlist;
      }
    }

    /// <summary> Splits a list (in string rep) up into its constituent fields.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="v">store the list elements in this vector.
    /// </param>
    /// <param name="s">the string to convert into a list.
    /// </param>
    /// <exception cref=""> TclException if the object doesn't contain a valid list.
    /// </exception>
    private static void splitList( Interp interp, ArrayList v, string s )
    {
      int len = s.Length;
      int i = 0;

      while ( i < len )
      {
        FindElemResult res = Util.findElement( interp, s, i, len );
        if ( res == null )
        {
          break;
        }
        else
        {
          TclObject tobj = TclString.newInstance( res.elem );
          tobj.preserve();
          v.Add( tobj );
        }
        i = res.elemEnd;
      }
    }


    /// <summary> Tcl_ListObjAppendElement -> TclList.append()
    /// 
    /// Appends a TclObject element to a list object.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the TclObject to append an element to.
    /// </param>
    /// <param name="elemObj">the element to append to the object.
    /// </param>
    /// <exception cref=""> TclException if tobj cannot be converted into a list.
    /// </exception>
    public static void append( Interp interp, TclObject tobj, TclObject elemObj )
    {
      if ( tobj.Shared )
      {
        throw new TclRuntimeError( "TclList.append() called with shared object" );
      }
      setListFromAny( interp, tobj );
      tobj.invalidateStringRep();

      TclList tlist = (TclList)tobj.InternalRep;

      if ( !String.IsNullOrEmpty( elemObj.stringRep ) && elemObj.stringRep[0] == '{' ) elemObj = TclString.newInstance( elemObj.stringRep.Substring( 1, elemObj.stringRep.Length - 2 ) );
      elemObj.preserve();
      tlist.vector.Add( elemObj );
    }

    /// <summary> Queries the length of the list. If tobj is not a list object,
    /// an attempt will be made to convert it to a list.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the TclObject to use as a list.
    /// </param>
    /// <returns> the length of the list.
    /// </returns>
    /// <exception cref=""> TclException if tobj is not a valid list.
    /// </exception>
    public static int getLength( Interp interp, TclObject tobj )
    {
      setListFromAny( interp, tobj );

      TclList tlist = (TclList)tobj.InternalRep;
      return tlist.vector.Count;
    }

    /// <summary> Returns a TclObject array of the elements in a list object.  If
    /// tobj is not a list object, an attempt will be made to convert
    /// it to a list. <p>
    /// 
    /// The objects referenced by the returned array should be treated
    /// as readonly and their ref counts are _not_ incremented; the
    /// caller must do that if it holds on to a reference.
    /// 
    /// </summary>
    /// <param name="interp">the current interpreter.
    /// </param>
    /// <param name="tobj">the list to sort.
    /// </param>
    /// <returns> a TclObject array of the elements in a list object.
    /// </returns>
    /// <exception cref=""> TclException if tobj is not a valid list.
    /// </exception>
    public static TclObject[] getElements( Interp interp, TclObject tobj )
    {
      setListFromAny( interp, tobj );
      TclList tlist = (TclList)tobj.InternalRep;

      int size = tlist.vector.Count;
      TclObject[] objArray = new TclObject[size];
      for ( int i = 0 ; i < size ; i++ )
      {
        objArray[i] = (TclObject)tlist.vector[i];
      }
      return objArray;
    }

    /// <summary> This procedure returns a pointer to the index'th object from
    /// the list referenced by tobj. The first element has index
    /// 0. If index is negative or greater than or equal to the number
    /// of elements in the list, a null is returned. If tobj is not a
    /// list object, an attempt will be made to convert it to a list.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the TclObject to use as a list.
    /// </param>
    /// <param name="index">the index of the requested element.
    /// </param>
    /// <returns> the the requested element.
    /// </returns>
    /// <exception cref=""> TclException if tobj is not a valid list.
    /// </exception>
    public static TclObject index( Interp interp, TclObject tobj, int index )
    {
      setListFromAny( interp, tobj );

      TclList tlist = (TclList)tobj.InternalRep;
      if ( index < 0 || index >= tlist.vector.Count )
      {
        return null;
      }
      else
      {
        return (TclObject)tlist.vector[index];
      }
    }

    /// <summary> This procedure inserts the elements in elements[] into the list at
    /// the given index. If tobj is not a list object, an attempt will
    /// be made to convert it to a list.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the TclObject to use as a list.
    /// </param>
    /// <param name="index">the starting index of the insertion operation. <=0 means
    /// the beginning of the list. >= TclList.getLength(tobj) means
    /// the end of the list.
    /// </param>
    /// <param name="elements">the element(s) to insert.
    /// </param>
    /// <param name="from">insert elements starting from elements[from] (inclusive)
    /// </param>
    /// <param name="to">insert elements up to elements[to] (inclusive)
    /// </param>
    /// <exception cref=""> TclException if tobj is not a valid list.
    /// </exception>
    internal static void insert( Interp interp, TclObject tobj, int index, TclObject[] elements, int from, int to )
    {
      if ( tobj.Shared )
      {
        throw new TclRuntimeError( "TclList.insert() called with shared object" );
      }
      replace( interp, tobj, index, 0, elements, from, to );
    }

    /// <summary> This procedure replaces zero or more elements of the list
    /// referenced by tobj with the objects from an TclObject array.
    /// If tobj is not a list object, an attempt will be made to
    /// convert it to a list.
    /// 
    /// </summary>
    /// <param name="interp">current interpreter.
    /// </param>
    /// <param name="tobj">the TclObject to use as a list.
    /// </param>
    /// <param name="index">the starting index of the replace operation. <=0 means
    /// the beginning of the list. >= TclList.getLength(tobj) means
    /// the end of the list.
    /// </param>
    /// <param name="count">the number of elements to delete from the list. <=0 means
    /// no elements should be deleted and the operation is equivalent to
    /// an insertion operation.
    /// </param>
    /// <param name="elements">the element(s) to insert.
    /// </param>
    /// <param name="from">insert elements starting from elements[from] (inclusive)
    /// </param>
    /// <param name="to">insert elements up to elements[to] (inclusive)
    /// </param>
    /// <exception cref=""> TclException if tobj is not a valid list.
    /// </exception>
    public static void replace( Interp interp, TclObject tobj, int index, int count, TclObject[] elements, int from, int to )
    {
      if ( tobj.Shared )
      {
        throw new TclRuntimeError( "TclList.replace() called with shared object" );
      }
      setListFromAny( interp, tobj );
      tobj.invalidateStringRep();
      TclList tlist = (TclList)tobj.InternalRep;

      int size = tlist.vector.Count;
      int i;

      if ( index >= size )
      {
        // Append to the end of the list. There is no need for deleting
        // elements.
        index = size;
      }
      else
      {
        if ( index < 0 )
        {
          index = 0;
        }
        if ( count > size - index )
        {
          count = size - index;
        }
        for ( i = 0 ; i < count ; i++ )
        {
          TclObject obj = (TclObject)tlist.vector[index];
          obj.release();
          tlist.vector.RemoveAt( index );
        }
      }
      for ( i = from ; i <= to ; i++ )
      {
        elements[i].preserve();
        tlist.vector.Insert( index++, elements[i] );
      }
    }

    /// <summary> Sorts the list according to the sort mode and (optional) sort command.
    /// The resulting list will contain no duplicates, if argument unique is
    /// specifed as true.
    /// If tobj is not a list object, an attempt will be made to
    /// convert it to a list.
    /// 
    /// </summary>
    /// <param name="interp">the current interpreter.
    /// </param>
    /// <param name="tobj">the list to sort.
    /// </param>
    /// <param name="sortMode">the sorting mode.
    /// </param>
    /// <param name="sortIncreasing">true if to sort the elements in increasing order.
    /// </param>
    /// <param name="command">the command to compute the order of two elements.
    /// </param>
    /// <param name="unique">true if the result should contain no duplicates.
    /// </param>
    /// <exception cref=""> TclException if tobj is not a valid list.
    /// </exception>

    internal static void sort( Interp interp, TclObject tobj, int sortMode, int sortIndex, bool sortIncreasing, string command, bool unique )
    {
      setListFromAny( interp, tobj );
      tobj.invalidateStringRep();
      TclList tlist = (TclList)tobj.InternalRep;

      int size = tlist.vector.Count;

      if ( size <= 1 )
      {
        return;
      }

      TclObject[] objArray = new TclObject[size];
      for ( int i = 0 ; i < size ; i++ )
      {
        objArray[i] = (TclObject)tlist.vector[i];
      }

      QSort s = new QSort();
      int newsize = s.sort( interp, objArray, sortMode, sortIndex, sortIncreasing, command, unique );

      for ( int i = 0 ; i < size   ; i++ )
      {
        if ( i < newsize )
        {
          tlist.vector[i] = objArray[i];
          objArray[i] = null;
        }
        else tlist.vector.RemoveAt( newsize  );
      }
    }
  }
}
