#undef DEBUG
/*
* Util.java --
*
*	This class provides useful Tcl utility methods.
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997-1999 by Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and redistribution
* of this file, and for a DISCLAIMER OF ALL WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Util.java,v 1.10 2002/05/16 22:53:45 mdejong Exp $
*/
using System;
using Regexp = sunlabs.brazil.util.regexp.Regexp;
namespace tcl.lang
{

  public class Util
  {
    public static int ActualPlatform
    {
      get
      {
        if ( Util.Windows )
        {
          return JACL.PLATFORM_WINDOWS;
        }
        if ( Util.Mac )
        {
          return JACL.PLATFORM_MAC;
        }
        return JACL.PLATFORM_UNIX;
      }

    }
    public static bool Unix
    {
      get
      {
        if ( Mac || Windows )
        {
          return false;
        }
        return true;
      }

    }
    public static bool Mac
    {
      get
      {
        return false;
      }

    }
    public static bool Windows
    {
      get
      {
        // TODO .NET ist always Windows now
        return true;
      }

    }

    internal const int TCL_DONT_USE_BRACES = 1;
    internal const int USE_BRACES = 2;
    internal const int BRACES_UNMATCHED = 4;

    // Some error messages.

    internal const string intTooBigCode = "ARITH IOVERFLOW {integer value too large to represent}";
    internal const string fpTooBigCode = "ARITH OVERFLOW {floating-point value too large to represent}";

    // This table below is used to convert from ASCII digits to a
    // numerical equivalent.  It maps from '0' through 'z' to integers
    // (100 for non-digit characters).

    internal static char[] cvtIn = new char[] { (char)( 0 ), (char)( 1 ), (char)( 2 ), (char)( 3 ), (char)( 4 ), (char)( 5 ), (char)( 6 ), (char)( 7 ), (char)( 8 ), (char)( 9 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 10 ), (char)( 11 ), (char)( 12 ), (char)( 13 ), (char)( 14 ), (char)( 15 ), (char)( 16 ), (char)( 17 ), (char)( 18 ), (char)( 19 ), (char)( 20 ), (char)( 21 ), (char)( 22 ), (char)( 23 ), (char)( 24 ), (char)( 25 ), (char)( 26 ), (char)( 27 ), (char)( 28 ), (char)( 29 ), (char)( 30 ), (char)( 31 ), (char)( 32 ), (char)( 33 ), (char)( 34 ), (char)( 35 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 100 ), (char)( 10 ), (char)( 11 ), (char)( 12 ), (char)( 13 ), (char)( 14 ), (char)( 15 ), (char)( 16 ), (char)( 17 ), (char)( 18 ), (char)( 19 ), (char)( 20 ), (char)( 21 ), (char)( 22 ), (char)( 23 ), (char)( 24 ), (char)( 25 ), (char)( 26 ), (char)( 27 ), (char)( 28 ), (char)( 29 ), (char)( 30 ), (char)( 31 ), (char)( 32 ), (char)( 33 ), (char)( 34 ), (char)( 35 ) };

    // Largest possible base 10 exponent.  Any
    // exponent larger than this will already
    // produce underflow or overflow, so there's
    // no need to worry about additional digits.

    internal const int maxExponent = 511;

    // Table giving binary powers of 10. Entry
    // is 10^2^i.  Used to convert decimal
    // exponents into floating-point numbers.

    internal static readonly double[] powersOf10 = new double[] { 10.0, 100.0, 1.0e4, 1.0e8, 1.0e16, 1.0e32, 1.0e64, 1.0e128, 1.0e256 };

    // Default precision for converting floating-point values to strings.

    internal const int DEFAULT_PRECISION = 12;

    // The following variable determine the precision used when converting
    // floating-point values to strings. This information is linked to all
    // of the tcl_precision variables in all interpreters inside a JVM via 
    // PrecTraceProc.
    //
    // Note: since multiple threads may change precision concurrently, race
    // conditions may occur.
    //
    // It should be modified only by the PrecTraceProc class.

