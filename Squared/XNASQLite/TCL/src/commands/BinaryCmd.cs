/*
* BinaryCmd.java --
*
*	Implements the built-in "binary" Tcl command.
*
* Copyright (c) 1999 Christian Krone.
* Copyright (c) 1997 by Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: BinaryCmd.java,v 1.2 2002/05/07 06:58:06 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{

  /*
  * This class implements the built-in "binary" command in Tcl.
  */

  class BinaryCmd : Command
  {

    private static readonly string[] validCmds = new string[] { "format", "scan" };
    private const string HEXDIGITS = "0123456789abcdef";

    private const int CMD_FORMAT = 0;
    private const int CMD_SCAN = 1;

    // The following constants are used by GetFormatSpec to indicate various
    // special conditions in the parsing of a format specifier.

    // Use all elements in the argument.
    private const int BINARY_ALL = -1;
    // No count was specified in format.
    private const int BINARY_NOCOUNT = -2;
    // End of format was found.
    private const char FORMAT_END = ' ';

    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
    {
      int arg; // Index of next argument to consume.
      char[] format = null; // User specified format string.
      char cmd; // Current format character.
      int cursor; // Current position within result buffer.
      int maxPos; // Greatest position within result buffer that
      // cursor has visited.
      int value = 0; // Current integer value to be packed.
      // Initialized to avoid compiler warning.
      int offset, size = 0, length;//, index;

      if ( argv.Length < 2 )
      {
        throw new TclNumArgsException( interp, 1, argv, "option ?arg arg ...?" );
      }
      int cmdIndex = TclIndex.get( interp, argv[1], validCmds, "option", 0 );

      switch ( cmdIndex )
      {

        case CMD_FORMAT:
          {
            if ( argv.Length < 3 )
            {
              throw new TclNumArgsException( interp, 2, argv, "formatString ?arg arg ...?" );
            }

            // To avoid copying the data, we format the string in two passes.
            // The first pass computes the size of the output buffer.  The
            // second pass places the formatted data into the buffer.

            format = argv[2].ToString().ToCharArray();
            arg = 3;
            length = 0;
            offset = 0;
            System.Int32 parsePos = 0;

            while ( ( cmd = GetFormatSpec( format, ref parsePos ) ) != FORMAT_END )
            {
              int count = GetFormatCount( format, ref parsePos );

              switch ( cmd )
              {

                case 'a':
                case 'A':
                case 'b':
                case 'B':
                case 'h':
                case 'H':
                  {
                    // For string-type specifiers, the count corresponds
                    // to the number of bytes in a single argument.

                    if ( arg >= argv.Length )
                    {
                      missingArg( interp );
                    }
                    if ( count == BINARY_ALL )
                    {
                      count = TclByteArray.getLength( interp, argv[arg] );
                    }
                    else if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    arg++;
                    switch ( cmd )
                    {

                      case 'a':
                      case 'A': offset += count; break;

                      case 'b':
                      case 'B': offset += ( count + 7 ) / 8; break;

                      case 'h':
                      case 'H': offset += ( count + 1 ) / 2; break;
                    }
                    break;
                  }

                case 'c':
                case 's':
                case 'S':
                case 'i':
                case 'I':
                case 'f':
                case 'd':
                  {
                    if ( arg >= argv.Length )
                    {
                      missingArg( interp );
                    }
                    switch ( cmd )
                    {

                      case 'c': size = 1; break;

                      case 's':
                      case 'S': size = 2; break;

                      case 'i':
                      case 'I': size = 4; break;

                      case 'f': size = 4; break;

                      case 'd': size = 8; break;
                    }

                    // For number-type specifiers, the count corresponds
                    // to the number of elements in the list stored in
                    // a single argument.  If no count is specified, then
                    // the argument is taken as a single non-list value.

                    if ( count == BINARY_NOCOUNT )
                    {
                      arg++;
                      count = 1;
                    }
                    else
                    {
                      int listc = TclList.getLength( interp, argv[arg++] );
                      if ( count == BINARY_ALL )
                      {
                        count = listc;
                      }
                      else if ( count > listc )
                      {
                        throw new TclException( interp, "number of elements in list" + " does not match count" );
                      }
                    }
                    offset += count * size;
                    break;
                  }

                case 'x':
                  {
                    if ( count == BINARY_ALL )
                    {
                      throw new TclException( interp, "cannot use \"*\"" + " in format string with \"x\"" );
                    }
                    if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    offset += count;
                    break;
                  }

                case 'X':
                  {
                    if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    if ( ( count > offset ) || ( count == BINARY_ALL ) )
                    {
                      count = offset;
                    }
                    if ( offset > length )
                    {
                      length = offset;
                    }
                    offset -= count;
                    break;
                  }

                case '@':
                  {
                    if ( offset > length )
                    {
                      length = offset;
                    }
                    if ( count == BINARY_ALL )
                    {
                      offset = length;
                    }
                    else if ( count == BINARY_NOCOUNT )
                    {
                      alephWithoutCount( interp );
                    }
                    else
                    {
                      offset = count;
                    }
                    break;
                  }

                default:
                  {
                    badField( interp, cmd );
                  }
                  break;

              }
            }
            if ( offset > length )
            {
              length = offset;
            }
            if ( length == 0 )
            {
              return TCL.CompletionCode.RETURN;
            }

            // Prepare the result object by preallocating the calculated
            // number of bytes and filling with nulls.

            TclObject resultObj = TclByteArray.newInstance();
            resultObj._typePtr = "bytearray";
            byte[] resultBytes = TclByteArray.setLength( interp, resultObj, length );
            interp.setResult( resultObj );

            // Pack the data into the result object.  Note that we can skip
            // the error checking during this pass, since we have already
            // parsed the string once.

            arg = 3;
            cursor = 0;
            maxPos = cursor;
            parsePos = 0;

            while ( ( cmd = GetFormatSpec( format, ref parsePos ) ) != FORMAT_END )
            {
              int count = GetFormatCount( format, ref parsePos );

              if ( ( count == 0 ) && ( cmd != '@' ) )
              {
                arg++;
                continue;
              }

              switch ( cmd )
              {

                case 'a':
                case 'A':
                  {
                    byte pad = ( cmd == 'a' ) ? (byte)0 : (byte)SupportClass.Identity( ' ' );
                    byte[] bytes = TclByteArray.getBytes( interp, argv[arg++] );
                    length = bytes.Length;

                    if ( count == BINARY_ALL )
                    {
                      count = length;
                    }
                    else if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    if ( length >= count )
                    {
                      Array.Copy( bytes, 0, resultBytes, cursor, count );
                    }
                    else
                    {
                      Array.Copy( bytes, 0, resultBytes, cursor, length );
                      for ( int ix = 0 ; ix < count - length ; ix++ )
                      {
                        resultBytes[cursor + length + ix] = pad;
                      }
                    }
                    cursor += count;
                    break;
                  }

                case 'b':
                case 'B':
                  {
                    char[] str = argv[arg++].ToString().ToCharArray();
                    if ( count == BINARY_ALL )
                    {
                      count = str.Length;
                    }
                    else if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    int last = cursor + ( ( count + 7 ) / 8 );
                    if ( count > str.Length )
                    {
                      count = str.Length;
                    }
                    if ( cmd == 'B' )
                    {
                      for ( offset = 0 ; offset < count ; offset++ )
                      {
                        value <<= 1;
                        if ( str[offset] == '1' )
                        {
                          value |= 1;
                        }
                        else if ( str[offset] != '0' )
                        {
                          expectedButGot( interp, "binary", new string( str ) );
                        }
                        if ( ( ( offset + 1 ) % 8 ) == 0 )
                        {
                          resultBytes[cursor++] = (byte)value;
                          value = 0;
                        }
                      }
                    }
                    else
                    {
                      for ( offset = 0 ; offset < count ; offset++ )
                      {
                        value >>= 1;
                        if ( str[offset] == '1' )
                        {
                          value |= 128;
                        }
                        else if ( str[offset] != '0' )
                        {
                          expectedButGot( interp, "binary", new string( str ) );
                        }
                        if ( ( ( offset + 1 ) % 8 ) == 0 )
                        {
                          resultBytes[cursor++] = (byte)value;
                          value = 0;
                        }
                      }
                    }
                    if ( ( offset % 8 ) != 0 )
                    {
                      if ( cmd == 'B' )
                      {
                        value <<= 8 - ( offset % 8 );
                      }
                      else
                      {
                        value >>= 8 - ( offset % 8 );
                      }
                      resultBytes[cursor++] = (byte)value;
                    }
                    while ( cursor < last )
                    {
                      resultBytes[cursor++] = 0;
                    }
                    break;
                  }

                case 'h':
                case 'H':
                  {
                    char[] str = argv[arg++].ToString().ToCharArray();
                    if ( count == BINARY_ALL )
                    {
                      count = str.Length;
                    }
                    else if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    int last = cursor + ( ( count + 1 ) / 2 );
                    if ( count > str.Length )
                    {
                      count = str.Length;
                    }
                    if ( cmd == 'H' )
                    {
                      for ( offset = 0 ; offset < count ; offset++ )
                      {
                        value <<= 4;
                        int c = HEXDIGITS.IndexOf( Char.ToLower( str[offset] ) );
                        if ( c < 0 )
                        {
                          expectedButGot( interp, "hexadecimal", new string( str ) );
                        }
                        value |= ( c & 0xf );
                        if ( ( offset % 2 ) != 0 )
                        {
                          resultBytes[cursor++] = (byte)value;
                          value = 0;
                        }
                      }
                    }
                    else
                    {
                      for ( offset = 0 ; offset < count ; offset++ )
                      {
                        value >>= 4;
                        int c = HEXDIGITS.IndexOf( Char.ToLower( str[offset] ) );
                        if ( c < 0 )
                        {
                          expectedButGot( interp, "hexadecimal", new string( str ) );
                        }
                        value |= ( ( c << 4 ) & 0xf0 );
                        if ( ( offset % 2 ) != 0 )
                        {
                          resultBytes[cursor++] = (byte)value;
                          value = 0;
                        }
                      }
                    }
                    if ( ( offset % 2 ) != 0 )
                    {
                      if ( cmd == 'H' )
                      {
                        value <<= 4;
                      }
                      else
                      {
                        value >>= 4;
                      }
                      resultBytes[cursor++] = (byte)value;
                    }
                    while ( cursor < last )
                    {
                      resultBytes[cursor++] = 0;
                    }
                    break;
                  }

                case 'c':
                case 's':
                case 'S':
                case 'i':
                case 'I':
                case 'f':
                case 'd':
                  {
                    TclObject[] listv;

                    if ( count == BINARY_NOCOUNT )
                    {
                      listv = new TclObject[1];
                      listv[0] = argv[arg++];
                      count = 1;
                    }
                    else
                    {
                      listv = TclList.getElements( interp, argv[arg++] );
                      if ( count == BINARY_ALL )
                      {
                        count = listv.Length;
                      }
                    }
                    for ( int ix = 0 ; ix < count ; ix++ )
                    {
                      cursor = FormatNumber( interp, cmd, listv[ix], resultBytes, cursor );
                    }
                    break;
                  }

                case 'x':
                  {
                    if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    for ( int ix = 0 ; ix < count ; ix++ )
                    {
                      resultBytes[cursor++] = 0;
                    }
                    break;
                  }

                case 'X':
                  {
                    if ( cursor > maxPos )
                    {
                      maxPos = cursor;
                    }
                    if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    if ( count == BINARY_ALL || count > cursor )
                    {
                      cursor = 0;
                    }
                    else
                    {
                      cursor -= count;
                    }
                    break;
                  }

                case '@':
                  {
                    if ( cursor > maxPos )
                    {
                      maxPos = cursor;
                    }
                    if ( count == BINARY_ALL )
                    {
                      cursor = maxPos;
                    }
                    else
                    {
                      cursor = count;
                    }
                    break;
                  }
              }
            }
            break;
          }

        case CMD_SCAN:
          {
            if ( argv.Length < 4 )
            {
              throw new TclNumArgsException( interp, 2, argv, "value formatString ?varName varName ...?" );
            }
            byte[] src = TclByteArray.getBytes( interp, argv[2] );
            length = src.Length;
            format = argv[3].ToString().ToCharArray();
            arg = 4;
            cursor = 0;
            offset = 0;
            System.Int32 parsePos = 0;

            while ( ( cmd = GetFormatSpec( format, ref parsePos ) ) != FORMAT_END )
            {
              int count = GetFormatCount( format, ref parsePos );

              switch ( cmd )
              {

                case 'a':
                case 'A':
                  {
                    if ( arg >= argv.Length )
                    {
                      missingArg( interp );
                    }
                    if ( count == BINARY_ALL )
                    {
                      count = length - offset;
                    }
                    else
                    {
                      if ( count == BINARY_NOCOUNT )
                      {
                        count = 1;
                      }
                      if ( count > length - offset )
                      {
                        break;
                      }
                    }

                    size = count;

                    // Trim trailing nulls and spaces, if necessary.

                    if ( cmd == 'A' )
                    {
                      while ( size > 0 )
                      {
                        if ( src[offset + size - 1] != '\x0000' && src[offset + size - 1] != ' ' )
                        {
                          break;
                        }
                        size--;
                      }
                    }

                    interp.setVar( argv[arg++], TclByteArray.newInstance( src, offset, size ), 0 );

                    offset += count;
                    break;
                  }

                case 'b':
                case 'B':
                  {
                    if ( arg >= argv.Length )
                    {
                      missingArg( interp );
                    }
                    if ( count == BINARY_ALL )
                    {
                      count = ( length - offset ) * 8;
                    }
                    else
                    {
                      if ( count == BINARY_NOCOUNT )
                      {
                        count = 1;
                      }
                      if ( count > ( length - offset ) * 8 )
                      {
                        break;
                      }
                    }
                    System.Text.StringBuilder s = new System.Text.StringBuilder( count );
                    int thisOffset = offset;

                    if ( cmd == 'b' )
                    {
                      for ( int ix = 0 ; ix < count ; ix++ )
                      {
                        if ( ( ix % 8 ) != 0 )
                        {
                          value >>= 1;
                        }
                        else
                        {
                          value = src[thisOffset++];
                        }
                        s.Append( ( value & 1 ) != 0 ? '1' : '0' );
                      }
                    }
                    else
                    {
                      for ( int ix = 0 ; ix < count ; ix++ )
                      {
                        if ( ( ix % 8 ) != 0 )
                        {
                          value <<= 1;
                        }
                        else
                        {
                          value = src[thisOffset++];
                        }
                        s.Append( ( value & 0x80 ) != 0 ? '1' : '0' );
                      }
                    }

                    interp.setVar( argv[arg++], TclString.newInstance( s.ToString() ), 0 );

                    offset += ( count + 7 ) / 8;
                    break;
                  }

                case 'h':
                case 'H':
                  {
                    if ( arg >= argv.Length )
                    {
                      missingArg( interp );
                    }
                    if ( count == BINARY_ALL )
                    {
                      count = ( length - offset ) * 2;
                    }
                    else
                    {
                      if ( count == BINARY_NOCOUNT )
                      {
                        count = 1;
                      }
                      if ( count > ( length - offset ) * 2 )
                      {
                        break;
                      }
                    }
                    System.Text.StringBuilder s = new System.Text.StringBuilder( count );
                    int thisOffset = offset;

                    if ( cmd == 'h' )
                    {
                      for ( int ix = 0 ; ix < count ; ix++ )
                      {
                        if ( ( ix % 2 ) != 0 )
                        {
                          value >>= 4;
                        }
                        else
                        {
                          value = src[thisOffset++];
                        }
                        s.Append( HEXDIGITS[value & 0xf] );
                      }
                    }
                    else
                    {
                      for ( int ix = 0 ; ix < count ; ix++ )
                      {
                        if ( ( ix % 2 ) != 0 )
                        {
                          value <<= 4;
                        }
                        else
                        {
                          value = src[thisOffset++];
                        }
                        s.Append( HEXDIGITS[value >> 4 & 0xf] );
                      }
                    }

                    interp.setVar( argv[arg++], TclString.newInstance( s.ToString() ), 0 );

                    offset += ( count + 1 ) / 2;
                    break;
                  }

                case 'c':
                case 's':
                case 'S':
                case 'i':
                case 'I':
                case 'f':
                case 'd':
                  {
                    if ( arg >= argv.Length )
                    {
                      missingArg( interp );
                    }
                    switch ( cmd )
                    {

                      case 'c': size = 1; break;

                      case 's':
                      case 'S': size = 2; break;

                      case 'i':
                      case 'I': size = 4; break;

                      case 'f': size = 4; break;

                      case 'd': size = 8; break;
                    }
                    TclObject valueObj;
                    if ( count == BINARY_NOCOUNT )
                    {
                      if ( length - offset < size )
                      {
                        break;
                      }
                      valueObj = ScanNumber( src, offset, cmd );
                      offset += size;
                    }
                    else
                    {
                      if ( count == BINARY_ALL )
                      {
                        count = ( length - offset ) / size;
                      }
                      if ( length - offset < count * size )
                      {
                        break;
                      }
                      valueObj = TclList.newInstance();
                      int thisOffset = offset;
                      for ( int ix = 0 ; ix < count ; ix++ )
                      {
                        TclList.append( null, valueObj, ScanNumber( src, thisOffset, cmd ) );
                        thisOffset += size;
                      }
                      offset += count * size;
                    }

                    interp.setVar( argv[arg++], valueObj, 0 );

                    break;
                  }

                case 'x':
                  {
                    if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    if ( count == BINARY_ALL || count > length - offset )
                    {
                      offset = length;
                    }
                    else
                    {
                      offset += count;
                    }
                    break;
                  }

                case 'X':
                  {
                    if ( count == BINARY_NOCOUNT )
                    {
                      count = 1;
                    }
                    if ( count == BINARY_ALL || count > offset )
                    {
                      offset = 0;
                    }
                    else
                    {
                      offset -= count;
                    }
                    break;
                  }

                case '@':
                  {
                    if ( count == BINARY_NOCOUNT )
                    {
                      alephWithoutCount( interp );
                    }
                    if ( count == BINARY_ALL || count > length )
                    {
                      offset = length;
                    }
                    else
                    {
                      offset = count;
                    }
                    break;
                  }

                default:
                  {
                    badField( interp, cmd );
                  }
                  break;

              }
            }

            // Set the result to the last position of the cursor.

            interp.setResult( arg - 4 );
          }
          break;
      }
      return TCL.CompletionCode.RETURN;
    }
    private char GetFormatSpec( char[] format, ref System.Int32 parsePos )
    // Current position in input.
    {
      int ix = parsePos;

      // Skip any leading blanks.

      while ( ix < format.Length && format[ix] == ' ' )
      {
        ix++;
      }

      // The string was empty, except for whitespace, so fail.

      if ( ix >= format.Length )
      {
        parsePos = ix;
        return FORMAT_END;
      }

      // Extract the command character.

      parsePos = ix + 1;

      return format[ix++];
    }
    private int GetFormatCount( char[] format, ref System.Int32 parsePos )
    // Current position in input.
    {
      int ix = parsePos;

      // Extract any trailing digits or '*'.

      if ( ix < format.Length && format[ix] == '*' )
      {
        parsePos = ix + 1;
        return BINARY_ALL;
      }
      else if ( ix < format.Length && System.Char.IsDigit( format[ix] ) )
      {
        int length = 1;
        while ( ix + length < format.Length && System.Char.IsDigit( format[ix + length] ) )
        {
          length++;
        }
        parsePos = ix + length;
        return System.Int32.Parse( new string( format, ix, length ) );
      }
      else
      {
        return BINARY_NOCOUNT;
      }
    }
    internal static int FormatNumber( Interp interp, char type, TclObject src, byte[] resultBytes, int cursor )
    {
      if ( type == 'd' )
      {
        double dvalue = TclDouble.get( interp, src );
        System.IO.MemoryStream ms = new System.IO.MemoryStream( resultBytes, cursor, 8 );
        System.IO.BinaryWriter writer = new System.IO.BinaryWriter( ms );
        writer.Write( dvalue );
        cursor += 8;
        writer.Close();
        ms.Close();
      }
      else if ( type == 'f' )
      {
        float fvalue = (float)TclDouble.get( interp, src );
        System.IO.MemoryStream ms = new System.IO.MemoryStream( resultBytes, cursor, 4 );
        System.IO.BinaryWriter writer = new System.IO.BinaryWriter( ms );
        writer.Write( fvalue );
        cursor += 4;
        writer.Close();
        ms.Close();
      }
      else
      {
        int value = TclInteger.get( interp, src );

        if ( type == 'c' )
        {
          resultBytes[cursor++] = (byte)value;
        }
        else if ( type == 's' )
        {
          resultBytes[cursor++] = (byte)value;
          resultBytes[cursor++] = (byte)( value >> 8 );
        }
        else if ( type == 'S' )
        {
          resultBytes[cursor++] = (byte)( value >> 8 );
          resultBytes[cursor++] = (byte)value;
        }
        else if ( type == 'i' )
        {
          resultBytes[cursor++] = (byte)value;
          resultBytes[cursor++] = (byte)( value >> 8 );
          resultBytes[cursor++] = (byte)( value >> 16 );
          resultBytes[cursor++] = (byte)( value >> 24 );
        }
        else if ( type == 'I' )
        {
          resultBytes[cursor++] = (byte)( value >> 24 );
          resultBytes[cursor++] = (byte)( value >> 16 );
          resultBytes[cursor++] = (byte)( value >> 8 );
          resultBytes[cursor++] = (byte)value;
        }
      }
      return cursor;
    }
    private static TclObject ScanNumber( byte[] src, int pos, int type )
    // Format character from "binary scan"
    {
      switch ( type )
      {

        case 'c':
          {
            return TclInteger.newInstance( (sbyte)src[pos] );
          }

        case 's':
          {
            short value = (short)( ( src[pos] & 0xff ) + ( ( src[pos + 1] & 0xff ) << 8 ) );
            return TclInteger.newInstance( (int)value );
          }

        case 'S':
          {
            short value = (short)( ( src[pos + 1] & 0xff ) + ( ( src[pos] & 0xff ) << 8 ) );
            return TclInteger.newInstance( (int)value );
          }

        case 'i':
          {
            int value = ( src[pos] & 0xff ) + ( ( src[pos + 1] & 0xff ) << 8 ) + ( ( src[pos + 2] & 0xff ) << 16 ) + ( ( src[pos + 3] & 0xff ) << 24 );
            return TclInteger.newInstance( value );
          }
        case 'I':
          {
            int value = ( src[pos + 3] & 0xff ) + ( ( src[pos + 2] & 0xff ) << 8 ) + ( ( src[pos + 1] & 0xff ) << 16 ) + ( ( src[pos] & 0xff ) << 24 );
            return TclInteger.newInstance( value );
          }
        case 'f':
          {
            System.IO.MemoryStream ms = new System.IO.MemoryStream( src, pos, 4, false );
            System.IO.BinaryReader reader = new System.IO.BinaryReader( ms );
            double fvalue = reader.ReadSingle();
            reader.Close();
            ms.Close();
            return TclDouble.newInstance( fvalue );
          }
        case 'd':
          {
            System.IO.MemoryStream ms = new System.IO.MemoryStream( src, pos, 8, false );
            System.IO.BinaryReader reader = new System.IO.BinaryReader( ms );
            double dvalue = reader.ReadDouble();
            reader.Close();
            ms.Close();
            return TclDouble.newInstance( dvalue );
          }
      }
      return null;
    }

    /// <summary> Called whenever a format specifier was detected
    /// but there are not enough arguments specified.
    /// 
    /// </summary>
    /// <param name="interp"> - The TclInterp which called the cmdProc method.
    /// </param>

    private static void missingArg( Interp interp )
    {
      throw new TclException( interp, "not enough arguments for all format specifiers" );
    }

    /// <summary> Called whenever an invalid format specifier was detected.
    /// 
    /// </summary>
    /// <param name="interp"> - The TclInterp which called the cmdProc method.
    /// </param>
    /// <param name="cmd">    - The invalid field specifier.
    /// </param>

    private static void badField( Interp interp, char cmd )
    {
      throw new TclException( interp, "bad field specifier \"" + cmd + "\"" );
    }

    /// <summary> Called whenever a letter aleph character (@) was detected
    /// but there was no count specified.
    /// 
    /// </summary>
    /// <param name="interp"> - The TclInterp which called the cmdProc method.
    /// </param>

    private static void alephWithoutCount( Interp interp )
    {
      throw new TclException( interp, "missing count for \"@\" field specifier" );
    }

    /// <summary> Called whenever a format was found which restricts the valid range
    /// of characters in the specified string, but the string contains
    /// at least one char not in this range.
    /// 
    /// </summary>
    /// <param name="interp"> - The TclInterp which called the cmdProc method.
    /// </param>

    private static void expectedButGot( Interp interp, string expected, string str )
    {
      throw new TclException( interp, "expected " + expected + " string but got \"" + str + "\" instead" );
    }
  } // end BinaryCmd
}
