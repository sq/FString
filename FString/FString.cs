using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Squared.Util.Text;

namespace Squared.FString {
    public interface IFString {
        string StringTableKey { get; }
        void EmitValue (ref FStringBuilder output, string id);
        void AppendTo (ref FStringBuilder output);
    }

    public static class FStringExtensions {
        public static void AppendTo<T> (this T str, StringBuilder output)
            where T : struct, IFString
        { 
            var fsb = new FStringBuilder(output, null);
            str.AppendTo(ref fsb);
        }

        public static void AppendTo<T> (this T str, StringBuilder output, FStringTable table)
            where T : struct, IFString 
        {
            var fsb = new FStringBuilder(output, table);
            str.AppendTo(ref fsb);
        }
    }

    public struct FStringLiteral : IFString {
        public readonly FStringTable Table;
        private readonly string Key;

        public string StringTableKey => Key;

        public FStringLiteral (FStringDefinition definition) {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            Table = definition.Table;
            Key = definition.Name;
        }

        public bool Equals (FStringLiteral rhs) => (Table == rhs.Table) && (Key == rhs.Key);

        public override bool Equals (object obj) {
            if (obj is string s)
                return s.Equals(ToString());
            else if (obj is FStringLiteral fsl)
                return Equals(fsl);
            else
                return false;
        }

        public override int GetHashCode () => Key?.GetHashCode() ?? 0;

        public override string ToString () {
            return Table?.Get(Key)?.GetStringLiteral();
        }

        void IFString.EmitValue (ref FStringBuilder output, string id) {
            throw new InvalidOperationException();
        }

        void IFString.AppendTo (ref FStringBuilder output) {
            output.Append(ToString());
        }

        public void AppendTo (StringBuilder output, FStringTable table) {
            output.Append(table.Get(StringTableKey).GetStringLiteral());
        }

        public void AppendTo (StringBuilder output) {
            output.Append(ToString());
        }

        public static implicit operator string (FStringLiteral fsl) => fsl.ToString();
    }
}
