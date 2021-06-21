using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Elmah.Io.Client.Extensions.SourceCode
{
    /// <summary>
    /// This class contain extension methods for getting source code from the file system.
    /// </summary>
    public static class SourceCodeFromFileSystemExtensions
    {
        private static readonly Dictionary<string, string> sourceCodeCache = new Dictionary<string, string>();

        /// <summary>
        /// Try to pull source code from the file system and include that as part of the log messages. To be able to do that you will need to
        /// have all source files present on the web server with the same absolute path as on the machine building the code.
        /// </summary>
        public static CreateMessage WithSourceCodeFromFileSystem(this CreateMessage message)
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

                var firstFrame = stackTrace.FirstOrDefault(st => !string.IsNullOrWhiteSpace(st.File) && File.Exists(st.File) && st.Line > 0);
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
                        sourceCode = File.ReadAllText(firstFrame.File);
                        if (!string.IsNullOrWhiteSpace(sourceCode))
                        {
                            sourceCodeCache.Add(firstFrame.File, sourceCode);
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
