/*
* Regexp.java
*
* Copyright (c) 1999 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* SCCS: %Z% %M% %I% %E% %U%
*/
// Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
//$Header$

using System;
namespace sunlabs.brazil.util.regexp
{
	
	/// <summary> The <code>Regexp</code> class can be used to match a pattern against a
	/// string and optionally replace the matched parts with new strings.
	/// <p>
	/// Regular expressions were implemented by translating Henry Spencer's
	/// regular expression package for <a href="http://www.scriptics.com">tcl8.0</a>.
	/// Much of the description below is copied verbatim from the tcl8.0 regsub
	/// manual entry.
	/// <hr>
	/// REGULAR EXPRESSIONS
	/// <p>
	/// A regular expression is zero or more <code>branches</code>, separated by
	/// "|".  It matches anything that matches one of the branches.
	/// <p>
	/// A branch is zero or more <code>pieces</code>, concatenated.
	/// It matches a match for the first piece, followed by a match for the
	/// second piece, etc.
	/// <p>
	/// A piece is an <code>atom</code>, possibly followed by "*", "+", or
	/// "?". <ul>
	/// <li> An atom followed by "*" matches a sequence of 0 or more matches of
	/// the atom.
	/// <li> An atom followed by "+" matches a sequence of 1 or more matches of
	/// the atom.
	/// <li> An atom followed by "?" matches either 0 or 1 matches of the atom.
	/// </ul>
	/// <p>
	/// An atom is <ul>
	/// <li> a regular expression in parentheses (matching a match for the
	/// regular expression)
	/// <li> a <code>range</code> (see below)
	/// <li> "." (matching any single character)
	/// <li> "^" (matching the null string at the beginning of the input string)
	/// <li> "$" (matching the null string at the end of the input string)
	/// <li> a "\" followed by a single character (matching that character)
	/// <li> a single character with no other significance (matching that
	/// character).
	/// </ul>
	/// <p>
	/// A <code>range</code> is a sequence of characters enclosed in "[]".
	/// The range normally matches any single character from the sequence.
	/// If the sequence begins with "^", the range matches any single character
	/// <b>not</b> from the rest of the sequence.
	/// If two characters in the sequence are separated by "-", this is shorthand
	/// for the full list of characters between them (e.g. "[0-9]" matches any
	/// decimal digit).  To include a literal "]" in the sequence, make it the
	/// first character (following a possible "^").  To include a literal "-",
	/// make it the first or last character.
	/// <p>
	/// In general there may be more than one way to match a regular expression
	/// to an input string.  For example, consider the command
	/// <pre>
	/// String[] match = new String[2];
	/// Regexp.match("(a*)b*", "aabaaabb", match);
	/// </pre>
	/// Considering only the rules given so far, <code>match[0]</code> and
	/// <code>match[1]</code> could end up with the values <ul>
	/// <li> "aabb" and "aa"
	/// <li> "aaab" and "aaa"
	/// <li> "ab" and "a"
	/// </ul>
	/// or any of several other combinations.  To resolve this potential ambiguity,
	/// Regexp chooses among alternatives using the rule "first then longest".
	/// In other words, it considers the possible matches in order working
	/// from left to right across the input string and the pattern, and it
	/// attempts to match longer pieces of the input string before shorter
	/// ones.  More specifically, the following rules apply in decreasing
	/// order of priority: <ol>
	/// <li> If a regular expression could match two different parts of an input
	/// string then it will match the one that begins earliest.
	/// <li> If a regular expression contains "|" operators then the
	/// leftmost matching sub-expression is chosen.
	/// <li> In "*", "+", and "?" constructs, longer matches are chosen in
	/// preference to shorter ones.
	/// <li>
	/// In sequences of expression components the components are considered
	/// from left to right.
	/// </ol>
	/// <p>
	/// In the example from above, "(a*)b*" therefore matches exactly "aab"; the
	/// "(a*)" portion of the pattern is matched first and it consumes the leading
	/// "aa", then the "b*" portion of the pattern consumes the next "b".  Or,
	/// consider the following example:
	/// <pre>
	/// String match = new String[3];
	/// Regexp.match("(ab|a)(b*)c", "abc", match);
	/// </pre>
	/// After this command, <code>match[0]</code> will be "abc",
	/// <code>match[1]</code> will be "ab", and <code>match[2]</code> will be an
	/// empty string.
	/// Rule 4 specifies that the "(ab|a)" component gets first shot at the input
	/// string and Rule 2 specifies that the "ab" sub-expression
	/// is checked before the "a" sub-expression.
	/// Thus the "b" has already been claimed before the "(b*)"
	/// component is checked and therefore "(b*)" must match an empty string.
	/// <hr>
	/// <a name=regsub></a>
	/// REGULAR EXPRESSION SUBSTITUTION
	/// <p>
	/// Regular expression substitution matches a string against a regular
	/// expression, transforming the string by replacing the matched region(s)
	/// with new substring(s).
	/// <p>
	/// What gets substituted into the result is controlled by a
	/// <code>subspec</code>.  The subspec is a formatting string that specifies
	/// what portions of the matched region should be substituted into the
	/// result.
	/// <ul>
	/// <li> "&" or "\0" is replaced with a copy of the entire matched region.
	/// <li> "\<code>n</code>", where <code>n</code> is a digit from 1 to 9,
	/// is replaced with a copy of the <code>n</code><i>th</i> subexpression.
	/// <li> "\&" or "\\" are replaced with just "&" or "\" to escape their
	/// special meaning.
	/// <li> any other character is passed through.
	/// </ul>
	/// In the above, strings like "\2" represents the two characters
	/// <code>backslash</code> and "2", not the Unicode character 0002.
	/// <hr>
	/// Here is an example of how to use Regexp
	/// <pre>
	/// 
	/// public static void
	/// main(String[] args)
	/// throws Exception
	/// {
	/// Regexp re;
	/// String[] matches;
	/// String s;
	/// 
	/// &#47;*
	/// * A regular expression to match the first line of a HTTP request.
	/// *
	/// * 1. ^               - starting at the beginning of the line
	/// * 2. ([A-Z]+)        - match and remember some upper case characters
	/// * 3. [ \t]+          - skip blank space
	/// * 4. ([^ \t]*)       - match and remember up to the next blank space
	/// * 5. [ \t]+          - skip more blank space
	/// * 6. (HTTP/1\\.[01]) - match and remember HTTP/1.0 or HTTP/1.1
	/// * 7. $		      - end of string - no chars left.
	/// *&#47;
	/// 
	/// s = "GET http://a.b.com:1234/index.html HTTP/1.1";
	/// 
	/// re = new Regexp("^([A-Z]+)[ \t]+([^ \t]+)[ \t]+(HTTP/1\\.[01])$");
	/// matches = new String[4];
	/// if (re.match(s, matches)) {
	/// System.out.println("METHOD  " + matches[1]);
	/// System.out.println("URL     " + matches[2]);
	/// System.out.println("VERSION " + matches[3]);
	/// }
	/// 
	/// &#47;*
	/// * A regular expression to extract some simple comma-separated data,
	/// * reorder some of the columns, and discard column 2.
	/// *&#47;
	/// 
	/// s = "abc,def,ghi,klm,nop,pqr";
	/// 
	/// re = new Regexp("^([^,]+),([^,]+),([^,]+),(.*)");
	/// System.out.println(re.sub(s, "\\3,\\1,\\4"));
	/// }
	/// </pre>
	/// 
	/// </summary>
	/// <author> 	Colin Stevens (colin.stevens@sun.com)
	/// </author>
	/// <version> 	1.7, 99/10/14
	/// </version>
	/// <seealso cref="Regsub">
	/// </seealso>
	
