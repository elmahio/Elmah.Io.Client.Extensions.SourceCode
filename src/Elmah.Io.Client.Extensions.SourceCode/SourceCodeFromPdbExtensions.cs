using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;

namespace Elmah.Io.Client.Extensions.SourceCode
{
    // This class borrows code from a range of GitHub repositories either not awailable as NuGet packages or as packages that requires
    // a lot of dependencies that we don't want to include as part of this package. Here are some sources:
    // https://github.com/atifaziz/StackTraceParser by https://github.com/atifaziz
    // https://github.com/SiamAbdullah/ExtractSourceCodeFromPortablePDB by https://github.com/SiamAbdullah

    /// <summary>
    /// .
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
        public static CreateMessage WithSourceCodeFromPdb(this CreateMessage message)
        {
            if (message == null) return message;
            if (string.IsNullOrWhiteSpace(message.Detail)) return message;

            try
            {
                var stackTrace = StackTraceParser.Parse(
                    message.Detail,
                    (f, t, m, pl, ps, fn, ln) => new
                    {
                        Frame = f,
                        Type = t,
                        Method = m,
                        ParameterList = pl,
                        Parameters = ps,
                        File = fn,
                        Line = int.TryParse(ln, out int line) ? line : 0,
                    });

                var firstFrame = stackTrace.FirstOrDefault(st => !string.IsNullOrWhiteSpace(st.File) && st.Line > 0);
                if (firstFrame != null)
                {
                    string sourceCode = null;
                    var lineNumber = firstFrame.Line;
                    if (sourceCodeCache.ContainsKey(firstFrame.File))
                    {
                        sourceCode = sourceCodeCache[firstFrame.File];
                    }
                    else
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

                        if (assembly == null) return message;

                        var fileInfo = new FileInfo(assembly.Location);
                        if (fileInfo.Extension != ".dll" && fileInfo.Extension != ".exe") return message;
                        var directory = fileInfo.Directory.FullName;
                        var file = fileInfo.Name;
                        var fullPdbPath = Path.Combine(directory, Path.ChangeExtension(file, "pdb"));
                        if (!File.Exists(fullPdbPath)) return message;

                        using (FileStream fs = new FileStream(fullPdbPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            var meta = MetadataReaderProvider.FromMetadataStream(fs);
                            var reader = meta.GetMetadataReader();
                            foreach (var document in reader.Documents)
                            {
                                var content = reader.GetDocument(document);
                                var embeddedSourceFileName = reader.GetString(content.Name);
                                if (firstFrame.File != embeddedSourceFileName) continue;
                                byte[] bytes = (from handle in reader.GetCustomDebugInformation(document)
                                                let cdi = reader.GetCustomDebugInformation(handle)
                                                where reader.GetGuid(cdi.Kind) == EmbeddedSource
                                                select reader.GetBlobBytes(cdi.Value)).SingleOrDefault();
                                if (bytes == null)
                                {
                                    return message;
                                }

                                int uncompressedSize = BitConverter.ToInt32(bytes, 0);
                                var stream = new MemoryStream(bytes, sizeof(int), bytes.Length - sizeof(int));

                                if (uncompressedSize != 0)
                                {
                                    var decompressed = new MemoryStream(uncompressedSize);

                                    using (var deflater = new DeflateStream(stream, CompressionMode.Decompress))
                                    {
                                        deflater.CopyTo(decompressed);
                                    }

                                    if (decompressed.Length != uncompressedSize)
                                    {
                                        return message;
                                    }

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
                                            sourceCodeCache.Add(firstFrame.File, sourceCode);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(sourceCode))
                    {
                        // Line numbers are 1 indexed. Lines in the source file are 0 indexed
                        var lineInSource = lineNumber - 1;

                        // It doesn't make sense to carry on if we don't have the line with the error in it
                        var lines = sourceCode.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                        if (lines.Length < lineInSource) return message;

                        // Start 10 lines before the line containing the error or with the first line if within the first 10 lines
                        var start = lineInSource >= 10 ? lineInSource - 10 : 0;

                        var sb = new StringBuilder();

                        var currentLineNumber = start;
                        foreach (var line in lines.Skip(start).Take(21))
                        {
                            sb.Append(line).AppendLine(currentLineNumber == lineInSource ? " // <-- An error is thrown in this line" : string.Empty);
                            currentLineNumber++;
                        }

                        var sourceSection = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(sourceSection))
                        {
                            (message.Data ?? (message.Data = new List<Item>())).Add(new Item("SourceCode", sourceSection));
                            return message;
                        }
                    }
                }
            }
            catch
            {
            }

            return message;
        }
    }
}
