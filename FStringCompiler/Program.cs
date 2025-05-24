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
        // HACK
        public static HashSet<string> KnownFStringKeys = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Regex UsingRegex = new Regex(@"^using .+?;", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            FunctionSignatureRegex = new Regex(@"^\(((?<type>(\w|\?)+)\s+(?<argName>\w+)\s*,?\s*)*\)\s*\{", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            // HACK: Including " in the name regex for switch cases
            StringRegex = new Regex("^(?<name>(\\w|[\"\\.\\-])+)\\s*=\\s*(null|(?<buck>\\$)?\"(?<text>.*)\");", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            CasesRegex = new Regex(@"^\s*(?<name>\w+)\s*=\s*switch\s*\((?<selector>.*)\)\s*{", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            StandaloneStringRegex = new Regex("^(?<name>\\w+)\\s*\\(((?<type>(\\w|\\?)+)\\s+(?<argName>\\w+)\\s*,?\\s*)*\\)\\s*=\\s*(null|(?<buck>\\$)?\"(?<text>.*)\");", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static void Main (string[] args) {
            if (args.Length < 3) {
                Console.Error.WriteLine("Usage: fstringcompiler [language name] [output directory] [input files...]");
                Environment.Exit(1);
            }

            Directory.CreateDirectory(args[1]);
            Console.WriteLine($"Writing output to {args[1]}");

            var commentBuffer = new StringBuilder();
            using (var xmlWriter = new FStringTableWriter(Path.Combine(args[1], $"FStringTable_{args[0]}.xml"), args[0])) {
                foreach (var inputFile in args.Skip(2).Distinct().OrderBy(s => s)) {
                    Console.WriteLine(inputFile);
                    xmlWriter.StartFile(inputFile, File.GetLastWriteTimeUtc(inputFile));

                    int ln = 0;
                    var inClass = false;
                    var outPath = Path.Combine(args[1], Path.GetFileNameWithoutExtension(inputFile) + ".cs");
                    if (File.Exists(outPath) && File.GetLastWriteTimeUtc(outPath) > File.GetLastWriteTimeUtc(inputFile))
                        Console.WriteLine($"Skipping, output is newer: {outPath}");

                    StringGroup group = null;
                    StringSwitch swtch = null;

                    using (var input = new StreamReader(inputFile))
                    using (var output = new StreamWriter(outPath, false, Encoding.UTF8)) {
                        commentBuffer.Clear();

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
                                commentBuffer.AppendLine(line);
                                output.WriteLine(line);
                                continue;
                            } else if (line.EndsWith("=")) {
                                if (input.EndOfStream) {
                                    Console.Error.WriteLine($"error: {inputFile}({ln}): Expected string constant after = or on the line following it");
                                    Environment.Exit(7);
                                }

                                var nextLine = input.ReadLine().Trim();
                                if (!nextLine.StartsWith("$\"") && !nextLine.StartsWith("\"")) {
                                    Console.Error.WriteLine($"error: {inputFile}({ln}): Expected string constant after = or on the line following it");
                                    Environment.Exit(7);
                                }

                                ln++;
                                line += nextLine;
                            }

                            while (line.EndsWith("\\")) {
                                if (input.EndOfStream) {
                                    Console.Error.WriteLine($"error: {inputFile}({ln}): File ended after \\");
                                    Environment.Exit(8);
                                }

                                var nextLine = input.ReadLine().Trim();
                                ln++;
                                line = line.Substring(0, line.Length - 1) + nextLine;
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

                                            AutoWriteComments();
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
                                        if (swtch == null) {
                                            AutoWriteComments();
                                            fs.Write(output, xmlWriter);
                                        }
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
                }

                void AutoWriteComments () {
                    if (commentBuffer.Length <= 0)
                        return;

                    xmlWriter.WriteComment(" " + commentBuffer.ToString().Replace("//", "").Trim() + " ");
                    commentBuffer.Clear();
                }
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

        internal void Write (StreamWriter output, FStringTableWriter writer) {
            foreach (var value in Cases.Values) {
                // Only generate all the code the first time.
                value.Write(output, writer);
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

            // HACK: Used for inference of whether an fstring argument is also an fstring, to avoid boxing
            var actualKey = swtch?.Name ?? Name;
            if ((swtch == null) && Program.KnownFStringKeys.Contains(actualKey))
                throw new Exception($"Duplicate definition for string '{actualKey}'");
            Program.KnownFStringKeys.Add(actualKey);

            if (swtch != null)
                swtch.Cases.Add(Name, this);

            StringTableKey = (swtch != null) ? $"{swtch.Name}_{HashUtil.GetShortHash(Name)}" : Name;
            FormatString = m.Groups["text"].Value;
            Definition = FStringDefinition.Parse(null, Name, FormatString, !m.Groups["buck"].Success);
            if (Definition.Opcodes.Any(o => o.emit && o.textOrId == "this"))
                throw new Exception("{this} is invalid in FStrings");
        }

        public void Write (StreamWriter output, FStringTableWriter writer) {
            // FIXME: Defer for sorting? Probably better to keep source file ordering
            if (Switch != null)
                writer.WriteComment($" ({Switch.Selector}) == {Name} ");
            writer.WriteEntry(StringTableKey, FormatString, HashUtil.GetShortHash(FormatString), Definition.IsLiteral);

            if (output == null)
                return;

            var structName = Switch?.Name ?? Name;

            if (Group.Arguments.Count == 0) {
                output.WriteLine($"\tpublic static FStringLiteral {Name} () => {Name}(FStringTable.Default);");
                output.WriteLine($"\tpublic static FStringLiteral {Name} (FStringTable table) => new FStringLiteral(table.Get(\"{StringTableKey}\"));");
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
                    if (IsTypeKnownToBeFString(key))
                        output.WriteLine($"\t\t\t\t\t({key}).AppendTo(ref output);");
                    else
                        output.WriteLine($"\t\t\t\t\toutput.Append({key});");
                    output.WriteLine($"\t\t\t\t\treturn;");
                }
                output.WriteLine("\t\t\t\tdefault:");
                output.WriteLine("\t\t\t\t\tthrow new ArgumentOutOfRangeException(nameof(id));");
                output.WriteLine("\t\t\t}");
                output.WriteLine("\t\t}");

                // Generate the append overloads that make things usable dynamically
                output.WriteLine("\t\tpublic void AppendTo (ref FStringBuilder output) => output.GetDefinition(StringTableKey).AppendTo(ref this, ref output);");

                // Generate ToString for simple uses
                output.WriteLine("\t\tpublic override string ToString () {");
                output.WriteLine("\t\t\tvar output = new FStringBuilder();");
                output.WriteLine("\t\t\tAppendTo(ref output);");
                output.WriteLine("\t\t\treturn output.ToString();");
                output.WriteLine("\t\t}");

                output.WriteLine("\t}");
            }
            output.WriteLine();
        }

        bool IsTypeKnownToBeFString (string key) {
            foreach (var arg in Group.Arguments) {
                if (arg.Name == key) {
                    if (Program.KnownFStringKeys.Contains(arg.Type))
                        return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetIds (FStringDefinition definition) =>
            definition.Opcodes.Where(o => o.emit).Select(o => o.textOrId);
    }
}