    internal static int precision;
    private Util()
    {
      // Do nothing.  This should never be called.
    }
    internal static StrtoulResult strtoul( string s, int start, int base_ )
    // Base for conversion.  Must be less than 37.  If 0,
    // then the base is chosen from the leading characters
    // of string:  "0x" means hex, "0" means octal, 
    // anything else means decimal.
    {
      long result = 0;
      int digit;
      bool anyDigits = false;
      int len = s.Length;
      int i = start;
      char c;

      // Skip any leading blanks.

      while ( i < len && System.Char.IsWhiteSpace( s[i] ) )
      {
        i++;
      }
      if ( i >= len )
      {
        return new StrtoulResult( 0, 0, TCL.INVALID_INTEGER );
      }

      // If no base was provided, pick one from the leading characters
      // of the string.

      if ( base_ == 0 )
      {
        c = s[i];
        if ( c == '0' )
        {
          if ( i < len - 1 )
          {
            i++;
            c = s[i];
            if ( c == 'x' || c == 'X' )
            {
              i += 1;
              base_ = 16;
            }
          }
          if ( base_ == 0 )
          {
            // Must set anyDigits here, otherwise "0" produces a
            // "no digits" error.

            anyDigits = true;
            base_ = 8;
          }
        }
        else
        {
          base_ = 10;
        }
      }
      else if ( base_ == 16 )
      {
        if ( i < len - 2 )
        {
          // Skip a leading "0x" from hex numbers.

          if ( ( s[i] == '0' ) && ( s[i + 1] == 'x' ) )
          {
            i += 2;
          }
        }
      }

      long max = ( Int64.MaxValue / ( (long)base_ ) );
      bool overflowed = false;

      for ( ; ; i += 1 )
      {
        if ( i >= len )
        {
          break;
        }
        digit = s[i] - '0';
        if ( digit < 0 || digit > ( 'z' - '0' ) )
        {
          break;
        }
        digit = cvtIn[digit];
        if ( digit >= base_ )
        {
          break;
        }

        if ( result > max )
        {
          overflowed = true;
        }

        result = result * base_ + digit;
        anyDigits = true;
      }

      // See if there were any digits at all.

      if ( !anyDigits )
      {
        return new StrtoulResult( 0, 0, TCL.INVALID_INTEGER );
      }
      else if ( overflowed )
      {
        return new StrtoulResult( 0, i, TCL.INTEGER_RANGE );
      }
      else
      {
        return new StrtoulResult( result, i, 0 );
      }
    }
    internal static int getInt( Interp interp, string s )
    {
      int len = s.Length;
      bool sign;
      int i = 0;

      // Skip any leading blanks.

      while ( i < len && System.Char.IsWhiteSpace( s[i] ) )
      {
        i++;
      }
      if ( i >= len )
      {
        throw new TclException( interp, "expected integer but got \"" + s + "\"" );
      }

      char c = s[i];
      if ( c == '-' )
      {
        sign = true;
        i += 1;
      }
      else
      {
        if ( c == '+' )
        {
          i += 1;
        }
        sign = false;
      }

      StrtoulResult res = strtoul( s, i, 0 );
      if ( res.errno < 0 )
      {
        if ( res.errno == TCL.INTEGER_RANGE )
        {
          if ( interp != null )
          {
            interp.setErrorCode( TclString.newInstance( intTooBigCode ) );
          }
          throw new TclException( interp, "integer value too large to represent" );
        }
        else
        {
          throw new TclException( interp, "expected integer but got \"" + s + "\"" + checkBadOctal( interp, s ) );
        }
      }
      else if ( res.index < len )
      {
        for ( i = res.index ; i < len ; i++ )
        {
          if ( !System.Char.IsWhiteSpace( s[i] ) )
          {
            throw new TclException( interp, "expected integer but got \"" + s + "\"" + checkBadOctal( interp, s ) );
          }
        }
      }

      if ( sign )
      {
        return (int)( -res.value );
      }
      else
      {
        return (int)( res.value );
      }
    }
    internal static long getLong( Interp interp, string s )
    {
      int len = s.Length;
      bool sign;
      int i = 0;

      // Skip any leading blanks.

      while ( i < len && System.Char.IsWhiteSpace( s[i] ) )
      {
        i++;
      }
      if ( i >= len )
      {
        throw new TclException( interp, "expected integer but got \"" + s + "\"" );
      }

      char c = s[i];
      if ( c == '-' )
      {
        sign = true;
        i += 1;
      }
      else
      {
        if ( c == '+' )
        {
          i += 1;
        }
        sign = false;
      }

      StrtoulResult res = strtoul( s, i, 0 );
      if ( res.errno < 0 )
      {
        if ( res.errno == TCL.INTEGER_RANGE )
        {
          if ( interp != null )
          {
            interp.setErrorCode( TclString.newInstance( intTooBigCode ) );
          }
          throw new TclException( interp, "integer value too large to represent" );
        }
        else
        {
          throw new TclException( interp, "expected integer but got \"" + s + "\"" + checkBadOctal( interp, s ) );
        }
      }
      else if ( res.index < len )
      {
        for ( i = res.index ; i < len ; i++ )
        {
          if ( !System.Char.IsWhiteSpace( s[i] ) )
          {
            throw new TclException( interp, "expected integer but got \"" + s + "\"" + checkBadOctal( interp, s ) );
          }
        }
      }

      if ( sign )
      {
        return (long)( -res.value );
      }
      else
      {
        return (long)( res.value );
      }
    }
    internal static int getIntForIndex( Interp interp, TclObject tobj, int endValue )
    {
      int length, offset;

      if ( tobj.InternalRep is TclInteger )
      {
        return TclInteger.get( interp, tobj );
      }


      string bytes = tobj.ToString();
      length = bytes.Length;

      string intforindex_error = "bad index \"" + bytes + "\": must be integer or end?-integer?" + checkBadOctal( interp, bytes );

      // FIXME : should we replace this call to regionMatches with a generic strncmp?
      if ( !( String.Compare( "end", 0, bytes, 0, ( length > 3 ) ? 3 : length ) == 0 ) )
      {
        try
        {
          offset = TclInteger.get( null, tobj );
        }
        catch ( TclException e )
        {
          throw new TclException( interp, "bad index \"" + bytes + "\": must be integer or end?-integer?" + checkBadOctal( interp, bytes ) );
        }
        return offset;
      }

      if ( length <= 3 )
      {
        return endValue;
      }
      else if ( bytes[3] == '-' )
      {
        // This is our limited string expression evaluator

        offset = Util.getInt( interp, bytes.Substring( 3 ) );
        return endValue + offset;
      }
      else
      {
        throw new TclException( interp, "bad index \"" + bytes + "\": must be integer or end?-integer?" + checkBadOctal( interp, bytes.Substring( 3 ) ) );
      }
    }
    internal static string checkBadOctal( Interp interp, string value )
    {
      int p = 0;
      int len = value.Length;

      // A frequent mistake is invalid octal values due to an unwanted
      // leading zero. Try to generate a meaningful error message.

      while ( p < len && System.Char.IsWhiteSpace( value[p] ) )
      {
        p++;
      }
      if ( ( p < len ) && ( value[p] == '+' || value[p] == '-' ) )
      {
        p++;
      }
      if ( ( p < len ) && ( value[p] == '0' ) )
      {
        while ( ( p < len ) && System.Char.IsDigit( value[p] ) )
        {
          // INTL: digit.
          p++;
        }
        while ( ( p < len ) && System.Char.IsWhiteSpace( value[p] ) )
        {
          // INTL: ISO space.
          p++;
        }
        if ( p >= len )
        {
          // Reached end of string
          if ( interp != null )
          {
            return " (looks like invalid octal number)";
          }
        }
      }
      return "";
    }
    internal static StrtodResult strtod( string s, int start )
    // The index to the char where the number starts.
    {
      //bool sign;
      char c;
      int mantSize; // Number of digits in mantissa.
      int decPt; // Number of mantissa digits BEFORE decimal
      // point. 
      int len = s.Length;
      int i = start;

      // Skip any leading blanks.

      while ( i < len && System.Char.IsWhiteSpace( s[i] ) )
      {
        i++;
      }
      if ( i >= len )
      {
        return new StrtodResult( 0, 0, TCL.INVALID_DOUBLE );
      }

      c = s[i];
      if ( c == '-' )
      {
//        sign = true;
        i += 1;
      }
      else
      {
        if ( c == '+' )
        {
          i += 1;
        }
//        sign = false;
      }

      // Count the number of digits in the mantissa (including the decimal
      // point), and also locate the decimal point.

      bool maybeZero = true;
      decPt = -1;
      for ( mantSize = 0 ; ; mantSize += 1 )
      {
        c = CharAt( s, i, len );
        if ( !System.Char.IsDigit( c ) )
        {
          if ( ( c != '.' ) || ( decPt >= 0 ) )
          {
            break;
          }
          decPt = mantSize;
        }
        if ( c != '0' && c != '.' )
        {
          maybeZero = false; // non zero digit found...
        }
        i++;
      }

      // Skim off the exponent.

      if ( ( CharAt( s, i, len ) == 'E' ) || ( CharAt( s, i, len ) == 'e' ) )
      {
        i += 1;
        if ( CharAt( s, i, len ) == '-' )
        {
          i += 1;
        }
        else if ( CharAt( s, i, len ) == '+' )
        {
          i += 1;
        }

        while ( System.Char.IsDigit( CharAt( s, i, len ) ) )
        {
          i += 1;
        }
      }

      s = s.Substring( start, ( i ) - ( start ) );
      double result = 0;

      try
      {
        result = System.Double.Parse( s, System.Globalization.NumberFormatInfo.InvariantInfo );
      }
      catch ( System.OverflowException e )
      {
        return new StrtodResult( 0, 0, TCL.DOUBLE_RANGE );
      }
      catch ( System.FormatException e )
      {
        return new StrtodResult( 0, 0, TCL.INVALID_DOUBLE );
      }

      if ( ( result == System.Double.NegativeInfinity ) || ( result == System.Double.PositiveInfinity ) || ( result == 0.0 && !maybeZero ) )
      {
        return new StrtodResult( result, i, TCL.DOUBLE_RANGE );
      }

      if ( result == System.Double.NaN )
      {
        return new StrtodResult( 0, 0, TCL.INVALID_DOUBLE );
      }

      return new StrtodResult( result, i, 0 );
    }
    internal static char CharAt( string s, int index, int len )
    {
      if ( index >= 0 && index < len )
      {
        return s[index];
      }
      else
      {
        return '\x0000';
      }
    }
    internal static double getDouble( Interp interp, string s )
    {
      int len = s.Length;
      bool sign;
      int i = 0;

      // Skip any leading blanks.

      while ( i < len && System.Char.IsWhiteSpace( s[i] ) )
      {
        i++;
      }
      if ( i >= len )
      {
        throw new TclException( interp, "expected floating-point number but got \"" + s + "\"" );
      }

      char c = s[i];
      if ( c == '-' )
      {
        sign = true;
        i += 1;
      }
      else
      {
        if ( c == '+' )
        {
          i += 1;
        }
        sign = false;
      }

      StrtodResult res = strtod( s, i );
      if ( res.errno != 0 )
      {
        if ( res.errno == TCL.DOUBLE_RANGE )
        {
          if ( interp != null )
          {
            interp.setErrorCode( TclString.newInstance( fpTooBigCode ) );
          }
          throw new TclException( interp, "floating-point value too large to represent" );
        }
        else
        {
          throw new TclException( interp, "expected floating-point number but got \"" + s + "\"" );
        }
      }
      else if ( res.index < len )
      {
        for ( i = res.index ; i < len ; i++ )
        {
          if ( !System.Char.IsWhiteSpace( s[i] ) )
          {
            throw new TclException( interp, "expected floating-point number but got \"" + s + "\"" );
          }
        }
      }

      if ( sign )
      {
        return (double)( -res.value );
      }
      else
      {
        return (double)( res.value );
      }
    }
    internal static string concat( int from, int to, TclObject[] argv )
    // The CmdArgs.
    {
      System.Text.StringBuilder sbuf;

      if ( from > argv.Length )
      {
        return "";
      }
      if ( to <= argv.Length )
      {
        to = argv.Length - 1;
      }

      sbuf = new System.Text.StringBuilder();
      for ( int i = from ; i <= to ; i++ )
      {

        string str = TrimLeft( argv[i].ToString() );
        str = TrimRight( str );
        if ( str.Length == 0 )
        {
          continue;
        }
        sbuf.Append( str );
        if ( i < to )
        {
          sbuf.Append( " " );
        }
      }

      return sbuf.ToString().TrimEnd();
    }
    public static bool stringMatch( string str, string pat )
    //Pattern which may contain special characters.
    {
      char[] strArr = str.ToCharArray();
      char[] patArr = pat.ToCharArray();
      int strLen = str.Length; // Cache the len of str.
      int patLen = pat.Length; // Cache the len of pat.
      int pIndex = 0; // Current index into patArr.
      int sIndex = 0; // Current index into patArr.
      char strch; // Stores current char in string.
      char ch1; // Stores char after '[' in pat.
      char ch2; // Stores look ahead 2 char in pat.
      bool incrIndex = false; // If true it will incr both p/sIndex.

      while ( true )
      {

        if ( incrIndex == true )
        {
          pIndex++;
          sIndex++;
          incrIndex = false;
        }

        // See if we're at the end of both the pattern and the string.
        // If so, we succeeded.  If we're at the end of the pattern
        // but not at the end of the string, we failed.

        if ( pIndex == patLen )
        {
          return sIndex == strLen;
        }
        if ( ( sIndex == strLen ) && ( patArr[pIndex] != '*' ) )
        {
          return false;
        }

        // Check for a "*" as the next pattern character.  It matches
        // any substring.  We handle this by calling ourselves
        // recursively for each postfix of string, until either we
        // match or we reach the end of the string.

        if ( patArr[pIndex] == '*' )
        {
          pIndex++;
          if ( pIndex == patLen )
          {
            return true;
          }
          while ( true )
          {
            if ( stringMatch( str.Substring( sIndex ), pat.Substring( pIndex ) ) )
            {
              return true;
            }
            if ( sIndex == strLen )
            {
              return false;
            }
            sIndex++;
          }
        }

        // Check for a "?" as the next pattern character.  It matches
        // any single character.

        if ( patArr[pIndex] == '?' )
        {
          incrIndex = true;
          continue;
        }

        // Check for a "[" as the next pattern character.  It is followed
        // by a list of characters that are acceptable, or by a range
        // (two characters separated by "-").

        if ( patArr[pIndex] == '[' )
        {
          pIndex++;
          while ( true )
          {
            if ( ( pIndex == patLen ) || ( patArr[pIndex] == ']' ) )
            {
              return false;
            }
            if ( sIndex == strLen )
            {
              return false;
            }
            ch1 = patArr[pIndex];
            strch = strArr[sIndex];
            if ( ( ( pIndex + 1 ) != patLen ) && ( patArr[pIndex + 1] == '-' ) )
            {
              if ( ( pIndex += 2 ) == patLen )
              {
                return false;
              }
              ch2 = patArr[pIndex];
              if ( ( ( ch1 <= strch ) && ( ch2 >= strch ) ) || ( ( ch1 >= strch ) && ( ch2 <= strch ) ) )
              {
                break;
              }
            }
            else if ( ch1 == strch )
            {
              break;
            }
            pIndex++;
          }

          for ( pIndex++ ; ( ( pIndex != patLen ) && ( patArr[pIndex] != ']' ) ) ; pIndex++ )
          {
          }
          if ( pIndex == patLen )
          {
            pIndex--;
          }
          incrIndex = true;
          continue;
        }

        // If the next pattern character is '\', just strip off the '\'
        // so we do exact matching on the character that follows.

        if ( patArr[pIndex] == '\\' )
        {
          pIndex++;
          if ( pIndex == patLen )
          {
            return false;
          }
        }

        // There's no special character.  Just make sure that the next
        // characters of each string match.

        if ( ( sIndex == strLen ) || ( patArr[pIndex] != strArr[sIndex] ) )
        {
          return false;
        }
        incrIndex = true;
      }
    }
    internal static string toTitle( string str )
    // String to convert in place.
    {
      // Capitalize the first character and then lowercase the rest of the
      // characters until we get to the end of string.

      int length = str.Length;
      if ( length == 0 )
      {
        return "";
      }
      System.Text.StringBuilder buf = new System.Text.StringBuilder( length );
      buf.Append( System.Char.ToUpper( str[0] ) );
      buf.Append( str.Substring( 1 ).ToLower() );
      return buf.ToString();
    }
    internal static bool regExpMatch( Interp interp, string inString, TclObject pattern )
    {
      Regexp r = TclRegexp.compile( interp, pattern, false );
      return r.match( inString, (string[])null );
    }
    internal static void appendElement( Interp interp, System.Text.StringBuilder sbuf, string s )
    {
      if ( sbuf.Length > 0 )
      {
        sbuf.Append( ' ' );
      }

      int flags = scanElement( interp, s );
      sbuf.Append( convertElement( s, flags ) );
    }
    internal static FindElemResult findElement( Interp interp, string s, int i, int len )
    {
      int openBraces = 0;
      bool inQuotes = false;

      for ( ; i < len && System.Char.IsWhiteSpace( s[i] ) ; i++ )
      {
        ;
      }
      if ( i >= len )
      {
        return null;
      }
      char c = s[i];
      if ( c == '{' )
      {
        openBraces = 1;
        i++;
      }
      else if ( c == '"' )
      {
        inQuotes = true;
        i++;
      }
      System.Text.StringBuilder sbuf = new System.Text.StringBuilder();

      while ( true )
      {
        if ( i >= len )
        {
          if ( openBraces != 0 )
          {
            throw new TclException( interp, "unmatched open brace in list" );
          }
          else if ( inQuotes )
          {
            throw new TclException( interp, "unmatched open quote in list" );
          }
          return new FindElemResult( i, sbuf.ToString() );
        }

        c = s[i];
        switch ( c )
        {

          // Open brace: don't treat specially unless the element is
          // in braces.  In this case, keep a nesting count.
          case '{':
            if ( openBraces != 0 )
            {
              openBraces++;
            }
            sbuf.Append( c );
            i++;
            break;

          // Close brace: if element is in braces, keep nesting
          // count and quit when the last close brace is seen.


          case '}':
            if ( openBraces == 1 )
            {
              if ( i == len - 1 || System.Char.IsWhiteSpace( s[i + 1] ) )
              {
                return new FindElemResult( i + 1, sbuf.ToString() );
              }
              else
              {
                int errEnd;
                for ( errEnd = i + 1 ; errEnd < len ; errEnd++ )
                {
                  if ( System.Char.IsWhiteSpace( s[errEnd] ) )
                  {
                    break;
                  }
                }
                throw new TclException( interp, "list element in braces followed by \"" + s.Substring( i + 1, ( errEnd ) - ( i + 1 ) ) + "\" instead of space" );
              }
            }
            else if ( openBraces != 0 )
            {
              openBraces--;
            }
            sbuf.Append( c );
            i++;
            break;

          // Backslash:  skip over everything up to the end of the
          // backslash sequence.


          case '\\':
            BackSlashResult bs = Interp.backslash( s, i, len );
            if ( openBraces > 0 )
            {
              // Quotes are ignored in brace-quoted stuff

              sbuf.Append( s.Substring( i, ( bs.nextIndex ) - ( i ) ) );
            }
            else
            {
              sbuf.Append( bs.c );
            }
            i = bs.nextIndex;

            break;

          // Space: ignore if element is in braces or quotes;  otherwise
          // terminate element.


          case ' ':
          case '\f':
          case '\n':
          case '\r':
          case '\t':
            if ( ( openBraces == 0 ) && !inQuotes )
            {
              return new FindElemResult( i + 1, sbuf.ToString() );
            }
            else
            {
              sbuf.Append( c );
              i++;
            }
            break;

          // Double-quote:  if element is in quotes then terminate it.


          case '"':
            if ( inQuotes )
            {
              if ( i == len - 1 || System.Char.IsWhiteSpace( s[i + 1] ) )
              {
                return new FindElemResult( i + 1, sbuf.ToString() );
              }
              else
              {
                int errEnd;
                for ( errEnd = i + 1 ; errEnd < len ; errEnd++ )
                {
                  if ( System.Char.IsWhiteSpace( s[errEnd] ) )
                  {
                    break;
                  }
                }
                throw new TclException( interp, "list element in quotes followed by \"" + s.Substring( i + 1, ( errEnd ) - ( i + 1 ) ) + "\" instead of space" );
              }
            }
            else
            {
              sbuf.Append( c );
              i++;
            }
            break;


          default:
            sbuf.Append( c );
            i++;
            break;

        }
      }
    }
    internal static int scanElement( Interp interp, string inString )
    {
      int flags, nestingLevel;
      char c;
      int len;
      int i;

      // This procedure and Tcl_ConvertElement together do two things:
      //
      // 1. They produce a proper list, one that will yield back the
      // argument strings when evaluated or when disassembled with
      // Tcl_SplitList.  This is the most important thing.
      // 
      // 2. They try to produce legible output, which means minimizing the
      // use of backslashes (using braces instead).  However, there are
      // some situations where backslashes must be used (e.g. an element
      // like "{abc": the leading brace will have to be backslashed.  For
      // each element, one of three things must be done:
      //
      // (a) Use the element as-is (it doesn't contain anything special
      // characters).  This is the most desirable option.
      //
      // (b) Enclose the element in braces, but leave the contents alone.
      // This happens if the element contains embedded space, or if it
      // contains characters with special interpretation ($, [, ;, or \),
      // or if it starts with a brace or double-quote, or if there are
      // no characters in the element.
      //
      // (c) Don't enclose the element in braces, but add backslashes to
      // prevent special interpretation of special characters.  This is a
      // last resort used when the argument would normally fall under case
      // (b) but contains unmatched braces.  It also occurs if the last
      // character of the argument is a backslash or if the element contains
      // a backslash followed by newline.
      //
      // The procedure figures out how many bytes will be needed to store
      // the result (actually, it overestimates).  It also collects
      // information about the element in the form of a flags word.

      nestingLevel = 0;
      flags = 0;

      i = 0;
      len = ( inString != null ? inString.Length : 0 );
      if ( len == 0 )
      {
        inString = '\x0000'.ToString();

        // FIXME : pizza compiler workaround
        // We really should be able to use the "\0" form but there
        // is a nasty bug in the pizza compiler shipped with kaffe
        // that causes "\0" to be read as the empty string.

        //string = "\0";
      }

      System.Diagnostics.Debug.WriteLine( "scanElement string is \"" + inString + "\"" );

      c = inString[i];
      if ( ( c == '{' ) || ( c == '"' ) || ( c == '\x0000' ) )
      {
        flags |= USE_BRACES;
      }
      for ( ; i < len ; i++ )
      {
        System.Diagnostics.Debug.WriteLine( "getting char at index " + i );
        System.Diagnostics.Debug.WriteLine( "char is '" + inString[i] + "'" );

        c = inString[i];
        switch ( c )
        {

          case '{':
            nestingLevel++;
            break;

          case '}':
            nestingLevel--;
            if ( nestingLevel < 0 )
            {
              flags |= TCL_DONT_USE_BRACES | BRACES_UNMATCHED;
            }
            break;

          case '[':
          case '$':
          case ';':
          case ' ':
          case '\f':
          case '\n':
          case '\r':
          case '\t':
          case (char)( 0x0b ):

            flags |= USE_BRACES;
            break;

          case '\\':
            if ( ( i >= len - 1 ) || ( inString[i + 1] == '\n' ) )
            {
              flags = TCL_DONT_USE_BRACES | BRACES_UNMATCHED;
            }
            else
            {
              BackSlashResult bs = Interp.backslash( inString, i, len );

              // Subtract 1 because the for loop will automatically
              // add one on the next iteration.

              i = ( bs.nextIndex - 1 );
              flags |= USE_BRACES;
            }
            break;
        }
      }
      if ( nestingLevel != 0 )
      {
        flags = TCL_DONT_USE_BRACES | BRACES_UNMATCHED;
      }

      return flags;
    }
    internal static string convertElement( string s, int flags )
    // Flags produced by ccanElement
    {
      int i = 0;
      char c;
      int len = ( s != null ? s.Length : 0 );

      // See the comment block at the beginning of the ScanElement
      // code for details of how this works.

      if ( ( (System.Object)s == null ) || ( s.Length == 0 ) || ( s[0] == '\x0000' ) )
      {
        return "{}";
      }

      System.Text.StringBuilder sbuf = new System.Text.StringBuilder();

      if ( ( ( flags & USE_BRACES ) != 0 ) && ( ( flags & TCL_DONT_USE_BRACES ) == 0 ) )
      {
        sbuf.Append( '{' );
        for ( i = 0 ; i < len ; i++ )
        {
          sbuf.Append( s[i] );
        }
        sbuf.Append( '}' );
      }
      else
      {
        c = s[0];
        if ( c == '{' )
        {
          // Can't have a leading brace unless the whole element is
          // enclosed in braces.  Add a backslash before the brace.
          // Furthermore, this may destroy the balance between open
          // and close braces, so set BRACES_UNMATCHED.

          sbuf.Append( '\\' );
          sbuf.Append( '{' );
          i++;
          flags |= BRACES_UNMATCHED;
        }

        for ( ; i < len ; i++ )
        {
          c = s[i];
          switch ( c )
          {

            case ']':
            case '[':
            case '$':
            case ';':
            case ' ':
            case '\\':
            case '"':
              sbuf.Append( '\\' );
              break;


            case '{':
            case '}':

              if ( ( flags & BRACES_UNMATCHED ) != 0 )
              {
                sbuf.Append( '\\' );
              }
              break;


            case '\f':
              sbuf.Append( '\\' );
              sbuf.Append( 'f' );
              continue;


            case '\n':
              sbuf.Append( '\\' );
              sbuf.Append( 'n' );
              continue;


            case '\r':
              sbuf.Append( '\\' );
              sbuf.Append( 'r' );
              continue;


            case '\t':
              sbuf.Append( '\\' );
              sbuf.Append( 't' );
              continue;

            case (char)( 0x0b ):

              sbuf.Append( '\\' );
              sbuf.Append( 'v' );
              continue;
          }

          sbuf.Append( c );
        }
      }

      return sbuf.ToString();
    }
    internal static string TrimLeft( string str, string pattern )
    {
      int i, j;
      char c;
      int strLen = str.Length;
      int patLen = pattern.Length;
      bool done = false;

      for ( i = 0 ; i < strLen ; i++ )
      {
        c = str[i];
        done = true;
        for ( j = 0 ; j < patLen ; j++ )
        {
          if ( c == pattern[j] )
          {
            done = false;
            break;
          }
        }
        if ( done )
        {
          break;
        }
      }
      return str.Substring( i, ( strLen ) - ( i ) );
    }
    internal static string TrimLeft( string str )
    {
      return TrimLeft( str, " \n\t\r" );
    }
    internal static string TrimRight( string str, string pattern )
    {
      int last = str.Length - 1;
      char[] strArray = str.ToCharArray();
      int c;

      // Remove trailing characters...

      while ( last >= 0 )
      {
        c = strArray[last];
        if ( pattern.IndexOf( (System.Char)c ) == -1 )
        {
          break;
        }
        last--;
      }
      return str.Substring( 0, ( last + 1 ) - ( 0 ) );
    }

