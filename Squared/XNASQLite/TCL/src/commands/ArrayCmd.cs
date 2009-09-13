/*
* ArrayCmd.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: ArrayCmd.java,v 1.4 2003/01/10 01:57:57 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "array" command in Tcl.</summary>
	
	class ArrayCmd : Command
	{
		internal static Type procClass = null;
		
		private static readonly string[] validCmds = new string[]{"anymore", "donesearch", "exists", "get", "names", "nextelement", "set", "size", "startsearch", "unset"};
		
		internal const int OPT_ANYMORE = 0;
		internal const int OPT_DONESEARCH = 1;
		internal const int OPT_EXISTS = 2;
		internal const int OPT_GET = 3;
		internal const int OPT_NAMES = 4;
		internal const int OPT_NEXTELEMENT = 5;
		internal const int OPT_SET = 6;
		internal const int OPT_SIZE = 7;
		internal const int OPT_STARTSEARCH = 8;
		internal const int OPT_UNSET = 9;
		
		/// <summary> This procedure is invoked to process the "array" Tcl command.
		/// See the user documentation for details on what it does.
		/// </summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			Var var = null, array = null;
			bool notArray = false;
			string varName, msg;
			int index;//, result;
			
			if (objv.Length < 3)
			{
				throw new TclNumArgsException(interp, 1, objv, "option arrayName ?arg ...?");
			}
			
			index = TclIndex.get(interp, objv[1], validCmds, "option", 0);
			
			// Locate the array variable (and it better be an array).
			
			
			varName = objv[2].ToString();
			Var[] retArray = Var.lookupVar(interp, varName, null, 0, null, false, false);
			
			// Assign the values returned in the array
			if (retArray != null)
			{
				var = retArray[0];
				array = retArray[1];
			}
			
			if ((var == null) || !var.isVarArray() || var.isVarUndefined())
			{
				notArray = true;
			}
			
			// Special array trace used to keep the env array in sync for
			// array names, array get, etc.
			
			if (var != null && var.traces != null)
			{
				msg = Var.callTraces(interp, array, var, varName, null, (TCL.VarFlag.LEAVE_ERR_MSG | TCL.VarFlag.NAMESPACE_ONLY | TCL.VarFlag.GLOBAL_ONLY | TCL.VarFlag.TRACE_ARRAY));
				if ((System.Object) msg != null)
				{
					throw new TclVarException(interp, varName, null, "trace array", msg);
				}
			}
			
			switch (index)
			{
				
				case OPT_ANYMORE:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName searchId");
						}
						if (notArray)
						{
							
							errorNotArray(interp, objv[2].ToString());
						}
						
						if (var.sidVec == null)
						{
							
							errorIllegalSearchId(interp, objv[2].ToString(), objv[3].ToString());
						}
						
						
						SearchId e = var.getSearch(objv[3].ToString());
						if (e == null)
						{
							
							errorIllegalSearchId(interp, objv[2].ToString(), objv[3].ToString());
						}
						
						if (e.HasMore)
						{
							interp.setResult("1");
						}
						else
						{
							interp.setResult("0");
						}
						break;
					}
				
				case OPT_DONESEARCH:  {
						
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName searchId");
						}
						if (notArray)
						{
							
							errorNotArray(interp, objv[2].ToString());
						}
						
						bool rmOK = true;
						if (var.sidVec != null)
						{
							
							rmOK = (var.removeSearch(objv[3].ToString()));
						}
						if ((var.sidVec == null) || !rmOK)
						{
							
							errorIllegalSearchId(interp, objv[2].ToString(), objv[3].ToString());
						}
						break;
					}
				
				case OPT_EXISTS:  {
						
						if (objv.Length != 3)
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName");
						}
						interp.setResult(!notArray);
						break;
					}
				
				case OPT_GET:  {
						// Due to the differences in the hashtable implementation 
						// from the Tcl core and Java, the output will be rearranged.
						// This is not a negative side effect, however, test results 
						// will differ.
						
						if ((objv.Length != 3) && (objv.Length != 4))
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName ?pattern?");
						}
						if (notArray)
						{
              return TCL.CompletionCode.RETURN;
						}
						
						string pattern = null;
						if (objv.Length == 4)
						{
							
							pattern = objv[3].ToString();
						}
						
						Hashtable table = (Hashtable) var.value;
						TclObject tobj = TclList.newInstance();
						
						string arrayName = objv[2].ToString();
						string key, strValue;
						Var var2;
						
						// Go through each key in the hash table.  If there is a 
						// pattern, test for a match.  Each valid key and its value 
						// is written into sbuf, which is returned.
						
						// FIXME : do we need to port over the 8.1 code for this loop?
						
						for (IDictionaryEnumerator e = table.GetEnumerator(); e.MoveNext(); )
						{
							key = ((string)e.Key);
							var2 = (Var)e.Value;
							if (var2.isVarUndefined())
							{
								continue;
							}
							
							if ((System.Object) pattern != null && !Util.stringMatch(key, pattern))
							{
								continue;
							}
							
							
							strValue = interp.getVar(arrayName, key, 0).ToString();
							
							TclList.append(interp, tobj, TclString.newInstance(key));
							TclList.append(interp, tobj, TclString.newInstance(strValue));
						}
						interp.setResult(tobj);
						break;
					}
				
				case OPT_NAMES:  {
						
						if ((objv.Length != 3) && (objv.Length != 4))
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName ?pattern?");
						}
						if (notArray)
						{
              return TCL.CompletionCode.RETURN;
						}
						
						string pattern = null;
						if (objv.Length == 4)
						{
							
							pattern = objv[3].ToString();
						}
						
						Hashtable table = (Hashtable) var.value;
						TclObject tobj = TclList.newInstance();
						string key;
						
						// Go through each key in the hash table.  If there is a 
						// pattern, test for a match. Each valid key and its value 
						// is written into sbuf, which is returned.
						
						for (IDictionaryEnumerator e = table.GetEnumerator(); e.MoveNext(); )
						{
							key = (string)e.Key;
							Var elem = (Var)e.Value;
							if (!elem.isVarUndefined())
							{
								if ((System.Object) pattern != null)
								{
									if (!Util.stringMatch(key, pattern))
									{
										continue;
									}
								}
								TclList.append(interp, tobj, TclString.newInstance(key));
							}
						}
						interp.setResult(tobj);
						break;
					}
				
				case OPT_NEXTELEMENT:  {
						
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName searchId");
						}
						if (notArray)
						{
							
							errorNotArray(interp, objv[2].ToString());
						}
						
						if (var.sidVec == null)
						{
							
							errorIllegalSearchId(interp, objv[2].ToString(), objv[3].ToString());
						}
						
						
						SearchId e = var.getSearch(objv[3].ToString());
						if (e == null)
						{
							
							errorIllegalSearchId(interp, objv[2].ToString(), objv[3].ToString());
						}
						if (e.HasMore)
						{
							Hashtable table = (Hashtable) var.value;
							DictionaryEntry entry = e.nextEntry();
							string key = (string)entry.Key;
							Var elem = (Var)entry.Value;
							if ((elem.flags & VarFlags.UNDEFINED) == 0)
							{
								interp.setResult(key);
							}
							else
							{
								interp.setResult("");
							}
						}
						break;
					}
				
				case OPT_SET:  {
						
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName list");
						}
						int size = TclList.getLength(interp, objv[3]);
						if (size % 2 != 0)
						{
							throw new TclException(interp, "list must have an even number of elements");
						}
						
						int i;
						
						string name1 = objv[2].ToString();
						string name2, strValue;
						
						// Set each of the array variable names in the interp
						
						for (i = 0; i < size; i++)
						{
							
							name2 = TclList.index(interp, objv[3], i++).ToString();
							
							strValue = TclList.index(interp, objv[3], i).ToString();
							interp.setVar(name1, name2, TclString.newInstance(strValue), 0);
						}
						break;
					}
				
				case OPT_SIZE:  {
						
						if (objv.Length != 3)
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName");
						}
						if (notArray)
						{
							interp.setResult(0);
						}
						else
						{
							Hashtable table = (Hashtable) var.value;
							int size = 0;
							for (IDictionaryEnumerator e = table.GetEnumerator(); e.MoveNext(); )
							{
								Var elem = (Var)e.Value;
								if ((elem.flags & VarFlags.UNDEFINED) == 0)
								{
									size++;
								}
							}
							interp.setResult(size);
						}
						break;
					}
				
				case OPT_STARTSEARCH:  {
						
						if (objv.Length != 3)
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName");
						}
						if (notArray)
						{
							
							errorNotArray(interp, objv[2].ToString());
						}
						
						if (var.sidVec == null)
						{
							var.sidVec = new ArrayList(10);
						}
						
						// Create a SearchId Object:
						// To create a new SearchId object, a unique string
						// identifier needs to be composed and we need to
						// create an Enumeration of the array keys.  The
						// unique string identifier is created from three
						// strings:
						//
						//     "s-"   is the default prefix
						//     "i"    is a unique number that is 1+ the greatest
						//	      SearchId index currently on the ArrayVar.
						//     "name" is the name of the array
						//
						// Once the SearchId string is created we construct a
						// new SearchId object using the string and the
						// Enumeration.  From now on the string is used to
						// uniquely identify the SearchId object.
						
						int i = var.NextIndex;
						
						string s = "s-" + i + "-" + objv[2].ToString();
						IDictionaryEnumerator e = ((Hashtable)var.value).GetEnumerator();
						var.sidVec.Add(new SearchId(e, s, i));
						interp.setResult(s);
						break;
					}
				
				case OPT_UNSET:  {
						string pattern;
						string name;
						
						if ((objv.Length != 3) && (objv.Length != 4))
						{
							throw new TclNumArgsException(interp, 2, objv, "arrayName ?pattern?");
						}
						if (notArray)
						{
							
							//Ignot this error -- errorNotArray(interp, objv[2].ToString());
              break;
            }
						if (objv.Length == 3)
						{
							// When no pattern is given, just unset the whole array
							
							interp.unsetVar(objv[2], 0);
						}
						else
						{
							
							pattern = objv[3].ToString();
							Hashtable table = (Hashtable) var.value;
							for (IDictionaryEnumerator e = table.GetEnumerator(); e.MoveNext(); )
							{
								name = (string)e.Key;
								Var elem = (Var)e.Value;
								if (var.isVarUndefined())
								{
									continue;
								}
								if (Util.stringMatch(name, pattern))
								{
									interp.unsetVar(varName, name, 0);
								}
							}
						}
						break;
					}
				}
        return TCL.CompletionCode.RETURN;
      }
		
		/// <summary> Error meassage thrown when an invalid identifier is used
		/// to access an array.
		/// 
		/// </summary>
		/// <param name="interp">currrent interpreter.
		/// </param>
		/// <param name="String">var is the string representation of the 
		/// variable that was passed in.
		/// </param>
		
		private static void  errorNotArray(Interp interp, string var)
		{
			throw new TclException(interp, "\"" + var + "\" isn't an array");
		}
		
		
		/// <summary> Error message thrown when an invalid SearchId is used.  The 
		/// string used to reference the SearchId is parced to determine
		/// the reason for the failure. 
		/// 
		/// </summary>
		/// <param name="interp">currrent interpreter.
		/// </param>
		/// <param name="String">sid is the string represenation of the 
		/// SearchId that was passed in.
		/// </param>
		
		internal static void  errorIllegalSearchId(Interp interp, string varName, string sid)
		{
			
			int val = validSearchId(sid.ToCharArray(), varName);
			
			if (val == 1)
			{
				throw new TclException(interp, "couldn't find search \"" + sid + "\"");
			}
			else if (val == 0)
			{
				throw new TclException(interp, "illegal search identifier \"" + sid + "\"");
			}
			else
			{
				throw new TclException(interp, "search identifier \"" + sid + "\" isn't for variable \"" + varName + "\"");
			}
		}
		
		/// <summary> A valid SearchId is represented by the format s-#-arrayName.  If
		/// the SearchId string does not match this format than it is illegal,
		/// else we cannot find it.  This method is used by the 
		/// ErrorIllegalSearchId method to determine the type of error message.
		/// 
		/// </summary>
		/// <param name="char">pattern[] is the string use dto identify the SearchId
		/// </param>
		/// <returns> 1 if its a valid searchID; 0 if it is not a valid searchId, 
		/// but it is for the array, -1 if it is not a valid searchId and NOT 
		/// for the array.
		/// </returns>
		
		private static int validSearchId(char[] pattern, string varName)
		{
			int i;
			
			if ((pattern[0] != 's') || (pattern[1] != '-') || (pattern[2] < '0') || (pattern[2] > '9'))
			{
				return 0;
			}
			for (i = 3; (i < pattern.Length && pattern[i] != '-'); i++)
			{
				if (pattern[i] < '0' || pattern[i] > '9')
				{
					return 0;
				}
			}
			if (++i >= pattern.Length)
			{
				return 0;
			}
			if (varName.Equals(new string(pattern, i, (pattern.Length - i))))
			{
				return 1;
			}
			else
			{
				return - 1;
			}
		}
	}
}
