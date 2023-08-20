using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Squared.FString;

namespace FStringCompiler {
    class Program {
        private static readonly Regex UsingRegex = new Regex(@"^using .+?;", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            FunctionSignatureRegex = new Regex(@"^\(((?<type>(\w|\?)+)\s+(?<argName>\w+)\s*,?\s*)*\)\s*\{", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            StringRegex = new Regex("^(?<name>(\\w|\\.)+)\\s*=\\s*\\$?\"(?<text>.*)\";", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            CasesRegex = new Regex(@"^\s*(?<name>\w+)\s*=\s*switch\s*\((?<selector>.*)\)\s*{", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            StandaloneStringRegex = new Regex("^(?<name>\\w+)\\s*\\(((?<type>(\\w|\\?)+)\\s+(?<argName>\\w+)\\s*,?\\s*)*\\)\\s*=\\s*\\$?\"(?<text>.*)\";", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static void Main (string[] args) {
            if (args.Length < 3) {
                Console.Error.WriteLine("Usage: fstringcompiler [ISO language name] [output directory] [input files...]");
                Environment.Exit(1);
            }

            Directory.CreateDirectory(args[1]);
            Console.WriteLine($"Writing output to {args[1]}");

            var started = DateTime.UtcNow;
            var xws = new XmlWriterSettings {
                Indent = true,
                Encoding = Encoding.UTF8,
                NewLineHandling = NewLineHandling.Entitize,
                CloseOutput = true,
                WriteEndDocumentOnClose = true,
            };
            using (var xmlWriter = XmlWriter.Create(Path.Combine(args[1], $"FStringTable_{args[0]}.xml"), xws)) {
                xmlWriter.WriteStartDocument();
                xmlWriter.WriteStartElement("FStringTable");
                xmlWriter.WriteAttributeString("GeneratedUtc", started.ToString("o"));

                foreach (var inputFile in args.Skip(2).Distinct().OrderBy(s => s)) {
                    Console.WriteLine(inputFile);
                    xmlWriter.WriteStartElement("File");

                    xmlWriter.WriteAttributeString("SourcePath", inputFile);
                    xmlWriter.WriteAttributeString("SourceCreatedUtc", File.GetCreationTimeUtc(inputFile).ToString("o"));
                    xmlWriter.WriteAttributeString("SourceModifiedUtc", File.GetLastWriteTimeUtc(inputFile).ToString("o"));

                    int ln = 0;
                    var inClass = false;
                    var outPath = Path.Combine(args[1], Path.GetFileNameWithoutExtension(inputFile) + ".cs");
                    if (File.Exists(outPath) && File.GetLastWriteTimeUtc(outPath) > File.GetLastWriteTimeUtc(inputFile))
                        Console.WriteLine($"Skipping, output is newer: {outPath}");

                    StringGroup group = null;
                    StringSwitch swtch = null;

                    using (var input = new StreamReader(inputFile))
                    using (var output = new StreamWriter(outPath, false, Encoding.UTF8)) {
                        output.WriteLine("using System;");
                        output.WriteLine("using System.Text;");
                        output.WriteLine("using Squared.FString;");
                        output.WriteLine();

                        while (!input.EndOfStream) {
                            var line = input.ReadLine();
                            ln++;
                            if (string.IsNullOrWhiteSpace(line)) {
                                output.WriteLine();
                                continue;
                            }

                            line = line.Trim();
                            if (line.StartsWith("//")) {
                                output.WriteLine(line);
                                continue;
                            }

                            try {
                                if (group == null) {
                                    if (UsingRegex.IsMatch(line)) {
                                        if (inClass) {
                                            Console.Error.WriteLine($"error: {inputFile}({ln}): Cannot add new using statements after defining a string");
                                            Environment.Exit(5);
                                        } else {
                                            output.WriteLine(line);
                                        }
                                    } else {
                                        var fsm = FunctionSignatureRegex.Match(line);
                                        var ssm = StandaloneStringRegex.Match(line);
                                        if (fsm.Success) {
                                            group = new StringGroup(fsm);
                                        } else if (ssm.Success) {
                                            if (!inClass) {
                                                output.WriteLine("public static partial class FStrings {");
                                                inClass = true;
                                            }

                                            var tempGroup = new StringGroup(ssm);
                                            var fs = new FString(tempGroup, ssm, swtch);
                                            fs.Write(output, xmlWriter);
                                        } else {
                                            Console.Error.WriteLine($"error: {inputFile}({ln}): Unrecognized line: {line}");
                                            Environment.Exit(2);
                                        }
                                    }
                                } else {
                                    var sm = StringRegex.Match(line);
                                    var cm = CasesRegex.Match(line);
                                    if (sm.Success) {
                                        if (!inClass) {
                                            output.WriteLine("public static partial class FStrings {");
                                            inClass = true;
                                        }

                                        var fs = new FString(group, sm, swtch);
                                        if (swtch == null)
                                            fs.Write(output, xmlWriter);
                                    } else if (cm.Success) {
                                        if (swtch != null){
                                            Console.Error.WriteLine($"error: {inputFile}({ln}): Nesting switches is disallowed");
                                            Environment.Exit(6);
                                        }

                                        swtch = new StringSwitch(group, cm);
                                    } else if (line == "}") {
                                        // End of group or cases block
                                        if (swtch != null) {
                                            if (!inClass) {
                                                output.WriteLine("public static partial class FStrings {");
                                                inClass = true;
                                            }

                                            swtch.Write(output, xmlWriter);
                                            swtch = null;
                                        } else
                                            group = null;
                                    } else {
                                        Console.Error.WriteLine($"error: {inputFile}({ln}): Unrecognized line: {line}");
                                        Environment.Exit(3);
                                    }
                                }
                            } catch (Exception exc) {
                                Console.Error.WriteLine($"error: {inputFile}({ln}): {exc}");
                                Environment.Exit(4);
                            }
                        }

                        if (inClass)
                            output.WriteLine("}");
                    }

                    xmlWriter.WriteEndElement();
                }

                xmlWriter.WriteEndElement();
                xmlWriter.WriteEndDocument();
            }

            Console.WriteLine("Done");
        }
    }

    public struct Argument {
        public string Type, Name;
    }

    public class StringGroup {
        public readonly List<Argument> Arguments = new List<Argument>();

        public StringGroup (Match m) {
            var types = m.Groups["type"].Captures.Cast<Capture>().Select(c => c.Value).ToArray();
            var names = m.Groups["argName"].Captures.Cast<Capture>().Select(c => c.Value).ToArray();
            for (int i = 0; i < types.Length; i++)
                Arguments.Add(new Argument { Type = types[i], Name = names[i] });
        }
    }

    public class StringSwitch {
        public StringGroup Group;
        public string Name, Selector;
        public Dictionary<string, FString> Cases = new Dictionary<string, FString>();

        public StringSwitch (StringGroup group, Match m) {
            Group = group;
            Name = m.Groups["name"].Value;
            Selector = m.Groups["selector"].Value;
        }

        internal void Write (StreamWriter output, XmlWriter xmlWriter) {
            foreach (var value in Cases.Values) {
                // Only generate all the code the first time.
                value.Write(output, xmlWriter);
                output = null;
            }
        }

        internal void WriteNameSelector (StreamWriter output) {
            output.WriteLine("\t\tpublic string StringTableKey { get {");
            output.WriteLine($"\t\t\tswitch ({Selector}) {{");
            foreach (var kvp in Cases) {
                if (kvp.Key == "default")
                    output.WriteLine($"\t\t\t\tdefault: return \"{kvp.Value.StringTableKey}\";");
                else
                    output.WriteLine($"\t\t\t\tcase {kvp.Key}: return \"{kvp.Value.StringTableKey}\";");
            }
            if (!Cases.ContainsKey("default")) {
                var printableSelector = Selector.Replace("\"", "\\\")}");
                output.WriteLine($"\t\t\t\tdefault: throw new ArgumentOutOfRangeException(\"{printableSelector}\");");
            }
            output.WriteLine("\t\t\t}");
            output.WriteLine("\t\t} }");
        }
    }

    public class FString {
        public StringGroup Group;
        public StringSwitch Switch;
        public FStringDefinition Definition;
        public string Name, StringTableKey, FormatString;

        public FString (StringGroup group, Match m, StringSwitch swtch) {
            Group = group;
            Switch = swtch;
            Name = m.Groups["name"].Value;
            if (swtch != null)
                swtch.Cases.Add(Name, this);
            StringTableKey = (swtch != null) ? $"{swtch.Name}_{HashUtil.GetShortHash(Name)}" : Name;
            FormatString = m.Groups["text"].Value;
            Definition = FStringDefinition.Parse(Name, FormatString);
            if (Definition.Opcodes.Any(o => o.emit && o.textOrId == "this"))
                throw new Exception("{this} is invalid in FStrings");
        }

        public void Write (StreamWriter output, XmlWriter xmlWriter) {
            if (Switch != null)
                xmlWriter.WriteComment($" {Switch.Selector} = {Name} ");
            xmlWriter.WriteStartElement(StringTableKey);
            xmlWriter.WriteString(FormatString);
            xmlWriter.WriteEndElement();

            if (output == null)
                return;

            var structName = Switch?.Name ?? Name;

            if (Group.Arguments.Count == 0) {
                output.WriteLine($"\tpublic static string {Name} () => {Name}(FStringTable.Default);");
                output.WriteLine($"\tpublic static string {Name} (FStringTable table) => table.Get(\"{StringTableKey}\").GetStringLiteral();");
            } else {
                output.WriteLine($"\tpublic struct {structName} : IFString {{");

                // Generate the name selector (for a switch) or name constant property
                if (Switch != null)
                    Switch.WriteNameSelector(output);
                else
                    output.WriteLine($"\t\tpublic string StringTableKey => \"{StringTableKey}\";");

                // Generate the fields
                foreach (var arg in Group.Arguments)
                    output.WriteLine($"\t\tpublic {arg.Type} {arg.Name};");
                output.WriteLine();

                // Generate the constructor
                output.Write($"\t\tpublic {structName} (");
                var isFirstArg = true;
                foreach (var arg in Group.Arguments) {
                    if (!isFirstArg)
                        output.Write(", ");
                    output.Write($"{arg.Type} @{arg.Name}");
                    isFirstArg = false;
                }
                output.WriteLine(") {");
                foreach (var arg in Group.Arguments)
                    output.WriteLine($"\t\t\tthis.{arg.Name} = @{arg.Name};");
                output.WriteLine("\t\t}");
                output.WriteLine();

                // Generate the emitter that looks up a value by string key. This makes everything work
                output.WriteLine("\t\tpublic void EmitValue (ref FStringBuilder output, string id) {");
                output.WriteLine("\t\t\tswitch(id) {");
                IEnumerable<string> keys;
                if (Switch != null) {
                    keys = new string[0];
                    foreach (var value in Switch.Cases.Values)
                        keys = keys.Concat(GetIds(value.Definition));
                } else {
                    keys = GetIds(Definition);
                }
                foreach (var key in keys.Distinct()) {
                    output.WriteLine($"\t\t\t\tcase \"{key.Replace("\"", "\\\"")}\":");
                    // TODO: Detect fstrings and try to do a fast-path, no-allocation append
                    // We need a way to store the fstring without boxing first though...
                    // if (Group.Arguments.Any(a => (a.Name == key) && (a.Type == "IFString"))
                    output.WriteLine($"\t\t\t\t\toutput.Append({key});");
                    output.WriteLine($"\t\t\t\t\treturn;");
                }
                output.WriteLine("\t\t\t\tdefault:");
                output.WriteLine("\t\t\t\t\tthrow new ArgumentOutOfRangeException(nameof(id));");
                output.WriteLine("\t\t\t}");
                output.WriteLine("\t\t}");

                // Generate the append overloads that make things usable dynamically
                output.WriteLine("\t\tpublic void AppendTo (ref FStringBuilder output, FStringTable table) => table.Get(StringTableKey).AppendTo(ref this, ref output);");
                output.WriteLine("\t\tpublic void AppendTo (StringBuilder output) => AppendTo(output, FStringTable.Default);");
                output.WriteLine("\t\tpublic void AppendTo (StringBuilder output, FStringTable table) {");
                output.WriteLine("\t\t\tvar fsb = new FStringBuilder(output);");
                output.WriteLine("\t\t\ttable.Get(StringTableKey).AppendTo(ref this, ref fsb);");
                output.WriteLine("\t\t}");

                // Generate ToString for simple uses
                output.WriteLine("\t\tpublic override string ToString () {");
                output.WriteLine("\t\t\tvar output = new FStringBuilder();");
                output.WriteLine("\t\t\tAppendTo(ref output, FStringTable.Default);");
                output.WriteLine("\t\t\treturn output.ToString();");
                output.WriteLine("\t\t}");

                output.WriteLine("\t}");
            }
            output.WriteLine();
        }

        private static IEnumerable<string> GetIds (FStringDefinition definition) =>
            definition.Opcodes.Where(o => o.emit).Select(o => o.textOrId);
    }

    public static class HashUtil {
        static ThreadLocal<SHA256> Hasher = new ThreadLocal<SHA256>(() => SHA256.Create());
        static ThreadLocal<StringBuilder> StringBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder());

        public static string GetShortHash (string text) {
            return GetShortHash(Encoding.UTF8.GetBytes(text));
        }

        public static string GetShortHash (byte[] bytes) {
            return GetHashString(bytes, 0, bytes.Length);
        }

        public static string GetHashString (byte[] bytes, int offset, int count, int hashLength = 8) {
            var hash = Hasher.Value;
            var sb = StringBuilder.Value;
            sb.Clear();
            var hashBytes = hash.ComputeHash(bytes, offset, count);
            hashLength = Math.Min(hashBytes.Length, hashLength);
            for (int i = 0; i < hashLength; i++) {
                var b = hashBytes[i];
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