    internal static string TrimRight( string str )
    {
      return TrimRight( str, " \n\t\r" );
    }
    internal static bool getBoolean( Interp interp, string inString )
    {
      string s = inString.ToLower();

      // The length of 's' needs to be > 1 if it begins with 'o', 
      // in order to compare between "on" and "off".

      if ( s.Length > 0 )
      {
        if ( "yes".StartsWith( s ) )
        {
          return true;
        }
        else if ( "no".StartsWith( s ) )
        {
          return false;
        }
        else if ( "true".StartsWith( s ) )
        {
          return true;
        }
        else if ( "false".StartsWith( s ) )
        {
          return false;
        }
        else if ( "on".StartsWith( s ) && s.Length > 1 )
        {
          return true;
        }
        else if ( "off".StartsWith( s ) && s.Length > 1 )
        {
          return false;
        }
        else if ( s.Equals( "0" ) )
        {
          return false;
        }
        else if ( s.Equals( "1" ) )
        {
          return true;
        }
      }

      throw new TclException( interp, "expected boolean value but got \"" + inString + "\"" );
    }
    internal static void setupPrecisionTrace( Interp interp )
    // Current interpreter.
    {
      try
      {
        interp.traceVar( "tcl_precision", new PrecTraceProc(), TCL.VarFlag.GLOBAL_ONLY | TCL.VarFlag.TRACE_WRITES | TCL.VarFlag.TRACE_READS | TCL.VarFlag.TRACE_UNSETS );
      }
      catch ( TclException e )
      {
        throw new TclRuntimeError( "unexpected TclException: " + e.Message, e );
      }
    }
    internal static string printDouble( double number )
    // The number to format into a string.
    {
      string s = FormatCmd.toString( number, precision, 10 ).Replace("E","e");
      int length = s.Length;
      for ( int i = 0 ; i < length ; i++ )
      {
        if ( ( s[i] == '.' ) || System.Char.IsLetter( s[i] ) )
        {
          return s;
        }
      }
      return string.Concat( s, ".0" );
    }
    internal static string tryGetSystemProperty( string propName, string defautlValue )
    // Default value.
    {
      try
      {
        
        // ATK return System_Renamed.getProperty(propName);
        return System.Environment.GetEnvironmentVariable( "os.name" );
      }
      catch ( System.Security.SecurityException e )
      {
        return defautlValue;
      }
    }
    static Util()
    {
      precision = DEFAULT_PRECISION;
    }
  } // end Util

