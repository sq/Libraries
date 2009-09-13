/*
* Expression.java
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Expression.java,v 1.10 2003/02/04 00:35:41 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> This class handles Tcl expressions.</summary>
	class Expression
	{
		
		// The token types are defined below.  In addition, there is a
		// table associating a precedence with each operator.  The order
		// of types is important.  Consult the code before changing it.
		
		internal const int VALUE = 0;
		internal const int OPEN_PAREN = 1;
		internal const int CLOSE_PAREN = 2;
		internal const int COMMA = 3;
		internal const int END = 4;
		internal const int UNKNOWN = 5;
		
		// Binary operators:
		
		internal const int MULT = 8;
		internal const int DIVIDE = 9;
		internal const int MOD = 10;
		internal const int PLUS = 11;
		internal const int MINUS = 12;
		internal const int LEFT_SHIFT = 13;
		internal const int RIGHT_SHIFT = 14;
		internal const int LESS = 15;
		internal const int GREATER = 16;
		internal const int LEQ = 17;
		internal const int GEQ = 18;
		internal const int EQUAL = 19;
		internal const int NEQ = 20;
		internal const int BIT_AND = 21;
		internal const int BIT_XOR = 22;
		internal const int BIT_OR = 23;
		internal const int AND = 24;
		internal const int OR = 25;
		internal const int QUESTY = 26;
		internal const int COLON = 27;
		
		// Unary operators:
		
		internal const int UNARY_MINUS = 28;
		internal const int UNARY_PLUS = 29;
		internal const int NOT = 30;
		internal const int BIT_NOT = 31;
    internal const int EQ = 32;
    internal const int NE = 33;
		
		// Precedence table.  The values for non-operator token types are ignored.
		
		internal static int[] precTable = new int[]{0, 0, 0, 0, 0, 0, 0, 0, 12, 12, 12, 11, 11, 10, 10, 9, 9, 9, 9, 8, 8, 7, 6, 5, 4, 3, 2, 1, 13, 13, 13, 13};
		
		// Mapping from operator numbers to strings;  used for error messages.
		
		internal static string[] operatorStrings = new string[]{"VALUE", "(", ")", ",", "END", "UNKNOWN", "6", "7", "*", "/", "%", "+", "-", "<<", ">>", "<", ">", "<=", ">=", "==", "!=", "&", "^", "|", "&&", "||", "?", ":", "-", "+", "!", "~","eq","ne"};
		
		internal Hashtable mathFuncTable;
		
		/// <summary> The entire expression, as originally passed to eval et al.</summary>
		private string m_expr;
		
		/// <summary> Length of the expression.</summary>
		private int m_len;
		
		/// <summary> Type of the last token to be parsed from the expression.
		/// Corresponds to the characters just before expr.
		/// </summary>
		internal int m_token;
		
		/// <summary> Position to the next character to be scanned from the expression
		/// string.
		/// </summary>
		private int m_ind;
		
		/// <summary> Evaluate a Tcl expression.
		/// 
		/// </summary>
		/// <param name="interp">the context in which to evaluate the expression.
		/// </param>
		/// <param name="string">expression to evaluate.
		/// </param>
		/// <returns> the value of the expression.
		/// </returns>
		/// <exception cref=""> TclException for malformed expressions.
		/// </exception>
		
		internal  TclObject eval(Interp interp, string inString)
		{
			ExprValue value = ExprTopLevel(interp, inString);
			switch (value.type)
			{
				
				case ExprValue.INT: 
					return TclInteger.newInstance((int) value.intValue);
				
				case ExprValue.DOUBLE: 
					return TclDouble.newInstance(value.doubleValue);
				
				case ExprValue.STRING: 
					return TclString.newInstance(value.stringValue);
				
				default: 
					throw new TclRuntimeError("internal error: expression, unknown");
				
			}
		}
		
		/// <summary> Evaluate an Tcl expression.</summary>
		/// <param name="interp">the context in which to evaluate the expression.
		/// </param>
		/// <param name="string">expression to evaluate.
		/// </param>
		/// <exception cref=""> TclException for malformed expressions.
		/// </exception>
		/// <returns> the value of the expression in boolean.
		/// </returns>
		internal  bool evalBoolean(Interp interp, string inString)
		{
			ExprValue value = ExprTopLevel(interp, inString);
			switch (value.type)
			{
				
				case ExprValue.INT: 
					return (value.intValue != 0);
				
				case ExprValue.DOUBLE: 
					return (value.doubleValue != 0.0);
				
				case ExprValue.STRING: 
					return Util.getBoolean(interp, value.stringValue);
				
				default: 
					throw new TclRuntimeError("internal error: expression, unknown");
				
			}
		}
		
		/// <summary> Constructor.</summary>
		internal Expression()
		{
			mathFuncTable = new Hashtable();
			
			// rand  -- needs testing
			// srand -- needs testing
			// hypot -- needs testing
			// fmod  -- needs testing
			//              try [expr fmod(4.67, 2.2)]
			//              the answer should be .27, but I got .2699999999999996
			
			SupportClass.PutElement(mathFuncTable, "atan2", new Atan2Function());
			SupportClass.PutElement(mathFuncTable, "pow", new PowFunction());
			SupportClass.PutElement(mathFuncTable, "acos", new AcosFunction());
			SupportClass.PutElement(mathFuncTable, "asin", new AsinFunction());
			SupportClass.PutElement(mathFuncTable, "atan", new AtanFunction());
			SupportClass.PutElement(mathFuncTable, "ceil", new CeilFunction());
			SupportClass.PutElement(mathFuncTable, "cos", new CosFunction());
			SupportClass.PutElement(mathFuncTable, "cosh", new CoshFunction());
			SupportClass.PutElement(mathFuncTable, "exp", new ExpFunction());
			SupportClass.PutElement(mathFuncTable, "floor", new FloorFunction());
			SupportClass.PutElement(mathFuncTable, "fmod", new FmodFunction());
			SupportClass.PutElement(mathFuncTable, "hypot", new HypotFunction());
			SupportClass.PutElement(mathFuncTable, "log", new LogFunction());
			SupportClass.PutElement(mathFuncTable, "log10", new Log10Function());
			SupportClass.PutElement(mathFuncTable, "rand", new RandFunction());
			SupportClass.PutElement(mathFuncTable, "sin", new SinFunction());
			SupportClass.PutElement(mathFuncTable, "sinh", new SinhFunction());
			SupportClass.PutElement(mathFuncTable, "sqrt", new SqrtFunction());
			SupportClass.PutElement(mathFuncTable, "srand", new SrandFunction());
			SupportClass.PutElement(mathFuncTable, "tan", new TanFunction());
			SupportClass.PutElement(mathFuncTable, "tanh", new TanhFunction());
			
			SupportClass.PutElement(mathFuncTable, "abs", new AbsFunction());
			SupportClass.PutElement(mathFuncTable, "double", new DoubleFunction());
			SupportClass.PutElement(mathFuncTable, "int", new IntFunction());
			SupportClass.PutElement(mathFuncTable, "round", new RoundFunction());
      SupportClass.PutElement( mathFuncTable, "wide", new WideFunction() );
			
			m_expr = null;
			m_ind = 0;
			m_len = 0;
			m_token = UNKNOWN;
		}
		
		/// <summary> Provides top-level functionality shared by procedures like ExprInt,
		/// ExprDouble, etc.
		/// </summary>
		/// <param name="interp">the context in which to evaluate the expression.
		/// </param>
		/// <param name="string">the expression.
		/// </param>
		/// <exception cref=""> TclException for malformed expressions.
		/// </exception>
		/// <returns> the value of the expression.
		/// </returns>
		private ExprValue ExprTopLevel(Interp interp, string inString)
		{
			
			// Saved the state variables so that recursive calls to expr
			// can work:
			//	expr {[expr 1+2] + 3}
			
			string m_expr_saved = m_expr;
			int m_len_saved = m_len;
			int m_token_saved = m_token;
			int m_ind_saved = m_ind;
			
			try
			{
				m_expr = inString;
				m_ind = 0;
				m_len = inString.Length;
				m_token = UNKNOWN;
				
				ExprValue val = ExprGetValue(interp, - 1);
				if (m_token != END)
				{
					SyntaxError(interp);
				}
				return val;
			}
			finally
			{
				m_expr = m_expr_saved;
				m_len = m_len_saved;
				m_token = m_token_saved;
				m_ind = m_ind_saved;
			}
		}
		
		internal static void  IllegalType(Interp interp, int badType, int Operator)
		{
			throw new TclException(interp, "can't use " + ((badType == ExprValue.DOUBLE)?"floating-point value":"non-numeric string") + " as operand of \"" + operatorStrings[Operator] + "\"");
		}
		
		internal  void  SyntaxError(Interp interp)
		{
			throw new TclException(interp, "syntax error in expression \"" + m_expr + "\"");
		}
		
		internal static void  DivideByZero(Interp interp)
		{
			interp.setErrorCode(TclString.newInstance("ARITH DIVZERO {divide by zero}"));
			throw new TclException(interp, "divide by zero");
		}
		
		internal static void  IntegerTooLarge(Interp interp)
		{
			interp.setErrorCode(TclString.newInstance("ARITH IOVERFLOW {integer value too large to represent}"));
			throw new TclException(interp, "integer value too large to represent");
		}

    internal static void WideTooLarge( Interp interp )
    {
      interp.setErrorCode( TclString.newInstance( "ARITH IOVERFLOW {wide value too large to represent}" ) );
      throw new TclException( interp, "wide value too large to represent" );
    }

    internal static void DoubleTooLarge( Interp interp )
		{
			interp.setErrorCode(TclString.newInstance("ARITH OVERFLOW {floating-point value too large to represent}"));
			throw new TclException(interp, "floating-point value too large to represent");
		}
		
		internal static void  DoubleTooSmall(Interp interp)
		{
			interp.setErrorCode(TclString.newInstance("ARITH UNDERFLOW {floating-point value too small to represent}"));
			throw new TclException(interp, "floating-point value too small to represent");
		}
		
		internal static void  DomainError(Interp interp)
		{
			interp.setErrorCode(TclString.newInstance("ARITH DOMAIN {domain error: argument not in valid range}"));
			throw new TclException(interp, "domain error: argument not in valid range");
		}
		
		/// <summary> Given a string (such as one coming from command or variable
		/// substitution), make a Value based on the string.  The value
		/// be a floating-point or integer, if possible, or else it
		/// just be a copy of the string.
		/// 
		/// </summary>
		/// <param name="interp">the context in which to evaluate the expression.
		/// </param>
		/// <param name="s">the string to parse.
		/// </param>
		/// <exception cref=""> TclException for malformed expressions.
		/// </exception>
		/// <returns> the value of the expression.
		/// </returns>
		
		private ExprValue ExprParseString(Interp interp, string s)
		{
			
			int len = s.Length;
			
			/*
			System.out.println("now to ExprParseString ->" + s +
			"<- of length " + len);*/
			
			// Take shortcut when string is of length 0, as there is
			// only a string rep for an empty string (no int or double rep)
			// this will happend a lot so this shortcut will speed things up!
			
			if (len == 0)
			{
				return new ExprValue(s);
			}
			
			// The strings "0" and "1" are going to occure a lot
			// it might be wise to include shortcuts for these cases
			
			
			int i;
			if (looksLikeInt(s, len, 0))
			{
				//System.out.println("string looks like an int");
				
				// Note: use strtoul instead of strtol for integer conversions
				// to allow full-size unsigned numbers, but don't depend on
				// strtoul to handle sign characters;  it won't in some
				// implementations.
				
				for (i = 0; System.Char.IsWhiteSpace(s[i]); i++)
				{
					// Empty loop body.
				}
				
				StrtoulResult res;
				if (s[i] == '-')
				{
					i++;
					res = Util.strtoul(s, i, 0);
					res.value = - res.value;
				}
				else if (s[i] == '+')
				{
					i++;
					res = Util.strtoul(s, i, 0);
				}
				else
				{
					res = Util.strtoul(s, i, 0);
				}
				
				if (res.errno == 0)
				{
					// We treat this string as a number if all the charcters
					// following the parsed number are a whitespace char
					// E.g.: " 1", "1", "1 ", and " 1 "  are all good numbers
					
					bool trailing_blanks = true;
					
					for (i = res.index; i < len; i++)
					{
						if (!System.Char.IsWhiteSpace(s[i]))
						{
							trailing_blanks = false;
						}
					}
					
					if (trailing_blanks)
					{
						//System.out.println("string is an Integer of value " + res.value);
						m_token = VALUE;
						return new ExprValue(res.value);
					}
				}
				else if (res.errno == TCL.INTEGER_RANGE)
				{
					IntegerTooLarge(interp);
				}
				
				
				/*
				if (res.index == len) {
				// We treat this string as a number only if the number
				// ends at the end of the string. E.g.: " 1", "1" are
				// good numbers but "1 " is not.
				
				if (res.errno == TCL.INTEGER_RANGE) {
				IntegerTooLarge(interp);
				} else {
				m_token = VALUE;
				return new ExprValue(res.value);
				}
				}*/
			}
			else
			{
				//System.out.println("string does not look like an int, checking for Double");
				
				StrtodResult res = Util.strtod(s, 0);
				
				if (res.errno == 0)
				{
					// Trailing whitespaces are treated just like the Integer case
					
					bool trailing_blanks = true;
					
					for (i = res.index; i < len; i++)
					{
						if (!System.Char.IsWhiteSpace(s[i]))
						{
							trailing_blanks = false;
						}
					}
					
					if (trailing_blanks)
					{
						//System.out.println("string is a Double of value " + res.value);
						m_token = VALUE;
						return new ExprValue(res.value);
					}
				}
				else if (res.errno == TCL.DOUBLE_RANGE)
				{
					if (res.value != 0)
					{
						DoubleTooLarge(interp);
					}
					else
					{
						DoubleTooSmall(interp);
					}
				}
				// if res.errno is any other value (like TCL.INVALID_DOUBLE)
				// just fall through and use the string rep
				
				
				/*
				if (res.index == len) {
				
				if (res.errno == 0) {
				//System.out.println("string is a Double of value " + res.value);
				m_token = VALUE;
				return new ExprValue(res.value);
				} else if (res.errno == TCL.DOUBLE_RANGE) {
				DoubleTooLarge(interp);
				}
				}*/
			}
			
			//System.out.println("string is not a valid number, returning as string");
			
			// Not a valid number.  Save a string value (but don't do anything
			// if it's already the value).
			
			return new ExprValue(s);
		}
		
		/// <summary> Parse a "value" from the remainder of the expression.
		/// 
		/// </summary>
		/// <param name="interp">the context in which to evaluate the expression.
		/// </param>
		/// <param name="prec">treat any un-parenthesized operator with precedence
		/// <= this as the end of the expression.
		/// </param>
		/// <exception cref=""> TclException for malformed expressions.
		/// </exception>
		/// <returns> the value of the expression.
		/// </returns>
		private ExprValue ExprGetValue(Interp interp, int prec)
		{
			int Operator;
			bool gotOp = false; // True means already lexed the
			// operator (while picking up value
			// for unary operator).  Don't lex
			// again.
			ExprValue value, value2;
			
			// There are two phases to this procedure.  First, pick off an
			// initial value.  Then, parse (binary operator, value) pairs
			// until done.
			
			value = ExprLex(interp);
			
			if (m_token == OPEN_PAREN)
			{
				
				// Parenthesized sub-expression.
				
				value = ExprGetValue(interp, - 1);
				if (m_token != CLOSE_PAREN)
				{
					SyntaxError(interp);
				}
			}
			else
			{
				if (m_token == MINUS)
				{
					m_token = UNARY_MINUS;
				}
				if (m_token == PLUS)
				{
					m_token = UNARY_PLUS;
				}
				if (m_token >= UNARY_MINUS)
				{
					
					// Process unary operators.
					
					Operator = m_token;
					value = ExprGetValue(interp, precTable[m_token]);
					
					if (interp.noEval == 0)
					{
						switch (Operator)
						{
							
							case UNARY_MINUS: 
								if (value.type == ExprValue.INT)
								{
									value.intValue = - value.intValue;
								}
								else if (value.type == ExprValue.DOUBLE)
								{
									value.doubleValue = - value.doubleValue;
								}
								else
								{
									IllegalType(interp, value.type, Operator);
								}
								break;
							
							case UNARY_PLUS: 
								if ((value.type != ExprValue.INT) && (value.type != ExprValue.DOUBLE))
								{
									IllegalType(interp, value.type, Operator);
								}
								break;
							
							case NOT: 
								if (value.type == ExprValue.INT)
								{
									if (value.intValue != 0)
									{
										value.intValue = 0;
									}
									else
									{
										value.intValue = 1;
									}
								}
								else if (value.type == ExprValue.DOUBLE)
								{
									if (value.doubleValue == 0.0)
									{
										value.intValue = 1;
									}
									else
									{
										value.intValue = 0;
									}
									value.type = ExprValue.INT;
								}
								else
								{
									IllegalType(interp, value.type, Operator);
								}
								break;
							
							case BIT_NOT: 
								if (value.type == ExprValue.INT)
								{
									value.intValue = ~ value.intValue;
								}
								else
								{
									IllegalType(interp, value.type, Operator);
								}
								break;
							}
					}
					gotOp = true;
				}
				else if (m_token == CLOSE_PAREN)
				{
					// Caller needs to deal with close paren token.
					return null;
				}
				else if (m_token != VALUE)
				{
					SyntaxError(interp);
				}
			}
			if (value == null)
			{
				SyntaxError(interp);
			}
			
			// Got the first operand.  Now fetch (operator, operand) pairs.
			
			if (!gotOp)
			{
				value2 = ExprLex(interp);
			}
			
			while (true)
			{
				Operator = m_token;
				if ((Operator < MULT) || (Operator >= UNARY_MINUS))
				{
					if ((Operator == END) || (Operator == CLOSE_PAREN) || (Operator == COMMA))
					{
						return value; // Goto Done
					}
					else
					{
						SyntaxError(interp);
					}
				}
				if (precTable[Operator] <= prec)
				{
					return value; // (goto done)
				}
				
				// If we're doing an AND or OR and the first operand already
				// determines the result, don't execute anything in the
				// second operand:  just parse.  Same style for ?: pairs.
				
				if ((Operator == AND) || (Operator == OR) || (Operator == QUESTY))
				{
					
					if (value.type == ExprValue.DOUBLE)
					{
						value.intValue = (value.doubleValue != 0)?1:0;
						value.type = ExprValue.INT;
					}
					else if (value.type == ExprValue.STRING)
					{
						try
						{
							bool b = Util.getBoolean(null, value.stringValue);
							value = new ExprValue(b?1:0);
						}
						catch (TclException e)
						{
							if (interp.noEval == 0)
							{
								IllegalType(interp, ExprValue.STRING, Operator);
							}
							
							// Must set value.intValue to avoid referencing
							// uninitialized memory in the "if" below;  the actual
							// value doesn't matter, since it will be ignored.
							
							value.intValue = 0;
						}
					}
					if (((Operator == AND) && (value.intValue == 0)) || ((Operator == OR) && (value.intValue != 0)))
					{
						interp.noEval++;
						try
						{
							value2 = ExprGetValue(interp, precTable[Operator]);
						}
						finally
						{
							interp.noEval--;
						}
						if (Operator == OR)
						{
							value.intValue = 1;
						}
						continue;
					}
					else if (Operator == QUESTY)
					{
						// Special note:  ?: operators must associate right to
						// left.  To make this happen, use a precedence one lower
						// than QUESTY when calling ExprGetValue recursively.
						
						if (value.intValue != 0)
						{
							value = ExprGetValue(interp, precTable[QUESTY] - 1);
							if (m_token != COLON)
							{
								SyntaxError(interp);
							}
							
							interp.noEval++;
							try
							{
								value2 = ExprGetValue(interp, precTable[QUESTY] - 1);
							}
							finally
							{
								interp.noEval--;
							}
						}
						else
						{
							interp.noEval++;
							try
							{
								value2 = ExprGetValue(interp, precTable[QUESTY] - 1);
							}
							finally
							{
								interp.noEval--;
							}
							if (m_token != COLON)
							{
								SyntaxError(interp);
							}
							value = ExprGetValue(interp, precTable[QUESTY] - 1);
						}
						continue;
					}
					else
					{
						value2 = ExprGetValue(interp, precTable[Operator]);
					}
				}
				else
				{
					value2 = ExprGetValue(interp, precTable[Operator]);
				}
				
				
				if ((m_token < MULT) && (m_token != VALUE) && (m_token != END) && (m_token != COMMA) && (m_token != CLOSE_PAREN))
				{
					SyntaxError(interp);
				}
				
				if (interp.noEval != 0)
				{
					continue;
				}
				
				// At this point we've got two values and an operator.  Check
				// to make sure that the particular data types are appropriate
				// for the particular operator, and perform type conversion
				// if necessary.
				
				switch (Operator)
				{
					
					
					// For the operators below, no strings are allowed and
					// ints get converted to floats if necessary.
					case MULT: 
					case DIVIDE: 
					case PLUS: 
					case MINUS: 
						if ((value.type == ExprValue.STRING) || (value2.type == ExprValue.STRING))
						{
							IllegalType(interp, ExprValue.STRING, Operator);
						}
						if (value.type == ExprValue.DOUBLE)
						{
							if (value2.type == ExprValue.INT)
							{
								value2.doubleValue = value2.intValue;
								value2.type = ExprValue.DOUBLE;
							}
						}
						else if (value2.type == ExprValue.DOUBLE)
						{
							if (value.type == ExprValue.INT)
							{
								value.doubleValue = value.intValue;
								value.type = ExprValue.DOUBLE;
							}
						}
						break;
						
						// For the operators below, only integers are allowed.
					
					
					case MOD: 
					case LEFT_SHIFT: 
					case RIGHT_SHIFT: 
					case BIT_AND: 
					case BIT_XOR: 
					case BIT_OR: 
						if (value.type != ExprValue.INT)
						{
							IllegalType(interp, value.type, Operator);
						}
						else if (value2.type != ExprValue.INT)
						{
							IllegalType(interp, value2.type, Operator);
						}
						break;
						
						// For the operators below, any type is allowed but the
						// two operands must have the same type.  Convert integers
						// to floats and either to strings, if necessary.
					
					
					case LESS: 
					case GREATER: 
					case LEQ: 
					case GEQ: 
					case EQUAL: 
          case EQ:
					case NEQ:
          case NE:
            if ( value.type == ExprValue.STRING )
						{
							if (value2.type != ExprValue.STRING)
							{
								ExprMakeString(interp, value2);
							}
						}
						else if (value2.type == ExprValue.STRING)
						{
							if (value.type != ExprValue.STRING)
							{
								ExprMakeString(interp, value);
							}
						}
						else if (value.type == ExprValue.DOUBLE)
						{
							if (value2.type == ExprValue.INT)
							{
								value2.doubleValue = value2.intValue;
								value2.type = ExprValue.DOUBLE;
							}
						}
						else if (value2.type == ExprValue.DOUBLE)
						{
							if (value.type == ExprValue.INT)
							{
								value.doubleValue = value.intValue;
								value.type = ExprValue.DOUBLE;
							}
						}
						break;
						
						// For the operators below, no strings are allowed, but
						// no int->double conversions are performed.
					
					
					case AND: 
					case OR: 
						if (value.type == ExprValue.STRING)
						{
							IllegalType(interp, value.type, Operator);
						}
						if (value2.type == ExprValue.STRING)
						{
							try
							{
								bool b = Util.getBoolean(null, value2.stringValue);
								value2 = new ExprValue(b?1:0);
							}
							catch (TclException e)
							{
								IllegalType(interp, value2.type, Operator);
							}
						}
						break;
						
						// For the operators below, type and conversions are
						// irrelevant:  they're handled elsewhere.
					
					
					case QUESTY: 
					case COLON: 
						break;
						
						// Any other operator is an error.
					
					
					default: 
						throw new TclException(interp, "unknown operator in expression");
					
				}
				
				// Carry out the function of the specified operator.
				
				switch (Operator)
				{
					
					case MULT: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = value.intValue * value2.intValue;
						}
						else
						{
							value.doubleValue *= value2.doubleValue;
						}
						break;
					
					case DIVIDE: 
					case MOD: 
						if (value.type == ExprValue.INT)
						{
							long divisor, quot, rem;
							bool negative;
							
							if (value2.intValue == 0)
							{
								DivideByZero(interp);
							}
							
							// The code below is tricky because C doesn't guarantee
							// much about the properties of the quotient or
							// remainder, but Tcl does:  the remainder always has
							// the same sign as the divisor and a smaller absolute
							// value.
							
							divisor = value2.intValue;
							negative = false;
							if (divisor < 0)
							{
								divisor = - divisor;
								value.intValue = - value.intValue;
								negative = true;
							}
							quot = value.intValue / divisor;
							rem = value.intValue % divisor;
							if (rem < 0)
							{
								rem += divisor;
								quot -= 1;
							}
							if (negative)
							{
								rem = - rem;
							}
							value.intValue = (Operator == DIVIDE)?quot:rem;
						}
						else
						{
							if (value2.doubleValue == 0.0)
							{
								DivideByZero(interp);
							}
							value.doubleValue /= value2.doubleValue;
						}
						break;
					
					case PLUS: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = value.intValue + value2.intValue;
						}
						else
						{
							value.doubleValue += value2.doubleValue;
						}
						break;
					
					case MINUS: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = value.intValue - value2.intValue;
						}
						else
						{
							value.doubleValue -= value2.doubleValue;
						}
						break;
					
					case LEFT_SHIFT: 
						value.intValue <<= (int) value2.intValue;
						break;
					
					case RIGHT_SHIFT: 
						
						if (value.intValue < 0)
						{
							value.intValue = ~ ((~ value.intValue) >> (int) value2.intValue);
						}
						else
						{
							value.intValue >>= (int) value2.intValue;
						}
						break;
					
					case LESS: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = (value.intValue < value2.intValue)?1:0;
						}
						else if (value.type == ExprValue.DOUBLE)
						{
							value.intValue = (value.doubleValue < value2.doubleValue)?1:0;
						}
						else
						{
							value.intValue = (value.stringValue.CompareTo(value2.stringValue) < 0)?1:0;
						}
						value.type = ExprValue.INT;
						break;
					
					case GREATER: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = (value.intValue > value2.intValue)?1:0;
						}
						else if (value.type == ExprValue.DOUBLE)
						{
							value.intValue = (value.doubleValue > value2.doubleValue)?1:0;
						}
						else
						{
							value.intValue = (value.stringValue.CompareTo(value2.stringValue) > 0)?1:0;
						}
						value.type = ExprValue.INT;
						break;
					
					case LEQ: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = (value.intValue <= value2.intValue)?1:0;
						}
						else if (value.type == ExprValue.DOUBLE)
						{
							value.intValue = (value.doubleValue <= value2.doubleValue)?1:0;
						}
						else
						{
							value.intValue = (value.stringValue.CompareTo(value2.stringValue) <= 0)?1:0;
						}
						value.type = ExprValue.INT;
						break;
					
					case GEQ: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = (value.intValue >= value2.intValue)?1:0;
						}
						else if (value.type == ExprValue.DOUBLE)
						{
							value.intValue = (value.doubleValue >= value2.doubleValue)?1:0;
						}
						else
						{
							value.intValue = (value.stringValue.CompareTo(value2.stringValue) >= 0)?1:0;
						}
						value.type = ExprValue.INT;
						break;
					
					case EQUAL: 
          case EQ: 
						if (value.type == ExprValue.INT)
						{
							value.intValue = (value.intValue == value2.intValue)?1:0;
						}
						else if (value.type == ExprValue.DOUBLE)
						{
							value.intValue = (value.doubleValue == value2.doubleValue)?1:0;
						}
						else
						{
							value.intValue = (value.stringValue.CompareTo(value2.stringValue) == 0)?1:0;
						}
						value.type = ExprValue.INT;
						break;
					
					case NEQ: 
          case NE:
						if (value.type == ExprValue.INT)
						{
							value.intValue = (value.intValue != value2.intValue)?1:0;
						}
						else if (value.type == ExprValue.DOUBLE)
						{
							value.intValue = (value.doubleValue != value2.doubleValue)?1:0;
						}
						else
						{
							value.intValue = (value.stringValue.CompareTo(value2.stringValue) != 0)?1:0;
						}
						value.type = ExprValue.INT;
						break;
					
					case BIT_AND: 
						value.intValue &= value2.intValue;
						break;
					
					case BIT_XOR: 
						value.intValue ^= value2.intValue;
						break;
					
					case BIT_OR: 
						value.intValue |= value2.intValue;
						break;
						
						// For AND and OR, we know that the first value has already
						// been converted to an integer.  Thus we need only consider
						// the possibility of int vs. double for the second value.
					
					
					case AND: 
						if (value2.type == ExprValue.DOUBLE)
						{
							value2.intValue = (value2.doubleValue != 0)?1:0;
							value2.type = ExprValue.INT;
						}
						value.intValue = ((value.intValue != 0) && (value2.intValue != 0))?1:0;
						break;
					
					case OR: 
						if (value2.type == ExprValue.DOUBLE)
						{
							value2.intValue = (value2.doubleValue != 0)?1:0;
							value2.type = ExprValue.INT;
						}
						value.intValue = ((value.intValue != 0) || (value2.intValue != 0))?1:0;
						break;
					
					
					case COLON: 
						SyntaxError(interp);
						break;
					}
			}
		}
		
		/// <summary> GetLexeme -> ExprLex
		/// 
		/// Lexical analyzer for expression parser:  parses a single value,
		/// operator, or other syntactic element from an expression string.
		/// 
		/// Size effects: the "m_token" member variable is set to the value of
		/// the current token.
		/// 
		/// </summary>
		/// <param name="interp">the context in which to evaluate the expression.
		/// </param>
		/// <exception cref=""> TclException for malformed expressions.
		/// </exception>
		/// <returns> the value of the expression.
		/// </returns>
		private ExprValue ExprLex(Interp interp)
		{
			char c, c2;
			
			while (m_ind < m_len && System.Char.IsWhiteSpace(m_expr[m_ind]))
			{
				m_ind++;
			}
			if (m_ind >= m_len)
			{
				m_token = END;
				return null;
			}
			
			// First try to parse the token as an integer or
			// floating-point number.  Don't want to check for a number if
			// the first character is "+" or "-".  If we do, we might
			// treat a binary operator as unary by
			// mistake, which will eventually cause a syntax error.
			
			c = m_expr[m_ind];
			if (m_ind < m_len - 1)
			{
				c2 = m_expr[m_ind + 1];
			}
			else
			{
				c2 = '\x0000';
			}
			
			if ((c != '+') && (c != '-'))
			{
				bool startsWithDigit = System.Char.IsDigit(c);
				if (startsWithDigit && looksLikeInt(m_expr, m_len, m_ind))
				{
					StrtoulResult res = Util.strtoul(m_expr, m_ind, 0);
					
					if (res.errno == 0)
					{
						m_ind = res.index;
						m_token = VALUE;
						return new ExprValue(res.value);
					}
					else
					{
						if (res.errno == TCL.INTEGER_RANGE)
						{
							IntegerTooLarge(interp);
						}
					}
				}
				else if (startsWithDigit || (c == '.') || (c == 'n') || (c == 'N'))
				{
					StrtodResult res = Util.strtod(m_expr, m_ind);
					if (res.errno == 0)
					{
						m_ind = res.index;
						m_token = VALUE;
						return new ExprValue(res.value);
					}
					else
					{
						if (res.errno == TCL.DOUBLE_RANGE)
						{
							if (res.value != 0)
							{
								DoubleTooLarge(interp);
							}
							else
							{
								DoubleTooSmall(interp);
							}
						}
					}
				}
			}
			
			ParseResult pres;
			ExprValue retval;
			m_ind += 1; // ind is advanced to point to the next token
			
			switch (c)
			{
				
				case '$': 
					m_token = VALUE;
					pres = ParseAdaptor.parseVar(interp, m_expr, m_ind, m_len);
					m_ind = pres.nextIndex;
					
					if (interp.noEval != 0)
					{
						retval = new ExprValue(0);
					}
					else
					{
						
						retval = ExprParseString(interp, pres.value.ToString());
					}
					pres.release();
					return retval;
				
				case '[': 
					m_token = VALUE;
					pres = ParseAdaptor.parseNestedCmd(interp, m_expr, m_ind, m_len);
					m_ind = pres.nextIndex;
					
					if (interp.noEval != 0)
					{
						retval = new ExprValue(0);
					}
					else
					{
						
						retval = ExprParseString(interp, pres.value.ToString());
					}
					pres.release();
					return retval;
				
				case '"': 
					m_token = VALUE;
					
					
					//System.out.println("now to parse from ->" + m_expr + "<- at index "
					//	+ m_ind);
					
					pres = ParseAdaptor.parseQuotes(interp, m_expr, m_ind, m_len);
					m_ind = pres.nextIndex;
					
					//   System.out.println("after parse next index is " + m_ind);
					
					if (interp.noEval != 0)
					{
						//      System.out.println("returning noEval zero value");
						retval = new ExprValue(0);
					}
					else
					{
						//     System.out.println("returning value string ->" + pres.value.toString() + "<-" );
						
						retval = ExprParseString(interp, pres.value.ToString());
					}
					pres.release();
					return retval;
				
				case '{': 
					m_token = VALUE;
					pres = ParseAdaptor.parseBraces(interp, m_expr, m_ind, m_len);
					m_ind = pres.nextIndex;
					if (interp.noEval != 0)
					{
						retval = new ExprValue(0);
					}
					else
					{
						
						retval = ExprParseString(interp, pres.value.ToString());
					}
					pres.release();
					return retval;
				
				case '(': 
					m_token = OPEN_PAREN;
					return null;
				
				
				case ')': 
					m_token = CLOSE_PAREN;
					return null;
				
				
				case ',': 
					m_token = COMMA;
					return null;
				
				
				case '*': 
					m_token = MULT;
					return null;
				
				
				case '/': 
					m_token = DIVIDE;
					return null;
				
				
				case '%': 
					m_token = MOD;
					return null;
				
				
				case '+': 
					m_token = PLUS;
					return null;
				
				
				case '-': 
					m_token = MINUS;
					return null;
				
				
				case '?': 
					m_token = QUESTY;
					return null;
				
				
				case ':': 
					m_token = COLON;
					return null;
				
				
				case '<': 
					switch (c2)
					{
						
						case '<': 
							m_ind += 1;
							m_token = LEFT_SHIFT;
							break;
						
						case '=': 
							m_ind += 1;
							m_token = LEQ;
							break;
						
						default: 
							m_token = LESS;
							break;
						
					}
					return null;
				
				
				case '>': 
					switch (c2)
					{
						
						case '>': 
							m_ind += 1;
							m_token = RIGHT_SHIFT;
							break;
						
						case '=': 
							m_ind += 1;
							m_token = GEQ;
							break;
						
						default: 
							m_token = GREATER;
							break;
						
					}
					return null;
				
				
				case '=': 
					if (c2 == '=')
					{
						m_ind += 1;
						m_token = EQUAL;
					}
					else
					{
						m_token = UNKNOWN;
					}
					return null;


        case 'e':
          if ( c2 == 'q' )
          {
            m_ind += 1;
            m_token = EQUAL;
          }
          else
          {
            m_token = UNKNOWN;
          }
          return null;


        case 'n':
          if ( c2 == 'e' )
          {
            m_ind += 1;
            m_token = NEQ;
          }
          else
          {
            m_token = UNKNOWN;
          }
          return null;
        
        
        case '!': 
					if (c2 == '=')
					{
						m_ind += 1;
						m_token = NEQ;
					}
					else
					{
						m_token = NOT;
					}
					return null;
				
				
				case '&': 
					if (c2 == '&')
					{
						m_ind += 1;
						m_token = AND;
					}
					else
					{
						m_token = BIT_AND;
					}
					return null;
				
				
				case '^': 
					m_token = BIT_XOR;
					return null;
				
				
				case '|': 
					if (c2 == '|')
					{
						m_ind += 1;
						m_token = OR;
					}
					else
					{
						m_token = BIT_OR;
					}
					return null;
				
				
				case '~': 
					m_token = BIT_NOT;
					return null;
				
				
				default: 
					if (System.Char.IsLetter(c))
					{
						// Oops, re-adjust m_ind so that it points to the beginning
						// of the function name.
						
						m_ind--;
						return mathFunction(interp);
					}
					m_token = UNKNOWN;
					return null;
				
			}
		}
		
		/// <summary> Parses a math function from an expression string, carry out the
		/// function, and return the value computed.
		/// 
		/// </summary>
		/// <param name="interp">current interpreter.
		/// </param>
		/// <returns> the value computed by the math function.
		/// </returns>
		/// <exception cref=""> TclException if any error happens.
		/// </exception>
		internal  ExprValue mathFunction(Interp interp)
		{
			int startIdx = m_ind;
			ExprValue value;
			string funcName;
			MathFunction mathFunc;
			TclObject[] argv = null;
			int numArgs;
			
			// Find the end of the math function's name and lookup the MathFunc
			// record for the function.  Search until the char at m_ind is not
			// alphanumeric or '_'
			
			for (; m_ind < m_len; m_ind++)
			{
				if (!(System.Char.IsLetterOrDigit(m_expr[m_ind]) || m_expr[m_ind] == '_'))
				{
					break;
				}
			}
			
			// Get the funcName BEFORE calling ExprLex, so the funcName
			// will not have trailing whitespace.
			
			funcName = m_expr.Substring(startIdx, (m_ind) - (startIdx));
			
			// Parse errors are thrown BEFORE unknown function names
			
			ExprLex(interp);
			if (m_token != OPEN_PAREN)
			{
				SyntaxError(interp);
			}
			
			// Now test for unknown funcName.  Doing the above statements
			// out of order will cause some tests to fail.
			
			mathFunc = (MathFunction) mathFuncTable[funcName];
			if (mathFunc == null)
			{
				throw new TclException(interp, "unknown math function \"" + funcName + "\"");
			}
			
			// Scan off the arguments for the function, if there are any.
			
			numArgs = mathFunc.argTypes.Length;
			
			if (numArgs == 0)
			{
				ExprLex(interp);
				if (m_token != CLOSE_PAREN)
				{
					SyntaxError(interp);
				}
			}
			else
			{
				argv = new TclObject[numArgs];
				
				for (int i = 0; ; i++)
				{
					value = ExprGetValue(interp, - 1);
					
					// Handle close paren with no value
					// % expr {srand()}
					
					if ((value == null) && (m_token == CLOSE_PAREN))
					{
						if (i == numArgs)
							break;
						else
							throw new TclException(interp, "too few arguments for math function");
					}
					
					if (value.type == ExprValue.STRING)
					{
						throw new TclException(interp, "argument to math function didn't have numeric value");
					}
					
					// Copy the value to the argument record, converting it if
					// necessary.
					
					if (value.type == ExprValue.INT)
					{
						if (mathFunc.argTypes[i] == MathFunction.DOUBLE)
						{
							argv[i] = TclDouble.newInstance((int) value.intValue);
						}
						else
						{
                argv[i] = TclLong.newInstance( value.intValue );
						}
					}
					else
					{
						if (mathFunc.argTypes[i] == MathFunction.INT)
						{
							
							argv[i] = TclInteger.newInstance((int) value.doubleValue);
						}
						else
						{
							argv[i] = TclDouble.newInstance(value.doubleValue);
						}
					}
					
					// Check for a comma separator between arguments or a
					// close-paren to end the argument list.
					
					if (i == (numArgs - 1))
					{
						if (m_token == CLOSE_PAREN)
						{
							break;
						}
						if (m_token == COMMA)
						{
							throw new TclException(interp, "too many arguments for math function");
						}
						else
						{
							SyntaxError(interp);
						}
					}
					if (m_token != COMMA)
					{
						if (m_token == CLOSE_PAREN)
						{
							throw new TclException(interp, "too few arguments for math function");
						}
						else
						{
							SyntaxError(interp);
						}
					}
				}
			}
			
			m_token = VALUE;
			if (interp.noEval != 0)
			{
				return new ExprValue(0);
			}
			else
			{
				/*
				* Invoke the function and copy its result back into valuePtr.
				*/
				return mathFunc.apply(interp, argv);
			}
		}
		
		/// <summary> This procedure decides whether the leading characters of a
		/// string look like an integer or something else (such as a
		/// floating-point number or string).
		/// </summary>
		/// <returns> a boolean value indicating if the string looks like an integer.
		/// </returns>
		
		internal static bool looksLikeInt(string s, int len, int i)
		{
			while (i < len && System.Char.IsWhiteSpace(s[i]))
			{
				i++;
			}
			if (i >= len)
			{
				return false;
			}
			char c = s[i];
			if ((c == '+') || (c == '-'))
			{
				i++;
				if (i >= len)
				{
					return false;
				}
				c = s[i];
			}
			if (!System.Char.IsDigit(c))
			{
				return false;
			}
			while (i < len && System.Char.IsDigit(s[i]))
			{
				//System.out.println("'" + s.charAt(i) + "' is a digit");
				i++;
			}
			if (i >= len)
			{
				return true;
			}
			
			//ported from C code
			c = s[i];
			if ((c != '.') && (c != 'e') && (c != 'E'))
			{
				return true;
			}
			
			//original
			/*
			if (i < len) {
			c = s.charAt(i);
			if ((c == '.') || (c == 'e') || (c == 'E')) {
			return false;
			}
			}*/
			
			return false;
		}
		
		/// <summary> Converts a value from int or double representation to a string.</summary>
		/// <param name="interp">interpreter to use for precision information.
		/// </param>
		/// <param name="value">Value to be converted.
		/// </param>
		
		internal static void  ExprMakeString(Interp interp, ExprValue value)
		{
			if (value.type == ExprValue.INT)
			{
				value.stringValue = System.Convert.ToString(value.intValue);
			}
			else if (value.type == ExprValue.DOUBLE)
			{
				value.stringValue = value.doubleValue.ToString();
			}
			value.type = ExprValue.STRING;
		}
		
		internal static void  checkIntegerRange(Interp interp, double d)
		{
			if (d < 0)
			{
				
				if (d < ((double) TCL.INT_MIN))
				{
					Expression.IntegerTooLarge(interp);
				}
			}
			else
			{
				
				if (d > ((double) TCL.INT_MAX))
				{
					Expression.IntegerTooLarge(interp);
				}
			}
		}
    internal static void checkWideRange( Interp interp, double d )
    {
      if ( d < 0 )
      {
        if ( d < Int64.MinValue  ) 
        {
          Expression.WideTooLarge( interp );
        }
      }
      else
      {
        if ( d >  Int64.MaxValue  ) 
        {
          Expression.WideTooLarge( interp );
        }
      }
    }
		
		internal static void  checkDoubleRange(Interp interp, double d)
		{
			if ((d == System.Double.NaN) || (d == System.Double.NegativeInfinity) || (d == System.Double.PositiveInfinity))
			{
				Expression.DoubleTooLarge(interp);
			}
		}
	}
	
	abstract class MathFunction
	{
		internal const int INT = 0;
		internal const int DOUBLE = 1;
		internal const int EITHER = 2;
		
		internal int[] argTypes;
		
		internal abstract ExprValue apply(Interp interp, TclObject[] argv);
	}
	
	abstract class UnaryMathFunction:MathFunction
	{
		internal UnaryMathFunction()
		{
			argTypes = new int[1];
			argTypes[0] = DOUBLE;
		}
	}
	
	abstract class BinaryMathFunction:MathFunction
	{
		internal BinaryMathFunction()
		{
			argTypes = new int[2];
			argTypes[0] = DOUBLE;
			argTypes[1] = DOUBLE;
		}
	}
	
	
	abstract class NoArgMathFunction:MathFunction
	{
		internal NoArgMathFunction()
		{
			argTypes = new int[0];
		}
	}
	
	
	class Atan2Function:BinaryMathFunction
	{
		internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Atan2(TclDouble.get(interp, argv[0]), TclDouble.get(interp, argv[1])));
		}
	}
	
	class AbsFunction:MathFunction
	{
		internal AbsFunction()
		{
			argTypes = new int[1];
			argTypes[0] = EITHER;
		}
		
		internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			if (argv[0].InternalRep is TclDouble)
			{
				double d = TclDouble.get(interp, argv[0]);
				if (d > 0)
				{
					return new ExprValue(d);
				}
				else
				{
					return new ExprValue(- d);
				}
			}
			else
			{
				int i = TclInteger.get(interp, argv[0]);
				if (i > 0)
				{
					return new ExprValue(i);
				}
				else
				{
					return new ExprValue(- i);
				}
			}
		}
	}
	
	class DoubleFunction:MathFunction
	{
		internal DoubleFunction()
		{
			argTypes = new int[1];
			argTypes[0] = EITHER;
		}
		
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(TclDouble.get(interp, argv[0]));
		}
	}
	
	class IntFunction:MathFunction
	{
		internal IntFunction()
		{
			argTypes = new int[1];
			argTypes[0] = EITHER;
		}
		
		internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double d = TclDouble.get(interp, argv[0]);
			Expression.checkIntegerRange(interp, d);
			return new ExprValue((int) d);
		}
	}

  class WideFunction : MathFunction
  {
    internal WideFunction()
    {
      argTypes = new int[1];
      argTypes[0] = EITHER;
    }

    internal override ExprValue apply( Interp interp, TclObject[] argv )
    {
      double d = TclDouble.get( interp, argv[0] );
      Expression.checkWideRange( interp, d );
      return new ExprValue( (long)d );
    }
  }
  class RoundFunction : 
    MathFunction
	{
		internal RoundFunction()
		{
			argTypes = new int[1];
			argTypes[0] = EITHER;
		}
		
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			if (argv[0].InternalRep is TclDouble)
			{
				double d = TclDouble.get(interp, argv[0]);
				if (d < 0)
				{
					Expression.checkIntegerRange(interp, d - 0.5);
					
					return new ExprValue((int) (d - 0.5));
				}
				else
				{
					Expression.checkIntegerRange(interp, d + 0.5);
					
					return new ExprValue((int) (d + 0.5));
				}
			}
			else
			{
				return new ExprValue(TclInteger.get(interp, argv[0]));
			}
		}
	}
	
	class PowFunction:BinaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double d = System.Math.Pow(TclDouble.get(interp, argv[0]), TclDouble.get(interp, argv[1]));
			Expression.checkDoubleRange(interp, d);
			return new ExprValue(d);
		}
	}
	
	/*
	* The following section is generated by this script.
	*
	set missing {fmod}
	set byhand {atan2 pow}
	
	
	foreach func {Acos Asin Atan Ceil Cos Exp Floor Log Sin
	Sqrt Tan} {
	puts "
	class $func\Function extends UnaryMathFunction {
	ExprValue apply(Interp interp, TclObject argv\[\])
	throws TclException {
	return new ExprValue(Math.[string tolower $func](TclDouble.get(interp, argv\[0\])));
	}
	}
	"
	}
	
	*/
	
	class AcosFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double d = TclDouble.get(interp, argv[0]);
			if ((d < - 1) || (d > 1))
			{
				Expression.DomainError(interp);
			}
			return new ExprValue(System.Math.Acos(d));
		}
	}
	
	class AsinFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Asin(TclDouble.get(interp, argv[0])));
		}
	}
	
	class AtanFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Atan(TclDouble.get(interp, argv[0])));
		}
	}
	
	
	class CeilFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Ceiling(TclDouble.get(interp, argv[0])));
		}
	}
	
	
	class CosFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Cos(TclDouble.get(interp, argv[0])));
		}
	}
	
	
	class CoshFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double x = TclDouble.get(interp, argv[0]);
			double d1 = System.Math.Pow(System.Math.E, x);
			double d2 = System.Math.Pow(System.Math.E, - x);
			
			Expression.checkDoubleRange(interp, d1);
			Expression.checkDoubleRange(interp, d2);
			return new ExprValue((d1 + d2) / 2);
		}
	}
	
	class ExpFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double d = System.Math.Exp(TclDouble.get(interp, argv[0]));
			if ((d == System.Double.NaN) || (d == System.Double.NegativeInfinity) || (d == System.Double.PositiveInfinity))
			{
				Expression.DoubleTooLarge(interp);
			}
			return new ExprValue(d);
		}
	}
	
	
	class FloorFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Floor(TclDouble.get(interp, argv[0])));
		}
	}
	
	
	class FmodFunction:BinaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.IEEERemainder(TclDouble.get(interp, argv[0]), TclDouble.get(interp, argv[1])));
		}
	}
	
	class HypotFunction:BinaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double x = TclDouble.get(interp, argv[0]);
			double y = TclDouble.get(interp, argv[1]);
			return new ExprValue(System.Math.Sqrt(((x * x) + (y * y))));
		}
	}
	
	
	class LogFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Log(TclDouble.get(interp, argv[0])));
		}
	}
	
	
	class Log10Function:UnaryMathFunction
	{
						private static readonly double log10;
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Log(TclDouble.get(interp, argv[0])) / log10);
		}
		static Log10Function()
		{
			log10 = System.Math.Log(10);
		}
	}
	
	
	class SinFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Sin(TclDouble.get(interp, argv[0])));
		}
	}
	
	
	class SinhFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double x = TclDouble.get(interp, argv[0]);
			
			double d1 = System.Math.Pow(System.Math.E, x);
			double d2 = System.Math.Pow(System.Math.E, - x);
			
			Expression.checkDoubleRange(interp, d1);
			Expression.checkDoubleRange(interp, d2);
			
			return new ExprValue((d1 - d2) / 2);
		}
	}
	
	
	class SqrtFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Sqrt(TclDouble.get(interp, argv[0])));
		}
	}
	
	
	class TanFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return new ExprValue(System.Math.Tan(TclDouble.get(interp, argv[0])));
		}
	}
	
	class TanhFunction:UnaryMathFunction
	{
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			double x = TclDouble.get(interp, argv[0]);
			if (x == 0)
			{
				return new ExprValue(0.0);
			}
			
			double d1 = System.Math.Pow(System.Math.E, x);
			double d2 = System.Math.Pow(System.Math.E, - x);
			
			Expression.checkDoubleRange(interp, d1);
			Expression.checkDoubleRange(interp, d2);
			
			return new ExprValue((d1 - d2) / (d1 + d2));
		}
	}
	
	class RandFunction:NoArgMathFunction
	{
		
		// Generate the random number using the linear congruential
		// generator defined by the following recurrence:
		//		seed = ( IA * seed ) mod IM
		// where IA is 16807 and IM is (2^31) - 1.  In order to avoid
		// potential problems with integer overflow, the  code uses
		// additional constants IQ and IR such that
		//		IM = IA*IQ + IR
		// For details on how this algorithm works, refer to the following
		// papers: 
		//
		//	S.K. Park & K.W. Miller, "Random number generators: good ones
		//	are hard to find," Comm ACM 31(10):1192-1201, Oct 1988
		//
		//	W.H. Press & S.A. Teukolsky, "Portable random number
		//	generators," Computers in Physics 6(5):522-524, Sep/Oct 1992.
		
		
		private const int randIA = 16807;
		private const int randIM = 2147483647;
		private const int randIQ = 127773;
		private const int randIR = 2836;
				private static readonly System.DateTime date = System.DateTime.Now;
		
		/// <summary> Srand calls the main algorithm for rand after it sets the seed.
		/// To facilitate this call, the method is static and can be used
		/// w/o creating a new object.  But we also need to maintain the
		/// inheritance hierarchy, thus the dynamic apply() calls the static 
		/// statApply().
		/// </summary>
		
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			return (statApply(interp, argv));
		}
		
		
		internal static ExprValue statApply(Interp interp, TclObject[] argv)
		{
			
			int tmp;
			
			if (!(interp.randSeedInit))
			{
				interp.randSeedInit = true;
				interp.randSeed = (int) date.Ticks;
			}
			
			if (interp.randSeed == 0)
			{
				// Don't allow a 0 seed, since it breaks the generator.  Shift
				// it to some other value.
				
				interp.randSeed = 123459876;
			}
			
			tmp = (int) (interp.randSeed / randIQ);
			interp.randSeed = ((randIA * (interp.randSeed - tmp * randIQ)) - randIR * tmp);
			
			if (interp.randSeed < 0)
			{
				interp.randSeed += randIM;
			}
			
			return new ExprValue(interp.randSeed * (1.0 / randIM));
		}
	}
	
	
	class SrandFunction:UnaryMathFunction
	{
		
				internal override ExprValue apply(Interp interp, TclObject[] argv)
		{
			
			// Reset the seed.
			
			interp.randSeedInit = true;
			
			interp.randSeed = (long) TclDouble.get(interp, argv[0]);
			
			// To avoid duplicating the random number generation code we simply
			// call the static random number generator in the RandFunction 
			// class.
			
			return (RandFunction.statApply(interp, null));
		}
	}
}
