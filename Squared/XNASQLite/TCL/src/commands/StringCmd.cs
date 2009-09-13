/*
* StringCmd.java
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
* Copyright (c) 1998-2000 Scriptics Corporation.
* Copyright (c) 2000 Christian Krone.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: StringCmd.java,v 1.4 2000/08/20 08:37:47 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "string" command in Tcl.</summary>
	
	class StringCmd : Command
	{
		
		private static readonly string[] options = new string[]{"bytelength", "compare", "equal", "first", "index", "is", "last", "length", "map", "match", "range", "repeat", "replace", "tolower", "toupper", "totitle", "trim", "trimleft", "trimright", "wordend", "wordstart"};
		private const int STR_BYTELENGTH = 0;
		private const int STR_COMPARE = 1;
		private const int STR_EQUAL = 2;
		private const int STR_FIRST = 3;
		private const int STR_INDEX = 4;
		private const int STR_IS = 5;
		private const int STR_LAST = 6;
		private const int STR_LENGTH = 7;
		private const int STR_MAP = 8;
		private const int STR_MATCH = 9;
		private const int STR_RANGE = 10;
		private const int STR_REPEAT = 11;
		private const int STR_REPLACE = 12;
		private const int STR_TOLOWER = 13;
		private const int STR_TOUPPER = 14;
		private const int STR_TOTITLE = 15;
		private const int STR_TRIM = 16;
		private const int STR_TRIMLEFT = 17;
		private const int STR_TRIMRIGHT = 18;
		private const int STR_WORDEND = 19;
		private const int STR_WORDSTART = 20;
		
		private static readonly string[] isOptions = new string[]{"alnum", "alpha", "ascii", "control", "boolean", "digit", "double", "false", "graph", "integer", "lower", "print", "punct", "space", "true", "upper", "wordchar", "xdigit"};
		private const int STR_IS_ALNUM = 0;
		private const int STR_IS_ALPHA = 1;
		private const int STR_IS_ASCII = 2;
		private const int STR_IS_CONTROL = 3;
		private const int STR_IS_BOOL = 4;
		private const int STR_IS_DIGIT = 5;
		private const int STR_IS_DOUBLE = 6;
		private const int STR_IS_FALSE = 7;
		private const int STR_IS_GRAPH = 8;
		private const int STR_IS_INT = 9;
		private const int STR_IS_LOWER = 10;
		private const int STR_IS_PRINT = 11;
		private const int STR_IS_PUNCT = 12;
		private const int STR_IS_SPACE = 13;
		private const int STR_IS_TRUE = 14;
		private const int STR_IS_UPPER = 15;
		private const int STR_IS_WORD = 16;
		private const int STR_IS_XDIGIT = 17;
		
		/// <summary> Java's Character class has a many boolean test functions to check
		/// the kind of a character (like isLowerCase() or isISOControl()).
		/// Unfortunately some are missing (like isPunct() or isPrint()), so
		/// here we define bitsets to compare the result of Character.getType().
		/// </summary>
		
		private static readonly int ALPHA_BITS = ((1 << (byte) System.Globalization.UnicodeCategory.UppercaseLetter) | (1 << (byte) System.Globalization.UnicodeCategory.LowercaseLetter) | (1 << (byte) System.Globalization.UnicodeCategory.TitlecaseLetter) | (1 << (byte) System.Globalization.UnicodeCategory.ModifierLetter) | (1 << (byte) System.Globalization.UnicodeCategory.OtherLetter));
		private static readonly int PUNCT_BITS = ((1 << (byte) System.Globalization.UnicodeCategory.ConnectorPunctuation) | (1 << (byte) System.Globalization.UnicodeCategory.DashPunctuation) | (1 << (byte) System.Globalization.UnicodeCategory.InitialQuotePunctuation) | (1 << (byte) System.Globalization.UnicodeCategory.FinalQuotePunctuation) | (1 << (byte) System.Globalization.UnicodeCategory.OtherPunctuation));
		private static readonly int PRINT_BITS = (ALPHA_BITS | (1 << (byte) System.Globalization.UnicodeCategory.DecimalDigitNumber) | (1 << (byte) System.Globalization.UnicodeCategory.SpaceSeparator) | (1 << (byte) System.Globalization.UnicodeCategory.LineSeparator) | (1 << (byte) System.Globalization.UnicodeCategory.ParagraphSeparator) | (1 << (byte) System.Globalization.UnicodeCategory.NonSpacingMark) | (1 << (byte) System.Globalization.UnicodeCategory.EnclosingMark) | (1 << (byte) System.Globalization.UnicodeCategory.SpacingCombiningMark) | (1 << (byte) System.Globalization.UnicodeCategory.LetterNumber) | (1 << (byte) System.Globalization.UnicodeCategory.OtherNumber) | PUNCT_BITS | (1 << (byte) System.Globalization.UnicodeCategory.MathSymbol) | (1 << (byte) System.Globalization.UnicodeCategory.CurrencySymbol) | (1 << (byte) System.Globalization.UnicodeCategory.ModifierSymbol) | (1 << (byte) System.Globalization.UnicodeCategory.OtherSymbol));
		private static readonly int WORD_BITS = (ALPHA_BITS | (1 << (byte) System.Globalization.UnicodeCategory.DecimalDigitNumber) | (1 << (byte) System.Globalization.UnicodeCategory.ConnectorPunctuation));
		
		/// <summary>----------------------------------------------------------------------
		/// 
		/// Tcl_StringObjCmd -> StringCmd.cmdProc
		/// 
		/// This procedure is invoked to process the "string" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// Results:
		/// None.
		/// 
		/// Side effects:
		/// See the user documentation.
		/// 
		/// ----------------------------------------------------------------------
		/// </summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "option arg ?arg ...?");
			}
			int index = TclIndex.get(interp, objv[1], options, "option", 0);
			
			switch (index)
			{
				
				case STR_EQUAL: 
				case STR_COMPARE:  {
						
						if (objv.Length < 4 || objv.Length > 7)
						{
							throw new TclNumArgsException(interp, 2, objv, "?-nocase? ?-length int? string1 string2");
						}
						
						bool nocase = false;
						int reqlength = - 1;
						for (int i = 2; i < objv.Length - 2; i++)
						{
							
							string string2 = objv[i].ToString();
							int length2 = string2.Length;
							if ((length2 > 1) && "-nocase".StartsWith(string2))
							{
								nocase = true;
							}
							else if ((length2 > 1) && "-length".StartsWith(string2))
							{
								if (i + 1 >= objv.Length - 2)
								{
									throw new TclNumArgsException(interp, 2, objv, "?-nocase? ?-length int? string1 string2");
								}
								reqlength = TclInteger.get(interp, objv[++i]);
							}
							else
							{
								throw new TclException(interp, "bad option \"" + string2 + "\": must be -nocase or -length");
							}
						}
						
						
						string string1 = objv[objv.Length - 2].ToString();
						
						string string3 = objv[objv.Length - 1].ToString();
						int length1 = string1.Length;
						int length3 = string3.Length;
						
						// This is the min length IN BYTES of the two strings
						
						int length = (length1 < length3)?length1:length3;
						
						int match;
						
						if (reqlength == 0)
						{
							// Anything matches at 0 chars, right?
							
							match = 0;
						}
						else if (nocase || ((reqlength > 0) && (reqlength <= length)))
						{
							// In Java, strings are always encoded in unicode, so we do
							// not need to worry about individual char lengths
							
							// Do the reqlength check again, against 0 as well for
							// the benfit of nocase
							
							if ((reqlength > 0) && (reqlength < length))
							{
								length = reqlength;
							}
							else if (reqlength < 0)
							{
								// The requested length is negative, so we ignore it by
								// setting it to the longer of the two lengths.
								
								reqlength = (length1 > length3)?length1:length3;
							}
							if (nocase)
							{
								string1 = string1.ToLower();
								string3 = string3.ToLower();
							}
							match = System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(string1,0,length,string3,0,length,System.Globalization.CompareOptions.Ordinal);
							// match = string1.Substring(0, (length) - (0)).CompareTo(string3.Substring(0, (length) - (0)));
							
							if ((match == 0) && (reqlength > length))
							{
								match = length1 - length3;
							}
						}
						else
						{
							match = System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(string1,0,length,string3,0,length,System.Globalization.CompareOptions.Ordinal);
							// ATK match = string1.Substring(0, (length) - (0)).CompareTo(string3.Substring(0, (length) - (0)));
							if (match == 0)
							{
								match = length1 - length3;
							}
						}
						
						if (index == STR_EQUAL)
						{
							interp.setResult((match != 0)?false:true);
						}
						else
						{
							interp.setResult(((match > 0)?1:(match < 0)?- 1:0));
						}
						break;
					}
				
				
				case STR_FIRST:  {
						if (objv.Length < 4 || objv.Length > 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "subString string ?startIndex?");
						}
						
						string string1 = objv[2].ToString();
						
						string string2 = objv[3].ToString();
						int length2 = string2.Length;
						
						int start = 0;
						
						if (objv.Length == 5)
						{
							// If a startIndex is specified, we will need to fast
							// forward to that point in the string before we think
							// about a match.
							
							start = Util.getIntForIndex(interp, objv[4], length2 - 1);
							if (start >= length2)
							{
								interp.setResult(- 1);
                return TCL.CompletionCode.RETURN;
							}
						}
						
						if (string1.Length == 0)
						{
							interp.setResult(- 1);
						}
						else
						{
							
							interp.setResult(string2.IndexOf(string1, start));
						}
						break;
					}
				
				
				case STR_INDEX:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "string charIndex");
						}
						
						
						string string1 = objv[2].ToString();
						int length1 = string1.Length;
						
						int i = Util.getIntForIndex(interp, objv[3], length1 - 1);
						
						if ((i >= 0) && (i < length1))
						{
							interp.setResult(string1.Substring(i, (i + 1) - (i)));
						}
						break;
					}
				
				
				case STR_IS:  {
						if (objv.Length < 4 || objv.Length > 7)
						{
							throw new TclNumArgsException(interp, 2, objv, "class ?-strict? ?-failindex var? str");
						}
						index = TclIndex.get(interp, objv[2], isOptions, "class", 0);
						
						bool strict = false;
						TclObject failVarObj = null;
						
						if (objv.Length != 4)
						{
							for (int i = 3; i < objv.Length - 1; i++)
							{
								
								string string2 = objv[i].ToString();
								int length2 = string2.Length;
								if ((length2 > 1) && "-strict".StartsWith(string2))
								{
									strict = true;
								}
								else if ((length2 > 1) && "-failindex".StartsWith(string2))
								{
									if (i + 1 >= objv.Length - 1)
									{
										throw new TclNumArgsException(interp, 3, objv, "?-strict? ?-failindex var? str");
									}
									failVarObj = objv[++i];
								}
								else
								{
									throw new TclException(interp, "bad option \"" + string2 + "\": must be -strict or -failindex");
								}
							}
						}
						
						bool result = true;
						int failat = 0;
						
						// We get the objPtr so that we can short-cut for some classes
						// by checking the object type (int and double), but we need
						// the string otherwise, because we don't want any conversion
						// of type occuring (as, for example, Tcl_Get*FromObj would do
						
						TclObject obj = objv[objv.Length - 1];
						
						string string1 = obj.ToString();
						int length1 = string1.Length;
						if (length1 == 0)
						{
							if (strict)
							{
								result = false;
							}
						}
						
						switch (index)
						{
							
							case STR_IS_BOOL: 
							case STR_IS_TRUE: 
							case STR_IS_FALSE:  {
									if (obj.InternalRep is TclBoolean)
									{
										if (((index == STR_IS_TRUE) && !TclBoolean.get(interp, obj)) || ((index == STR_IS_FALSE) && TclBoolean.get(interp, obj)))
										{
											result = false;
										}
									}
									else
									{
										try
										{
											bool i = TclBoolean.get(null, obj);
											if (((index == STR_IS_TRUE) && !i) || ((index == STR_IS_FALSE) && i))
											{
												result = false;
											}
										}
										catch (TclException e)
										{
											result = false;
										}
									}
									break;
								}
							
							case STR_IS_DOUBLE:  {
									if ((obj.InternalRep is TclDouble) || (obj.InternalRep is TclInteger))
									{
										break;
									}
									
									// This is adapted from Tcl_GetDouble
									//
									// The danger in this function is that
									// "12345678901234567890" is an acceptable 'double',
									// but will later be interp'd as an int by something
									// like [expr].  Therefore, we check to see if it looks
									// like an int, and if so we do a range check on it.
									// If strtoul gets to the end, we know we either
									// received an acceptable int, or over/underflow
									
									if (Expression.looksLikeInt(string1, length1, 0))
									{
										char c = string1[0];
										int signIx = (c == '-' || c == '+')?1:0;
										StrtoulResult res = Util.strtoul(string1, signIx, 0);
										if (res.index == length1)
										{
											if (res.errno == TCL.INTEGER_RANGE)
											{
												result = false;
												failat = - 1;
											}
											break;
										}
									}
									
									char c2 = string1[0];
									int signIx2 = (c2 == '-' || c2 == '+')?1:0;
									StrtodResult res2 = Util.strtod(string1, signIx2);
									if (res2.errno == TCL.DOUBLE_RANGE)
									{
										// if (errno == ERANGE), then it was an over/underflow
										// problem, but in this method, we only want to know
										// yes or no, so bad flow returns 0 (false) and sets
										// the failVarObj to the string length.
										
										result = false;
										failat = - 1;
									}
									else if (res2.index == 0)
									{
										// In this case, nothing like a number was found
										
										result = false;
										failat = 0;
									}
									else
									{
										// Go onto SPACE, since we are
										// allowed trailing whitespace
										
										failat = res2.index;
										for (int i = res2.index; i < length1; i++)
										{
											if (!System.Char.IsWhiteSpace(string1[i]))
											{
												result = false;
												break;
											}
										}
									}
									break;
								}
							
							case STR_IS_INT:  {
									if (obj.InternalRep is TclInteger)
									{
										break;
									}
									bool isInteger = true;
									try
									{
										TclInteger.get(null, obj);
									}
									catch (TclException e)
									{
										isInteger = false;
									}
									if (isInteger)
									{
										break;
									}
									
									char c = string1[0];
									int signIx = (c == '-' || c == '+')?1:0;
									StrtoulResult res = Util.strtoul(string1, signIx, 0);
									if (res.errno == TCL.INTEGER_RANGE)
									{
										// if (errno == ERANGE), then it was an over/underflow
										// problem, but in this method, we only want to know
										// yes or no, so bad flow returns false and sets
										// the failVarObj to the string length.
										
										result = false;
										failat = - 1;
									}
									else if (res.index == 0)
									{
										// In this case, nothing like a number was found
										
										result = false;
										failat = 0;
									}
									else
									{
										// Go onto SPACE, since we are
										// allowed trailing whitespace
										
										failat = res.index;
										for (int i = res.index; i < length1; i++)
										{
											if (!System.Char.IsWhiteSpace(string1[i]))
											{
												result = false;
												break;
											}
										}
									}
									break;
								}
							
							default:  {
									for (failat = 0; failat < length1; failat++)
									{
										char c = string1[failat];
										switch (index)
										{
											
											case STR_IS_ASCII: 
												
												result = c < 0x80;
												break;
											
											case STR_IS_ALNUM: 
												result = System.Char.IsLetterOrDigit(c);
												break;
											
											case STR_IS_ALPHA: 
												result = System.Char.IsLetter(c);
												break;
											
											case STR_IS_DIGIT: 
												result = System.Char.IsDigit(c);
												break;
											
											case STR_IS_GRAPH: 
												result = ((1 << (int) System.Char.GetUnicodeCategory(c)) & PRINT_BITS) != 0 && c != ' ';
												break;
											
											case STR_IS_PRINT: 
												result = ((1 << (int) System.Char.GetUnicodeCategory(c)) & PRINT_BITS) != 0;
												break;
											
											case STR_IS_PUNCT: 
												result = ((1 << (int) System.Char.GetUnicodeCategory(c)) & PUNCT_BITS) != 0;
												break;
											
											case STR_IS_UPPER: 
												result = System.Char.IsUpper(c);
												break;
											
											case STR_IS_SPACE: 
												result = System.Char.IsWhiteSpace(c);
												break;
											
											case STR_IS_CONTROL: 
												result = (System.Char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Control);
												break;
											
											case STR_IS_LOWER: 
												result = System.Char.IsLower(c);
												break;
											
											case STR_IS_WORD: 
												result = ((1 << (int) System.Char.GetUnicodeCategory(c)) & WORD_BITS) != 0;
												break;
											
											case STR_IS_XDIGIT: 
												result = "0123456789ABCDEFabcdef".IndexOf(c) >= 0;
												break;
											
											default: 
												throw new TclRuntimeError("unimplemented");
											
										}
										if (!result)
										{
											break;
										}
									}
								}
								break;
							
						}
						
						// Only set the failVarObj when we will return 0
						// and we have indicated a valid fail index (>= 0)
						
						if ((!result) && (failVarObj != null))
						{
							interp.setVar(failVarObj, TclInteger.newInstance(failat), 0);
						}
						interp.setResult(result);
						break;
					}
				
				
				case STR_LAST:  {
						if (objv.Length < 4 || objv.Length > 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "subString string ?startIndex?");
						}
						
						string string1 = objv[2].ToString();
						
						string string2 = objv[3].ToString();
						int length2 = string2.Length;
						
						int start = 0;
						if (objv.Length == 5)
						{
							// If a startIndex is specified, we will need to fast
							// forward to that point in the string before we think
							// about a match.
							
							start = Util.getIntForIndex(interp, objv[4], length2 - 1);
							if (start < 0)
							{
								interp.setResult(- 1);
								break;
							}
							else if (start < length2)
							{
								string2 = string2.Substring(0, (start + 1) - (0));
							}
						}
						
						if (string1.Length == 0)
						{
							interp.setResult(- 1);
						}
						else
						{
							interp.setResult(string2.LastIndexOf(string1));
						}
						break;
					}
				
				
				case STR_BYTELENGTH: 
					if (objv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, objv, "string");
					}
					
					interp.setResult(Utf8Count(objv[2].ToString()));
					break;
				
				
				case STR_LENGTH:  {
						if (objv.Length != 3)
						{
							throw new TclNumArgsException(interp, 2, objv, "string");
						}
						
						interp.setResult(objv[2].ToString().Length);
						break;
					}
				
				
				case STR_MAP:  {
						if (objv.Length < 4 || objv.Length > 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "?-nocase? charMap string");
						}
						
						bool nocase = false;
						if (objv.Length == 5)
						{
							
							string string2 = objv[2].ToString();
							int length2 = string2.Length;
							if ((length2 > 1) && "-nocase".StartsWith(string2))
							{
								nocase = true;
							}
							else
							{
								throw new TclException(interp, "bad option \"" + string2 + "\": must be -nocase");
							}
						}
						
						TclObject[] mapElemv = TclList.getElements(interp, objv[objv.Length - 2]);
						if (mapElemv.Length == 0)
						{
							// empty charMap, just return whatever string was given
							
							interp.setResult(objv[objv.Length - 1]);
						}
						else if ((mapElemv.Length % 2) != 0)
						{
							// The charMap must be an even number of key/value items
							
							throw new TclException(interp, "char map list unbalanced");
						}
						
						string string1 = objv[objv.Length - 1].ToString();
						string cmpString1;
						if (nocase)
						{
							cmpString1 = string1.ToLower();
						}
						else
						{
							cmpString1 = string1;
						}
						int length1 = string1.Length;
						if (length1 == 0)
						{
							// Empty input string, just stop now
							
							break;
						}
						
						// Precompute pointers to the unicode string and length.
						// This saves us repeated function calls later,
						// significantly speeding up the algorithm.
						
						string[] mapStrings = new string[mapElemv.Length];
						int[] mapLens = new int[mapElemv.Length];
						for (int ix = 0; ix < mapElemv.Length; ix++)
						{
							
							mapStrings[ix] = mapElemv[ix].ToString();
							mapLens[ix] = mapStrings[ix].Length;
						}
						string[] cmpStrings;
						if (nocase)
						{
							cmpStrings = new string[mapStrings.Length];
							for (int ix = 0; ix < mapStrings.Length; ix++)
							{
								cmpStrings[ix] = mapStrings[ix].ToLower();
							}
						}
						else
						{
							cmpStrings = mapStrings;
						}
						
						TclObject result = TclString.newInstance("");
						int p, str1;
						for (p = 0, str1 = 0; str1 < length1; str1++)
						{
							for (index = 0; index < mapStrings.Length; index += 2)
							{
								// Get the key string to match on
								
								string string2 = mapStrings[index];
								int length2 = mapLens[index];
								if ((length2 > 0) && (cmpString1.Substring(str1).StartsWith(cmpStrings[index])))
								{
									if (p != str1)
									{
										// Put the skipped chars onto the result first
										
										TclString.append(result, string1.Substring(p, (str1) - (p)));
										p = str1 + length2;
									}
									else
									{
										p += length2;
									}
									
									// Adjust len to be full length of matched string
									
									str1 = p - 1;
									
									// Append the map value to the unicode string
									
									TclString.append(result, mapStrings[index + 1]);
									break;
								}
							}
						}
						
						if (p != str1)
						{
							// Put the rest of the unmapped chars onto result
							
							TclString.append(result, string1.Substring(p, (str1) - (p)));
						}
						interp.setResult(result);
						break;
					}
				
				
				case STR_MATCH:  {
						if (objv.Length < 4 || objv.Length > 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "?-nocase? pattern string");
						}
						
						string string1, string2;
						if (objv.Length == 5)
						{
							
							string inString = objv[2].ToString();
							if (!((inString.Length > 1) && "-nocase".StartsWith(inString)))
							{
								throw new TclException(interp, "bad option \"" + inString + "\": must be -nocase");
							}
							
							string1 = objv[4].ToString().ToLower();
							
							string2 = objv[3].ToString().ToLower();
						}
						else
						{
							
							string1 = objv[3].ToString();
							
							string2 = objv[2].ToString();
						}
						
						interp.setResult(Util.stringMatch(string1, string2));
						break;
					}
				
				
				case STR_RANGE:  {
						if (objv.Length != 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "string first last");
						}
						
						
						string string1 = objv[2].ToString();
						int length1 = string1.Length;
						
						int first = Util.getIntForIndex(interp, objv[3], length1 - 1);
						if (first < 0)
						{
							first = 0;
						}
						int last = Util.getIntForIndex(interp, objv[4], length1 - 1);
						if (last >= length1)
						{
							last = length1 - 1;
						}
						
						if (first > last)
						{
							interp.resetResult();
						}
						else
						{
							interp.setResult(string1.Substring(first, (last + 1) - (first)));
						}
						break;
					}
				
				
				case STR_REPEAT:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "string count");
						}
						
						int count = TclInteger.get(interp, objv[3]);
						
						
						string string1 = objv[2].ToString();
						if (string1.Length > 0)
						{
							TclObject tstr = TclString.newInstance("");
							for (index = 0; index < count; index++)
							{
								TclString.append(tstr, string1);
							}
							interp.setResult(tstr);
						}
						break;
					}
				
				
				case STR_REPLACE:  {
						if (objv.Length < 5 || objv.Length > 6)
						{
							throw new TclNumArgsException(interp, 2, objv, "string first last ?string?");
						}
						
						
						string string1 = objv[2].ToString();
						int length1 = string1.Length - 1;
						
						int first = Util.getIntForIndex(interp, objv[3], length1);
						int last = Util.getIntForIndex(interp, objv[4], length1);
						
						if ((last < first) || (first > length1) || (last < 0))
						{
							interp.setResult(objv[2]);
						}
						else
						{
							if (first < 0)
							{
								first = 0;
							}
							string start = string1.Substring(first);
							int ind = ((last > length1)?length1:last) - first + 1;
							string end;
							if (ind <= 0)
							{
								end = start;
							}
							else if (ind >= start.Length)
							{
								end = "";
							}
							else
							{
								end = start.Substring(ind);
							}
							
							TclObject tstr = TclString.newInstance(string1.Substring(0, (first) - (0)));
							
							if (objv.Length == 6)
							{
								TclString.append(tstr, objv[5]);
							}
							if (last < length1)
							{
								TclString.append(tstr, end);
							}
							
							interp.setResult(tstr);
						}
						break;
					}
				
				
				case STR_TOLOWER: 
				case STR_TOUPPER: 
				case STR_TOTITLE:  {
						if (objv.Length < 3 || objv.Length > 5)
						{
							throw new TclNumArgsException(interp, 2, objv, "string ?first? ?last?");
						}
						
						string string1 = objv[2].ToString();
						
						if (objv.Length == 3)
						{
							if (index == STR_TOLOWER)
							{
								interp.setResult(string1.ToLower());
							}
							else if (index == STR_TOUPPER)
							{
								interp.setResult(string1.ToUpper());
							}
							else
							{
								interp.setResult(Util.toTitle(string1));
							}
						}
						else
						{
							int length1 = string1.Length - 1;
							int first = Util.getIntForIndex(interp, objv[3], length1);
							if (first < 0)
							{
								first = 0;
							}
							int last = first;
							if (objv.Length == 5)
							{
								last = Util.getIntForIndex(interp, objv[4], length1);
							}
							if (last >= length1)
							{
								last = length1;
							}
							if (last < first)
							{
								interp.setResult(objv[2]);
								break;
							}
							
							string string2;
							System.Text.StringBuilder buf = new System.Text.StringBuilder();
							buf.Append(string1.Substring(0, (first) - (0)));
							if (last + 1 > length1)
							{
								string2 = string1.Substring(first);
							}
							else
							{
								string2 = string1.Substring(first, (last + 1) - (first));
							}
							if (index == STR_TOLOWER)
							{
								buf.Append(string2.ToLower());
							}
							else if (index == STR_TOUPPER)
							{
								buf.Append(string2.ToUpper());
							}
							else
							{
								buf.Append(Util.toTitle(string2));
							}
							if (last + 1 <= length1)
							{
								buf.Append(string1.Substring(last + 1));
							}
							
							interp.setResult(buf.ToString());
						}
						break;
					}
				
				
				case STR_TRIM:  {
						if (objv.Length == 3)
						{
							// Case 1: "string trim str" --
							// Remove leading and trailing white space
							
							
							interp.setResult(objv[2].ToString().Trim());
						}
						else if (objv.Length == 4)
						{
							
							// Case 2: "string trim str chars" --
							// Remove leading and trailing chars in the chars set
							
							
							string tmp = Util.TrimLeft(objv[2].ToString(), objv[3].ToString());
							
							interp.setResult(Util.TrimRight(tmp, objv[3].ToString()));
						}
						else
						{
							// Case 3: Wrong # of args
							
							throw new TclNumArgsException(interp, 2, objv, "string ?chars?");
						}
						break;
					}
				
				
				case STR_TRIMLEFT:  {
						if (objv.Length == 3)
						{
							// Case 1: "string trimleft str" --
							// Remove leading and trailing white space
							
							
							interp.setResult(Util.TrimLeft(objv[2].ToString()));
						}
						else if (objv.Length == 4)
						{
							// Case 2: "string trimleft str chars" --
							// Remove leading and trailing chars in the chars set
							
							
							interp.setResult(Util.TrimLeft(objv[2].ToString(), objv[3].ToString()));
						}
						else
						{
							// Case 3: Wrong # of args
							
							throw new TclNumArgsException(interp, 2, objv, "string ?chars?");
						}
						break;
					}
				
				
				case STR_TRIMRIGHT:  {
						if (objv.Length == 3)
						{
							// Case 1: "string trimright str" --
							// Remove leading and trailing white space
							
							
							interp.setResult(Util.TrimRight(objv[2].ToString()));
						}
						else if (objv.Length == 4)
						{
							// Case 2: "string trimright str chars" --
							// Remove leading and trailing chars in the chars set
							
							
							interp.setResult(Util.TrimRight(objv[2].ToString(), objv[3].ToString()));
						}
						else
						{
							// Case 3: Wrong # of args
							
							throw new TclNumArgsException(interp, 2, objv, "string ?chars?");
						}
						break;
					}
				
				
				case STR_WORDEND:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "string index");
						}
						
						
						string string1 = objv[2].ToString();
						char[] strArray = string1.ToCharArray();
						int cur;
						int length1 = string1.Length;
						index = Util.getIntForIndex(interp, objv[3], length1 - 1);
						
						if (index < 0)
						{
							index = 0;
						}
						if (index >= length1)
						{
							interp.setResult(length1);
              return TCL.CompletionCode.RETURN;
						}
						for (cur = index; cur < length1; cur++)
						{
							char c = strArray[cur];
							if (((1 << (int) System.Char.GetUnicodeCategory(c)) & WORD_BITS) == 0)
							{
								break;
							}
						}
						if (cur == index)
						{
							cur = index + 1;
						}
						interp.setResult(cur);
						break;
					}
				
				
				case STR_WORDSTART:  {
						if (objv.Length != 4)
						{
							throw new TclNumArgsException(interp, 2, objv, "string index");
						}
						
						
						string string1 = objv[2].ToString();
						char[] strArray = string1.ToCharArray();
						int cur;
						int length1 = string1.Length;
						index = Util.getIntForIndex(interp, objv[3], length1 - 1);
						
						if (index > length1)
						{
							index = length1 - 1;
						}
						if (index < 0)
						{
							interp.setResult(0);
              return TCL.CompletionCode.RETURN;
						}
						for (cur = index; cur >= 0; cur--)
						{
							char c = strArray[cur];
							if (((1 << (int) System.Char.GetUnicodeCategory(c)) & WORD_BITS) == 0)
							{
								break;
							}
						}
						if (cur != index)
						{
							cur += 1;
						}
						interp.setResult(cur);
						break;
					}
				}
        return TCL.CompletionCode.RETURN;
      }
		
		// return the number of Utf8 bytes that would be needed to store s
		
		private int Utf8Count(string s)
		{
			int p = 0;
						int len = s.Length;
			char c;
			int sum = 0;
			
			while (p < len)
			{
				c = s[p++];
				
				if ((c > 0) && (c < 0x80))
				{
					sum += 1;
					continue;
				}
				if (c <= 0x7FF)
				{
					sum += 2;
					continue;
				}
				if (c <= 0xFFFF)
				{
					sum += 3;
					continue;
				}
			}
			
			return sum;
		}
	} // end StringCmd
}
