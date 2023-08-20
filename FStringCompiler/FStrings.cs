using System;
using System.Collections.Generic;
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
            = new Dictionary<string, FStringDefinition>(StringComparer.OrdinalIgnoreCase);

        public FStringTable (string name) {
            Name = name;
        }

        public FStringTable (string name, Stream input) 
            : this (name) {
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

            void AddSegment (int end) {
                if (end <= segmentStart)
                    return;

                // Create an ImmutableAbstractString with a pre-populated hash code.
                var text = formatString.Substring(segmentStart, end - segmentStart);
                if (buildingEmit)
                    text = string.Intern(text);
                result.Opcodes.Add((buildingEmit, text));
                buildingEmit = false;
                segmentStart = end;
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
            where TInstance : IFString 
        {
            foreach (var opcode in Opcodes) {
                if (opcode.emit)
                    instance.EmitValue(ref output, opcode.textOrId);
                else
                    output.Append(opcode.textOrId);
            }
        }
    }

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

        public void Append (char ch) {
            O.Append(ch);
        }

        public void Append (string text) {
            O.Append(text);
        }

        public void Append (AbstractString text) {
            text.CopyTo(O);
        }

        public void Append<T> (T value) {
            var t = typeof(T);
            if (t.IsEnum) {
                // FIXME
                O.Append(value);
            } else if (t.IsValueType) {
                // FIXME: Numeric types
                O.Append(value);
            } else {
                O.Append(value);
            }
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
