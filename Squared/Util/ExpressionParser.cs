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
            Field_runtext = Field_ref.FieldType.GetField("runtext", BindingFlags.Instance | BindingFlags.NonPublic);
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