	public class Regexp
	{
		//[STAThread]
    //public static void  Main(string[] args)
    //{
    //  if ((args.Length == 2) && (args[0].Equals("compile")))
    //  {
    //    System.Diagnostics.Debug.WriteLine(new Regexp(args[1]));
    //  }
    //  else if ((args.Length == 3) && (args[0].Equals("match")))
    //  {
    //    Regexp r = new Regexp(args[1]);
    //    string[] substrs = new string[r.subspecs()];
    //    bool match = r.match(args[2], substrs);
    //    System.Diagnostics.Debug.WriteLine("match:\t" + match);
    //    for (int i = 0; i < substrs.Length; i++)
    //    {
    //      System.Diagnostics.Debug.WriteLine((i + 1) + ":\t" + substrs[i]);
    //    }
    //  }
    //  else if ((args.Length == 4) && (args[0].Equals("sub")))
    //  {
    //    Regexp r = new Regexp(args[1]);
    //    System.Diagnostics.Debug.WriteLine(r.subAll(args[2], args[3]));
    //  }
    //  else
    //  {
    //    System.Diagnostics.Debug.WriteLine("usage:");
    //    System.Diagnostics.Debug.WriteLine("\tRegexp match <pattern> <string>");
    //    System.Diagnostics.Debug.WriteLine("\tRegexp sub <pattern> <string> <subspec>");
    //    System.Diagnostics.Debug.WriteLine("\tRegexp compile <pattern>");
    //  }
    //}
		
		/*
		* Structure for regexp "program".  This is essentially a linear encoding
		* of a nondeterministic finite-state machine (aka syntax charts or
		* "railroad normal form" in parsing technology).  Each node is an opcode
		* plus a "next" pointer, possibly plus an operand.  "Next" pointers of
		* all nodes except BRANCH implement concatenation; a "next" pointer with
		* a BRANCH on both ends of it is connecting two alternatives.  (Here we
		* have one of the subtle syntax dependencies:  an individual BRANCH (as
		* opposed to a collection of them) is never concatenated with anything
		* because of operator precedence.)  The operand of some types of node is
		* a literal string; for others, it is a node leading into a sub-FSM.  In
		* particular, the operand of a BRANCH node is the first node of the branch.
		* (NB this is *not* a tree structure:  the tail of the branch connects
		* to the thing following the set of BRANCHes.)  The opcodes are:
		*/
		
		internal const int NSUBEXP = 100;
		
		/* definition	number	opnd?	meaning */
		
		internal const char END = (char) (0); /* no	End of program. */
		internal const char BOL = (char) (1); /* no	Match "" at beginning of line. */
		internal const char EOL = (char) (2); /* no	Match "" at end of line. */
		internal const char ANY = (char) (3); /* no	Match any one character. */
		internal const char ANYOF = (char) (4); /* str	Match any character in this string. */
		internal const char ANYBUT = (char) (5); /* str	Match any character not in this string. */
		internal const char BRANCH = (char) (6); /* node	Match this alternative, or the next... */
		internal const char BACK = (char) (7); /* no	Match "", "next" ptr points backward. */
		internal const char EXACTLY = (char) (8); /* str	Match this string. */
		internal const char NOTHING = (char) (9); /* no	Match empty string. */
		internal const char STAR = (char) (10); /* node	Match this (simple) thing 0 or more times. */
		internal const char PLUS = (char) (11); /* node	Match this (simple) thing 1 or more times. */
		internal const char OPEN = (char) (20); /* no	Mark this point in input as start of #n. */
		/*	OPEN+1 is number 1, etc. */
		internal static readonly char CLOSE = (char) (OPEN + NSUBEXP);
		/* no	Analogous to OPEN. */
		internal static readonly string[] opnames = new string[]{"END", "BOL", "EOL", "ANY", "ANYOF", "ANYBUT", "BRANCH", "BACK", "EXACTLY", "NOTHING", "STAR", "PLUS"};
		
		/*
		* A node is one char of opcode followed by one char of "next" pointer.
		* The value is a positive offset from the opcode of the node containing
		* it.  An operand, if any, simply follows the node.  (Note that much of
		* the code generation knows about this implicit relationship.)
		*
		* Opcode notes:
		*
		* BRANCH	The set of branches constituting a single choice are hooked
		*		together with their "next" pointers, since precedence prevents
		*		anything being concatenated to any individual branch.  The
		*		"next" pointer of the last BRANCH in a choice points to the
		*		thing following the whole choice.  This is also where the
		*		final "next" pointer of each individual branch points; each
		*		branch starts with the operand node of a BRANCH node.
		*
		* ANYOF, ANYBUT, EXACTLY
		*		The format of a string operand is one char of length
		*		followed by the characters making up the string.
		*
		* BACK	Normal "next" pointers all implicitly point forward; BACK
		*		exists to make loop structures possible.
		*
		* STAR, PLUS
		* 		'?', and complex '*' and '+' are implemented as circular
		*		BRANCH structures using BACK.  Simple cases (one character
		*		per match) are implemented with STAR and PLUS for speed
		*		and to minimize recursive plunges.
		*
		* OPENn, CLOSEn
		*		are numbered at compile time.
		*/
		
		
		/// <summary> The bytecodes making up the regexp program.</summary>
		internal char[] program;
		
