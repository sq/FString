using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Squared.FString {
    public class FStringTableWriter : IDisposable {
        public readonly XmlWriter Writer;
        private (string, DateTime) CurrentFile;
        private HashSet<(string, string, string, bool, string)> DeferredItems = new ();
        private HashSet<string> KeysWritten = new ();

        public FStringTableWriter (Stream stream, string locale, bool ownsStream = true) {
            var started = DateTime.UtcNow;
            var xws = new XmlWriterSettings {
                Indent = true,
                Encoding = Encoding.UTF8,
                NewLineHandling = NewLineHandling.Entitize,
                CloseOutput = ownsStream,
                WriteEndDocumentOnClose = true,
            };
            Writer = XmlWriter.Create(stream, xws);
            Writer.WriteStartDocument();
            Writer.WriteStartElement("FStringTable");
            Writer.WriteAttributeString("GeneratedUtc", started.ToString("o"));
            Writer.WriteAttributeString("Locale", locale);
        }

        public FStringTableWriter (string path, string locale) 
            : this (File.Open(path, FileMode.Create), locale, true) 
        {
        }

        public void StartFile (string sourcePath, DateTime sourceModifiedUtc) {
            var newFile = (sourcePath, sourceModifiedUtc);
            if (CurrentFile == newFile)
                return;
            if (CurrentFile.Item1 != null)
                EndFile();

            KeysWritten.Clear();
            DeferredItems.Clear();
            CurrentFile = newFile;
            Writer.WriteStartElement("File");

            Writer.WriteAttributeString("SourcePath", sourcePath);
            Writer.WriteAttributeString("SourceModifiedUtc", sourceModifiedUtc.ToString("o"));
        }

        public void Flush () {
            foreach (var di in DeferredItems.OrderBy(tup => tup.Item1)) {
                if (di.Item5 != null)
                    WriteComment(di.Item5);
                WriteEntry(di.Item1, di.Item2, di.Item3, di.Item4);
            }
            DeferredItems.Clear();
        }

        public void EndFile () {
            Flush();
            if (CurrentFile.Item1 == null)
                throw new InvalidOperationException();
            Writer.WriteEndElement();
            CurrentFile = default;
        }

        public void WriteComment (string comment) => Writer.WriteComment(comment);

        public void Dispose () {
            Flush();
            Writer.Dispose();
        }

        public void WriteEntry (string key, string text, string hash, bool isLiteral) {
            if (KeysWritten.Contains(key))
                throw new Exception($"Key '{key}' already written to string table with text '{text}'");
            Writer.WriteStartElement(isLiteral ? "Literal" : "String");
            Writer.WriteAttributeString("Name", key);
            Writer.WriteAttributeString("Hash", hash);
            Writer.WriteString(text);
            Writer.WriteEndElement();
        }

        public void DeferWriteEntry (string key, string text, string hash, bool isLiteral, string precedingComment = null) {
            var tup = (key, text, hash, isLiteral, precedingComment);
            if (DeferredItems.Contains(tup))
                throw new Exception($"Key '{key}' already deferred for string table write with text '{text}'");
            DeferredItems.Add(tup);
        }
    }
}
