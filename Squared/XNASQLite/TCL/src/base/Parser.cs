#undef DEBUG

/*
* Parser.java --
*
*	This class contains methods that parse Tcl scripts.  They
*	do so in a general-purpose fashion that can be used for many
*	different purposes, including compilation, direct execution,
*	code analysis, etc.  This class also includes a few additional
*	procedures such as evalObjv, eval, and eval2, which allow 
*	scripts to be evaluated directly, without compiling.
*
* Copyright (c) 1998 by Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and redistribution
* of this file, and for a DISCLAIMER OF ALL WARRANTIES.
*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Parser.java,v 1.17 2003/07/25 17:41:53 mdejong Exp $
*/
using System;
using System.Diagnostics;
namespace tcl.lang
{

  public class Parser
  {
    public static TclParse parseCommand( Interp interp, char[] script_array, int script_index, int numBytes, string fileName, int lineNum, bool nested )
    // True means that this is a nested command:
    // close bracket should be considered
    // a command terminator.  If false, then close
    // bracket has no special meaning. 
    {

      char cur; //the char we are currently parsing
      int type; // Result returned by charType(src.charAt()). 
      TclToken token; // Pointer to token being filled in. 
      int wordIndex; // Index of word token for current word. 
      int level; // Nesting level of curly braces: gives
      // number of right braces we must find to
      // end word. 
      TclParse parse; // Return value to fill in with information
      // about the parsed command.
      int terminators; // charType() bits that indicate the end
      // of a command. 
      BackSlashResult bs; // Result of a call to backslash(...).
      int endIndex; // Index that points to the character after
      // the last character to be parsed.
      char savedChar; // To terminate the parsing correctly, the
      // character at endIndex is set to \0.  This
      // stores the value to return when finished.


      int saved_script_index = script_index; //save the original index


      int script_length = script_array.Length - 1;


      if ( numBytes < 0 )
      {
        numBytes = script_length - script_index;
      }
      endIndex = script_index + numBytes;
      if ( endIndex > script_length )
      {
        endIndex = script_length;
      }

      savedChar = script_array[endIndex];
      script_array[endIndex] = '\x0000';

      parse = new TclParse( interp, script_array, endIndex, fileName, lineNum );

      if ( nested )
      {
        terminators = TYPE_COMMAND_END | TYPE_CLOSE_BRACK;
      }
      else
      {
        terminators = TYPE_COMMAND_END;
      }

      // Parse any leading space and comments before the first word of the
      // command.


      try
      {

        while ( true )
        {

          cur = script_array[script_index];

          while ( ( ( cur <= TYPE_MAX ) && ( typeTable[cur] == TYPE_SPACE ) ) || ( cur == '\n' ) )
          {
            cur = script_array[++script_index];
          }

          if ( ( cur == '\\' ) && ( script_array[script_index + 1] == '\n' ) )
          {

            // Skip backslash-newline sequence: it should be treated
            // just like white space.

            if ( ( script_index + 2 ) == parse.endIndex )
            {
              parse.incomplete = true;
            }

            //this will add 2 to the offset and return to the top
            //of the while(true) loop which will get the next cur

            script_index += 2;
            continue;
          }

          // If we have found the start of a command goto the word parsing loop
          if ( cur != '#' )
          {
            break;
          }

          // Record the index where the comment starts
          if ( parse.commentStart < 0 )
          {
            parse.commentStart = script_index;
          }

          while ( true )
          {
            cur = script_array[script_index];
            if ( script_index == parse.endIndex )
            {
              if ( nested )
                parse.incomplete = true;
              parse.commentSize = script_index - parse.commentStart;
              break;
            }
            else if ( cur == '\\' )
            {
              if ( ( script_array[script_index + 1] == '\n' ) && ( ( script_index + 2 ) == parse.endIndex ) )
              {
                parse.incomplete = true;
              }
              bs = backslash( script_array, script_index );
              script_index = bs.nextIndex;
            }
            else if ( cur == '\n' )
            {
              script_index++;
              parse.commentSize = script_index - parse.commentStart;
              break;
            }
            else
            {
              script_index++;
            }
          }
        }



        // The following loop parses the words of the command, one word
        // in each iteration through the loop.

        parse.commandStart = script_index;

        while ( true )
        {

          // Create the token for  the word.
          wordIndex = parse.numTokens;

          token = parse.getToken( wordIndex );
          token.type = TCL_TOKEN_WORD;

          // Skip white space before the word. Also skip a backslash-newline
          // sequence: it should be treated just like white space.


          while ( true )
          {
            cur = script_array[script_index];
            type = ( ( cur > TYPE_MAX ) ? TYPE_NORMAL : typeTable[cur] );

            if ( type == TYPE_SPACE )
            {
              script_index++;
              continue;
            }
            else if ( ( cur == '\\' ) && ( script_array[script_index + 1] == '\n' ) )
            {
              if ( ( script_index + 2 ) == parse.endIndex )
              {
                parse.incomplete = true;
              }
              bs = backslash( script_array, script_index );
              script_index = bs.nextIndex;
              continue;
            }
            break;
          }

          if ( ( type & terminators ) != 0 )
          {
            script_index++;
            break;
          }

          if ( script_index == parse.endIndex )
          {
            if ( nested && savedChar != ']' )
            {
              parse.incomplete = true;
              throw new TclException( interp, "missing close-bracket" );
            }
            break;
          }

          token.script_array = script_array;
          token.script_index = script_index;

          parse.numTokens++;
          parse.numWords++;


          // At this point the word can have one of three forms: something
          // enclosed in quotes, something enclosed in braces, or an
          // unquoted word (anything else).

          cur = script_array[script_index];

          if ( cur == '"' )
          {
            script_index++;
            parse = parseTokens( script_array, script_index, TYPE_QUOTE, parse );
            if ( parse.result != TCL.CompletionCode.OK )
            {
              throw new TclException( parse.result );
            }
            if ( parse.inString[parse.termIndex] != '"' )
            {
              parse.termIndex = script_index - 1;
              parse.incomplete = true;
              throw new TclException( parse.interp, "missing \"" );
            }
            script_index = parse.termIndex + 1;
          }
          else if ( cur == '{' )
          {
            // Find the matching right brace that terminates the word,
            // then generate a single token for everything between the
            // braces.

            script_index++;
            token = parse.getToken( parse.numTokens );
            token.type = TCL_TOKEN_TEXT;
            token.script_array = script_array;
            token.script_index = script_index;
            token.numComponents = 0;
            level = 1;
            while ( true )
            {
              cur = script_array[script_index];

              // get the current char in the array and lookup its type
              while ( ( ( cur > TYPE_MAX ) ? TYPE_NORMAL : typeTable[cur] ) == TYPE_NORMAL )
              {
                cur = script_array[++script_index];
              }
              if ( script_array[script_index] == '}' )
              {
                level--;
                if ( level == 0 )
                {
                  break;
                }
                script_index++;
              }
              else if ( script_array[script_index] == '{' )
              {
                level++;
                script_index++;
              }
              else if ( script_array[script_index] == '\\' )
              {
                bs = backslash( script_array, script_index );
                if ( script_array[script_index + 1] == '\n' )
                {
                  // A backslash-newline sequence requires special
                  // treatment: it must be collapsed, even inside
                  // braces, so we have to split the word into
                  // multiple tokens so that the backslash-newline
                  // can be represented explicitly.

                  if ( ( script_index + 2 ) == parse.endIndex )
                  {
                    parse.incomplete = true;
                  }
                  token.size = script_index - token.script_index;
                  if ( token.size != 0 )
                  {
                    parse.numTokens++;
                  }
                  token = parse.getToken( parse.numTokens );
                  token.type = TCL_TOKEN_BS;
                  token.script_array = script_array;
                  token.script_index = script_index;
                  token.size = bs.nextIndex - script_index;
                  token.numComponents = 0;
                  parse.numTokens++;
                  script_index = bs.nextIndex;
                  token = parse.getToken( parse.numTokens );
                  token.type = TCL_TOKEN_TEXT;
                  token.script_array = script_array;
                  token.script_index = script_index;
                  token.numComponents = 0;
                }
                else
                {
                  script_index = bs.nextIndex;
                }
              }
              else if ( script_index == parse.endIndex )
              {
                parse.termIndex = parse.getToken( wordIndex ).script_index;
                parse.incomplete = true;
                throw new TclException( interp, "missing close-brace" );
              }
              else
              {
                script_index++;
              }
            }
            if ( ( script_index != token.script_index ) || ( parse.numTokens == ( wordIndex + 1 ) ) )
            {
              token.size = script_index - token.script_index;
              parse.numTokens++;
            }
            script_index++;
          }
          else
          {
            // This is an unquoted word.  Call parseTokens and let it do
            // all of the work.

            parse = parseTokens( script_array, script_index, TYPE_SPACE | terminators, parse );
            if ( parse.result != TCL.CompletionCode.OK )
            {
              throw new TclException( parse.result );
            }
            script_index = parse.termIndex;
          }

          // Finish filling in the token for the word and check for the
          // special case of a word consisting of a single range of
          // literal text.

          token = parse.getToken( wordIndex );
          token.size = script_index - token.script_index;
          token.numComponents = parse.numTokens - ( wordIndex + 1 );
          if ( ( token.numComponents == 1 ) && ( parse.getToken( wordIndex + 1 ).type == TCL_TOKEN_TEXT ) )
          {
            token.type = TCL_TOKEN_SIMPLE_WORD;
          }

          // Do two additional checks: (a) make sure we're really at the
          // end of a word (there might have been garbage left after a
          // quoted or braced word), and (b) check for the end of the
          // command.


          cur = script_array[script_index];
          type = ( ( cur > TYPE_MAX ) ? TYPE_NORMAL : typeTable[cur] );

          if ( type == TYPE_SPACE )
          {
            script_index++;
            continue;
          }
          else
          {
            // Backslash-newline (and any following white space) must be
            // treated as if it were a space character.

            if ( ( cur == '\\' ) && ( script_array[script_index + 1] == '\n' ) )
            {
              if ( ( script_index + 2 ) == parse.endIndex )
              {
                parse.incomplete = true;
              }
              bs = backslash( script_array, script_index );
              script_index = bs.nextIndex;
              continue;
            }
          }

          if ( ( type & terminators ) != 0 )
          {
            script_index++;
            break;
          }
          if ( script_index == parse.endIndex )
          {
            if ( nested && savedChar != ']' )
            {
              parse.incomplete = true;
              throw new TclException( interp, "missing close-bracket" );
            }
            break;
          }
          parse.termIndex = script_index;
          if ( script_array[script_index - 1] == '"' )
          {
            throw new TclException( interp, "extra characters after close-quote" );
          }
          else
          {
            throw new TclException( interp, "extra characters after close-brace" );
          }
        }
      }
      catch ( TclException e )
      {
        script_array[endIndex] = savedChar;
        if ( parse.commandStart < 0 )
        {
          parse.commandStart = saved_script_index;
        }
        parse.commandSize = parse.termIndex - parse.commandStart;
        parse.result = TCL.CompletionCode.ERROR;
        return parse;
      }

      script_array[endIndex] = savedChar;
      parse.commandSize = script_index - parse.commandStart;
      parse.result = TCL.CompletionCode.OK;
      return parse;
    }











