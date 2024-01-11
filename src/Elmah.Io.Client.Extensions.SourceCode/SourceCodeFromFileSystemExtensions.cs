using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        public static CreateMessage WithSourceCodeFromFileSystem(this CreateMessage message, bool useCacheIfPossible = true)
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

                var frames = stackTrace.Where(st => !string.IsNullOrWhiteSpace(st.File) && File.Exists(st.File) && st.Line > 0);
                if (!frames.Any()) return message;

                string sourceCode = null;
                int? lineNumber = null;
                string filename = null;
                foreach (var frame in frames)
                {
                    lineNumber = frame.Line;
                    filename = frame.File;
                    if (useCacheIfPossible && sourceCodeCache.ContainsKey(frame.File))
                    {
                        sourceCode = sourceCodeCache[frame.File];
                        break;
                    }
                    else
                    {
                        sourceCode = File.ReadAllText(frame.File);
                        if (!string.IsNullOrWhiteSpace(sourceCode))
                        {
                            sourceCodeCache[frame.File] = sourceCode;
                            break;
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
                        message.Data.Add(new Item("X-ELMAHIO-CODEFILENAME", filename));
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
    }
}
