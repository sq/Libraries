/*
* LsearchCmd.java
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
* Copyright (c) 1998-1999 by Scriptics Corporation.
* Copyright (c) 2000 Christian Krone.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: LsearchCmd.java,v 1.2 2000/08/21 04:12:51 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This class implements the built-in "lsearch" command in Tcl.
	*/
	
	class LsearchCmd : Command
	{
		
		private static readonly string[] options = new string[]{"-ascii", "-decreasing", "-dictionary", "-exact", "-increasing", "-integer", "-glob", "-real", "-regexp", "-sorted"};
		internal const int LSEARCH_ASCII = 0;
		internal const int LSEARCH_DECREASING = 1;
		internal const int LSEARCH_DICTIONARY = 2;
		internal const int LSEARCH_EXACT = 3;
		internal const int LSEARCH_INCREASING = 4;
		internal const int LSEARCH_INTEGER = 5;
		internal const int LSEARCH_GLOB = 6;
		internal const int LSEARCH_REAL = 7;
		internal const int LSEARCH_REGEXP = 8;
		internal const int LSEARCH_SORTED = 9;
		
		internal const int ASCII = 0;
		internal const int DICTIONARY = 1;
		internal const int INTEGER = 2;
		internal const int REAL = 3;
		
		internal const int EXACT = 0;
		internal const int GLOB = 1;
		internal const int REGEXP = 2;
		internal const int SORTED = 3;
		
		/*
		*-----------------------------------------------------------------------------
		*
		* cmdProc --
		*
		*      This procedure is invoked to process the "lsearch" Tcl command.
		*      See the user documentation for details on what it does.
		*
		* Results:
		*      None.
		*
		* Side effects:
		*      See the user documentation.
		*
		*-----------------------------------------------------------------------------
		*/
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			int mode = GLOB;
			int dataType = ASCII;
			bool isIncreasing = true;
			TclObject pattern ;
			TclObject list;
			
			if (objv.Length < 3)
			{
				throw new TclNumArgsException(interp, 1, objv, "?options? list pattern");
			}
			
			for (int i = 1; i < objv.Length - 2; i++)
			{
				switch (TclIndex.get(interp, objv[i], options, "option", 0))
				{
					
					case LSEARCH_ASCII: 
						dataType = ASCII;
						break;
					
					case LSEARCH_DECREASING: 
						isIncreasing = false;
						break;
					
					case LSEARCH_DICTIONARY: 
						dataType = DICTIONARY;
						break;
					
					case LSEARCH_EXACT: 
						mode = EXACT;
						break;
					
					case LSEARCH_INCREASING: 
						isIncreasing = true;
						break;
					
					case LSEARCH_INTEGER: 
						dataType = INTEGER;
						break;
					
					case LSEARCH_GLOB: 
						mode = GLOB;
						break;
					
					case LSEARCH_REAL: 
						dataType = REAL;
						break;
					
					case LSEARCH_REGEXP: 
						mode = REGEXP;
						break;
					
					case LSEARCH_SORTED: 
						mode = SORTED;
						break;
					}
			}
			
			// Make sure the list argument is a list object and get its length and
			// a pointer to its array of element pointers.
			
			TclObject[] listv = TclList.getElements(interp, objv[objv.Length - 2]);
			
			TclObject patObj = objv[objv.Length - 1];
			string patternBytes = null;
			int patInt = 0;
			double patDouble = 0.0;
			int length = 0;
			if (mode == EXACT || mode == SORTED)
			{
				switch (dataType)
				{
					
					case ASCII: 
					case DICTIONARY: 
						
						patternBytes = patObj.ToString();
						length = patternBytes.Length;
						break;
					
					case INTEGER: 
						patInt = TclInteger.get(interp, patObj);
						break;
					
					case REAL: 
						patDouble = TclDouble.get(interp, patObj);
						break;
					}
			}
			else
			{
				
				patternBytes = patObj.ToString();
				length = patternBytes.Length;
			}
			
			// Set default index value to -1, indicating failure; if we find the
			// item in the course of our search, index will be set to the correct
			// value.
			
			int index = - 1;
			if (mode == SORTED)
			{
				// If the data is sorted, we can do a more intelligent search.
				int match = 0;
				int lower = - 1;
				int upper = listv.Length;
				while (lower + 1 != upper)
				{
					int i = (lower + upper) / 2;
					switch (dataType)
					{
						
						case ASCII:  {
								
								string bytes = listv[i].ToString();
								match = patternBytes.CompareTo(bytes);
								break;
							}
						
						case DICTIONARY:  {
								
								string bytes = listv[i].ToString();
								match = DictionaryCompare(patternBytes, bytes);
								break;
							}
						
						case INTEGER:  {
								int objInt = TclInteger.get(interp, listv[i]);
								if (patInt == objInt)
								{
									match = 0;
								}
								else if (patInt < objInt)
								{
									match = - 1;
								}
								else
								{
									match = 1;
								}
								break;
							}
						
						case REAL:  {
								double objDouble = TclDouble.get(interp, listv[i]);
								if (patDouble == objDouble)
								{
									match = 0;
								}
								else if (patDouble < objDouble)
								{
									match = - 1;
								}
								else
								{
									match = 1;
								}
								break;
							}
						}
					if (match == 0)
					{
						
						// Normally, binary search is written to stop when it
						// finds a match.  If there are duplicates of an element in
						// the list, our first match might not be the first occurance.
						// Consider:  0 0 0 1 1 1 2 2 2
						// To maintain consistancy with standard lsearch semantics,
						// we must find the leftmost occurance of the pattern in the
						// list.  Thus we don't just stop searching here.  This
						// variation means that a search always makes log n
						// comparisons (normal binary search might "get lucky" with
						// an early comparison).
						
						index = i;
						upper = i;
					}
					else if (match > 0)
					{
						if (isIncreasing)
						{
							lower = i;
						}
						else
						{
							upper = i;
						}
					}
					else
					{
						if (isIncreasing)
						{
							upper = i;
						}
						else
						{
							lower = i;
						}
					}
				}
			}
			else
			{
				for (int i = 0; i < listv.Length; i++)
				{
					bool match = false;
					switch (mode)
					{
						
						case SORTED: 
						case EXACT:  {
								switch (dataType)
								{
									
									case ASCII:  {
											
											string bytes = listv[i].ToString();
											int elemLen = bytes.Length;
											if (length == elemLen)
											{
												match = bytes.Equals(patternBytes);
											}
											break;
										}
									
									case DICTIONARY:  {
											
											string bytes = listv[i].ToString();
											match = (DictionaryCompare(bytes, patternBytes) == 0);
											break;
										}
									
									case INTEGER:  {
											int objInt = TclInteger.get(interp, listv[i]);
											match = (objInt == patInt);
											break;
										}
									
									case REAL:  {
											double objDouble = TclDouble.get(interp, listv[i]);
											match = (objDouble == patDouble);
											break;
										}
									}
								break;
							}
						
						case GLOB:  {
								
								match = Util.stringMatch(listv[i].ToString(), patternBytes);
								break;
							}
						
						case REGEXP:  {
								
								match = Util.regExpMatch(interp, listv[i].ToString(), patObj);
								break;
							}
						}
					if (match)
					{
						index = i;
						break;
					}
				}
			}
			interp.setResult(index);
      return TCL.CompletionCode.RETURN;
    }
		
		/*
		*----------------------------------------------------------------------
		*
		* DictionaryCompare -> dictionaryCompare
		*
		*      This function compares two strings as if they were being used in
		*      an index or card catalog.  The case of alphabetic characters is
		*      ignored, except to break ties.  Thus "B" comes before "b" but
		*      after "a".  Also, integers embedded in the strings compare in
		*      numerical order.  In other words, "x10y" comes after "x9y", not
		*      before it as it would when using strcmp().
		*
		* Results:
		*      A negative result means that the first element comes before the
		*      second, and a positive result means that the second element
		*      should come first.  A result of zero means the two elements
		*      are equal and it doesn't matter which comes first.
		*
		* Side effects:
		*      None.
		*
		*----------------------------------------------------------------------
		*/
		
		private static int DictionaryCompare(string left, string right)
		// The strings to compare
		{
			char[] leftArr = left.ToCharArray();
			char[] rightArr = right.ToCharArray();
			char leftChar, rightChar, leftLower, rightLower;
			int lInd = 0;
			int rInd = 0;
			int diff;
			int secondaryDiff = 0;
			
			while (true)
			{
				if ((rInd < rightArr.Length) && (System.Char.IsDigit(rightArr[rInd])) && (lInd < leftArr.Length) && (System.Char.IsDigit(leftArr[lInd])))
				{
					// There are decimal numbers embedded in the two
					// strings.  Compare them as numbers, rather than
					// strings.  If one number has more leading zeros than
					// the other, the number with more leading zeros sorts
					// later, but only as a secondary choice.
					
					int zeros = 0;
					while ((rightArr[rInd] == '0') && (rInd + 1 < rightArr.Length) && (System.Char.IsDigit(rightArr[rInd + 1])))
					{
						rInd++;
						zeros--;
					}
					while ((leftArr[lInd] == '0') && (lInd + 1 < leftArr.Length) && (System.Char.IsDigit(leftArr[lInd + 1])))
					{
						lInd++;
						zeros++;
					}
					if (secondaryDiff == 0)
					{
						secondaryDiff = zeros;
					}
					
					// The code below compares the numbers in the two
					// strings without ever converting them to integers.  It
					// does this by first comparing the lengths of the
					// numbers and then comparing the digit values.
					
					diff = 0;
					while (true)
					{
						if ((diff == 0) && (lInd < leftArr.Length) && (rInd < rightArr.Length))
						{
							diff = leftArr[lInd] - rightArr[rInd];
						}
						rInd++;
						lInd++;
						if (rInd >= rightArr.Length || !System.Char.IsDigit(rightArr[rInd]))
						{
							if (lInd < leftArr.Length && System.Char.IsDigit(leftArr[lInd]))
							{
								return 1;
							}
							else
							{
								// The two numbers have the same length. See
								// if their values are different.
								
								if (diff != 0)
								{
									return diff;
								}
								break;
							}
						}
						else if (lInd >= leftArr.Length || !System.Char.IsDigit(leftArr[lInd]))
						{
							return - 1;
						}
					}
					continue;
				}
				
				// Convert character to Unicode for comparison purposes.  If either
				// string is at the terminating null, do a byte-wise comparison and
				// bail out immediately.
				
				if ((lInd < leftArr.Length) && (rInd < rightArr.Length))
				{
					
					// Convert both chars to lower for the comparison, because
					// dictionary sorts are case insensitve.  Covert to lower, not
					// upper, so chars between Z and a will sort before A (where most
					// other interesting punctuations occur)
					
					leftChar = leftArr[lInd++];
					rightChar = rightArr[rInd++];
					leftLower = System.Char.ToLower(leftChar);
					rightLower = System.Char.ToLower(rightChar);
				}
				else if (lInd < leftArr.Length)
				{
					diff = - rightArr[rInd];
					break;
				}
				else if (rInd < rightArr.Length)
				{
					diff = leftArr[lInd];
					break;
				}
				else
				{
					diff = 0;
					break;
				}
				
				diff = leftLower - rightLower;
				if (diff != 0)
				{
					return diff;
				}
				else if (secondaryDiff == 0)
				{
					if (System.Char.IsUpper(leftChar) && System.Char.IsLower(rightChar))
					{
						secondaryDiff = - 1;
					}
					else if (System.Char.IsUpper(rightChar) && System.Char.IsLower(leftChar))
					{
						secondaryDiff = 1;
					}
				}
			}
			if (diff == 0)
			{
				diff = secondaryDiff;
			}
			return diff;
		}
	} // end LsearchCmd
}
