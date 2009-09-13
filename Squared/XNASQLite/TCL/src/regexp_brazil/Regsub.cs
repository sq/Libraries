/*
* Regsub.java
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
	
	/// <summary> The <code>Regsub</code> class provides an iterator-like object to
	/// extract the matched and unmatched portions of a string with respect to
	/// a given regular expression.
	/// <p>
	/// After each match is found, the portions of the string already
	/// checked are not searched again -- searching for the next match will
	/// begin at the character just after where the last match ended.
	/// <p>
	/// Here is an example of using Regsub to replace all "%XX" sequences in
	/// a string with the ASCII character represented by the hex digits "XX":
	/// <pre>
	/// public static void
	/// main(String[] args)
	/// throws Exception
	/// {
	/// Regexp re = new Regexp("%[a-fA-F0-9][a-fA-F0-9]");
	/// Regsub rs = new Regsub(re, args[0]);
	/// 
	/// StringBuffer sb = new StringBuffer();
	/// 
	/// while (rs.nextMatch()) {
	/// sb.append(rs.skipped());
	/// 
	/// String match = rs.matched();
	/// 
	/// int hi = Character.digit(match.charAt(1), 16);
	/// int lo = Character.digit(match.charAt(2), 16);
	/// sb.append((char) ((hi &lt;&lt; 4) | lo));
	/// }
	/// sb.append(rs.rest());
	/// 
	/// System.out.println(sb);
	/// }
	/// </pre>
	/// 
	/// </summary>
	/// <author> 	Colin Stevens (colin.stevens@sun.com)
	/// </author>
	/// <version> 	1.4, 99/10/14
	/// </version>
	/// <seealso cref="Regexp">
	/// </seealso>
	public class Regsub
	{
		internal Regexp r;
		internal string str;
		internal int ustart;
		internal int mstart;
		internal int end;
		internal Regexp.Match m;
		
		/// <summary> Construct a new <code>Regsub</code> that can be used to step 
		/// through the given string, finding each substring that matches
		/// the given regular expression.
		/// <p>
		/// <code>Regexp</code> contains two substitution methods,
		/// <code>sub</code> and <code>subAll</code>, that can be used instead
		/// of <code>Regsub</code> if just simple substitutions are being done.
		/// 
		/// </summary>
		/// <param name="">r
		/// The compiled regular expression.
		/// 
		/// </param>
		/// <param name="">str
		/// The string to search.
		/// 
		/// </param>
		/// <seealso cref="Regexp#sub">
		/// </seealso>
		/// <seealso cref="Regexp#subAll">
		/// </seealso>
		public Regsub(Regexp r, string str)
		{
			this.r = r;
			this.str = str;
			this.ustart = 0;
			this.mstart = - 1;
			this.end = 0;
		}
		
		/// <summary> Searches for the next substring that matches the regular expression.
		/// After calling this method, the caller would call methods like
		/// <code>skipped</code>, <code>matched</code>, etc. to query attributes
		/// of the matched region.
		/// <p>
		/// Calling this function again will search for the next match, beginning
		/// at the character just after where the last match ended.
		/// 
		/// </summary>
		/// <returns>	<code>true</code> if a match was found, <code>false</code>
		/// if there are no more matches.
		/// </returns>
		public  bool nextMatch()
		{
			ustart = end;
			
			/*
			* Consume one character if the last match didn't consume any
			* characters, to avoid an infinite loop.
			*/
			
			int off = ustart;
			if (off == mstart)
			{
				off++;
				if (off >= str.Length)
				{
					return false;
				}
			}
			
			
			m = r.exec(str, 0, off);
			if (m == null)
			{
				return false;
			}
			
			mstart = m.indices[0];
			end = m.indices[1];
			
			return true;
		}
		
		/// <summary> Returns a substring consisting of all the characters skipped
		/// between the end of the last match (or the start of the original
		/// search string) and the start of this match.
		/// <p>
		/// This method can be used extract all the portions of string that
		/// <b>didn't</b> match the regular expression.
		/// 
		/// </summary>
		/// <returns>	The characters that didn't match.
		/// </returns>
		public  string skipped()
		{
			return str.Substring(ustart, (mstart) - (ustart));
		}
		
		/// <summary> Returns a substring consisting of the characters that matched
		/// the entire regular expression during the last call to
		/// <code>nextMatch</code>.  
		/// 
		/// </summary>
		/// <returns>	The characters that did match.
		/// 
		/// </returns>
		/// <seealso cref="#submatch">
		/// </seealso>
		public  string matched()
		{
			return str.Substring(mstart, (end) - (mstart));
		}
		
		/// <summary> Returns a substring consisting of the characters that matched
		/// the given parenthesized subexpression during the last call to
		/// <code>nextMatch</code>.
		/// 
		/// </summary>
		/// <param name="">i
		/// The index of the parenthesized subexpression.
		/// 
		/// </param>
		/// <returns>	The characters that matched the subexpression, or
		/// <code>null</code> if the given subexpression did not
		/// exist or did not match.
		/// </returns>
		public  string submatch(int i)
		{
			if (i * 2 + 1 >= m.indices.Length)
			{
				return null;
			}
			int start = m.indices[i * 2];
			int end = m.indices[i * 2 + 1];
			if ((start < 0) || (end < 0))
			{
				return null;
			}
			return str.Substring(start, (end) - (start));
		}
		
		/// <summary> Returns a substring consisting of all the characters that come
		/// after the last match.  As the matches progress, the <code>rest</code>
		/// gets shorter.  When <code>nextMatch</code> returns <code>false</code>,
		/// then this method will return the rest of the string that can't be
		/// matched.
		/// 
		/// </summary>
		/// <returns>	The rest of the characters after the last match.
		/// </returns>
		public  string rest()
		{
			return str.Substring(end);
		}
	}
}
