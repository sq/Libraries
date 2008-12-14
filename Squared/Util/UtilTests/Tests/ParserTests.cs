using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util.Expressions;

namespace Squared.Util {
    [TestFixture]
    public class ParserTests {
        public Parser Parser = new Parser();

        [Test]
        public void TestBasicExpression () {
            var expression = "a + b / c * d";

            var expected = new Token[] {
                new Token { Type = TokenType.Identifier, Text = "a" },
                new Token { Type = TokenType.Operator, Text = "+" },
                new Token { Type = TokenType.Identifier, Text = "b" },
                new Token { Type = TokenType.Operator, Text = "/" },
                new Token { Type = TokenType.Identifier, Text = "c" },
                new Token { Type = TokenType.Operator, Text = "*" },
                new Token { Type = TokenType.Identifier, Text = "d" }
            };

            var tokens = Parser.Parse(expression).ToArray();
            Assert.AreEqual(expected, tokens);
        }

        [Test]
        public void TestNumbers () {
            var expression = "1 + 2.503 + -37 - -15.6";

            var expected = new Token[] {
                new Token { Type = TokenType.Number, Text = "1" },
                new Token { Type = TokenType.Operator, Text = "+" },
                new Token { Type = TokenType.Number, Text = "2.503" },
                new Token { Type = TokenType.Operator, Text = "+" },
                new Token { Type = TokenType.Number, Text = "-37" },
                new Token { Type = TokenType.Operator, Text = "-" },
                new Token { Type = TokenType.Number, Text = "-15.6" }
            };

            var tokens = Parser.Parse(expression).ToArray();
            Assert.AreEqual(expected, tokens);
        }

        [Test]
        public void TestComplexExpression () {
            var expression = "(obj.field + (4 * 2)) > 0.5";

            var expected = new Token[] {
                new Token { Type = TokenType.Paren, Text = "(" },
                new Token { Type = TokenType.Identifier, Text = "obj" },
                new Token { Type = TokenType.Operator, Text = "." },
                new Token { Type = TokenType.Identifier, Text = "field" },
                new Token { Type = TokenType.Operator, Text = "+" },
                new Token { Type = TokenType.Paren, Text = "(" },
                new Token { Type = TokenType.Number, Text = "4" },
                new Token { Type = TokenType.Operator, Text = "*" },
                new Token { Type = TokenType.Number, Text = "2" },
                new Token { Type = TokenType.Paren, Text = ")" },
                new Token { Type = TokenType.Paren, Text = ")" },
                new Token { Type = TokenType.Operator, Text = ">" },
                new Token { Type = TokenType.Number, Text = "0.5" }
            };

            var tokens = Parser.Parse(expression).ToArray();
            Assert.AreEqual(expected, tokens);
        }

        [Test]
        public void TestMultiCharacterOperators () {
            var expression = "a >= b != c";

            var expected = new Token[] {
                new Token { Type = TokenType.Identifier, Text = "a" },
                new Token { Type = TokenType.Operator, Text = ">=" },
                new Token { Type = TokenType.Identifier, Text = "b" },
                new Token { Type = TokenType.Operator, Text = "!=" },
                new Token { Type = TokenType.Identifier, Text = "c" }
            };

            var tokens = Parser.Parse(expression).ToArray();
            Assert.AreEqual(expected, tokens);
        }
    }
}