    internal static TclParse parseTokens( char[] script_array, int script_index, int mask, TclParse parse )
    // Information about parse in progress.
    // Updated with additional tokens and
    // termination information. 
    {
      char cur;
      int type, originalTokens, varToken;
      TclToken token;
      TclParse nested;
      BackSlashResult bs;



#if DEBUG
				System.Diagnostics.Debug.WriteLine();
				System.Diagnostics.Debug.WriteLine("Entered Parser.parseTokens()");
				System.Diagnostics.Debug.Write("now to parse the string \"");
				for (int k = script_index; k < script_array.Length; k++)
				{
					System.Diagnostics.Debug.Write(script_array[k]);
				}
				System.Diagnostics.Debug.WriteLine("\"");
#endif


      // Each iteration through the following loop adds one token of
      // type TCL_TOKEN_TEXT, TCL_TOKEN_BS, TCL_TOKEN_COMMAND, or
      // TCL_TOKEN_VARIABLE to parsePtr.  For TCL_TOKEN_VARIABLE additional,
      // tokens tokens are added for the parsed variable name.

      originalTokens = parse.numTokens;
      while ( true )
      {
        token = parse.getToken( parse.numTokens );
        token.script_array = script_array;
        token.script_index = script_index;
        token.numComponents = 0;

#if DEBUG
					System.Diagnostics.Debug.WriteLine();
					System.Diagnostics.Debug.WriteLine("Now on index " + script_index);
					char tmp_c = script_array[script_index];
					System.Diagnostics.Debug.WriteLine("Char is '" + tmp_c + "'");
					System.Diagnostics.Debug.WriteLine("Unicode id is " + ((int) tmp_c));
					int tmp_i = ((int) ((tmp_c > TYPE_MAX)?TYPE_NORMAL:typeTable[tmp_c]));
					System.Diagnostics.Debug.WriteLine("Type is " + tmp_i);
					System.Diagnostics.Debug.WriteLine("Mask is " + mask);
					System.Diagnostics.Debug.WriteLine("(type & mask) is " + ((int) (tmp_i & mask)));
					System.Diagnostics.Debug.WriteLine("orig token.size is " + token.size);
#endif


        cur = script_array[script_index];
        type = ( ( cur > TYPE_MAX ) ? TYPE_NORMAL : typeTable[cur] );

        if ( ( type & mask ) != 0 )
        {
          System.Diagnostics.Debug.WriteLine( "mask break" );
          break;
        }

        if ( ( type & TYPE_SUBS ) == 0 )
        {
          // This is a simple range of characters.  Scan to find the end
          // of the range.

          System.Diagnostics.Debug.WriteLine( "simple range" );

          while ( true )
          {
            cur = script_array[++script_index];
            type = ( ( cur > TYPE_MAX ) ? TYPE_NORMAL : typeTable[cur] );

            System.Diagnostics.Debug.WriteLine( "skipping '" + cur + "'" );

            if ( ( type & ( mask | TYPE_SUBS ) ) != 0 )
            {
              break;
            }
          }
          token.type = TCL_TOKEN_TEXT;
          token.size = script_index - token.script_index;
          parse.numTokens++;

          System.Diagnostics.Debug.WriteLine( "end simple range" );
          System.Diagnostics.Debug.WriteLine( "token.size is " + token.size );
          System.Diagnostics.Debug.WriteLine( "parse.numTokens is " + parse.numTokens );
        }
        else if ( cur == '$' )
        {
          // This is a variable reference.  Call parseVarName to do
          // all the dirty work of parsing the name.

          System.Diagnostics.Debug.WriteLine( "dollar sign" );

          varToken = parse.numTokens;
          parse = parseVarName( parse.interp, script_array, script_index, parse.endIndex - script_index, parse, true );
          if ( parse.result != TCL.CompletionCode.OK )
          {
            return parse;
          }
          script_index += parse.getToken( varToken ).size;
        }
        else if ( cur == '[' )
        {
          // Command substitution.  Call parseCommand recursively
          // (and repeatedly) to parse the nested command(s), then
          // throw away the parse information.

          System.Diagnostics.Debug.WriteLine( "command" );

          script_index++;
          while ( true )
          {
            nested = parseCommand( parse.interp, script_array, script_index, parse.endIndex - script_index, parse.fileName, parse.lineNum, true );
            if ( nested.result != TCL.CompletionCode.OK )
            {
              parse.termIndex = nested.termIndex;
              parse.incomplete = nested.incomplete;
              parse.result = nested.result;
              return parse;
            }
            script_index = nested.commandStart + nested.commandSize;
            if ( ( script_array[script_index - 1] == ']' ) && !nested.incomplete )
            {
              break;
            }
            if ( script_index == parse.endIndex )
            {
              if ( parse.interp != null )
              {
                parse.interp.setResult( "missing close-bracket" );
              }
              parse.termIndex = token.script_index;
              parse.incomplete = true;
              parse.result = TCL.CompletionCode.ERROR;
              return parse;
            }
          }
          token.type = TCL_TOKEN_COMMAND;
          token.size = script_index - token.script_index;
          parse.numTokens++;
        }
        else if ( cur == '\\' )
        {
          // Backslash substitution.

          System.Diagnostics.Debug.WriteLine( "backslash" );

          if ( script_array[script_index + 1] == '\n' )
          {
            if ( ( script_index + 2 ) == parse.endIndex )
            {
              parse.incomplete = true;
            }

            // Note: backslash-newline is special in that it is
            // treated the same as a space character would be.  This
            // means that it could terminate the token.

            if ( ( mask & TYPE_SPACE ) != 0 )
            {
              break;
            }
          }
          token.type = TCL_TOKEN_BS;
          bs = backslash( script_array, script_index );
          token.size = bs.nextIndex - script_index;
          parse.numTokens++;
          script_index += token.size;
        }
        else if ( cur == '\x0000' )
        {
          // We encountered a null character.  If it is the null
          // character at the end of the string, then return.
          // Otherwise generate a text token for the single
          // character.

          System.Diagnostics.Debug.WriteLine( "null char" );
          System.Diagnostics.Debug.WriteLine( "script_index is " + script_index );
          System.Diagnostics.Debug.WriteLine( "parse.endIndex is " + parse.endIndex );

          if ( script_index == parse.endIndex )
          {
            break;
          }

          token.type = TCL_TOKEN_TEXT;
          token.size = 1;
          parse.numTokens++;
          script_index++;
        }
        else
        {
          throw new TclRuntimeError( "parseTokens encountered unknown character" );
        }
      } // end while (true)


      if ( parse.numTokens == originalTokens )
      {
        // There was nothing in this range of text.  Add an empty token
        // for the empty range, so that there is always at least one
        // token added.

        System.Diagnostics.Debug.WriteLine( "empty token" );

        token.type = TCL_TOKEN_TEXT;
        token.size = 0;
        parse.numTokens++;
      }
      else
      {
        System.Diagnostics.Debug.WriteLine( "non empty token case" );
      }

      parse.termIndex = script_index;
      parse.result = TCL.CompletionCode.OK;

#if DEBUG			
				System.Diagnostics.Debug.WriteLine();
				System.Diagnostics.Debug.WriteLine("Leaving Parser.parseTokens()");
				
				System.Diagnostics.Debug.WriteLine("after parse, parse.numTokens is " + parse.numTokens);
				System.Diagnostics.Debug.WriteLine("after parse, token.size is " + token.size);
				System.Diagnostics.Debug.WriteLine("after parse, token.hashCode() is " + token.GetHashCode());
				
				
				//System.out.println( parse.toString() );
				
				
				System.Diagnostics.Debug.Write("printing " + (parse.numTokens - originalTokens) + " token(s)");
				
				for (int k = originalTokens; k < parse.numTokens; k++)
				{
					token = parse.getToken(k);
					System.Diagnostics.Debug.WriteLine(token);
				}
				
				System.Diagnostics.Debug.WriteLine("done printing tokens");
#endif

      return parse;
    }

