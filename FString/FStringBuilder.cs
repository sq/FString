using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.FString {
    // HACK: Prevent the MSVC debugger from obliterating our state in a terrible way
    [DebuggerDisplay("{DebuggerDisplayText}")]
    public struct FStringBuilder : IDisposable {
        // Ensure the scratch builders are pre-allocated and have a good capacity.
        // If they're too small, Append operations can (????) allocate new StringBuilders. I don't know why the BCL does this.
        private const int DefaultStringBuilderSize = 1024 * 8;
        private static readonly ThreadLocal<StringBuilder> ScratchBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder(DefaultStringBuilderSize));
        private static readonly char[] ms_digits = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        public IFormatProvider FormatProvider;
        public FStringTable DefaultStringTable;
        
        internal bool OwnsOutput;
        internal StringBuilder Output;
        // HACK: Optimization for cases where all that happens is a single string gets appended to the builder
        internal string Prefix;
        internal string Result;

        internal string DebuggerDisplayText => $"FStringBuilder(OwnsOutput = {OwnsOutput}, Output.Length={Output?.Length}, Result='{Result}')";

        public FStringBuilder (StringBuilder output, FStringTable stringTable = null) {
            FormatProvider = System.Globalization.CultureInfo.CurrentCulture;
            DefaultStringTable = stringTable ?? FStringTable.Default;
            OwnsOutput = false;
            Output = output;
            Result = null;
            Prefix = null;
        }

        public FStringDefinition GetDefinition (string name, bool optional = true) =>
            (DefaultStringTable ?? FStringTable.Default).Get(name, optional);

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private StringBuilder O {
            get {
                if (Result != null)
                    throw new InvalidOperationException("String already built");
                else if (Output == null) {
                    OwnsOutput = true;
                    Output = ScratchBuilder.Value ?? new StringBuilder(DefaultStringBuilderSize);
                    ScratchBuilder.Value = null;
                    if (Prefix != null)
                        Output.Append(Prefix);
                    Prefix = null;
                }

                return Output;
            }
        }

        public void Append (char ch) {
            O.Append(ch);
        }

        public void Append (string text) {
            if ((Output == null) && (Prefix == null) && (Result == null))
                Prefix = text;
            else
                O.Append(text);
        }

        public void Append (StringBuilder stringBuilder) {
            stringBuilder.CopyTo(O);
        }

        public void Append (IFormattable formattable) {
            if (formattable == null)
                return;

            // If the specialized ToString returns null that indicates we should use the default one.
            var str = formattable.ToString(null, FormatProvider) ?? formattable.ToString();
            Append(str);
        }

        public void Append (IFString fstring) {
            if (fstring == null)
                return;

            // FIXME
            fstring.AppendTo(ref this);
            // fstring.AppendTo(O, DefaultStringTable ?? FStringTable.Default);
        }

        public void Append (uint? value) {
            if (value.HasValue)
                Append(value.Value);
            else
                Append("null");
        }
        public void Append (int? value) {
            if (value.HasValue)
                Append(value.Value);
            else
                Append("null");
        }
        public void Append (float? value) {
            if (value.HasValue)
                Append(value.Value);
            else
                Append("null");
        }
        public void Append (double? value) {
            if (value.HasValue)
                Append(value.Value);
            else
                Append("null");
        }

        public void Append (uint value) => Append((ulong)value);
        public void Append (int value) => Append((long)value);
        public void Append (float value) => Append((double)value);

        public void Append (double value) {
            unchecked {
                var truncated = (long)value;
                if (truncated == value)
                    Append(truncated);
                else
                    // FIXME: non-integral values without allocating (the default double append allocates :()
                    // This value.ToString() is still much more efficient than O.Append(value), oddly enough.
                    // The tradeoffs may be different in .NET 7, I haven't checked
                    Append(value.ToString(FormatProvider));
            }
        }

        public void Append (ulong value) {
            // Calculate length of integer when written out
            const uint base_val = 10;
            ulong length = 0;
            ulong length_calc = value;

            do {
                length_calc /= base_val;
                length++;
            } while (length_calc > 0);

            // Pad out space for writing.
            var string_builder = O.Append(' ', (int)length);
            int strpos = string_builder.Length;

            while (length > 0) {
                strpos--;
                if ((strpos < 0) || (strpos >= string_builder.Length))
                    throw new InvalidDataException();

                string_builder[strpos] = ms_digits[value % base_val];

                value /= base_val;
                length--;
            }
        }

        public void Append (long value) {
            if (value < 0) {
                O.Append('-');
                ulong uint_val = ulong.MaxValue - ((ulong)value) + 1; //< This is to deal with Int32.MinValue
                Append(uint_val);
            } else
                Append((ulong)value);
        }

        public void Append (AbstractString text) {
            text.CopyTo(O);
        }

        public void Append (ImmutableAbstractString text) {
            text.Value.CopyTo(O);
        }

        public void AppendFString<T> (T value)
            where T : struct, IFString {
            value.AppendTo(ref this);
        }
        
        public void Append<T> (T value) {
            var t = typeof(T);
            if (t.IsEnum) {
                if (EnumNameCache<T>.Cache.TryGetValue(value, out var cachedName))
                    Append(cachedName);
                else
                    Append(value.ToString());
            } else if (value is IFString ifs) {
                ifs.AppendTo(ref this);
            } else if (t.IsValueType) {
                // We do a valuetype check to avoid the boxing operation necessary to do 'value != null' below
                Append(value.ToString());
            } else if (value != null) {
                Append(value.ToString());
            }
        }

        public void Dispose () {
            if (OwnsOutput && Output != null) {
                Output.Clear();
                ScratchBuilder.Value = Output;
            }
            Prefix = null;
            Output = null;
        }

        public override string ToString () {
            if (Result != null)
                return Result;
            else if (Prefix != null) {
                Result = Prefix;
                Prefix = null;
            } else if (Output == null)
                return null;
            else
                Result = Output.ToString();

            Dispose();
            return Result;
        }

        public static implicit operator FStringBuilder (StringBuilder stringBuilder)
            => new FStringBuilder(stringBuilder);
    }
}
