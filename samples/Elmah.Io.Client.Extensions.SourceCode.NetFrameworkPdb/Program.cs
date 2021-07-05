using System;
using Elmah.Io.Client;
using Elmah.Io.Client.Extensions.SourceCode;

namespace Elmah.Io.Client.Extensions.SourceCode.NetFrameworkPdb
{
    class Program
    {
        static void Main(string[] args)
        {
            var elmahIoClient = ElmahioAPI.Create("API_KEY");
            elmahIoClient.Messages.OnMessage += (sender, e) => e.Message.WithSourceCodeFromPdb();
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