    public static void evalObjv( Interp interp, TclObject[] objv, int length, int flags )
    {
      Command cmd;
      WrappedCommand wCmd = null;
      TclObject[] newObjv;
      int i;
      CallFrame savedVarFrame; //Saves old copy of interp.varFrame
      // in case TCL.EVAL_GLOBAL was set.

      interp.resetResult();
      if ( objv.Length == 0 )
      {
        return;
      }

      // If the interpreter was deleted, return an error.

      if ( interp.deleted )
      {
        interp.setResult( "attempt to call eval in deleted interpreter" );
        interp.setErrorCode( TclString.newInstance( "CORE IDELETE {attempt to call eval in deleted interpreter}" ) );
        throw new TclException( TCL.CompletionCode.ERROR );
      }

      // Check depth of nested calls to eval:  if this gets too large,
      // it's probably because of an infinite loop somewhere.

      if ( interp.nestLevel >= interp.maxNestingDepth )
      {
        throw new TclException( interp, "too many nested calls to eval (infinite loop?)" );
      }
      interp.nestLevel++;

      try
      {
        // Find the procedure to execute this command. If there isn't one,
        // then see if there is a command "unknown".  If so, create a new
        // word array with "unknown" as the first word and the original
        // command words as arguments.  Then call ourselves recursively
        // to execute it.


        cmd = interp.getCommand( objv[0].ToString() );
        if ( cmd == null ) wCmd = interp.getObjCommand( objv[0].ToString() );
        if ( cmd == null && wCmd == null )
        {
          newObjv = new TclObject[objv.Length + 1];
          for ( i = ( objv.Length - 1 ) ; i >= 0 ; i-- )
          {
            newObjv[i + 1] = objv[i];
          }
          newObjv[0] = TclString.newInstance( "unknown" );
          newObjv[0].preserve();
          cmd = interp.getCommand( "unknown" );
          if ( cmd == null )
          {

            Debug.Assert( false, "invalid command name \"" + objv[0].ToString() + "\"" );
            throw new TclException( interp, "invalid command name \"" + objv[0].ToString() + "\"" );
          }
          else
          {
            evalObjv( interp, newObjv, length, 0 );
          }
          newObjv[0].release();
          return;
        }

        // Finally, invoke the Command's cmdProc.

        interp.cmdCount++;
        savedVarFrame = interp.varFrame;
        if ( ( flags & TCL.EVAL_GLOBAL ) != 0 )
        {
          interp.varFrame = null;
        }

        int rc = 0;
        if ( cmd != null )
          cmd.cmdProc( interp, objv );
        else
        {
          rc = wCmd.objProc( wCmd.objClientData, interp, objv.Length, objv );
          if ( rc != 0 )
          {
            if (rc == TCL.TCL_RETURN) throw new TclException( TCL.CompletionCode.RETURN );
            throw new TclException( TCL.CompletionCode.ERROR );
          }
        }
        interp.varFrame = savedVarFrame;
      }
      finally
      {
        interp.nestLevel--;
      }
    }
    internal static void logCommandInfo( Interp interp, char[] script_array, int script_index, int cmdIndex, int length, TclException e )
    // The exception caused by the script 
    // evaluation. 
    {
      string ellipsis;
      string msg;
      int offset;
      int pIndex;

      if ( interp.errAlreadyLogged )
      {
        // Someone else has already logged error information for this
        // command; we shouldn't add anything more.

        return;
      }

      // Compute the line number where the error occurred.
      // Note: The script array must be accessed directly
      // because we want to count from the beginning of
      // the script, not the current index.

      interp.errorLine = 1;

      for ( pIndex = 0 ; pIndex < cmdIndex ; pIndex++ )
      {
        if ( script_array[pIndex] == '\n' )
        {
          interp.errorLine++;
        }
      }


      // Create an error message to add to errorInfo, including up to a
      // maximum number of characters of the command.

      if ( length < 0 )
      {
        //take into account the trailing '\0'
        int script_length = script_array.Length - 1;

        length = script_length - cmdIndex;
      }
      if ( length > 150 )
      {
        offset = 150;
        ellipsis = "...";
      }
      else
      {
        offset = length;
        ellipsis = "";
      }

      msg = new string( script_array, cmdIndex, offset );
      if ( !( interp.errInProgress ) )
      {
        interp.addErrorInfo( "\n    while executing\n\"" + msg + ellipsis + "\"" );
      }
      else
      {
        interp.addErrorInfo( "\n    invoked from within\n\"" + msg + ellipsis + "\"" );
      }
      interp.errAlreadyLogged = false;
      e.errIndex = cmdIndex + offset;
    }
    internal static TclObject evalTokens( Interp interp, TclToken[] tokenList, int tIndex, int count )
    {
      TclObject result, index, value;
      TclToken token;
      string p = null;
      string varName;
      BackSlashResult bs;

      // The only tricky thing about this procedure is that it attempts to
      // avoid object creation and string copying whenever possible.  For
      // example, if the value is just a nested command, then use the
      // command's result object directly.

      result = null;
      for ( ; count > 0 ; count-- )
      {
        token = tokenList[tIndex];

        // The switch statement below computes the next value to be
        // concat to the result, as either a range of text or an
        // object.

        value = null;
        switch ( token.type )
        {

          case TCL_TOKEN_TEXT:
            p = token.TokenString;
            break;


          case TCL_TOKEN_BS:
            bs = backslash( token.script_array, token.script_index );
            if ( bs.isWordSep )
            {
              p = "\\" + bs.c;
            }
            else
            {
              System.Char ch = bs.c;
              p = ch.ToString();
            }
            break;


          case TCL_TOKEN_COMMAND:
            interp.evalFlags |= Parser.TCL_BRACKET_TERM;
            token.script_index++;

            //should the nest level be changed???
            //interp.nestLevel++;

            eval2( interp, token.script_array, token.script_index, token.size - 2, 0 );

            token.script_index--;
            //interp.nestLevel--;
            value = interp.getResult();
            break;


          case TCL_TOKEN_VARIABLE:
            if ( token.numComponents == 1 )
            {
              index = null;
            }
            else
            {
              index = evalTokens( interp, tokenList, tIndex + 2, token.numComponents - 1 );
              if ( index == null )
              {
                return null;
              }
            }
            varName = tokenList[tIndex + 1].TokenString;

            // In order to get the existing expr parser to work with the
            // new Parser, we test the interp.noEval flag which is set
            // by the expr parser.  If it is != 0, then we do not evaluate 
            // the variable.  This should be removed when the new expr
            // parser is implemented.

            if ( interp.noEval == 0 )
            {
              if ( index != null )
              {
                try
                {

                  value = interp.getVar( varName, index.ToString(), 0 );
                }
                finally
                {
                  index.release();
                }
              }
              else
              {
                value = interp.getVar( varName, null, 0 );
              }
            }
            else
            {
              value = TclString.newInstance( "" );
              value.preserve();
            }
            count -= token.numComponents;
            tIndex += token.numComponents;
            break;


          default:
            throw new TclRuntimeError( "unexpected token type in evalTokens" );

        }

        // If value isn't null, the next piece of text comes from that
        // object; otherwise, take value of p.

        if ( result == null )
        {
          if ( value != null )
          {
            result = value;
          }
          else
          {
            result = TclString.newInstance( p );
          }
          result.preserve();
        }
        else
        {
          if ( result.Shared )
          {
            result.release();
            result = result.duplicate();
            result.preserve();
          }
          if ( value != null )
          {

            p = value.ToString();
          }
          TclString.append( result, p );
        }
        tIndex++;
      }
      return result;
    }
    public static void eval2( Interp interp, char[] script_array, int script_index, int numBytes, int flags )
    {
      int i;
      int objUsed = 0;
      int nextIndex, tokenIndex;
      int commandLength, bytesLeft;
      bool nested;
      TclObject[] objv;
      TclObject obj;
      TclParse parse = null;
      TclToken token;

      // Saves old copy of interp.varFrame in case TCL.EVAL_GLOBAL was set
      CallFrame savedVarFrame;

      // Take into account the trailing '\0'
      int script_length = script_array.Length - 1;


      // These are modified instead of script_array and script_index
      char[] src_array = script_array;
      int src_index = script_index;

#if DEBUG
				System.Diagnostics.Debug.WriteLine();
				System.Diagnostics.Debug.WriteLine("Entered eval2()");
				System.Diagnostics.Debug.Write("now to eval2 the string \"");
				for (int k = script_index; k < script_array.Length; k++)
				{
					System.Diagnostics.Debug.Write(script_array[k]);
				}
				System.Diagnostics.Debug.WriteLine("\"");
#endif



      if ( numBytes < 0 )
      {
        numBytes = script_length - script_index;
      }
      interp.resetResult();
      savedVarFrame = interp.varFrame;
      if ( ( flags & TCL.EVAL_GLOBAL ) != 0 )
      {
        interp.varFrame = null;
      }

      // Each iteration through the following loop parses the next
      // command from the script and then executes it.

      bytesLeft = numBytes;

      // Init objv with the most commonly used array size
      objv = grabObjv( interp, 3 );

      if ( ( interp.evalFlags & TCL_BRACKET_TERM ) != 0 )
      {
        nested = true;
      }
      else
      {
        nested = false;
      }
      interp.evalFlags &= ~TCL_BRACKET_TERM;

      try
      {

        do
        {
          parse = parseCommand( interp, src_array, src_index, bytesLeft, null, 0, nested );

          if ( parse.result != TCL.CompletionCode.OK )
          {
            throw new TclException( parse.result );
          }

          // The test on noEval is temporary.  As soon as the new expr
          // parser is implemented it should be removed.

          if ( parse.numWords > 0 && interp.noEval == 0 )
          {
            // Generate an array of objects for the words of the command.

            try
            {
              tokenIndex = 0;
              token = parse.getToken( tokenIndex );

              // Test to see if new space needs to be allocated.  If objv
              // is the EXACT size of parse.numWords, then no allocation
              // needs to be performed.

              if ( objv.Length != parse.numWords )
              {
                //System.out.println("need new size " + objv.length);
                releaseObjv( interp, objv ); //let go of resource
                objv = grabObjv( interp, parse.numWords ); //get new resource
              }
              else
              {
                //System.out.println("reusing size " + objv.length);
              }

              for ( objUsed = 0 ; objUsed < parse.numWords ; objUsed++ )
              {
                obj = evalTokens( interp, parse.tokenList, tokenIndex + 1, token.numComponents );
                if ( obj == null )
                {
                  throw new TclException( TCL.CompletionCode.ERROR );
                }
                else
                {
                  objv[objUsed] = obj;
                }
                tokenIndex += ( token.numComponents + 1 );
                token = parse.getToken( tokenIndex );
              }

              // Execute the command and free the objects for its words.
              try
              {
                evalObjv( interp, objv, bytesLeft, 0 );
              }
              catch ( System.StackOverflowException e )
              {
                interp.setResult( "too many nested calls" + " to eval (infinite loop?)" );
                throw new TclException( TCL.CompletionCode.ERROR );
              }
            }
            catch ( TclException e )
            {
              // Generate various pieces of error information, such 
              // as the line number where the error occurred and 
              // information to add to the errorInfo variable.  Then 
              // free resources that had been allocated
              // to the command.

              if ( e.getCompletionCode() == TCL.CompletionCode.ERROR && !( interp.errAlreadyLogged ) )
              {
                commandLength = parse.commandSize;

                char term = script_array[parse.commandStart + commandLength - 1];
                int type = charType( term );
                int terminators;
                if ( nested )
                {
                  terminators = TYPE_COMMAND_END | TYPE_CLOSE_BRACK;
                }
                else
                {
                  terminators = TYPE_COMMAND_END;
                }
                if ( ( type & terminators ) != 0 )
                {
                  // The command where the error occurred didn't end 
                  // at the end of the script (i.e. it ended at a 
                  // terminator character such as ";".  Reduce the 
                  // length by one so that the error message
                  // doesn't include the terminator character.

                  commandLength -= 1;
                }
                interp.varFrame = savedVarFrame;
                logCommandInfo( interp, script_array, script_index, parse.commandStart, commandLength, e );
                throw e;
              }
              else throw;
            }
            finally
            {
              for ( i = 0 ; i < objUsed ; i++ )
              {
                objv[i].release();
              }
              objUsed = 0;

              parse.release(); // Cleanup parser resources
            }
          }


          // Advance to the next command in the script.

          nextIndex = parse.commandStart + parse.commandSize;
          bytesLeft -= ( nextIndex - src_index );
          src_index = nextIndex;
          if ( nested && ( src_index > 1 ) && ( src_array[src_index - 1] == ']' ) )
          {

            // We get here in the special case where the TCL_BRACKET_TERM
            // flag was set in the interpreter and we reached a close
            // bracket in the script.  Return immediately.

            interp.termOffset = ( src_index - 1 ) - script_index;
            interp.varFrame = savedVarFrame;
            return;
          }
        }
        while ( bytesLeft > 0 );
      }
      finally
      {
        if ( parse != null )
        {
          parse.release(); // Let go of parser resources
        }
        releaseObjv( interp, objv ); // Let go of objv buffer
      }

      interp.termOffset = src_index - script_index;
      interp.varFrame = savedVarFrame;
      return;
    }
    public static TclParse parseVarName( Interp interp, char[] script_array, int script_index, int numBytes, TclParse parse, bool append )
    // Non-zero means append tokens to existing
    // information in parsePtr; zero means ignore
    // existing tokens in parsePtr and reinitialize
    // it. 
    {
      char cur;
      TclToken token, startToken;
      int endIndex, varIndex;

#if DEBUG
				System.Diagnostics.Debug.WriteLine();
				System.Diagnostics.Debug.WriteLine("Entered parseVarName()");
				System.Diagnostics.Debug.Write("now to parse var off the string \"");
				for (int k = script_index; k < script_array.Length; k++)
				{
					System.Diagnostics.Debug.Write(script_array[k]);
				}
				System.Diagnostics.Debug.WriteLine("\"");
#endif


      if ( numBytes >= 0 )
      {
        endIndex = script_index + numBytes;
      }
      else
      {
        endIndex = script_array.Length - 1;
      }
      if ( !append )
      {
        parse = new TclParse( interp, script_array, endIndex, null, -1 );
      }

      // Generate one token for the variable, an additional token for the
      // name, plus any number of additional tokens for the index, if
      // there is one.

      token = parse.getToken( parse.numTokens );
      token.type = TCL_TOKEN_VARIABLE;
      token.script_array = script_array;
      token.script_index = script_index;
      varIndex = parse.numTokens;
      parse.numTokens++;
      script_index++;
      if ( script_index >= endIndex )
      {
        // The dollar sign isn't followed by a variable name.
        // replace the TCL_TOKEN_VARIABLE token with a
        // TCL_TOKEN_TEXT token for the dollar sign.

        token.type = TCL_TOKEN_TEXT;
        token.size = 1;
        token.numComponents = 0;
        parse.result = TCL.CompletionCode.OK;
        return parse;
      }
      startToken = token;
      token = parse.getToken( parse.numTokens );

      // The name of the variable can have three forms:
      // 1. The $ sign is followed by an open curly brace.  Then 
      //    the variable name is everything up to the next close
      //    curly brace, and the variable is a scalar variable.
      // 2. The $ sign is not followed by an open curly brace.  Then
      //    the variable name is everything up to the next
      //    character that isn't a letter, digit, or underscore.
      //    :: sequences are also considered part of the variable
      //    name, in order to support namespaces. If the following
      //    character is an open parenthesis, then the information
      //    between parentheses is the array element name.
      // 3. The $ sign is followed by something that isn't a letter,
      //    digit, or underscore:  in this case, there is no variable
      //    name and the token is just "$".

      if ( script_array[script_index] == '{' )
      {
        System.Diagnostics.Debug.WriteLine( "parsing curley var name" );

        script_index++;
        token.type = TCL_TOKEN_TEXT;
        token.script_array = script_array;
        token.script_index = script_index;
        token.numComponents = 0;

        while ( true )
        {
          if ( script_index == endIndex )
          {
            if ( interp != null )
            {
              interp.setResult( "missing close-brace for variable name" );
            }
            parse.termIndex = token.script_index - 1;
            parse.incomplete = true;
            parse.result = TCL.CompletionCode.ERROR;
            return parse;
          }
          if ( script_array[script_index] == '}' )
          {
            break;
          }
          script_index++;
        }
        token.size = script_index - token.script_index;
        startToken.size = script_index - startToken.script_index;
        parse.numTokens++;
        script_index++;
      }
      else
      {
        System.Diagnostics.Debug.WriteLine( "parsing non curley var name" );

        token.type = TCL_TOKEN_TEXT;
        token.script_array = script_array;
        token.script_index = script_index;
        token.numComponents = 0;
        while ( script_index != endIndex )
        {
          cur = script_array[script_index];
          if ( ( System.Char.IsLetterOrDigit( cur ) ) || ( cur == '_' ) )
          {
            script_index++;
            continue;
          }
          if ( ( cur == ':' ) && ( ( ( script_index + 1 ) != endIndex ) && ( script_array[script_index + 1] == ':' ) ) )
          {
            script_index += 2;
            while ( ( script_index != endIndex ) && ( script_array[script_index] == ':' ) )
            {
              script_index++;
            }
            continue;
          }
          break;
        }
        token.size = script_index - token.script_index;
        if ( token.size == 0 )
        {
          // The dollar sign isn't followed by a variable name.
          // replace the TCL_TOKEN_VARIABLE token with a
          // TCL_TOKEN_TEXT token for the dollar sign.

          System.Diagnostics.Debug.WriteLine( "single $ with no var name found" );

          startToken.type = TCL_TOKEN_TEXT;
          startToken.size = 1;
          startToken.numComponents = 0;
          parse.result = TCL.CompletionCode.OK;
          return parse;
        }
        parse.numTokens++;
        if ( ( script_index != endIndex ) && ( script_array[script_index] == '(' ) )
        {
          // This is a reference to an array element.  Call
          // parseTokens recursively to parse the element name,
          // since it could contain any number of substitutions.

          System.Diagnostics.Debug.WriteLine( "parsing array element" );

          script_index++;
          parse = parseTokens( script_array, script_index, TYPE_CLOSE_PAREN, parse );
          if ( parse.result != TCL.CompletionCode.OK )
          {
            return parse;
          }
          if ( ( parse.termIndex == endIndex ) || ( parse.inString[parse.termIndex] != ')' ) )
          {
            if ( interp != null )
            {
              interp.setResult( "missing )" );
            }
            parse.termIndex = script_index - 1;
            parse.incomplete = true;
            parse.result = TCL.CompletionCode.ERROR;
            return parse;
          }
          script_index = parse.termIndex + 1;
        }
      }


#if DEBUG
				System.Diagnostics.Debug.WriteLine("default end parse case");
				System.Diagnostics.Debug.Write("var token is \"");
				for (int k = startToken.script_index; k < script_index; k++)
				{
					System.Diagnostics.Debug.Write(script_array[k]);
				}
				System.Diagnostics.Debug.WriteLine("\"");
#endif

      startToken.size = script_index - startToken.script_index;
      startToken.numComponents = parse.numTokens - ( varIndex + 1 );
      parse.result = TCL.CompletionCode.OK;
      return parse;
    }


