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

        public FStringTableWriter (Stream stream, bool ownsStream = true) {
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
        }

        public FStringTableWriter (string path) 
            : this (File.Open(path, FileMode.Create)) 
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

        public void EndFile () {
            if (CurrentFile.Item1 == null)
                throw new InvalidOperationException();
            Writer.WriteEndElement();
            CurrentFile = default;
        }

        public void WriteComment (string comment) => Writer.WriteComment(comment);

        public void Dispose () {
            Writer.Dispose();
        }

        public void WriteEntry (string key, string text, bool isLiteral) {
            Writer.WriteStartElement(isLiteral ? "Literal" : "String");
            Writer.WriteAttributeString("Name", key);
            Writer.WriteString(text);
            Writer.WriteEndElement();
        }
    }
}
