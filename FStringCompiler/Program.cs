using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Squared.FString;

namespace FStringCompiler {
    class Program {
        private static readonly Regex UsingRegex = new Regex(@"using .+?;", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            FunctionSignatureRegex = new Regex(@"\(((?<type>(\w|\?)+)\s+(?<argName>\w+)\s*,?\s*)*\)\s*\{", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            StringRegex = new Regex("(?<name>\\w+)\\s*=\\s*\"(?<text>(\\.|[^\"\\n])*)\";", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            StandaloneStringRegex = new Regex("(?<name>\\w+)\\s*\\(((?<type>(\\w|\\?)+)\\s+(?<argName>\\w+)\\s*,?\\s*)*\\)\\s*=\\s*\"(?<text>(\\.|[^\"\n])*)\";", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static void Main (string[] args) {
            if (args.Length != 3) {
                Console.Error.WriteLine("Usage: fstringcompiler [input directory] [output directory] [ISO language name]");
                Environment.Exit(1);
            }

            var sourceDir = Path.GetFullPath(args[0]);
            var started = DateTime.UtcNow;
            var xws = new XmlWriterSettings {
                Indent = true,
                Encoding = Encoding.UTF8,
                NewLineHandling = NewLineHandling.Entitize,
                CloseOutput = true,
                WriteEndDocumentOnClose = true,
            };
            using (var xmlWriter = XmlWriter.Create(Path.Combine(args[1], $"FStringTable_{args[2]}.xml"), xws)) {
                xmlWriter.WriteStartDocument();
                xmlWriter.WriteStartElement("FStringTable");
                xmlWriter.WriteAttributeString("GeneratedUtc", started.ToString("o"));

                foreach (var inputFile in Directory.EnumerateFiles(sourceDir, "*.fstring", SearchOption.AllDirectories)) {
                    xmlWriter.WriteStartElement("File");

                    var shortPath = Path.GetFullPath(inputFile).Replace(sourceDir, "");
                    xmlWriter.WriteAttributeString("SourcePath", shortPath);
                    xmlWriter.WriteAttributeString("SourceCreatedUtc", File.GetCreationTimeUtc(inputFile).ToString("o"));
                    xmlWriter.WriteAttributeString("SourceModifiedUtc", File.GetLastWriteTimeUtc(inputFile).ToString("o"));

                    int ln = 0;
                    var inClass = false;
                    var outPath = Path.Combine(args[1], Path.GetFileNameWithoutExtension(inputFile) + ".cs");
                    if (File.Exists(outPath) && File.GetLastWriteTimeUtc(outPath) > File.GetLastWriteTimeUtc(inputFile))
                        Console.WriteLine($"Skipping, not modified: {inputFile} -> {outPath}");

                    StringGroup group = null;
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
                                            Console.Error.WriteLine($"{args[0]}({ln}): Cannot add new using statements after defining a string");
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
                                            var fs = new FString(tempGroup, ssm);
                                            fs.Write(output);
                                            xmlWriter.WriteStartElement(fs.Name);
                                            xmlWriter.WriteString(fs.FormatString);
                                            xmlWriter.WriteEndElement();
                                        } else {
                                            Console.Error.WriteLine($"{args[0]}({ln}): Unrecognized line: {line}");
                                            Environment.Exit(2);
                                        }
                                    }
                                } else {
                                    var sm = StringRegex.Match(line);
                                    if (sm.Success) {
                                        if (!inClass) {
                                            output.WriteLine("public static partial class FStrings {");
                                            inClass = true;
                                        }

                                        var fs = new FString(group, sm);
                                        fs.Write(output);
                                        xmlWriter.WriteStartElement(fs.Name);
                                        xmlWriter.WriteString(fs.FormatString);
                                        xmlWriter.WriteEndElement();
                                    } else if (line == "}") {
                                        group = null;
                                        // End of group
                                    } else {
                                        Console.Error.WriteLine($"{args[0]}({ln}): Unrecognized line: {line}");
                                        Environment.Exit(3);
                                    }
                                }
                            } catch (Exception exc) {
                                Console.Error.WriteLine($"{args[0]}({ln}): {exc}");
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

    public class FString {
        public StringGroup Group;
        public FStringDefinition Definition;
        public string Name, FormatString;

        public FString (StringGroup group, Match m) {
            Group = group;
            Name = m.Groups["name"].Value;
            FormatString = m.Groups["text"].Value;
            Definition = FStringDefinition.Parse(Name, FormatString);
        }

        public void Write (StreamWriter output) {
            output.WriteLine($"\tpublic struct {Name} : IFString {{");
            output.WriteLine($"\t\tpublic string Name => \"{Name}\";");
            foreach (var arg in Group.Arguments)
                output.WriteLine($"\t\tpublic {arg.Type} {arg.Name};");
            output.WriteLine();

            output.WriteLine("\t\tpublic void EmitValue (ref FStringBuilder output, string id) {");
            output.WriteLine("\t\t\tswitch(id) {");
            var keys = Definition.Opcodes.Where(o => o.emit).Select(o => o.textOrId).Distinct();
            foreach (var key in keys) {
                output.WriteLine($"\t\t\t\tcase \"{key}\":");
                output.WriteLine($"\t\t\t\t\toutput.Append({key});");
                output.WriteLine($"\t\t\t\t\treturn;");
            }
            output.WriteLine("\t\t\t\tdefault:");
            output.WriteLine("\t\t\t\t\tthrow new ArgumentOutOfRangeException(nameof(id));");
            output.WriteLine("\t\t\t}");
            output.WriteLine("\t\t}");

            output.WriteLine("\t\tpublic void AppendTo (ref FStringBuilder output, FStringDefinition definition) => definition.AppendTo(ref this, ref output);");
            output.WriteLine("\t\tpublic void AppendTo (StringBuilder output) {");
            output.WriteLine("\t\t\tvar fsb = new FStringBuilder(output);");
            output.WriteLine("\t\t\tFStringTable.Default.Get(Name).AppendTo(ref this, ref fsb);");
            output.WriteLine("\t\t}");

            output.WriteLine("\t\tpublic override string ToString () {");
            output.WriteLine("\t\t\tvar output = new FStringBuilder();");
            output.WriteLine("\t\t\tAppendTo(ref output, FStringTable.Default.Get(Name));");
            output.WriteLine("\t\t\treturn output.ToString();");
            output.WriteLine("\t\t}");

            output.WriteLine("\t}");
            output.WriteLine();
        }
    }
}
