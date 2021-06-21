using System;

namespace Elmah.Io.Client.Extensions.SourceCode.FileSystem
{
    class Program
    {
        static void Main(string[] args)
        {
            var elmahIoClient = ElmahioAPI.Create("API_KEY");
            elmahIoClient.Messages.OnMessage += (sender, e) => e.Message.WithSourceCodeFromFileSystem();
            try
            {
                new A().X();
            }
            catch (Exception e)
            {
                elmahIoClient.Messages.Error(new Guid("LOG_ID"), e, e.Message);
            }
        }
    }

    class A
    {
        public void X()
        {
            new B().Y();
        }
    }

    class B
    {
        public void Y()
        {
            throw new ApplicationException("An error happened");
        }
    }
}