		/// <summary> Whether the regexp matching should be case insensitive.</summary>
		internal bool ignoreCase;
		
		/// <summary> The number of parenthesized subexpressions in the regexp pattern,
		/// plus 1 for the match of the whole pattern itself.
		/// </summary>
		internal int npar;
		
		/// <summary> <code>true</code> if the pattern must match the beginning of the
		/// string, so we don't have to waste time matching against all possible
		/// starting locations in the string.
		/// </summary>
		internal bool anchored;
		
		internal int startChar;
		internal string must;
		
		/// <summary> Compiles a new Regexp object from the given regular expression
		/// pattern.
		/// <p>
		/// It takes a certain amount of time to parse and validate a regular
		/// expression pattern before it can be used to perform matches
		/// or substitutions.  If the caller caches the new Regexp object, that
		/// parsing time will be saved because the same Regexp can be used with
		/// respect to many different strings.
		/// 
		/// </summary>
		/// <param name="">pat
		/// The string holding the regular expression pattern.
		/// 
		/// @throws	IllegalArgumentException if the pattern is malformed.
		/// The detail message for the exception will be set to a
		/// string indicating how the pattern was malformed.
		/// </param>
		public Regexp(string pat)
		{
			compile(pat);
		}
		
		/// <summary> Compiles a new Regexp object from the given regular expression
		/// pattern.
		/// 
		/// </summary>
		/// <param name="">pat
		/// The string holding the regular expression pattern.
		/// 
		/// </param>
		/// <param name="">ignoreCase
		/// If <code>true</code> then this regular expression will
		/// do case-insensitive matching.  If <code>false</code>, then
		/// the matches are case-sensitive.  Regular expressions
		/// generated by <code>Regexp(String)</code> are case-sensitive.
		/// 
		/// @throws	IllegalArgumentException if the pattern is malformed.
		/// The detail message for the exception will be set to a
		/// string indicating how the pattern was malformed.
		/// </param>
		public Regexp(string pat, bool ignoreCase)
		{
			this.ignoreCase = ignoreCase;
			if (ignoreCase)
			{
				pat = pat.ToLower();
			}
			compile(pat);
		}
		
		/// <summary> Returns the number of parenthesized subexpressions in this regular
		/// expression, plus one more for this expression itself.
		/// 
		/// </summary>
		/// <returns>	The number.
		/// </returns>
		public  int subspecs()
		{
			return npar;
		}
		
		/// <summary> Matches the given string against this regular expression.
		/// 
		/// </summary>
		/// <param name="">str
		/// The string to match.
		/// 
		/// </param>
		/// <returns>	The substring of <code>str</code> that matched the entire
		/// regular expression, or <code>null</code> if the string did not
		/// match this regular expression.
		/// </returns>
		public  string match(string str)
		{
			Match m = exec(str, 0, 0);
			
			if (m == null)
			{
				return null;
			}
			return str.Substring(m.indices[0], (m.indices[1]) - (m.indices[0]));
		}
		
		/// <summary> Matches the given string against this regular expression, and computes
		/// the set of substrings that matched the parenthesized subexpressions.
		/// <p>
		/// <code>substrs[0]</code> is set to the range of <code>str</code>
		/// that matched the entire regular expression.
		/// <p>
		/// <code>substrs[1]</code> is set to the range of <code>str</code>
		/// that matched the first (leftmost) parenthesized subexpression.
		/// <code>substrs[n]</code> is set to the range that matched the
		/// <code>n</code><i>th</i> subexpression, and so on.
		/// <p>
		/// If subexpression <code>n</code> did not match, then
		/// <code>substrs[n]</code> is set to <code>null</code>.  Not to
		/// be confused with "", which is a valid value for a
		/// subexpression that matched 0 characters.
		/// <p>
		/// The length that the caller should use when allocating the
		/// <code>substr</code> array is the return value of
		/// <code>Regexp.subspecs</code>.  The array
		/// can be shorter (in which case not all the information will
		/// be returned), or longer (in which case the remainder of the
		/// elements are initialized to <code>null</code>), or
		/// <code>null</code> (to ignore the subexpressions).
		/// 
		/// </summary>
		/// <param name="">str
		/// The string to match.
		/// 
		/// </param>
		/// <param name="">substrs
		/// An array of strings allocated by the caller, and filled in
		/// with information about the portions of <code>str</code> that
		/// matched the regular expression.  May be <code>null</code>.
		/// 
		/// </param>
		/// <returns>	<code>true</code> if <code>str</code> that matched this
		/// regular expression, <code>false</code> otherwise.
		/// If <code>false</code> is returned, then the contents of
		/// <code>substrs</code> are unchanged.
		/// 
		/// </returns>
		/// <seealso cref="#subspecs">
		/// </seealso>
		public  bool match(string str, string[] substrs)
		{
			Match m = exec(str, 0, 0);
			
			if (m == null)
			{
				return false;
			}
			if (substrs != null)
			{
				int max = System.Math.Min(substrs.Length, npar);
				int i;
				int j = 0;
				for (i = 0; i < max; i++)
				{
					int start = m.indices[j++];
					int end = m.indices[j++];
					if (start < 0)
					{
						substrs[i] = null;
					}
					else
					{
						substrs[i] = str.Substring(start, (end) - (start));
					}
				}
				for (; i < substrs.Length; i++)
				{
					substrs[i] = null;
				}
			}
			return true;
		}
		
