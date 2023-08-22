using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.FString {
    public interface IFString {
        string StringTableKey { get; }
        void EmitValue (ref FStringBuilder output, string id);
        void AppendTo (ref FStringBuilder output);
        void AppendTo (StringBuilder output, FStringTable table);
        void AppendTo (StringBuilder output);
    }
}
