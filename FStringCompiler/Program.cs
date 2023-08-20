using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FStringCompiler {
    class Program {
        private static readonly Regex UsingRegex = new Regex(@"using .+?;", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            FunctionSignatureRegex = new Regex(@"\(((?<type>\w+)\s+(?<name>\w+)\s*,?\s*)*\)\s*=>\s*\{", RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            StringRegex = new Regex("(?<name>\\w+)\\s*=\\s*\"(?<text>(\\.|[^\"\\n])*)\";", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static void Main (string[] args) {
            if (args.Length != 2) {
                Console.Error.WriteLine("Usage: fstringcompiler [input directory] [output directory]");
                Environment.Exit(1);
            }

            foreach (var inputFile in Directory.EnumerateFiles(args[0], "*.fstring", SearchOption.AllDirectories)) {
                int ln = 0;
                var inClass = false;
                var outPath = Path.Combine(args[1], Path.GetFileNameWithoutExtension(inputFile) + ".cs");
                StringGroup group = null;
                using (var input = new StreamReader(inputFile))
                using (var output = new StreamWriter(outPath, false, Encoding.UTF8)) {
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
                                    if (fsm.Success) {
                                        group = new StringGroup(fsm);
                                    } else {
                                        Console.Error.WriteLine($"{args[0]}({ln}): Unrecognized line: {line}");
                                        Environment.Exit(2);
                                    }
                                }
                            } else {
                                var sm = StringRegex.Match(line);
                                if (sm.Success) {
                                    if (!inClass) {
                                        output.WriteLine("using Squared.FString;");
                                        output.WriteLine();
                                        output.WriteLine("public static partial class FStrings {");
                                        inClass = true;
                                    }

                                    var fs = new FString(group, sm);
                                    fs.Write(output);
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
            var names = m.Groups["name"].Captures.Cast<Capture>().Select(c => c.Value).ToArray();
            for (int i = 0; i < types.Length; i++)
                Arguments.Add(new Argument { Type = types[i], Name = names[i] });
        }
    }

    public class FString {
        public StringGroup Group;
        public string Name, RawText;

        public FString (StringGroup group, Match m) {
            Group = group;
            Name = m.Groups["name"].Value;
            RawText = m.Groups["text"].Value;
        }

        public void Write (StreamWriter output) {
            output.Write($"\tpublic static FStringBuilder {Name} (FStringBuilder output");
            foreach (var arg in Group.Arguments)
                output.Write($", {arg.Type} {arg.Name}");
            output.WriteLine(") {");
            // FIXME
            output.Write("\t\toutput.Append($\"");
            output.Write(RawText);
            output.WriteLine("\");");
            output.WriteLine("\t\treturn output;");
            output.WriteLine("\t}");
            output.WriteLine();

            output.Write($"\tpublic static string {Name} (");
            var isFirst = true;
            foreach (var arg in Group.Arguments) {
                if (!isFirst)
                    output.Write(", ");
                isFirst = false;
                output.Write($"{arg.Type} {arg.Name}");
            }
            output.WriteLine(") {");
            output.Write($"\t\treturn {Name}(new FStringBuilder()");
            foreach (var arg in Group.Arguments)
                output.Write($", {arg.Name}");
            output.Write(").ToString();");
            output.WriteLine("\t\t");
            output.WriteLine("\t}");
            output.WriteLine();
        }
    }
}