		/// <summary> Matches the given string against this regular expression, and computes
		/// the set of substrings that matched the parenthesized subexpressions.
		/// <p>
		/// For the indices specified below, the range extends from the character
		/// at the starting index up to, but not including, the character at the
		/// ending index.
		/// <p>
		/// <code>indices[0]</code> and <code>indices[1]</code> are set to
		/// starting and ending indices of the range of <code>str</code>
		/// that matched the entire regular expression.
		/// <p>
		/// <code>indices[2]</code> and <code>indices[3]</code> are set to the
		/// starting and ending indices of the range of <code>str</code> that
		/// matched the first (leftmost) parenthesized subexpression.
		/// <code>indices[n * 2]</code> and <code>indices[n * 2 + 1]</code>
		/// are set to the range that matched the <code>n</code><i>th</i>
		/// subexpression, and so on.
		/// <p>
		/// If subexpression <code>n</code> did not match, then
		/// <code>indices[n * 2]</code> and <code>indices[n * 2 + 1]</code>
		/// are both set to <code>-1</code>.
		/// <p>
		/// The length that the caller should use when allocating the
		/// <code>indices</code> array is twice the return value of
		/// <code>Regexp.subspecs</code>.  The array
		/// can be shorter (in which case not all the information will
		/// be returned), or longer (in which case the remainder of the
		/// elements are initialized to <code>-1</code>), or
		/// <code>null</code> (to ignore the subexpressions).
		/// 
		/// </summary>
		/// <param name="">str
		/// The string to match.
		/// 
		/// </param>
		/// <param name="">indices
		/// An array of integers allocated by the caller, and filled in
		/// with information about the portions of <code>str</code> that
		/// matched all the parts of the regular expression.
		/// May be <code>null</code>.
		/// 
		/// </param>
		/// <returns>	<code>true</code> if the string matched the regular expression,
		/// <code>false</code> otherwise.  If <code>false</code> is
		/// returned, then the contents of <code>indices</code> are
		/// unchanged.
		/// 
		/// </returns>
		/// <seealso cref="#subspecs">
		/// </seealso>
		public  bool match(string str, int[] indices)
		{
			Match m = exec(str, 0, 0);
			
			if (m == null)
			{
				return false;
			}
			if (indices != null)
			{
				int max = System.Math.Min(indices.Length, npar * 2);
				Array.Copy((System.Array) m.indices, 0, (System.Array) indices, 0, max);
				
				for (int i = max; i < indices.Length; i++)
				{
					indices[i] = - 1;
				}
			}
			return true;
		}
		
		/// <summary> Matches a string against a regular expression and replaces the first
		/// match with the string generated from the substitution parameter.
		/// 
		/// </summary>
		/// <param name="">str
		/// The string to match against this regular expression.
		/// 
		/// </param>
		/// <param name="">subspec
		/// The substitution parameter, described in <a href=#regsub>
		/// REGULAR EXPRESSION SUBSTITUTION</a>.
		/// 
		/// </param>
		/// <returns>	The string formed by replacing the first match in
		/// <code>str</code> with the string generated from
		/// <code>subspec</code>.  If no matches were found, then
		/// the return value is <code>null</code>.
		/// </returns>
		public  string sub(string str, string subspec)
		{
			Regsub rs = new Regsub(this, str);
			if (rs.nextMatch())
			{
				System.Text.StringBuilder sb = new System.Text.StringBuilder(rs.skipped());
				applySubspec(rs, subspec, sb);
				sb.Append(rs.rest());
				
				return sb.ToString();
			}
			else
			{
				return null;
			}
		}
		
		/// <summary> Matches a string against a regular expression and replaces all
		/// matches with the string generated from the substitution parameter.
		/// After each substutition is done, the portions of the string already
		/// examined, including the newly substituted region, are <b>not</b> checked
		/// again for new matches -- only the rest of the string is examined.
		/// 
		/// </summary>
		/// <param name="">str
		/// The string to match against this regular expression.
		/// 
		/// </param>
		/// <param name="">subspec
		/// The substitution parameter, described in <a href=#regsub>
		/// REGULAR EXPRESSION SUBSTITUTION</a>.
		/// 
		/// </param>
		/// <returns>	The string formed by replacing all the matches in
		/// <code>str</code> with the strings generated from
		/// <code>subspec</code>.  If no matches were found, then
		/// the return value is a copy of <code>str</code>.
		/// </returns>
		public  string subAll(string str, string subspec)
		{
			return sub(str, new SubspecFilter(subspec, true));
		}
		
		/// <summary> Utility method to give access to the standard substitution algorithm
		/// used by <code>sub</code> and <code>subAll</code>.  Appends to the
		/// string buffer the string generated by applying the substitution
		/// parameter to the matched region.
		/// 
		/// </summary>
		/// <param name="">rs
		/// Information about the matched region.
		/// 
		/// </param>
		/// <param name="">subspec
		/// The substitution parameter.
		/// 
		/// </param>
		/// <param name="">sb
		/// StringBuffer to which the generated string is appended.
		/// </param>
		public static void  applySubspec(Regsub rs, string subspec, System.Text.StringBuilder sb)
		{
			try
			{
				int len = subspec.Length;
				for (int i = 0; i < len; i++)
				{
					char ch = subspec[i];
					switch (ch)
					{
						
						case '&':  {
								sb.Append(rs.matched());
								break;
							}
						
						case '\\':  {
								i++;
								ch = subspec[i];
								if ((ch >= '0') && (ch <= '9'))
								{
									string match = rs.submatch(ch - '0');
									if ((System.Object) match != null)
									{
										sb.Append(match);
									}
									break;
								}
								// fall through.
							}
							goto default;
						
						default:  {
								sb.Append(ch);
							}
							break;
						
					}
				}
			}
			catch (System.IndexOutOfRangeException e)
			{
				/*
				* Ignore malformed substitution pattern.
				* Return string matched so far.
				*/
			}
		}
		
		public  string sub(string str, Filter rf)
		{
			Regsub rs = new Regsub(this, str);
			if (rs.nextMatch() == false)
			{
				return str;
			}
			
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			do 
			{
				sb.Append(rs.skipped());
				if (rf.filter(rs, sb) == false)
				{
					break;
				}
			}
			while (rs.nextMatch());
			sb.Append(rs.rest());
			return sb.ToString();
		}
		
