#undef DEBUG
/*
* TclToken.java --
*
*	For each word of a command, and for each piece of a word such as a
* 	variable reference, a TclToken is used to describe the word.
*
* 	Note: TclToken is designed to be write-once with respect to 
* 	setting the script and size variables.  Failure to do this 
* 	may lead to inconsistencies in calls to getTokenString(). 
*
* Copyright (c) 1997 by Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and redistribution
* of this file, and for a DISCLAIMER OF ALL WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclToken.java,v 1.2 1999/05/09 01:34:50 dejong Exp $
*/
using System;
namespace tcl.lang
{
	
	public class TclToken
	{
		 internal string TokenString
		{
			get
			{
				#if DEBUG				
				if ((script_index + size) > script_array.Length)
				{
					System.Diagnostics.Debug.WriteLine("Entered TclToken.getTokenString()");
					System.Diagnostics.Debug.WriteLine("hashCode() is " + GetHashCode());
					System.Diagnostics.Debug.WriteLine("script_array.length is " + script_array.Length);
					System.Diagnostics.Debug.WriteLine("script_index is " + script_index);
					System.Diagnostics.Debug.WriteLine("size is " + size);
					
					System.Diagnostics.Debug.Write("the string is \"");
					for (int k = 0; k < script_array.Length; k++)
					{
						System.Diagnostics.Debug.Write(script_array[k]);
					}
					System.Diagnostics.Debug.WriteLine("\"");
				}
				#endif
				
				return (new string(script_array, script_index, size));
			}
			
		}
		
		// Contains an array the references the script from where the
		// token originates from and an index to the first character
		// of the token inside the script.
		
		
		internal char[] script_array;
		internal int script_index;
		
		// Number of bytes in token. 
		
		public int size;
		
		// Type of token, such as TCL_TOKEN_WORD;  See Parse.java 
		// for valid types. 
		
		internal int type;
		
		// If this token is composed of other tokens, this field 
		// tells how many of them there are (including components
		// of components, etc.).  The component tokens immediately
		// follow this one.
		
		internal int numComponents;
		internal TclToken()
		{
			script_array = null;
			script_index = - 1;
		}
		public override string ToString()
		{
			System.Text.StringBuilder sbuf = new System.Text.StringBuilder();
			switch (type)
			{
				
				case Parser.TCL_TOKEN_WORD:  {
						sbuf.Append("\n  Token Type: TCL_TOKEN_WORD");
						break;
					}
				
				case Parser.TCL_TOKEN_SIMPLE_WORD:  {
						sbuf.Append("\n  Token Type: TCL_TOKEN_SIMPLE_WORD");
						break;
					}
				
				case Parser.TCL_TOKEN_TEXT:  {
						sbuf.Append("\n  Token Type: TCL_TOKEN_TEXT");
						break;
					}
				
				case Parser.TCL_TOKEN_BS:  {
						sbuf.Append("\n  Token Type: TCL_TOKEN_BS");
						break;
					}
				
				case Parser.TCL_TOKEN_COMMAND:  {
						sbuf.Append("\n  Token Type: TCL_TOKEN_COMMAND");
						break;
					}
				
				case Parser.TCL_TOKEN_VARIABLE:  {
						sbuf.Append("\n  Token Type: TCL_TOKEN_VARIABLE");
						break;
					}
				}
			sbuf.Append("\n  String:      " + TokenString);
			sbuf.Append("\n  String Size: " + TokenString.Length);
			sbuf.Append("\n  ScriptIndex: " + script_index);
			sbuf.Append("\n  NumComponents: " + numComponents);
			sbuf.Append("\n  Token Size: " + size);
			return sbuf.ToString();
		}
	} // end TclToken
}
