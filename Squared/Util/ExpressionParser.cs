using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Squared.Util.Expressions {
    public enum TokenType {
        Number,
        Identifier,
        Operator,
        Paren,
        Comma
    }

    public struct Token {
        public TokenType Type;
        public string Text;

        public override string ToString () {
            return String.Format("{0}({1})", Type, Text);
        }
    }

    public class Parser {
        protected Regex _Regex;
        protected int _Group_Number, _Group_Identifier, _Group_Operator, _Group_Paren, _Group_Comma;

        public Parser () {
            _Regex = new Regex(
                @"((?'number'(-?)[0-9]+(\.[0-9]+)?)|(?'identifier'[_A-Za-z][A-Za-z0-9_]*)|(?'operator'<=|>=|!=|[\-+/*&|^!><=.%?:@])|(?'paren'[(){}]))|(?'comma',)",
                RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture
            );

            _Group_Number = _Regex.GroupNumberFromName("number");
            _Group_Identifier = _Regex.GroupNumberFromName("identifier");
            _Group_Operator = _Regex.GroupNumberFromName("operator");
            _Group_Paren = _Regex.GroupNumberFromName("paren");
            _Group_Comma = _Regex.GroupNumberFromName("comma");
        }

        public IEnumerable<Token> Parse (string expression) {
            var current = new Token();
            var matches = _Regex.Matches(expression);

            foreach (var match in matches.Cast<Match>()) {
                current.Text = match.ToString();
                if (match.Groups[_Group_Number].Success)
                    current.Type = TokenType.Number;
                else if (match.Groups[_Group_Identifier].Success)
                    current.Type = TokenType.Identifier;
                else if (match.Groups[_Group_Operator].Success)
                    current.Type = TokenType.Operator;
                else if (match.Groups[_Group_Paren].Success)
                    current.Type = TokenType.Paren;
                else if (match.Groups[_Group_Comma].Success)
                    current.Type = TokenType.Comma;

                yield return current;
            }
        }
    }
}