		/// <summary> This interface is used by the <code>Regexp</code> class to generate
		/// the replacement string for each pattern match found in the source
		/// string.
		/// 
		/// </summary>
		/// <author> 	Colin Stevens (colin.stevens@sun.com)
		/// </author>
		/// <version> 	1.7, 99/10/14
		/// </version>
		public interface Filter
			{
				/// <summary> Given the current state of the match, generate the replacement
				/// string.  This method will be called for each match found in
				/// the source string, unless this filter decides not to handle any
				/// more matches.
				/// <p>
				/// The implementation can use whatever rules it chooses
				/// to generate the replacement string.  For example, here is an
				/// example of a filter that replaces the first <b>5</b>
				/// occurrences of "%XX" in a string with the ASCII character
				/// represented by the hex digits "XX":
				/// <pre>
				/// String str = ...;
				/// 
				/// Regexp re = new Regexp("%[a-fA-F0-9][a-fA-F0-9]");
				/// 
				/// Regexp.Filter rf = new Regexp.Filter() {
				/// int count = 5;
				/// public boolean filter(Regsub rs, StringBuffer sb) {
				/// String match = rs.matched();
				/// int hi = Character.digit(match.charAt(1), 16);
				/// int lo = Character.digit(match.charAt(2), 16);
				/// sb.append((char) ((hi &lt;&lt; 4) | lo));
				/// return (--count > 0);
				/// }
				/// }
				/// 
				/// String result = re.sub(str, rf);
				/// </pre>
				/// 
				/// </summary>
				/// <param name="">rs
				/// <code>Regsub</code> containing the state of the current
				/// match.
				/// 
				/// </param>
				/// <param name="">sb
				/// The string buffer that this filter should append the
				/// generated string to.  This string buffer actually
				/// contains the results the calling <code>Regexp</code> has
				/// generated up to this point.
				/// 
				/// </param>
				/// <returns>  <code>false</code> if no further matches should be
				/// considered in this string, <code>true</code> to allow
				/// <code>Regexp</code> to continue looking for further
				/// matches.
				/// </returns>
				bool filter(Regsub rs, System.Text.StringBuilder sb);
			}
		
		private class SubspecFilter : Filter
		{
			internal string subspec;
			internal bool all;
			
			public SubspecFilter(string subspec, bool all)
			{
				this.subspec = subspec;
				this.all = all;
			}
			
			public  bool filter(Regsub rs, System.Text.StringBuilder sb)
			{
				sunlabs.brazil.util.regexp.Regexp.applySubspec(rs, subspec, sb);
				return all;
			}
		}
		
		/// <summary> Returns a string representation of this compiled regular
		/// expression.  The format of the string representation is a
		/// symbolic dump of the bytecodes.
		/// 
		/// </summary>
		/// <returns>	A string representation of this regular expression.
		/// </returns>
		public override string ToString()
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			
			sb.Append("# subs:  " + npar + "\n");
			sb.Append("anchor:  " + anchored + "\n");
			sb.Append("start:   " + (char) startChar + "\n");
			sb.Append("must:    " + must + "\n");
			
			for (int i = 0; i < program.Length; )
			{
				sb.Append(i + ":\t");
				int op = program[i];
				if (op >= CLOSE)
				{
					sb.Append("CLOSE" + (op - CLOSE));
				}
				else if (op >= OPEN)
				{
					sb.Append("OPEN" + (op - OPEN));
				}
				else
				{
					sb.Append(opnames[op]);
				}
				int line;
				int offset = (int) program[i + 1];
				if (offset == 0)
				{
					sb.Append('\t');
				}
				else if (op == BACK)
				{
					sb.Append("\t-" + offset + "," + (i - offset));
				}
				else
				{
					sb.Append("\t+" + offset + "," + (i + offset));
				}
				
				if ((op == ANYOF) || (op == ANYBUT) || (op == EXACTLY))
				{
					sb.Append("\t'");
					sb.Append(program, i + 3, program[i + 2]);
					sb.Append("'");
					i += 3 + program[i + 2];
				}
				else
				{
					i += 2;
				}
				sb.Append('\n');
			}
			return sb.ToString();
		}
		
		
		private void  compile(string exp)
		{
			Compiler rcstate = new Compiler();
			rcstate.parse = exp.ToCharArray();
			rcstate.off = 0;
			rcstate.npar = 1;
			rcstate.code = new System.Text.StringBuilder();
			
			rcstate.reg(false);
			
			program = rcstate.code.ToString().ToCharArray();
			npar = rcstate.npar;
			startChar = - 1;
			
			/* optimize */
			if (program[rcstate.regnext(0)] == END)
			{
				if (program[2] == BOL)
				{
					anchored = true;
				}
				else if (program[2] == EXACTLY)
				{
					startChar = (int) program[5];
				}
			}
			
			/*
			* If there's something expensive in the r.e., find the
			* longest literal string that must appear and make it the
			* regmust.  Resolve ties in favor of later strings, since
			* the regstart check works with the beginning of the r.e.
			* and avoiding duplication strengthens checking.  Not a
			* strong reason, but sufficient in the absence of others.
			*/
			/*
			if ((rcstate.flagp & Compiler.SPSTART) != 0) {
			int index = -1;
			int longest = 0;
			
			for (scan = 0; scan < program.length; ) {
			switch (program[scan]) {
			case EXACTLY:
			int length = program[scan + 2];
			if (length > longest) {
			index = scan;
			longest = length;
			}
			// fall through;
			
			case ANYOF:
			case ANYBUT:
			scan += 3 + program[scan + 2];
			break;
			
			default:
			scan += 2;
			break;
			}
			}
			if (longest > 0) {
			must = new String(program, index + 3, longest);
			}
			}*/
		}
		
		internal  Match exec(string str, int start, int off)
		{
			if (ignoreCase)
			{
				str = str.ToLower();
			}
			
			Match match = new Match();
			
			match.program = program;
			
			/* Mark beginning of line for ^ . */
			match.str = str;
			match.bol = start;
			match.length = str.Length;
			
			match.indices = new int[npar * 2];
			
			if (anchored)
			{
				/* Simplest case:  anchored match need be tried only once. */
				if (match.regtry(off))
				{
					return match;
				}
			}
			else if (startChar >= 0)
			{
				/* We know what char it must start with. */
				while (off < match.length)
				{
					off = str.IndexOf((System.Char) startChar, off);
					if (off < 0)
					{
						break;
					}
					if (match.regtry(off))
					{
						return match;
					}
					off++;
				}
			}
			else
			{
				/* Messy cases:  unanchored match. */
				do 
				{
					if (match.regtry(off))
					{
						return match;
					}
				}
				while (off++ < match.length);
			}
			return null;
		}
		