    public static ParseResult parseVar( Interp interp, string inString )
    {
      TclParse parse;
      TclObject obj;

      System.Diagnostics.Debug.WriteLine( "Entered parseVar()" );
      System.Diagnostics.Debug.Write( "now to parse var off the string \"" + inString + "\"" );


      CharPointer src = new CharPointer( inString );
      parse = parseVarName( interp, src.array, src.index, -1, null, false );
      if ( parse.result != TCL.CompletionCode.OK )
      {

        throw new TclException( interp, interp.getResult().ToString() );
      }

      try
      {
        System.Diagnostics.Debug.Write( "parsed " + parse.numTokens + " tokens" );

        if ( parse.numTokens == 1 )
        {
          // There isn't a variable name after all: the $ is just a $.
          return new ParseResult( "$", 1 );
        }

        obj = evalTokens( interp, parse.tokenList, 0, parse.numTokens );
        if ( !obj.Shared )
        {
          throw new TclRuntimeError( "parseVar got temporary object from evalTokens" );
        }
        return new ParseResult( obj, parse.tokenList[0].size );
      }
      finally
      {
        parse.release(); // Release parser resources
      }
    }
    internal static bool commandComplete( string inString, int length )
    // Number of bytes in script.
    {
      TclParse parse;

      CharPointer src = new CharPointer( inString );

      do
      {
        parse = parseCommand( null, src.array, src.index, length, null, 0, false );

        src.index = parse.commandStart + parse.commandSize;

        parse.release(); // Release parser resources

        if ( src.index >= length )
        {
          break;
        }
      }
      while ( parse.result == TCL.CompletionCode.OK );

      if ( parse.incomplete )
      {
        return false;
      }
      return true;
    }
    internal static bool objCommandComplete( TclObject obj )
    // Points to object holding script
    // to check.
    {

      string inString = obj.ToString();
      return commandComplete( inString, inString.Length );
    }
    internal static BackSlashResult backslash( char[] script_array, int script_index )
    {
      int result;

      script_index++;
      int endIndex = script_array.Length - 1;

      if ( script_index == endIndex )
      {
        return new BackSlashResult( '\\', script_index );
      }

      char c = script_array[script_index];
      switch ( c )
      {

        case 'a':
          return new BackSlashResult( (char)0x7, script_index + 1 );

        case 'b':
          return new BackSlashResult( (char)0x8, script_index + 1 );

        case 'f':
          return new BackSlashResult( (char)0xc, script_index + 1 );

        case 'n':
          return new BackSlashResult( '\n', script_index + 1 );

        case 'r':
          return new BackSlashResult( '\r', script_index + 1 );

        case 't':
          return new BackSlashResult( '\t', script_index + 1 );

        case 'v':
          return new BackSlashResult( (char)0xb, script_index + 1 );

        case 'x':
          script_index++;
          if ( script_index < endIndex )
          {
            c = script_array[script_index];

            if ( ( ( c >= '0' ) && ( c <= '9' ) ) || ( ( c >= 'A' ) && ( c <= 'F' ) ) || ( ( c >= 'a' ) && ( c <= 'f' ) ) )
            {

              string str = new string( script_array, script_index, endIndex - script_index );
              StrtoulResult res = Util.strtoul( str, 0, 16 );
              if ( res.errno == 0 )
              {
                // We force res.value to be a 8-bit (ASCII) character
                // so that it is compatible with Tcl.

                char b = (char)( res.value & 0xff );
                return new BackSlashResult( b, script_index + res.index );
              }
            }
          }
          return new BackSlashResult( 'x', script_index );

        case 'u':  // TODO -- determine how to handle Unicode
          int count, n;
          result = 0;
          for ( count = 0 ; count < 4 ; count++ )
          {
            script_index++;
            c = script_array[script_index];
            if ( ( ( c >= '0' ) && ( c <= '9' ) ) || ( ( c >= 'a' ) && ( c <= 'f' ) ) || ( ( c >= 'A' ) && ( c <= 'F' ) ) )
            {
              n = c - '0';
              if ( n > 9 )
              {
                n = n + '0' + 10 - 'A';
              }
              if ( n > 16 )
              {
                n = n + 'A' - 'a';
              }
              result = ( result << 4 ) + n;
            }
            else
            {
              break;
            }
          }
          if ( count == 0 )
          {
            result = 'u';
          }
          return new BackSlashResult( (char)result, script_index + 1 );

        case '\r':
        case '\n':
          if ( c == '\r' )
          {
            if ( ( script_index + 1 ) < endIndex )
            {
              if ( script_array[script_index + 1] == '\n' )
              {
                script_index++;
              }
            }
          }
          do
          {
            script_index++;
            c = script_array[script_index];
          }
          while ( ( script_index < endIndex ) && ( ( c == ' ' ) || ( c == '\t' ) ) );
          return new BackSlashResult( (char)' ', script_index );

        case (char)( 0 ):
          return new BackSlashResult( (char)'\\', script_index + 1 );

        default:
          if ( ( c >= '0' ) && ( c <= '9' ) )
          {
            // Convert it to an octal number. This implementation is
            // compatible with tcl 7.6 - characters 8 and 9 are allowed.

            result = c - '0';
            script_index++;

            {
              if ( script_index == endIndex )
              {
                goto getoctal_brk;
              }
              c = script_array[script_index];
              if ( !( ( c >= '0' ) && ( c <= '9' ) ) )
              {
                goto getoctal_brk;
              }
              result = ( result * 8 ) + ( c - '0' );
              script_index++;

              if ( script_index == endIndex )
              {
                goto getoctal_brk;
              }
              c = script_array[script_index];
              if ( !( ( c >= '0' ) && ( c <= '9' ) ) )
              {
                goto getoctal_brk;
              }
              result = ( result * 8 ) + ( c - '0' );
              script_index++;
            }

getoctal_brk: ;


            // We force result to be a 8-bit (ASCII) character so
            // that it compatible with Tcl 7.6.

            return new BackSlashResult( (char)( result & 0xff ), script_index );
          }
          else
          {
            return new BackSlashResult( c, script_index + 1 );
          }
      }
    }






