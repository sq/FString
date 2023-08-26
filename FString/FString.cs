using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util.Text;

namespace Squared.FString {
    public interface IFString {
        string StringTableKey { get; }
        void EmitValue (ref FStringBuilder output, string id);
        void AppendTo (ref FStringBuilder output);
        void AppendTo (StringBuilder output, FStringTable table);
        void AppendTo (StringBuilder output);
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

        void IFString.AppendTo (StringBuilder output, FStringTable table) {
            output.Append(table.Get(StringTableKey).GetStringLiteral());
        }

        void IFString.AppendTo (StringBuilder output) {
            output.Append(ToString());
        }

        public static implicit operator string (FStringLiteral fsl) => fsl.ToString();
    }
}
