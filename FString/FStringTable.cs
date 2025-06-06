﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Squared.Util.Text;

namespace Squared.FString {
    public delegate void OnMissingString (FStringTable table, string key);

    public class FStringTable {
        public static FStringTable Default = new FStringTable("empty");

        /// <summary>
        /// If a string is not found in this table, it will be searched for in FallbackTable
        /// </summary>
        public FStringTable FallbackTable;

        public event OnMissingString MissingString;

        public readonly string Name;
        private readonly Dictionary<string, FStringDefinition> Entries
            = new Dictionary<string, FStringDefinition>(StringComparer.Ordinal);

        public string Path { get; internal set; }

        public FStringTable (string name) {
            Name = name;
        }

        public FStringTable (string name, Stream input)
            : this(name) {
            PopulateFromXmlStream(input, false);
        }

        public void Clear () => Entries.Clear();
        public int Count => Entries.Count;

        public void PopulateFromXmlStream (Stream input, bool allowOverwrite) {
            var xrs = new XmlReaderSettings {
                CloseInput = false,
            };
            using (var xr = XmlReader.Create(input, xrs)) {
                // Seek to first file and then read each file
                while (xr.ReadToFollowing("File")) {
                    if (xr.IsEmptyElement)
                        continue;

                    try {
                        PopulateFromXmlNode(xr, allowOverwrite);
                    } catch (Exception exc) {
                        var fs = input as FileStream;
                        throw new Exception($"Parse error in file {fs?.Name ?? "unknown"}: {exc.Message}", exc);
                    }
                }
            }
        }

        private void PopulateFromXmlNode (XmlReader xr, bool allowOverwrite) {
            while (xr.Read()) {
                switch (xr.NodeType) {
                    case XmlNodeType.Element:
                        var isLiteral = xr.Name == "Literal";
                        if ((xr.Name != "String") && (xr.Name != "Literal"))
                            throw new Exception($"Unexpected element '{xr.Name}', expected String or Literal");
                        var name = xr.GetAttribute("Name");
                        var formatString = xr.ReadElementContentAsString();
                        if (isLiteral)
                            AddRaw(name, formatString, allowOverwrite);
                        else
                            Add(name, formatString, allowOverwrite);
                        break;
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.Comment:
                        break;
                    case XmlNodeType.EndElement:
                        if ((xr.Name == "File") || (xr.Name == "FStringTable"))
                            return;
                        else if (xr.Name != "String")
                            throw new Exception($"Unexpected end of element '{xr.Name}', expected String");
                        break;
                    default:
                        return;
                }
            }
        }

        public FStringDefinition AddRaw (string name, string text, bool allowOverwrite = false) {
            var definition = FStringDefinition.Parse(this, name, text, true);
            if (allowOverwrite)
                Entries[name] = definition;
            else
                Entries.Add(name, definition);
            return definition;
        }

        public FStringDefinition Add (string name, string formatString, bool allowOverwrite = false) {
            var definition = FStringDefinition.Parse(this, name, formatString, false);
            if (allowOverwrite)
                Entries[name] = definition;
            else
                Entries.Add(name, definition);
            return definition;
        }

        // FIXME: Flow through caller information so it can be provided to the MissingString event handler
        public FStringDefinition Get (string name, bool optional = true) {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (!Entries.TryGetValue(name, out var entry)) {
                if (MissingString != null)
                    MissingString(this, name);

                if (!optional)
                    throw new KeyNotFoundException(name);
                else
                    return FallbackTable?.Get(name, optional) ?? FStringDefinition.Missing(this, name);
            } else
                return entry;
        }
    }

    public class FStringDefinition {
        private static readonly Dictionary<string, FStringDefinition> MissingStringCache = new Dictionary<string, FStringDefinition>();

        public readonly FStringTable Table;
        public readonly string Name;
        public readonly bool IsMissing;
        public readonly List<(bool emit, string textOrId)> Opcodes =
            new List<(bool emit, string textOrId)>();

        protected FStringDefinition (FStringTable table, string name, bool isMissing) {
            Table = table;
            Name = name;
            IsMissing = isMissing;
        }

