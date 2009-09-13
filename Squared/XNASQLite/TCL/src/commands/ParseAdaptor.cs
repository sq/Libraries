#undef DEBUG
/*
* ParseAdaptor.java --
*
*	Temporary adaptor class that creates the interface from the 
*	current expression parser to the new Parser class.
*
* Copyright (c) 1997 by Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and redistribution
* of this file, and for a DISCLAIMER OF ALL WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: ParseAdaptor.java,v 1.6 2003/02/05 09:24:40 mdejong Exp $
*/
using System;
namespace tcl.lang
{
	
	class ParseAdaptor
	{
		internal static ParseResult parseVar(Interp interp, string inString, int index, int length)
		{
			ParseResult result;
			
			index--;
			result = Parser.parseVar(interp, inString.Substring(index, (length) - (index)));
			result.nextIndex += index;
			return (result);
		}
		internal static ParseResult parseNestedCmd(Interp interp, string inString, int index, int length)
		{
			CharPointer script;
			TclObject obj;
			
			// Check for the easy case where the last character in the string is '['.
			if (index == length)
			{
				throw new TclException(interp, "missing close-bracket");
			}
			
			script = new CharPointer(inString);
			script.index = index;
			
			interp.evalFlags |= Parser.TCL_BRACKET_TERM;
			Parser.eval2(interp, script.array, script.index, length - index, 0);
			obj = interp.getResult();
			obj.preserve();
			return (new ParseResult(obj, index + interp.termOffset + 1));
		}
		internal static ParseResult parseQuotes(Interp interp, string inString, int index, int length)
		{
			TclObject obj;
			TclParse parse = null;
			TclToken token;
			CharPointer script;
			
			try
			{
				
				script = new CharPointer(inString);
				script.index = index;
				
				parse = new TclParse(interp, script.array, length, null, 0);
				
					System.Diagnostics.Debug.WriteLine("string is \"" + inString + "\"");
					System.Diagnostics.Debug.WriteLine("script.array is \"" + new string(script.array) + "\"");
					
					System.Diagnostics.Debug.WriteLine("index is " + index);
					System.Diagnostics.Debug.WriteLine("length is " + length);
					
				System.Diagnostics.Debug.WriteLine("parse.endIndex is " + parse.endIndex);
				
				
				parse.commandStart = script.index;
				token = parse.getToken(0);
				token.type = Parser.TCL_TOKEN_WORD;
				token.script_array = script.array;
				token.script_index = script.index;
				parse.numTokens++;
				parse.numWords++;
				parse = Parser.parseTokens(script.array, script.index, Parser.TYPE_QUOTE, parse);
				
				// Check for the error condition where the parse did not end on
				// a '"' char. Is this happened raise an error.
				
				if (script.array[parse.termIndex] != '"')
				{
					throw new TclException(interp, "missing \"");
				}
				
				// if there was no error then parsing will continue after the
				// last char that was parsed from the string
				
				script.index = parse.termIndex + 1;
				
				// Finish filling in the token for the word and check for the
				// special case of a word consisting of a single range of
				// literal text.
				
				token = parse.getToken(0);
				token.size = script.index - token.script_index;
				token.numComponents = parse.numTokens - 1;
				if ((token.numComponents == 1) && (parse.getToken(1).type == Parser.TCL_TOKEN_TEXT))
				{
					token.type = Parser.TCL_TOKEN_SIMPLE_WORD;
				}
				parse.commandSize = script.index - parse.commandStart;
				if (parse.numTokens > 0)
				{
					obj = Parser.evalTokens(interp, parse.tokenList, 1, parse.numTokens - 1);
				}
				else
				{
					throw new TclRuntimeError("parseQuotes error: null obj result");
				}
			}
			finally
			{
				parse.release();
			}
			
			return (new ParseResult(obj, script.index));
		}
		internal static ParseResult parseBraces(Interp interp, string str, int index, int length)
		{
			char[] arr = str.ToCharArray();
			int level = 1;
			
			for (int i = index; i < length; )
			{
				if (Parser.charType(arr[i]) == Parser.TYPE_NORMAL)
				{
					i++;
				}
				else if (arr[i] == '}')
				{
					level--;
					if (level == 0)
					{
						str = new string(arr, index, i - index);
						return new ParseResult(str, i + 1);
					}
					i++;
				}
				else if (arr[i] == '{')
				{
					level++;
					i++;
				}
				else if (arr[i] == '\\')
				{
					BackSlashResult bs = Parser.backslash(arr, i);
					i = bs.nextIndex;
				}
				else
				{
					i++;
				}
			}
			
			//if you run off the end of the string you went too far
			throw new TclException(interp, "missing close-brace");
		}
	} // end ParseAdaptor
}
