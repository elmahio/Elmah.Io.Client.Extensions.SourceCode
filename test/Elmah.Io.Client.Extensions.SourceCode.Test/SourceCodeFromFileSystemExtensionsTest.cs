using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Elmah.Io.Client.Extensions.SourceCode.Test
{
    public class SourceCodeFromFileSystemExtensionsTest
    {
        internal const string DotNetStackTrace = @"Elmah.TestException: This is a test exception that can be safely ignored.
    at Elmah.Io.Client.HeartbeatsClient.CreateAsync(String id, String logId, CreateHeartbeat body, CancellationToken cancellationToken) in /_/src/Elmah.Io.Client/ElmahioClient.cs:line 730
    at Elmah.Io.Client.Extensions.SourceCode.Test.SourceCodeFromFileSystemExtensionsTest.CanDecorateMessageWithCode() in REPLACE_ME\SourceCodeFromFileSystemExtensionsTest.cs:line 36
    at Elmah.ErrorLogPageFactory.GetHandler(HttpContext context, String requestType, String url, String pathTranslated)
    at System.Web.HttpApplication.MapHttpHandler(HttpContext context, String requestType, VirtualPath path, String pathTranslated, Boolean useAppConfig)
    at System.Web.HttpApplication.MapHandlerExecutionStep.System.Web.HttpApplication.IExecutionStep.Execute()
    at System.Web.HttpApplication.ExecuteStep(IExecutionStep step, Boolean& completedSynchronously)";
        internal const string DotNetStackTraceWithoutSourceFileInfo = @"Elmah.TestException: This is a test exception that can be safely ignored.
    at Elmah.Io.Client.Extensions.SourceCode.Test.SourceCodeFromFileSystemExtensionsTest.CanDecorateMessageWithCode()
    at Elmah.ErrorLogPageFactory.GetHandler(HttpContext context, String requestType, String url, String pathTranslated)
    at System.Web.HttpApplication.MapHttpHandler(HttpContext context, String requestType, VirtualPath path, String pathTranslated, Boolean useAppConfig)
    at System.Web.HttpApplication.MapHandlerExecutionStep.System.Web.HttpApplication.IExecutionStep.Execute()
    at System.Web.HttpApplication.ExecuteStep(IExecutionStep step, Boolean& completedSynchronously)";

        [Test]
        public void CanDecorateMessageWithCode()
        {
            // Arrange
            var path = new FileInfo(Assembly.GetExecutingAssembly().Location);
            var root = path.Directory.Parent.Parent.Parent;
            var msg = new CreateMessage
            {
                Detail = DotNetStackTrace.Replace("REPLACE_ME", root.FullName)
            };

            // Act
            msg = msg.WithSourceCodeFromFileSystem();

            // Assert
            Assert.That(!string.IsNullOrWhiteSpace(msg.Code));
            Assert.That(msg.Code.Contains("Detail = DotNetStackTrace.Replace(\"REPLACE_ME\", root.FullName)"));
            Assert.That(msg.Data != null);
            Assert.That(msg.Data.Count, Is.EqualTo(2));
            Assert.That(msg.Data.First(d => d.Key == "X-ELMAHIO-CODESTARTLINE").Value, Is.EqualTo("26"));
            Assert.That(msg.Data.First(d => d.Key == "X-ELMAHIO-CODELINE").Value, Is.EqualTo("36"));
        }

        [Test]
        public void CanRunOnStackTraceWithoutSourceFileInfo()
        {
            // Arrange
            var msg = new CreateMessage
            {
                Detail = DotNetStackTraceWithoutSourceFileInfo
            };

            // Act
            msg = msg.WithSourceCodeFromFileSystem();

            // Assert
            Assert.That(msg.Code == null);
            Assert.That(msg.Data == null);
        }

        [Test] public void CanRunOnNull() => Assert.That(new CreateMessage().WithSourceCodeFromFileSystem().Code, Is.Null);
    }
}
