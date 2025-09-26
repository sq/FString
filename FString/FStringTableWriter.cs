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
        private (string sourcePath, DateTime sourceModifiedUtc, string sourceCommitHash) CurrentFile;
        private HashSet<(string key, string text, string hash, bool isLiteral, string precedingComment, Dictionary<string, string> attributes)> DeferredItems = new ();
        private HashSet<string> KeysWritten = new ();

        public FStringTableWriter (Stream stream, string locale, bool ownsStream = true) {
            var started = DateTime.UtcNow;
            var xws = new XmlWriterSettings {
                Indent = true,
                Encoding = Encoding.UTF8,
                NewLineHandling = NewLineHandling.Entitize,
                CloseOutput = ownsStream,
                WriteEndDocumentOnClose = true,
                IndentChars = "\t",
                NewLineOnAttributes = true,
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

        public void StartFile (string sourcePath, DateTime sourceModifiedUtc, string sourceCommitHash) {
            var newFile = (sourcePath, sourceModifiedUtc, sourceCommitHash);
            if (CurrentFile == newFile)
                return;
            if (CurrentFile.sourcePath != null)
                EndFile();

            KeysWritten.Clear();
            DeferredItems.Clear();
            CurrentFile = newFile;
            Writer.WriteComment($"Start of file {Path.GetFileName(sourcePath)}");
            Writer.WriteStartElement("File");

            Writer.WriteAttributeString("SourcePath", sourcePath);
            Writer.WriteAttributeString("SourceModifiedUtc", sourceModifiedUtc.ToString("o"));
            if (!string.IsNullOrWhiteSpace(sourceCommitHash))
                Writer.WriteAttributeString("SourceCommitHash", sourceCommitHash);
        }

        public void Flush () {
            foreach (var di in DeferredItems.OrderBy(tup => tup.Item1)) {
                if (di.precedingComment != null)
                    WriteComment(di.precedingComment);
                WriteEntry(di.key, di.text, di.hash, di.isLiteral, di.attributes);
            }
            DeferredItems.Clear();
        }

        public void EndFile () {
            Flush();
            if (CurrentFile.sourcePath == null)
                throw new InvalidOperationException();
            Writer.WriteEndElement();
            Writer.WriteComment($"End of file {Path.GetFileName(CurrentFile.sourcePath)}");
            CurrentFile = default;
        }

        public void WriteComment (string comment) => Writer.WriteComment(comment);

        public void Dispose () {
            Flush();
            Writer.Dispose();
        }

        public void WriteEntry (string key, string text, string hash, bool isLiteral, Dictionary<string, string> extraAttributes = null) {
            if (KeysWritten.Contains(key))
                throw new Exception($"Key '{key}' already written to string table with text '{text}'");
            Writer.WriteStartElement(isLiteral ? "Literal" : "String");
            Writer.WriteAttributeString("Name", key);
            Writer.WriteAttributeString("Hash", hash);
            if (extraAttributes != null)
                foreach (var kvp in extraAttributes)
                    Writer.WriteAttributeString(kvp.Key, kvp.Value);

            if (text.Contains('<') || text.Contains('&') || text.Contains('\n'))
                Writer.WriteCData(text);
            else
                Writer.WriteString(text);

            Writer.WriteEndElement();
        }

        public void DeferWriteEntry (string key, string text, string hash, bool isLiteral, string precedingComment = null, Dictionary<string, string> extraAttributes = null) {
            var tup = (key, text, hash, isLiteral, precedingComment, extraAttributes);
            foreach (var di in DeferredItems) {
                if (di.key == key)
                    throw new Exception($"Key '{key}' already deferred for string table write with text '{text}'");
            }

            DeferredItems.Add(tup);
        }
    }
}