		internal class Compiler
		{
			internal char[] parse;
			internal int off;
			internal int npar;
			internal System.Text.StringBuilder code;
			internal int flagp;
			
			
			internal const string META = "^$.[()|?+*\\";
			internal const string MULT = "*+?";
			
			internal const int WORST = 0; /* Worst case. */
			internal const int HASWIDTH = 1; /* Known never to match null string. */
			internal const int SIMPLE = 2; /* Simple enough to be STAR/PLUS operand. */
			internal const int SPSTART = 4; /* Starts with * or +. */
			
			/*
			- reg - regular expression, i.e. main body or parenthesized thing
			*
			* Caller must absorb opening parenthesis.
			*
			* Combining parenthesis handling with the base level of regular expression
			* is a trifle forced, but the need to tie the tails of the branches to what
			* follows makes it hard to avoid.
			*/
			internal  int reg(bool paren)
			{
				int netFlags = HASWIDTH;
				int parno = 0;
				
				int ret = - 1;
				if (paren)
				{
					parno = npar++;
					if (npar >= sunlabs.brazil.util.regexp.Regexp.NSUBEXP)
					{
						throw new System.ArgumentException("too many ()");
					}
					ret = regnode((char) (sunlabs.brazil.util.regexp.Regexp.OPEN + parno));
				}
				
				/* Pick up the branches, linking them together. */
				int br = regbranch();
				if (ret >= 0)
				{
					regtail(ret, br);
				}
				else
				{
					ret = br;
				}
				
				if ((flagp & HASWIDTH) == 0)
				{
					netFlags &= ~ HASWIDTH;
				}
				netFlags |= (flagp & SPSTART);
				while ((off < parse.Length) && (parse[off] == '|'))
				{
					off++;
					br = regbranch();
					regtail(ret, br);
					if ((flagp & HASWIDTH) == 0)
					{
						netFlags &= ~ HASWIDTH;
					}
					netFlags |= (flagp & SPSTART);
				}
				
				/* Make a closing node, and hook it on the end. */
				int ender = regnode((paren)?(char) (sunlabs.brazil.util.regexp.Regexp.CLOSE + parno):sunlabs.brazil.util.regexp.Regexp.END);
				regtail(ret, ender);
				
				/* Hook the tails of the branches to the closing node. */
				for (br = ret; br >= 0; br = regnext(br))
				{
					regoptail(br, ender);
				}
				
				/* Check for proper termination. */
				if (paren && ((off >= parse.Length) || (parse[off++] != ')')))
				{
					throw new System.ArgumentException("missing )");
				}
				else if ((paren == false) && (off < parse.Length))
				{
					throw new System.ArgumentException("unexpected )");
				}
				
				flagp = netFlags;
				return ret;
			}
			
			/*
			- regbranch - one alternative of an | operator
			*
			* Implements the concatenation operator.
			*/
			internal  int regbranch()
			{
				int netFlags = WORST; /* Tentatively. */
				
				int ret = regnode(sunlabs.brazil.util.regexp.Regexp.BRANCH);
				int chain = - 1;
				while ((off < parse.Length) && (parse[off] != '|') && (parse[off] != ')'))
				{
					int latest = regpiece();
					netFlags |= flagp & HASWIDTH;
					if (chain < 0)
					{
						/* First piece. */
						netFlags |= (flagp & SPSTART);
					}
					else
					{
						regtail(chain, latest);
					}
					chain = latest;
				}
				if (chain < 0)
				{
					/* Loop ran zero times. */
					regnode(sunlabs.brazil.util.regexp.Regexp.NOTHING);
				}
				
				flagp = netFlags;
				return ret;
			}
			
			/*
			- regpiece - something followed by possible [*+?]
			*
			* Note that the branching code sequences used for ? and the general cases
			* of * and + are somewhat optimized:  they use the same NOTHING node as
			* both the endmarker for their branch list and the body of the last branch.
			* It might seem that this node could be dispensed with entirely, but the
			* endmarker role is not redundant.
			*/
			internal  int regpiece()
			{
				int netFlags;
				
				int ret = regatom();
				
				if ((off >= parse.Length) || (isMult(parse[off]) == false))
				{
					return ret;
				}
				char op = parse[off];
				
				if (((flagp & HASWIDTH) == 0) && (op != '?'))
				{
					throw new System.ArgumentException("*+ operand could be empty");
				}
				netFlags = (op != '+')?(WORST | SPSTART):(WORST | HASWIDTH);
				
				if ((op == '*') && ((flagp & SIMPLE) != 0))
				{
					reginsert(sunlabs.brazil.util.regexp.Regexp.STAR, ret);
				}
				else if (op == '*')
				{
					/* Emit x* as (x&|), where & means "self". */
					reginsert(sunlabs.brazil.util.regexp.Regexp.BRANCH, ret); /* Either x */
					regoptail(ret, regnode(sunlabs.brazil.util.regexp.Regexp.BACK)); /* and loop */
					regoptail(ret, ret); /* back */
					regtail(ret, regnode(sunlabs.brazil.util.regexp.Regexp.BRANCH)); /* or */
					regtail(ret, regnode(sunlabs.brazil.util.regexp.Regexp.NOTHING)); /* null. */
				}
				else if ((op == '+') && ((flagp & SIMPLE) != 0))
				{
					reginsert(sunlabs.brazil.util.regexp.Regexp.PLUS, ret);
				}
				else if (op == '+')
				{
					/* Emit x+ as x(&|), where & means "self". */
					int next = regnode(sunlabs.brazil.util.regexp.Regexp.BRANCH); /* Either */
					regtail(ret, next);
					regtail(regnode(sunlabs.brazil.util.regexp.Regexp.BACK), ret); /* loop back */
					regtail(next, regnode(sunlabs.brazil.util.regexp.Regexp.BRANCH)); /* or */
					regtail(ret, regnode(sunlabs.brazil.util.regexp.Regexp.NOTHING)); /* null. */
				}
				else if (op == '?')
				{
					/* Emit x? as (x|) */
					reginsert(sunlabs.brazil.util.regexp.Regexp.BRANCH, ret); /* Either x */
					regtail(ret, regnode(sunlabs.brazil.util.regexp.Regexp.BRANCH)); /* or */
					int next = regnode(sunlabs.brazil.util.regexp.Regexp.NOTHING); /* null. */
					regtail(ret, next);
					regoptail(ret, next);
				}
				off++;
				if ((off < parse.Length) && isMult(parse[off]))
				{
					throw new System.ArgumentException("nested *?+");
				}
				
				flagp = netFlags;
				return ret;
			}
			
