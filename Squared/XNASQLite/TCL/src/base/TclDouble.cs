/*
* TclDouble.java --
*
*	Implements the TclDouble internal object representation, as well
*	variable traces for the tcl_precision variable.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclDouble.java,v 1.2 2000/10/29 06:00:42 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the double object type in Tcl.
	*/
	
	public class TclDouble : InternalRep
	{
		
		/*
		* Internal representation of a double value.
		*/
		
		private double value;
    
    private TclDouble( double i )
		{
			value = i;
		}
		private TclDouble(Interp interp, string str)
		{
			value = Util.getDouble(interp, str);
		}
		public  InternalRep duplicate()
		{
			return new TclDouble(value);
		}
		public  void  dispose()
		{
		}
		public static TclObject newInstance(double d)
		// Initial value.
		{
			return new TclObject(new TclDouble(d));
		}
		private static void  setDoubleFromAny(Interp interp, TclObject tobj)
		{
			InternalRep rep = tobj.InternalRep;
			
			if (rep is TclDouble)
			{
				/*
				* Do nothing.
				*/
			}
			else if (rep is TclBoolean)
			{
				/*
				* Short-cut.
				*/
				
				bool b = TclBoolean.get(interp, tobj);
				if (b)
				{
					tobj.InternalRep = new TclDouble(1.0);
				}
				else
				{
					tobj.InternalRep = new TclDouble(0.0);
				}
			}
			else if (rep is TclInteger)
			{
				/*
				* Short-cut.
				*/
				
				int i = TclInteger.get(interp, tobj);
				tobj.InternalRep = new TclDouble(i);
			}
			else
			{
				tobj.InternalRep = new TclDouble(interp, tobj.ToString());
			}
		}
		public static double get(Interp interp, TclObject tobj)
		{
			InternalRep rep = tobj.InternalRep;
			TclDouble tdouble;
			
			if (!(rep is TclDouble))
			{
				setDoubleFromAny(interp, tobj);
				tdouble = (TclDouble) (tobj.InternalRep);
			}
			else
			{
				tdouble = (TclDouble) rep;
			}
			
			return tdouble.value;
		}
		public static void  set(TclObject tobj, double d)
		// The new value for the object. 
		{
			tobj.invalidateStringRep();
			InternalRep rep = tobj.InternalRep;
			
			if (rep is TclDouble)
			{
				TclDouble tdouble = (TclDouble) rep;
				tdouble.value = d;
			}
			else
			{
				tobj.InternalRep = new TclDouble(d);
			}
		}
		public override string ToString()
		{
			return Util.printDouble(value);
		}
	} // end TclDouble
}