        private static char GetChar (string s, int index) {
            if ((index < 0) || (index >= s.Length))
                return '\0';
            else
                return s[index];
        }

        public static FStringDefinition Raw (FStringTable table, string name, string text) {
            var result = new FStringDefinition(table, name, false);
            result.Opcodes.Add((false, text));
            return result;
        }

        public static FStringDefinition Parse (FStringTable table, string name, string formatString, bool escapesOnly) {
            var result = new FStringDefinition(table, name, false);

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
                        if (!escapesOnly) {
                            if (buildingEmit)
                                throw new Exception($"Unexpected '{{' inside of expansion value in string {name}");

                            AddSegment(i);

                            if (GetChar(formatString, i + 1) == '{') {
                                i++;
                                AddString("{");
                            } else {
                                buildingEmit = true;
                            }
                            segmentStart = i + 1;
                        }
                        break;

                    case '}':
                        if (!escapesOnly) {
                            var wasBuildingEmit = buildingEmit;
                            AddSegment(i);

                            if (GetChar(formatString, i + 1) == '}') {
                                if (wasBuildingEmit)
                                    throw new Exception($"Unexpected '}}' inside of expansion value in string {name}");
                                i++;
                                AddString("}");
                            }

                            segmentStart = i + 1;
                        }
                        break;
                }
            }

            AddSegment(formatString.Length);

            void AddString (string text) {
                if (result.Opcodes.Count > 0) {
                    var previousOpcode = result.Opcodes[result.Opcodes.Count - 1];
                    // Merge the two strings. This will occur for escape sequences
                    if (!previousOpcode.emit) {
                        text = previousOpcode.textOrId + text;
                        result.Opcodes.RemoveAt(result.Opcodes.Count - 1);
                    }
                }

                result.Opcodes.Add((false, text));
            }

            int AddEscape (char ch, int offset) {
                switch (ch) {
                    case 'u':
                        var hex = formatString.Substring(offset + 1, 4);
                        var ch2 = (char)int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                        AddString(new string(ch2, 1));
                        return 5;
                    default:
                        throw new NotImplementedException($"Escape sequence \\{ch}");
                    case 't':
                        AddString("\t");
                        return 1;
                    case 'r':
                        AddString("\r");
                        return 1;
                    case 'n':
                        AddString("\n");
                        return 1;
                    case '0':
                        AddString("\0");
                        return 1;
                }
            }

            void AddSegment (int end) {
                if (end <= segmentStart)
                    return;

                var text = formatString.Substring(segmentStart, end - segmentStart);
                if (buildingEmit) {
                    text = string.Intern(text);
                    result.Opcodes.Add((buildingEmit, text));
                } else
                    AddString(text);
                buildingEmit = false;
            }

            return result;
        }

        public static FStringDefinition Missing (FStringTable table, string name) {
            lock (MissingStringCache) {
                if (!MissingStringCache.TryGetValue(name, out var result))
                    MissingStringCache[name] = result = new FStringDefinition(table, name, true);
                return result;
            }
        }

        public override string ToString () {
            return $"String '{Name}'";
        }

        public bool IsLiteral => (Opcodes.Count <= 1) && (Opcodes.FirstOrDefault().emit != true);

        public string GetStringLiteral () {
            if (IsMissing)
                return $"<MISSING: {Name}>";
            else if (Opcodes.Count == 0)
                return "";
            else if ((Opcodes.Count != 1) || Opcodes[0].emit)
                throw new InvalidOperationException($"{Name} is not a literal");
            else
                return Opcodes[0].textOrId;
        }

        public void AppendTo<TInstance> (ref TInstance instance, ref FStringBuilder output)
            where TInstance : IFString 
        {
            if (IsMissing) {
                output.Append("<MISSING: ");
                output.Append(Name);
                output.Append('>');
            } else {
                foreach (var opcode in Opcodes) {
                    if (opcode.emit)
                        instance.EmitValue(ref output, opcode.textOrId);
                    else
                        output.Append(opcode.textOrId);
                }
            }
        }
    }
}
