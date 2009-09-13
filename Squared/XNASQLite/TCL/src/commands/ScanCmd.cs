/*
* ScanCmd.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: ScanCmd.java,v 1.2 1999/05/09 01:22:09 dejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "scan" command in Tcl.
	/// 
	/// </summary>
	
	class ScanCmd : Command
	{
		/// <summary> This procedure is invoked to process the "scan" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// Each iteration of the cmdProc compares the scanArr's current index to 
		/// the frmtArr's index.  If the chars are equal then the indicies are
		/// incremented.  If a '%' is found in the frmtArr, the formatSpecifier 
		/// is parced from the frmtArr, the corresponding value is extracted from 
		/// the scanArr, and that value is set in the Tcl Interp.
		/// 
		/// If the chars are not equal, or the conversion fails, the boolean 
		/// scanArrDone is set to true, indicating the scanArr is not to be 
		/// parced and no new values are to be set.  However the frmtArr is still 
		/// parced because of the priority of error messages.  In the C version 
		/// of Tcl, bad format specifiers throw errors before incorrect argument 
		/// input or other scan errors.  Thus we need to parce the entire frmtArr 
		/// to verify correct formating.  This is dumb and inefficient but it is 
		/// consistent w/ the current C-version of Tcl.
		/// </summary>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			
			if (argv.Length < 3)
			{
				throw new TclNumArgsException(interp, 1, argv, "string format ?varName varName ...?");
			}
			;
			
			StrtoulResult strul; // Return value for parcing the scanArr when
			// extracting integers/longs
			StrtodResult strd; ; // Return value for parcing the scanArr when
			// extracting doubles
			char[] scanArr; // Array containing parce info
			char[] frmtArr; // Array containing info on how to 
			// parse the scanArr
			int scanIndex; // Index into the scan array
			int frmtIndex; // Index into the frmt array
			int tempIndex; // Temporary index holder
			int argIndex; // Index into the current arg
			int width; // Stores the user specified result width 
			int base_; // Base of the integer being converted
			int numUnMatched; // Number of fields actually set.
			int numMatched; // Number of fields actually matched.
			int negateScan; // Mult by result, set to -1 if true
			int i; // Generic variable
			char ch; // Generic variable
			bool cont; // Used in loops to indicate when to stop
			bool scanOK; // Set to false if strtoul/strtod fails
			bool scanArrDone; // Set to false if strtoul/strtod fails
			bool widthFlag; // True is width is specified
			bool discardFlag; // If a "%*" is in the formatString dont 
			// write output to arg
			
			
			scanArr = argv[1].ToString().ToCharArray();
			
			frmtArr = argv[2].ToString().ToCharArray();
			width = base_ = numMatched = numUnMatched = 0;
			scanIndex = frmtIndex = 0;
			scanOK = true;
			scanArrDone = false;
			argIndex = 3;
			
			// Skip all (if any) of the white space before getting to a char
			
			frmtIndex = skipWhiteSpace(frmtArr, frmtIndex);
			
			// Search through the frmtArr.  If the next char is a '%' parse the
			// next chars and determine the type (if any) of the format specifier.
			// If the scanArr has been fully searched, do nothing but incerment
			// "numUnMatched".  The reason to continue the frmtArr search is for 
			// consistency in output.  Previously scan format errors were reported
			// before arg input mismatch, so this maintains the same level of error
			// checking.
			
			while (frmtIndex < frmtArr.Length)
			{
				discardFlag = widthFlag = false;
				negateScan = 1;
				cont = true;
				
				// Parce the format array and read in the correct value from the 
				// scan array.  When the correct value is retrieved, set the 
				// variable (from argv) in the interp.
				
				if (frmtArr[frmtIndex] == '%')
				{
					
					frmtIndex++;
					checkOverFlow(interp, frmtArr, frmtIndex);
					
					// Two '%'s in a row, do nothing...
					
					if (frmtArr[frmtIndex] == '%')
					{
						frmtIndex++;
						scanIndex++;
						continue;
					}
					
					// Check for a discard field flag
					
					if (frmtArr[frmtIndex] == '*')
					{
						discardFlag = true;
						frmtIndex++;
						checkOverFlow(interp, frmtArr, frmtIndex);
					}
					
					// Check for a width field and accept the 'h', 'l', 'L'
					// characters, but do nothing with them.
					//
					// Note: The order of the width specifier and the other
					// chars is unordered, so we need to iterate until all
					// of the specifiers are identified.
					
					while (cont)
					{
						cont = false;
						
						switch (frmtArr[frmtIndex])
						{
							
							case 'h': 
							case 'l': 
							case 'L':  {
									// Just ignore these values
									
									frmtIndex++;
									cont = true;
									break;
								}
							
							default:  {
									if (System.Char.IsDigit(frmtArr[frmtIndex]))
									{
										strul = Util.strtoul(new string(frmtArr), frmtIndex, base_);
										frmtIndex = strul.index;
										width = (int) strul.value;
										widthFlag = true;
										cont = true;
									}
								}
								break;
							
						}
						checkOverFlow(interp, frmtArr, frmtIndex);
					}
					
					// On all conversion specifiers except 'c', move the
					// scanIndex to the next non-whitespace.
					
					ch = frmtArr[frmtIndex];
					if ((ch != 'c') && (ch != '[') && !scanArrDone)
					{
						scanIndex = skipWhiteSpace(scanArr, scanIndex);
					}
					if (scanIndex >= scanArr.Length)
					{
						scanArrDone = true;
					}
					
					if ((scanIndex < scanArr.Length) && (ch != 'c') && (ch != '['))
					{
						// Since strtoul dosent take signed numbers, make the
						// value positive and store the sign.
						
						if (scanArr[scanIndex] == '-')
						{
							negateScan = - 1;
							scanIndex++;
							width--;
						}
						else if (scanArr[scanIndex] == '+')
						{
							scanIndex++;
							width--;
						}
						
						// The width+scanIndex might be greater than
						// the scanArr so we need to re-adjust when this
						// happens.
						
						if (widthFlag && (width + scanIndex > scanArr.Length))
						{
							width = scanArr.Length - scanIndex;
						}
					}
					
					if (scanIndex >= scanArr.Length)
					{
						scanArrDone = true;
					}
					
					// Foreach iteration we want strul and strd to be
					// null since we error check on this case.
					
					strul = null;
					strd = null;
					
					switch (ch)
					{
						
						case 'd': 
						case 'o': 
						case 'x':  {
								
								if (!scanArrDone)
								{
									
									if (ch == 'd')
									{
										base_ = 10;
									}
									else if (ch == 'o')
									{
										base_ = 8;
									}
									else
									{
										base_ = 16;
									}
									
									// If the widthFlag is set then convert only 
									// "width" characters to an ascii representation, 
									// else read in until the end of the integer.  The 
									// scanIndex is moved to the point where we stop
									// reading in.
									
									if (widthFlag)
									{
										strul = Util.strtoul(new string(scanArr, 0, width + scanIndex), scanIndex, base_);
									}
									else
									{
										strul = Util.strtoul(new string(scanArr), scanIndex, base_);
									}
									if (strul.errno != 0)
									{
										scanOK = false;
										break;
									}
									scanIndex = strul.index;
									
									if (!discardFlag)
									{
										i = (int) strul.value * negateScan;
                    if ( argIndex == argv.Length ) numMatched--;
                    else testAndSetVar( interp, argv, argIndex++, TclInteger.newInstance( i ) );
                  }
								}
								break;
							}
						
						case 'c':  {
								if (widthFlag)
								{
									errorCharFieldWidth(interp);
								}
								if (!discardFlag && !scanArrDone)
								{
									testAndSetVar(interp, argv, argIndex++, TclInteger.newInstance(scanArr[scanIndex++]));
								}
								break;
							}
						
						case 's':  {
								if (!scanArrDone)
								{
									// If the widthFlag is set then read only "width"
									// characters into the string, else read in until 
									// the first whitespace or endArr is found.  The 
									// scanIndex is moved to the point where we stop 
									// reading in.
									
									tempIndex = scanIndex;
									if (!widthFlag)
									{
										width = scanArr.Length;
									}
									for (i = 0; (scanIndex < scanArr.Length) && (i < width); i++)
									{
										ch = scanArr[scanIndex];
										if ((ch == ' ') || (ch == '\n') || (ch == '\r') || (ch == '\t') || (ch == '\f'))
										{
											break;
										}
										scanIndex++;
									}
									
									if (!discardFlag)
									{
										string str = new string(scanArr, tempIndex, scanIndex - tempIndex);
										testAndSetVar(interp, argv, argIndex++, TclString.newInstance(str));
									}
								}
								break;
							}
						
						case 'e': 
						case 'f': 
						case 'g':  {
								if (!scanArrDone)
								{
									// If the wisthFlag is set then read only "width"
									// characters into the string, else read in until 
									// the first whitespace or endArr is found.  The 
									// scanIndex is moved to the point where we stop 
									// reading in.
									
									if (widthFlag)
									{
										strd = Util.strtod(new string(scanArr, 0, width + scanIndex), scanIndex);
									}
									else
									{
										strd = Util.strtod(new string(scanArr), scanIndex);
									}
									if (strd.errno != 0)
									{
										scanOK = false;
										break;
									}
									scanIndex = strd.index;
									
									if (!discardFlag)
									{
										double d = strd.value * negateScan;
										testAndSetVar(interp, argv, argIndex++, TclDouble.newInstance(d));
									}
								}
								break;
							}
						
						case '[':  {
								bool charMatchFound = false;
								bool charNotMatch = false;
								char[] tempArr;
								int startIndex;
								int endIndex;
								string unmatched = "unmatched [ in format string";
								
								if ((++frmtIndex) >= frmtArr.Length)
								{
									throw new TclException(interp, unmatched);
								}
								
								if (frmtArr[frmtIndex] == '^')
								{
									charNotMatch = true;
									frmtIndex += 2;
								}
								else
								{
									frmtIndex++;
								}
								tempIndex = frmtIndex - 1;
								
								if (frmtIndex >= frmtArr.Length)
								{
									throw new TclException(interp, unmatched);
								}
								
								// Extract the list of chars for matching.
								
								while (frmtArr[frmtIndex] != ']')
								{
									if ((++frmtIndex) >= frmtArr.Length)
									{
										throw new TclException(interp, unmatched);
									}
								}
								tempArr = new string(frmtArr, tempIndex, frmtIndex - tempIndex).ToCharArray();
								
								startIndex = scanIndex;
								if (charNotMatch)
								{
									// Format specifier contained a '^' so interate
									// until one of the chars in tempArr is found.
									
									while (scanOK && !charMatchFound)
									{
										if (scanIndex >= scanArr.Length)
										{
											scanOK = false;
											break;
										}
										for (i = 0; i < tempArr.Length; i++)
										{
											if (tempArr[i] == scanArr[scanIndex])
											{
												charMatchFound = true;
												break;
											}
										}
										if (widthFlag && ((scanIndex - startIndex) >= width))
										{
											break;
										}
										if (!charMatchFound)
										{
											scanIndex++;
										}
									}
								}
								else
								{
									// Iterate until the char in the scanArr is not 
									// in the tempArr.
									
									charMatchFound = true;
									while (scanOK && charMatchFound)
									{
										if (scanIndex >= scanArr.Length)
										{
											scanOK = false;
											break;
										}
										charMatchFound = false;
										for (i = 0; i < tempArr.Length; i++)
										{
											if (tempArr[i] == scanArr[scanIndex])
											{
												charMatchFound = true;
												break;
											}
										}
										if (widthFlag && (scanIndex - startIndex) >= width)
										{
											break;
										}
										if (charMatchFound)
										{
											scanIndex++;
										}
									}
								}
								
								// Indicates nothing was found.
								
								endIndex = scanIndex - startIndex;
								if (endIndex <= 0)
								{
									scanOK = false;
									break;
								}
								
								if (!discardFlag)
								{
									string str = new string(scanArr, startIndex, endIndex);
									testAndSetVar(interp, argv, argIndex++, TclString.newInstance(str));
								}
								break;
							}
						
						default:  {
								errorBadField(interp, ch);
							}
							break;
						
					}
					
					// As long as the scan was successful (scanOK), the format
					// specifier did not contain a '*' (discardFlag), and
					// we are not at the end of the scanArr (scanArrDone);
					// increment the num of vars set in the interp.  Otherwise
					// increment the number of valid format specifiers.
					
					if (scanOK && !discardFlag && !scanArrDone)
					{
						numMatched++;
					}
					else if ((scanArrDone || !scanOK) && !discardFlag)
					{
						numUnMatched++;
					}
					frmtIndex++;
				}
				else if (scanIndex < scanArr.Length && scanArr[scanIndex] == frmtArr[frmtIndex])
				{
					// No '%' was found, but the characters matched
					
					scanIndex++;
					frmtIndex++;
				}
				else
				{
					// No '%' found and the characters int frmtArr & scanArr
					// did not match.
					
					frmtIndex++;
				}
			}
			
			// The numMatched is the return value: a count of the num of vars set.
			// While the numUnMatched is the number of formatSpecifiers that
			// passed the parsing stage, but did not match anything in the scanArr.
			
			if ((numMatched + numUnMatched) != (argv.Length - 3))
			{
				errorDiffVars(interp);
			}
			interp.setResult(TclInteger.newInstance(numMatched));
      return TCL.CompletionCode.RETURN;
    }
		
		
		/// <summary> Given an array and an index into it, move the index forward
		/// until a non-whitespace char is found.
		/// 
		/// </summary>
		/// <param name="arr">  - the array to search
		/// </param>
		/// <param name="index">- where to begin the search
		/// </param>
		/// <returns> The index value where the whitespace ends.
		/// </returns>
		
		private int skipWhiteSpace(char[] arr, int index)
		{
			bool cont;
			do 
			{
				if (index >= arr.Length)
				{
					return index;
				}
				cont = false;
				switch (arr[index])
				{
					
					case '\t': 
					case '\n': 
					case '\r': 
					case '\f': 
					case ' ':  {
							cont = true;
							index++;
						}
						break;
					}
			}
			while (cont);
			
			return index;
		}
		
		
		/// <summary> Called whenever the cmdProc wants to set an interp value.  
		/// This method <ol>
		/// <li> verifies that there exisits a varName from the argv array, 
		/// <li> that the variable either dosent exisit or is of type scalar
		/// <li> set the variable in interp if (1) and (2) are OK
		/// </ol>
		/// 
		/// </summary>
		/// <param name="interp">  - the Tcl interpreter
		/// </param>
		/// <param name="argv">    - the argument array
		/// </param>
		/// <param name="argIndex">- the current index into the argv array
		/// </param>
		/// <param name="tobj">    - the TclObject that the varName equals
		/// 
		/// </param>
		
		private static void  testAndSetVar(Interp interp, TclObject[] argv, int argIndex, TclObject tobj)
		{
			if (argIndex < argv.Length)
			{
				try
				{
					
					interp.setVar(argv[argIndex].ToString(), tobj, 0);
				}
				catch (TclException e)
				{
					
					throw new TclException(interp, "couldn't set variable \"" + argv[argIndex].ToString() + "\"");
				}
			}
			else
			{
				errorDiffVars(interp);
			}
		}
		
		
		/// <summary> Called whenever the frmtIndex in the cmdProc is changed.  It verifies
		/// the the array index is still within the bounds of the array.  If no
		/// throw error.
		/// </summary>
		/// <param name="interp"> - The TclInterp which called the cmdProc method .
		/// </param>
		/// <param name="arr">    - The array to be checked.
		/// </param>
		/// <param name="index">  - The new value for the array index.
		/// </param>
		
		private static void  checkOverFlow(Interp interp, char[] arr, int index)
		{
			if ((index >= arr.Length) || (index < 0))
			{
				throw new TclException(interp, "\"%n$\" argument index out of range");
			}
		}
		
		
		/// <summary> Called whenever the number of varName args do not match the number
		/// of found and valid formatSpecifiers (matched and unmatched).
		/// 
		/// </summary>
		/// <param name="interp"> - The TclInterp which called the cmdProc method .
		/// </param>
		
		private static void  errorDiffVars(Interp interp)
		{
			
			throw new TclException(interp, "different numbers of variable names and field specifiers");
		}
		
		
		/// <summary> Called whenever the current char in the frmtArr is erroneous
		/// 
		/// </summary>
		/// <param name="interp"> - The TclInterp which called the cmdProc method .
		/// </param>
		/// <param name="fieldSpecifier"> - The erroneous character
		/// </param>
		
		private static void  errorBadField(Interp interp, char fieldSpecifier)
		{
			throw new TclException(interp, "bad scan conversion character \"" + fieldSpecifier + "\"");
		}
		
		
		/// <summary> Called whenever the a width field is used in a char ('c') format 
		/// specifier
		/// 
		/// </summary>
		/// <param name="interp"> - The TclInterp which called the cmdProc method .
		/// </param>
		
		private static void  errorCharFieldWidth(Interp interp)
		{
			throw new TclException(interp, "field width may not be specified in %c conversion");
		}
	}
}
