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
        private List<(string, string, string, bool, string)> DeferredItems = new ();

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

            CurrentFile = newFile;
            Writer.WriteStartElement("File");

            Writer.WriteAttributeString("SourcePath", sourcePath);
            Writer.WriteAttributeString("SourceModifiedUtc", sourceModifiedUtc.ToString("o"));
        }

        public void Flush () {
            DeferredItems.Sort((l, r) => l.Item1.CompareTo(r.Item1));
            foreach (var di in DeferredItems) {
                if (di.Item5 != null)
                    WriteComment(di.Item5);
                WriteEntry(di.Item1, di.Item2, di.Item3, di.Item4);
            }
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
            Writer.WriteStartElement(isLiteral ? "Literal" : "String");
            Writer.WriteAttributeString("Name", key);
            Writer.WriteAttributeString("Hash", hash);
            Writer.WriteString(text);
            Writer.WriteEndElement();
        }

        public void DeferWriteEntry (string key, string text, string hash, bool isLiteral, string precedingComment = null) {
            DeferredItems.Add((key, text, hash, isLiteral, precedingComment));
        }
    }
}