    internal static char charType( char c )
    {
      return ( ( c > TYPE_MAX ) ? TYPE_NORMAL : typeTable[c] );
    }

    // The following table provides parsing information about each possible
    // character.  
    //
    // The method charType is used to index into the table and return
    // information about its character argument.  The following return
    // values are defined.
    //
    // TYPE_NORMAL -	All characters that don't have special significance
    //			to the Tcl parser.
    // TYPE_SPACE -		The character is a whitespace character other
    //			than newline.
    // TYPE_COMMAND_END -	Character is newline or semicolon.
    // TYPE_SUBS -		Character begins a substitution or has other
    //			special meaning in parseTokens: backslash, dollar
    //			sign, open bracket, or null.
    // TYPE_QUOTE -		Character is a double quote.
    // TYPE_CLOSE_PAREN -	Character is a right parenthesis.
    // TYPE_CLOSE_BRACK -	Character is a right square bracket.
    // TYPE_BRACE -		Character is a curly brace (either left or right).

    internal const char TYPE_NORMAL = (char)( 0 );
    internal const char TYPE_SPACE = (char)( 0x1 );
    internal const char TYPE_COMMAND_END = (char)( 0x2 );
    internal const char TYPE_SUBS = (char)( 0x4 );
    internal const char TYPE_QUOTE = (char)( 0x8 );
    internal const char TYPE_CLOSE_PAREN = (char)( 0x10 );
    internal const char TYPE_CLOSE_BRACK = (char)( 0x20 );
    internal const char TYPE_BRACE = (char)( 0x40 );

