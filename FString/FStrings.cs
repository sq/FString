using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Squared.Util.Text;

namespace Squared.FString {
    public interface IFString {
        string Name { get; }
        void EmitValue (ref FStringBuilder output, string id);
        void AppendTo (ref FStringBuilder output, FStringDefinition definition);
        void AppendTo (StringBuilder output);
    }

    public class FStringTable {
        public static FStringTable Default = new FStringTable("empty");

        public readonly string Name;
        private readonly Dictionary<string, FStringDefinition> Entries
            = new Dictionary<string, FStringDefinition>(StringComparer.Ordinal);

        public FStringTable (string name) {
            Name = name;
        }

        public FStringTable (string name, Stream input)
            : this(name) {
            var xrs = new XmlReaderSettings {
                CloseInput = false,
            };
            using (var xr = XmlReader.Create(input, xrs)) {
                // Seek to first file and then read each file
                while (xr.ReadToFollowing("File"))
                    PopulateFromXmlNode(xr);
            }
        }

        private void PopulateFromXmlNode (XmlReader xr) {
            while (xr.Read()) {
                switch (xr.NodeType) {
                    case XmlNodeType.Element:
                        var name = xr.Name;
                        var formatString = xr.ReadElementContentAsString();
                        Add(name, formatString);
                        break;
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.Comment:
                        break;
                    case XmlNodeType.EndElement:
                        if (xr.Name == "File")
                            return;
                        break;
                    default:
                        return;
                }
            }
        }

        public FStringDefinition Add (string name, string formatString) {
            var definition = FStringDefinition.Parse(name, formatString);
            Entries.Add(name, definition);
            return definition;
        }

        public FStringDefinition Get (string name, bool optional = true) {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (!Entries.TryGetValue(name, out var entry))
                return FStringDefinition.Missing(name);
            else
                return entry;
        }
    }

    public class FStringDefinition {
        public readonly string Name;
        public readonly List<(bool emit, string textOrId)> Opcodes =
            new List<(bool emit, string textOrId)>();

        protected FStringDefinition (string name) {
            Name = name;
        }

        private static char GetChar (string s, int index) {
            if ((index < 0) || (index >= s.Length))
                return '\0';
            else
                return s[index];
        }

        public static FStringDefinition Parse (string name, string formatString) {
            var result = new FStringDefinition(name);

            var buildingEmit = false;
            int segmentStart = 0;
            for (int i = 0; i < formatString.Length; i++) {
                switch (formatString[i]) {
                    case '\\':
                        if (buildingEmit)
                            continue;

                        AddSegment(i);
                        i += AddEscape(GetChar(formatString, ++i), i);
                        segmentStart = i + 1;
                        break;

                    case '{':
                        if (buildingEmit)
                            throw new Exception($"Unexpected '{{' inside of expansion value in string {name}");

                        AddSegment(i);

                        if (GetChar(formatString, i + 1) == '{') {
                            i++;
                            result.Opcodes.Add((false, "{"));
                        } else {
                            buildingEmit = true;
                        }
                        segmentStart = i + 1;
                        break;

                    case '}':
                        var wasBuildingEmit = buildingEmit;
                        AddSegment(i);

                        if (GetChar(formatString, i + 1) == '}') {
                            if (wasBuildingEmit)
                                throw new Exception($"Unexpected '}}' inside of expansion value in string {name}");
                            i++;
                            result.Opcodes.Add((false, "}"));
                        }

                        segmentStart = i + 1;
                        break;
                }
            }

            AddSegment(formatString.Length);

            int AddEscape (char ch, int offset) {
                switch (ch) {
                    case 'u':
                        var hex = formatString.Substring(offset + 1, 4);
                        var ch2 = (char)int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                        result.Opcodes.Add((false, new string(ch2, 1)));
                        return 5;
                    default:
                        throw new NotImplementedException($"Escape sequence \\{ch}");
                    case 't':
                        result.Opcodes.Add((false, "\t"));
                        return 1;
                    case 'r':
                        result.Opcodes.Add((false, "\r"));
                        return 1;
                    case 'n':
                        result.Opcodes.Add((false, "\n"));
                        return 1;
                    case '0':
                        result.Opcodes.Add((false, "\0"));
                        return 1;
                }
            }

            void AddSegment (int end) {
                if (end <= segmentStart)
                    return;

                // Create an ImmutableAbstractString with a pre-populated hash code.
                var text = formatString.Substring(segmentStart, end - segmentStart);
                if (buildingEmit)
                    text = string.Intern(text);
                result.Opcodes.Add((buildingEmit, text));
                buildingEmit = false;
            }

            return result;
        }

        public static FStringDefinition Missing (string name) {
            return new FStringDefinition(name) {
                Opcodes = {
                    (false, "MISSING: "),
                    (false, name),
                },
            };
        }

        public void AppendTo<TInstance> (ref TInstance instance, ref FStringBuilder output)
            where TInstance : IFString {
            foreach (var opcode in Opcodes) {
                if (opcode.emit)
                    instance.EmitValue(ref output, opcode.textOrId);
                else
                    output.Append(opcode.textOrId);
            }
        }
    }

    static class EnumNameCache<TEnum> {
        public static readonly Dictionary<TEnum, string> Cache;

        static EnumNameCache () {
            var values = Enum.GetValues(typeof(TEnum));
            var names = Enum.GetNames(typeof(TEnum));
            Cache = new Dictionary<TEnum, string>(values.Length);
            for (int i = 0; i < values.Length; i++) {
                var value = (TEnum)values.GetValue(i);
                var name = names[i];
                Cache[value] = name;
            }
        }
    }

    public struct FStringBuilder : IDisposable {
        // Ensure the scratch builders are pre-allocated and have a good capacity.
        // If they're too small, Append operations can (????) allocate new StringBuilders. I don't know why the BCL does this.
        private const int DefaultStringBuilderSize = 1024 * 8;
        private static readonly ThreadLocal<StringBuilder> ScratchBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder(DefaultStringBuilderSize));
        private static readonly char[] ms_digits = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        public IFormatProvider NumberFormatProvider;

        internal bool OwnsOutput;
        internal StringBuilder Output;
        internal string Result;

        public FStringBuilder (StringBuilder output) {
            NumberFormatProvider = System.Globalization.NumberFormatInfo.CurrentInfo;
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
                    Output = ScratchBuilder.Value ?? new StringBuilder(DefaultStringBuilderSize);
                    ScratchBuilder.Value = null;
                }

                return Output;
            }
        }

        public void Append (char ch) {
            O.Append(ch);
        }

        public void Append (string text) {
            O.Append(text);
        }

        public void Append (StringBuilder stringBuilder) {
            stringBuilder.CopyTo(O);
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
                    O.Append(value.ToString(NumberFormatProvider));
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

        public void Append<T> (T value) {
            var t = typeof(T);
            if (t.IsEnum) {
                if (EnumNameCache<T>.Cache.TryGetValue(value, out var cachedName))
                    O.Append(cachedName);
                else
                    O.Append(value.ToString());
            } else if (t.IsValueType) {
                O.Append(value.ToString());
            } else if (value != null)
                O.Append(value.ToString());
        }

        public void Dispose () {
            if (OwnsOutput && Output != null) {
                Output.Clear();
                ScratchBuilder.Value = Output;
                Output = null;
            }
        }

        public override string ToString () {
            if (Result != null)
                return Result;
            else if (Output == null)
                return null;

            Result = Output.ToString();
            Dispose();
            return Result;
        }

        public static implicit operator FStringBuilder (StringBuilder stringBuilder)
            => new FStringBuilder(stringBuilder);
    }
}