			/*
			- regatom - the lowest level
			*
			* Optimization:  gobbles an entire sequence of ordinary characters so that
			* it can turn them into a single node, which is smaller to store and
			* faster to run.  Backslashed characters are exceptions, each becoming a
			* separate node; the code is simpler that way and it's not worth fixing.
			*/
			internal  int regatom()
			{
				int netFlags = WORST; /* Tentatively. */
				int ret;
				
				switch (parse[off++])
				{
					
					case '^': 
						ret = regnode(sunlabs.brazil.util.regexp.Regexp.BOL);
						break;
					
					case '$': 
						ret = regnode(sunlabs.brazil.util.regexp.Regexp.EOL);
						break;
					
					case '.': 
						ret = regnode(sunlabs.brazil.util.regexp.Regexp.ANY);
						netFlags |= (HASWIDTH | SIMPLE);
						break;
					
					case '[':  {
							try
							{
								if (parse[off] == '^')
								{
									ret = regnode(sunlabs.brazil.util.regexp.Regexp.ANYBUT);
									off++;
								}
								else
								{
									ret = regnode(sunlabs.brazil.util.regexp.Regexp.ANYOF);
								}
								
								int pos = reglen();
								regc('\x0000');
								
								if ((parse[off] == ']') || (parse[off] == '-'))
								{
									regc(parse[off++]);
								}
								while (parse[off] != ']')
								{
									if (parse[off] == '-')
									{
										off++;
										if (parse[off] == ']')
										{
											regc('-');
										}
										else
										{
											int start = parse[off - 2];
											int end = parse[off++];
											if (start > end)
											{
												throw new System.ArgumentException("invalid [] range");
											}
											for (int i = start + 1; i <= end; i++)
											{
												regc((char) i);
											}
										}
									}
									else
									{
										regc(parse[off++]);
									}
								}
								regset(pos, (char) (reglen() - pos - 1));
								off++;
								netFlags |= HASWIDTH | SIMPLE;
							}
							catch (System.IndexOutOfRangeException e)
							{
								throw new System.ArgumentException("missing ]");
							}
							break;
						}
					
					case '(': 
						ret = reg(true);
						netFlags |= (flagp & (HASWIDTH | SPSTART));
						break;
					
					case '|': 
					case ')': 
						throw new System.ArgumentException("internal urp");
					
					case '?': 
					case '+': 
					case '*': 
						throw new System.ArgumentException("?+* follows nothing");
					
					case '\\': 
						if (off >= parse.Length)
						{
							throw new System.ArgumentException("trailing \\");
						}
						ret = regnode(sunlabs.brazil.util.regexp.Regexp.EXACTLY);
						regc((char) 1);
						regc(parse[off++]);
						netFlags |= HASWIDTH | SIMPLE;
						break;
					
					default:  {
							off--;
							int end;
							for (end = off; end < parse.Length; end++)
							{
								if (META.IndexOf((System.Char) parse[end]) >= 0)
								{
									break;
								}
							}
							if ((end > off + 1) && (end < parse.Length) && isMult(parse[end]))
							{
								end--; /* Back off clear of ?+* operand. */
							}
							netFlags |= HASWIDTH;
							if (end == off + 1)
							{
								netFlags |= SIMPLE;
							}
							ret = regnode(sunlabs.brazil.util.regexp.Regexp.EXACTLY);
							regc((char) (end - off));
							for (; off < end; off++)
							{
								regc(parse[off]);
							}
						}
						break;
					
				}
				
				flagp = netFlags;
				return ret;
			}
			
			/*
			- regnode - emit a node
			*/
			internal  int regnode(char op)
			{
				int ret = code.Length;
				code.Append(op);
				code.Append('\x0000');
				
				return ret;
			}
			
			/*
			- regc - emit (if appropriate) a byte of code
			*/
			internal  void  regc(char b)
			{
				code.Append(b);
			}
			
			internal  int reglen()
			{
				return code.Length;
			}
			
			internal  void  regset(int pos, char ch)
			{
				code[pos] = ch;
			}
			
			
			/*
			- reginsert - insert an operator in front of already-emitted operand
			*
			* Means relocating the operand.
			*/
			internal  void  reginsert(char op, int pos)
			{
				char[] tmp = new char[]{op, '\x0000'};
				code.Insert(pos, tmp);
			}
			
			/*
			- regtail - set the next-pointer at the end of a node chain
			*/
			internal  void  regtail(int pos, int val)
			{
				/* Find last node. */
				
				int scan = pos;
				while (true)
				{
					int tmp = regnext(scan);
					if (tmp < 0)
					{
						break;
					}
					scan = tmp;
				}
				
				int offset = (code[scan] == sunlabs.brazil.util.regexp.Regexp.BACK)?scan - val:val - scan;
				code[scan + 1] = (char) offset;
			}
			
			/*
			- regoptail - regtail on operand of first argument; nop if operandless
			*/
			internal  void  regoptail(int pos, int val)
			{
				if ((pos < 0) || (code[pos] != sunlabs.brazil.util.regexp.Regexp.BRANCH))
				{
					return ;
				}
				regtail(pos + 2, val);
			}
			
			
			/*
			- regnext - dig the "next" pointer out of a node
			*/
			internal  int regnext(int pos)
			{
				int offset = code[pos + 1];
				if (offset == 0)
				{
					return - 1;
				}
				if (code[pos] == sunlabs.brazil.util.regexp.Regexp.BACK)
				{
					return pos - offset;
				}
				else
				{
					return pos + offset;
				}
			}
			
			internal static bool isMult(char ch)
			{
				return (ch == '*') || (ch == '+') || (ch == '?');
			}
		}
		
		internal class Match
		{
			internal char[] program;
			
			internal string str;
			internal int bol;
			internal int input;
			internal int length;
			
			internal int[] indices;
			
			internal  bool regtry(int off)
			{
				this.input = off;
				
				for (int i = 0; i < indices.Length; i++)
				{
					indices[i] = - 1;
				}
				
				if (regmatch(0))
				{
					indices[0] = off;
					indices[1] = input;
					return true;
				}
				else
				{
					return false;
				}
			}
			