    // This is the largest value in the type table. If a
    // char value is larger then the char type is TYPE_NORMAL.
    // Lookup -> ((c > TYPE_MAX) ? TYPE_NORMAL : typeTable[c])

    internal const char TYPE_MAX = (char)( 127 );


    internal static char[] typeTable;


    // Type values defined for TclToken structures.  These values are
    // defined as mask bits so that it's easy to check for collections of
    // types.
    //
    // TCL_TOKEN_WORD -		The token describes one word of a command,
    //				from the first non-blank character of
    //				the word (which may be " or {) up to but
    //				not including the space, semicolon, or
    //				bracket that terminates the word. 
    //				NumComponents counts the total number of
    //				sub-tokens that make up the word.  This
    //				includes, for example, sub-tokens of
    //				TCL_TOKEN_VARIABLE tokens.
    // TCL_TOKEN_SIMPLE_WORD -	This token is just like TCL_TOKEN_WORD
    //				except that the word is guaranteed to
    //				consist of a single TCL_TOKEN_TEXT
    //				sub-token.
    // TCL_TOKEN_TEXT -		The token describes a range of literal
    //				text that is part of a word. 
    //				NumComponents is always 0.
    // TCL_TOKEN_BS -		The token describes a backslash sequence
    //				that must be collapsed.  NumComponents
    //				is always 0.
    // TCL_TOKEN_COMMAND -		The token describes a command whose result
    //				must be substituted into the word.  The
    //				token includes the enclosing brackets. 
    //				NumComponents is always 0.
    // TCL_TOKEN_VARIABLE -		The token describes a variable
    //				substitution, including the dollar sign,
    //				variable name, and array index (if there
    //				is one) up through the right
    //				parentheses.  NumComponents tells how
    //				many additional tokens follow to
    //				represent the variable name.  The first
    //				token will be a TCL_TOKEN_TEXT token
    //				that describes the variable name.  If
    //				the variable is an array reference then
    //				there will be one or more additional
    //				tokens, of type TCL_TOKEN_TEXT,
    //				TCL_TOKEN_BS, TCL_TOKEN_COMMAND, and
    //				TCL_TOKEN_VARIABLE, that describe the
    //				array index; numComponents counts the
    //				total number of nested tokens that make
    //				up the variable reference, including
    //				sub-tokens of TCL_TOKEN_VARIABLE tokens.

