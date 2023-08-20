using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.FString {
    public struct FStringBuilder {
        private static readonly ThreadLocal<StringBuilder> ScratchBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder());

        internal bool OwnsOutput;
        internal StringBuilder Output;
        internal string Result;

        public FStringBuilder (StringBuilder output) {
            OwnsOutput = false;
            Output = output;
            Result = null;
        }

        private StringBuilder O {
            get {
                if (Result != null)
                    throw new InvalidOperationException("String already built");
                else if (Output == null) {
                    OwnsOutput = true;
                    Output = ScratchBuilder.Value ?? new StringBuilder();
                    ScratchBuilder.Value = null;
                }

                return Output;
            }
        }

        public void Append (string text) {
            O.Append(text);
        }

        public void Append (object value) {
            O.Append(value);
        }

        public override string ToString () {
            if (Result != null)
                return Result;
            else if (Output == null)
                return null;

            Result = Output.ToString();
            if (OwnsOutput) {
                Output.Clear();
                ScratchBuilder.Value = Output;
            }
            Output = null;
            return Result;
        }

        public static implicit operator FStringBuilder (StringBuilder stringBuilder)
            => new FStringBuilder(stringBuilder);
    }
}
