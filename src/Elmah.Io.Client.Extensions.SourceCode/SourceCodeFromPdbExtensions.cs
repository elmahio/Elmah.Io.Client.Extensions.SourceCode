using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Elmah.Io.Client.Extensions.SourceCode
{
    // This class borrows code from a range of GitHub repositories either not awailable as NuGet packages or as packages that requires
    // a lot of dependencies that we don't want to include as part of this package. Here are the sources:
    // https://github.com/atifaziz/StackTraceParser by https://github.com/atifaziz
    // https://github.com/SiamAbdullah/ExtractSourceCodeFromPortablePDB by https://github.com/SiamAbdullah
    // https://github.com/ctaggart/SourceLink by https://github.com/ctaggart

    /// <summary>
    /// This class contain extension methods for getting source code from a PDB file.
    /// </summary>
    public static class SourceCodeFromPdbExtensions
    {
        // Magic string??! Documentation is here: https://github.com/dotnet/corefx/blob/master/src/System.Reflection.Metadata/specs/PortablePdb-Metadata.md#embedded-source-c-and-vb-compilers
        private static readonly Guid EmbeddedSource = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

        private static readonly Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        private static readonly Dictionary<string, string> sourceCodeCache = new Dictionary<string, string>();

        /// <summary>
        /// Try to pull source code from the PDB file and include that as part of the log messages. To be able to do that you will need to
        /// include the following in your .csproj file:<br/><br/>
        /// &lt;EmbedAllSources&gt;true&lt;/EmbedAllSources&gt;<br/>
        /// </summary>
        public static CreateMessage WithSourceCodeFromPdb(this CreateMessage message, bool useCacheIfPossible = true)
        {
            if (message == null) return message;
            if (string.IsNullOrWhiteSpace(message.Detail)) return message;

            try
            {
                var stackTrace = StackTraceParser.Parse(
                    message.Detail,
                    (f, t, m, pl, ps, fn, ln) => new StackFrame
                    {
                        Type = SimplyfyType(t),
                        File = fn,
                        Line = int.TryParse(ln, out int line) ? line : 0,
                    });

                var frames = stackTrace.Where(st => !string.IsNullOrWhiteSpace(st.File) && st.Line > 0);
                if (frames == null || !frames.Any()) return message;

                string sourceCode = null;
                int? lineNumber = null;
                string codeFilename = null;

                foreach (var frame in frames)
                {
                    // If a previous iteration already found source code we don't need to lookup more source
                    if (!string.IsNullOrWhiteSpace(sourceCode)) break;

                    lineNumber = frame.Line;
                    codeFilename = frame.File;
                    if (useCacheIfPossible && sourceCodeCache.ContainsKey(frame.File))
                    {
                        sourceCode = sourceCodeCache[frame.File];
                        break;
                    }
                    else
                    {
                        Assembly assembly = GetAseembly(frame);
                        if (assembly == null) continue;

                        var fileInfo = new FileInfo(assembly.Location);
                        if (fileInfo.Extension != ".dll" && fileInfo.Extension != ".exe") continue;

                        var directory = fileInfo.Directory.FullName;
                        var file = fileInfo.Name;
                        var fullPdbPath = Path.Combine(directory, Path.ChangeExtension(file, "pdb"));

                        var usePdb = File.Exists(fullPdbPath);

                        var filename = usePdb ? fullPdbPath : fileInfo.FullName;

                        using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            var meta = usePdb
                                ? GetMetadataReaderProviderFromPdbFile(fs)
                                : GetMetadataReaderProviderFromEmbeddedPdb(fs);

                            if (meta == null) continue;

                            var reader = meta.GetMetadataReader();

                            foreach (var document in reader.Documents)
                            {
                                var content = reader.GetDocument(document);
                                var embeddedSourceFileName = reader.GetString(content.Name);
                                if (frame.File != embeddedSourceFileName) continue;

                                byte[] bytes;
                                bytes = (from handle in reader.GetCustomDebugInformation(document)
                                         let cdi = reader.GetCustomDebugInformation(handle)
                                         where reader.GetGuid(cdi.Kind) == EmbeddedSource
                                         select reader.GetBlobBytes(cdi.Value)).SingleOrDefault();
                                if (bytes == null) continue;

                                int uncompressedSize = BitConverter.ToInt32(bytes, 0);
                                var stream = new MemoryStream(bytes, sizeof(int), bytes.Length - sizeof(int));

                                if (uncompressedSize != 0)
                                {
                                    var decompressed = new MemoryStream(uncompressedSize);

                                    using (var deflater = new DeflateStream(stream, CompressionMode.Decompress))
                                    {
                                        deflater.CopyTo(decompressed);
                                    }

                                    if (decompressed.Length != uncompressedSize) break;

                                    stream = decompressed;
                                }

                                using (stream)
                                {
                                    stream.Position = 0;

                                    using (var streamReader = new StreamReader(stream, DefaultEncoding, true))
                                    {
                                        sourceCode = streamReader.ReadToEnd();
                                        if (!string.IsNullOrWhiteSpace(sourceCode))
                                        {
                                            sourceCodeCache[frame.File] = sourceCode;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(sourceCode) && lineNumber.HasValue)
                {
                    // Line numbers are 1 indexed. Lines in the source file are 0 indexed
                    var lineInSource = lineNumber.Value - 1;

                    // It doesn't make sense to carry on if we don't have the line with the error in it
                    var lines = sourceCode.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length < lineInSource) return message;

                    // Start 10 lines before the line containing the error or with the first line if within the first 10 lines
                    var start = lineInSource >= 10 ? lineInSource - 10 : 0;

                    var sourceSection = string.Join(Environment.NewLine, lines.Skip(start).Take(21));
                    if (!string.IsNullOrWhiteSpace(sourceSection))
                    {
                        message.Code = sourceSection;
                        if (message.Data == null) message.Data = new List<Item>();
                        message.Data.Add(new Item("X-ELMAHIO-CODESTARTLINE", $"{1 + start}"));
                        message.Data.Add(new Item("X-ELMAHIO-CODELINE", $"{lineNumber}"));
                        message.Data.Add(new Item("X-ELMAHIO-CODEFILENAME", codeFilename));
                    }
                }
            }
            catch (Exception e)
            {
                if (message.Data == null) message.Data = new List<Item>();
                message.Data.Add(new Item("X-ELMAHIO-CODEERROR", e.Message));
            }

            return message;
        }

        private static string SimplyfyType(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return t;
            var parts = t.Split(new[] { '.' }).Where(part => !part.StartsWith("<"));
            if (parts.Count() < 1) return t;

            return string.Join(".", parts);
        }

        private static MetadataReaderProvider GetMetadataReaderProviderFromEmbeddedPdb(FileStream fs)
        {
            // There's no PDB file. Let's try reading embedded portable PDB.
            var peReader = new PEReader(fs);
            if (!peReader.HasMetadata)
                return null;

            var debugDirectoryEntries = peReader.ReadDebugDirectory();
            var embeddedPdb = debugDirectoryEntries.Where(dde => dde.Type == DebugDirectoryEntryType.EmbeddedPortablePdb).FirstOrDefault();
            if (embeddedPdb.Equals(default(DebugDirectoryEntry)))
                return null;

            return peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedPdb);
        }

        private static MetadataReaderProvider GetMetadataReaderProviderFromPdbFile(FileStream fs)
        {
            // There's a PDB file. Let's use that.
            return MetadataReaderProvider.FromMetadataStream(fs);
        }

        private static Assembly GetAseembly(StackFrame firstFrame)
        {
            Assembly assembly = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.FullName.StartsWith("System.", StringComparison.InvariantCultureIgnoreCase)))
            {
                var type = a.GetType(firstFrame.Type, false, true);
                if (type != null)
                {
                    assembly = a;
                    break;
                }
            }

            return assembly;
        }

        private class StackFrame
        {
            public string File { get; internal set; }
            public int Line { get; internal set; }
            public string Type { get; internal set; }
        }
    }
}