    internal const int TCL_TOKEN_WORD = 1;
    internal const int TCL_TOKEN_SIMPLE_WORD = 2;
    internal const int TCL_TOKEN_TEXT = 4;
    internal const int TCL_TOKEN_BS = 8;
    internal const int TCL_TOKEN_COMMAND = 16;
    internal const int TCL_TOKEN_VARIABLE = 32;


    // Note: Most of the variables below will not be used until the
    // Compilier is implemented, but are left for consistency.

    // A structure of the following type is filled in by parseCommand.
    // It describes a single command parsed from an input string.

    // evalFlag bits for Interp structures:
    //
    // TCL_BRACKET_TERM	1 means that the current script is terminated by
    //			a close bracket rather than the end of the string.
    // TCL_ALLOW_EXCEPTIONS	1 means it's OK for the script to terminate with
    //			a code other than TCL_OK or TCL_ERROR;  0 means
    //			codes other than these should be turned into errors.

    public const int TCL_BRACKET_TERM = 1;
    public const int TCL_ALLOW_EXCEPTIONS = 4;

    // Flag bits for Interp structures:
    //
    // DELETED:		Non-zero means the interpreter has been deleted:
    //			don't process any more commands for it, and destroy
    //			the structure as soon as all nested invocations of
    //			Tcl_Eval are done.
    // ERR_IN_PROGRESS:	Non-zero means an error unwind is already in
    //			progress. Zero means a command proc has been
    //			invoked since last error occurred.
    // ERR_ALREADY_LOGGED:	Non-zero means information has already been logged
    //			in $errorInfo for the current Tcl_Eval instance,
    //			so Tcl_Eval needn't log it (used to implement the
    //			"error message log" command).
    // ERROR_CODE_SET:	Non-zero means that Tcl_SetErrorCode has been
    //			called to record information for the current
    //			error.  Zero means Tcl_Eval must clear the
    //			errorCode variable if an error is returned.
    // EXPR_INITIALIZED:	Non-zero means initialization specific to
    //			expressions has	been carried out.
    // DONT_COMPILE_CMDS_INLINE: Non-zero means that the bytecode compiler
    //			should not compile any commands into an inline
    //			sequence of instructions. This is set 1, for
    //			example, when command traces are requested.
    // RAND_SEED_INITIALIZED: Non-zero means that the randSeed value of the
    //			interp has not be initialized.  This is set 1
    //			when we first use the rand() or srand() functions.
    // SAFE_INTERP:         Non zero means that the current interp is a
    //                      safe interp (ie it has only the safe commands
    //                      installed, less priviledge than a regular interp).
    // USE_EVAL_DIRECT:	Non-zero means don't use the compiler or byte-code
    //			interpreter; instead, have Tcl_EvalObj call
    //			Tcl_EvalDirect.  Used primarily for testing the
    //			new parser.