  /* 
  *----------------------------------------------------------------------
  *
  * PrecTraceProc.java --
  *
  *	 The PrecTraceProc class is used to implement variable traces for
  * 	the tcl_precision variable to control precision used when
  * 	converting floating-point values to strings.
  *
  *----------------------------------------------------------------------
  */

  sealed class PrecTraceProc : VarTrace
  {

    // Maximal precision supported by Tcl.

    internal const int TCL_MAX_PREC = 17;

    public void traceProc( Interp interp, string name1, string name2, TCL.VarFlag flags )
    {
      // If the variable is unset, then recreate the trace and restore
      // the default value of the format string.

      if ( ( flags & TCL.VarFlag.TRACE_UNSETS ) != 0 )
      {
        if ( ( ( flags & TCL.VarFlag.TRACE_DESTROYED ) != 0 ) && ( ( flags & TCL.VarFlag.INTERP_DESTROYED ) == 0 ) )
        {
          interp.traceVar( name1, name2, new PrecTraceProc(), TCL.VarFlag.GLOBAL_ONLY | TCL.VarFlag.TRACE_WRITES | TCL.VarFlag.TRACE_READS | TCL.VarFlag.TRACE_UNSETS );
          Util.precision = Util.DEFAULT_PRECISION;
        }
        return;
      }

      // When the variable is read, reset its value from our shared
      // value. This is needed in case the variable was modified in
      // some other interpreter so that this interpreter's value is
      // out of date.

      if ( ( flags & TCL.VarFlag.TRACE_READS ) != 0 )
      {
        interp.setVar( name1, name2, TclInteger.newInstance( Util.precision ), flags & TCL.VarFlag.GLOBAL_ONLY );
        return;
      }

      // The variable is being written. Check the new value and disallow
      // it if it isn't reasonable.
      //
      // (ToDo) Disallow it if this is a safe interpreter (we don't want
      // safe interpreters messing up the precision of other
      // interpreters).

      TclObject tobj = null;
      try
      {
        tobj = interp.getVar( name1, name2, ( flags & TCL.VarFlag.GLOBAL_ONLY ) );
      }
      catch ( TclException e )
      {
        // Do nothing when var does not exist.
      }

      string value;

      if ( tobj != null )
      {

        value = tobj.ToString();
      }
      else
      {
        value = "";
      }

      StrtoulResult r = Util.strtoul( value, 0, 10 );

      if ( ( r == null ) || ( r.value <= 0 ) || ( r.value > TCL_MAX_PREC ) || ( r.value > 100 ) || ( r.index == 0 ) || ( r.index != value.Length ) )
      {
        interp.setVar( name1, name2, TclInteger.newInstance( Util.precision ), TCL.VarFlag.GLOBAL_ONLY );
        throw new TclException( interp, "improper value for precision" );
      }

      Util.precision = (int)r.value;
    }
  } // end PrecTraceProc
}