			/*
			- regmatch - main matching routine
			*
			* Conceptually the strategy is simple:  check to see whether the current
			* node matches, call self recursively to see whether the rest matches,
			* and then act accordingly.  In practice we make some effort to avoid
			* recursion, in particular by going through "ordinary" nodes (that don't
			* need to know whether the rest of the match failed) by a loop instead of
			* by recursion.
			*/
			internal  bool regmatch(int scan)
			{
				while (true)
				{
					int next = regnext(scan);
					int op = program[scan];
					switch (op)
					{
						
						case sunlabs.brazil.util.regexp.Regexp.BOL: 
							if (input != bol)
							{
								return false;
							}
							break;
						
						
						case sunlabs.brazil.util.regexp.Regexp.EOL: 
							if (input != length)
							{
								return false;
							}
							break;
						
						
						case sunlabs.brazil.util.regexp.Regexp.ANY: 
							if (input >= length)
							{
								return false;
							}
							input++;
							break;
						
						
						case sunlabs.brazil.util.regexp.Regexp.EXACTLY:  {
								if (compare(scan) == false)
								{
									return false;
								}
								break;
							}
						
						
						case sunlabs.brazil.util.regexp.Regexp.ANYOF: 
							if (input >= length)
							{
								return false;
							}
							if (present(scan) == false)
							{
								return false;
							}
							input++;
							break;
						
						
						case sunlabs.brazil.util.regexp.Regexp.ANYBUT: 
							if (input >= length)
							{
								return false;
							}
							if (present(scan))
							{
								return false;
							}
							input++;
							break;
						
						
						case sunlabs.brazil.util.regexp.Regexp.NOTHING: 
						case sunlabs.brazil.util.regexp.Regexp.BACK: 
							break;
						
						
						case sunlabs.brazil.util.regexp.Regexp.BRANCH:  {
								if (program[next] != sunlabs.brazil.util.regexp.Regexp.BRANCH)
								{
									next = scan + 2;
								}
								else
								{
									do 
									{
										int save = input;
										if (regmatch(scan + 2))
										{
											return true;
										}
										input = save;
										scan = regnext(scan);
									}
									while ((scan >= 0) && (program[scan] == sunlabs.brazil.util.regexp.Regexp.BRANCH));
									return false;
								}
								break;
							}
						
						
						case sunlabs.brazil.util.regexp.Regexp.STAR: 
						case sunlabs.brazil.util.regexp.Regexp.PLUS:  {
								/*
								* Lookahead to avoid useless match attempts
								* when we know what character comes next.
								*/
								
								int ch = - 1;
								if (program[next] == sunlabs.brazil.util.regexp.Regexp.EXACTLY)
								{
									ch = program[next + 3];
								}
								
								int min = (op == sunlabs.brazil.util.regexp.Regexp.STAR)?0:1;
								int save = input;
								int no = regrepeat(scan + 2);
								
								while (no >= min)
								{
									/* If it could work, try it. */
									if ((ch < 0) || ((input < length) && (str[input] == ch)))
									{
										if (regmatch(next))
										{
											return true;
										}
									}
									/* Couldn't or didn't -- back up. */
									no--;
									input = save + no;
								}
								return false;
							}
						
						
						case sunlabs.brazil.util.regexp.Regexp.END: 
							return true;
						
						
						default: 
							if (op >= sunlabs.brazil.util.regexp.Regexp.CLOSE)
							{
								int no = op - sunlabs.brazil.util.regexp.Regexp.CLOSE;
								int save = input;
								
								if (regmatch(next))
								{
									/*
									* Don't set endp if some later
									* invocation of the same parentheses
									* already has.
									*/
									if (indices[no * 2 + 1] <= 0)
									{
										indices[no * 2 + 1] = save;
									}
									return true;
								}
							}
							else if (op >= sunlabs.brazil.util.regexp.Regexp.OPEN)
							{
								int no = op - sunlabs.brazil.util.regexp.Regexp.OPEN;
								int save = input;
								
								if (regmatch(next))
								{
									/*
									* Don't set startp if some later invocation of the
									* same parentheses already has.
									*/
									if (indices[no * 2] <= 0)
									{
										indices[no * 2] = save;
									}
									return true;
								}
							}
							return false;
						
					}
					scan = next;
				}
			}
			
			internal  bool compare(int scan)
			{
				int count = program[scan + 2];
				if (input + count > length)
				{
					return false;
				}
				int start = scan + 3;
				int end = start + count;
				for (int i = start; i < end; i++)
				{
					if (str[input++] != program[i])
					{
						return false;
					}
				}
				return true;
			}
			
			internal  bool present(int scan)
			{
				char ch = str[input];
				
				int count = program[scan + 2];
				int start = scan + 3;
				int end = start + count;
				
				for (int i = start; i < end; i++)
				{
					if (program[i] == ch)
					{
						return true;
					}
				}
				return false;
			}
			
			
			/*
			- regrepeat - repeatedly match something simple, report how many
			*/
			internal  int regrepeat(int scan)
			{
				int op = program[scan];
				int count = 0;
				
				switch (op)
				{
					
					case sunlabs.brazil.util.regexp.Regexp.ANY: 
						
						count = length - input;
						input = length;
						break;
					
					
					case sunlabs.brazil.util.regexp.Regexp.EXACTLY:  {
							// 'g*' matches all the following 'g' characters.
							
							char ch = program[scan + 3];
							while ((input < length) && (str[input] == ch))
							{
								input++;
								count++;
							}
							break;
						}
					
					
					case sunlabs.brazil.util.regexp.Regexp.ANYOF: 
						
						while ((input < length) && present(scan))
						{
							input++;
							count++;
						}
						break;
					
					
					
					case sunlabs.brazil.util.regexp.Regexp.ANYBUT: 
						while ((input < length) && !present(scan))
						{
							input++;
							count++;
						}
						break;
					}
				return count;
			}
			
			/*
			- regnext - dig the "next" pointer out of a node
			*/
			internal  int regnext(int scan)
			{
				int offset = program[scan + 1];
				if (program[scan] == sunlabs.brazil.util.regexp.Regexp.BACK)
				{
					return scan - offset;
				}
				else
				{
					return scan + offset;
				}
			}
		}
	}
}