    public const int DELETED = 1;
    public const int ERR_IN_PROGRESS = 2;
    public const int ERR_ALREADY_LOGGED = 4;
    public const int ERROR_CODE_SET = 8;
    public const int EXPR_INITIALIZED = 0x10;
    public const int DONT_COMPILE_CMDS_INLINE = 0x20;
    public const int RAND_SEED_INITIALIZED = 0x40;
    public const int SAFE_INTERP = 0x80;
    public const int USE_EVAL_DIRECT = 0x100;






    // These are private read only values that are used by the parser
    // class to implement a TclObject[] cache

    // Max size of array to cache (1..N)
    private const int OBJV_CACHE_MAX = 10;

    // The number of array to cache for each size
    // for example if the number of 3 elements is set to 5
    // an array of 5 TclObject[] objects
    // which will each be 3 elements long

    private static readonly int[] OBJV_CACHE_SIZES = new int[] { 0, 4, 4, 10, 4, 4, 4, 4, 4, 4 };

    // use test results
    // 1 373
    // 2 2424
    // 3 11889
    // 4 840
    // 5 1374
    // 6 926
    // 7 0
    // 8 74
    // 9 0


    internal static void init( Interp interp )
    {
      //System.out.println("called Parser.init()");

      TclObject[][][] OBJV = new TclObject[OBJV_CACHE_MAX][][];
      int[] USED = new int[OBJV_CACHE_MAX];

      int i, j, size;

      for ( i = 0 ; i < OBJV_CACHE_MAX ; i++ )
      {
        size = OBJV_CACHE_SIZES[i];
        //System.out.println("size " + i + " has " + size + " cache blocks");
        OBJV[i] = new TclObject[size][];
        USED[i] = 0;
        for ( j = 0 ; j < size ; j++ )
        {
          OBJV[i][j] = new TclObject[i];
        }
      }

      interp.parserObjv = OBJV;
      interp.parserObjvUsed = USED;
    }


    private static TclObject[] grabObjv( Interp interp, int size )
    {

      if ( size >= OBJV_CACHE_MAX )
      {
        //System.out.println("allocate for big objv of size " + size);
        return new TclObject[size];
      }

      //get array of used markers for this size
      int OPEN = interp.parserObjvUsed[size];

      if ( OPEN < OBJV_CACHE_SIZES[size] )
      {
        // Found an open cache slot
        //System.out.println("cache hit for objv of size " + size);
        interp.parserObjvUsed[size] += 1;
        return interp.parserObjv[size][OPEN];
      }
      else
      {
        // Did not find a free cache array of this size
        //System.out.println("cache miss for objv of size " + size);
        return new TclObject[size];
      }
    }


    private static void releaseObjv( Interp interp, TclObject[] objv )
    {
      int size = objv.Length;

      if ( size >= OBJV_CACHE_MAX )
      {
        //System.out.println("release for big objv of size " + size);
        return;
      }

      int OPEN = interp.parserObjvUsed[size];

      if ( OPEN > 0 )
      {
        OPEN--;
        interp.parserObjvUsed[size] = OPEN;
        interp.parserObjv[size][OPEN] = objv;
        //System.out.println("released objv of size " + size);
      }
      /*
      else {
      System.out.println("no release for objv of size " + size);
      }
      */

      return;
    }
    static Parser()
    {
      typeTable = new char[] { TYPE_SUBS, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_SPACE, TYPE_COMMAND_END, TYPE_SPACE, TYPE_SPACE, TYPE_SPACE, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_SPACE, TYPE_NORMAL, TYPE_QUOTE, TYPE_NORMAL, TYPE_SUBS, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_CLOSE_PAREN, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_COMMAND_END, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_SUBS, TYPE_SUBS, TYPE_CLOSE_BRACK, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_NORMAL, TYPE_BRACE, TYPE_NORMAL, TYPE_BRACE, TYPE_NORMAL, TYPE_NORMAL };
    }
  } // end class Parser
}
