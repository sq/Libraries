using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;

namespace Squared.Util.RegexExtensions {
    public static class RegexLeak {
        private static FieldInfo Field_runtext;
        private static FieldInfo Field_runnerref;
        private static FieldInfo Field_ref;

        static RegexLeak () {
            Field_runnerref = typeof(Regex).GetField("runnerref", BindingFlags.Instance | BindingFlags.NonPublic);
            var xref = typeof(Regex).Assembly.GetType("System.Text.RegularExpressions.ExclusiveReference", true);
            Field_ref = xref.GetField("_ref", BindingFlags.Instance | BindingFlags.NonPublic);
            Field_runtext = typeof(RegexRunner).GetField("runtext", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        /// <summary>
        /// Works around the string reference leak in the implementation of System.Text.RegularExpressions.Regex.
        /// </summary>
        /// <param name="regex">The regex you want to ensure has no reference to your strings.</param>
        public static void Fix (Regex regex) {
            var runnerref = Field_runnerref.GetValue(regex);
            var runner = Field_ref.GetValue(runnerref);
            Field_runtext.SetValue(runner, null);
        }
    }

    public static class Extensions {
        public static bool TryMatch (this Regex regex, string input, out Match match) {
            match = regex.Match(input);
            return (match != null) && (match.Success);
        }

        public static bool TryMatch (
            this Regex regex, string input,
            int start, out Match match
        ) {
            match = regex.Match(input, start);
            return (match != null) && (match.Success);
        }

        public static bool TryMatch (
            this Regex regex, string input, 
            int start, int length, 
            out Match match
        ) {
            match = regex.Match(input, start, length);
            return (match != null) && (match.Success);
        }
    }
}

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
