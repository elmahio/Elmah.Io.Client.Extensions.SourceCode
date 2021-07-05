﻿using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Elmah.Io.Client.Extensions.SourceCode.Test
{
    public class SourceCodeFromPdbExtensionsTest
    {
        internal const string DotNetStackTrace = @"Elmah.TestException: This is a test exception that can be safely ignored.
    at Elmah.Io.Client.Extensions.SourceCode.Test.SourceCodeFromPdbExtensionsTest.CanDecorateMessageWithCode() in REPLACE_ME\SourceCodeFromPdbExtensionsTest.cs:line 35
    at Elmah.ErrorLogPageFactory.GetHandler(HttpContext context, String requestType, String url, String pathTranslated)
    at System.Web.HttpApplication.MapHttpHandler(HttpContext context, String requestType, VirtualPath path, String pathTranslated, Boolean useAppConfig)
    at System.Web.HttpApplication.MapHandlerExecutionStep.System.Web.HttpApplication.IExecutionStep.Execute()
    at System.Web.HttpApplication.ExecuteStep(IExecutionStep step, Boolean& completedSynchronously)";
        internal const string DotNetStackTraceWithoutSourceFileInfo = @"Elmah.TestException: This is a test exception that can be safely ignored.
    at Elmah.Io.Client.Extensions.SourceCode.Test.SourceCodeFromPdbExtensionsTest.CanDecorateMessageWithCode()
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
            msg = msg.WithSourceCodeFromPdb();

            // Assert
            Assert.That(!string.IsNullOrWhiteSpace(msg.Code));
            Assert.That(msg.Code.Contains("Detail = DotNetStackTrace.Replace(\"REPLACE_ME\", root.FullName)"));
            Assert.That(msg.Data != null);
            Assert.That(msg.Data.Count, Is.EqualTo(2));
            Assert.That(msg.Data.First(d => d.Key == "X-ELMAHIO-CODESTARTLINE").Value, Is.EqualTo("25"));
            Assert.That(msg.Data.First(d => d.Key == "X-ELMAHIO-CODELINE").Value, Is.EqualTo("35"));
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
            msg = msg.WithSourceCodeFromPdb();

            // Assert
            Assert.That(msg.Code == null);
            Assert.That(msg.Data == null);
        }

        [Test] public void CanRunOnNull()
        {
            Assert.That(new CreateMessage().WithSourceCodeFromPdb().Code, Is.Null);
        }
    }
}
