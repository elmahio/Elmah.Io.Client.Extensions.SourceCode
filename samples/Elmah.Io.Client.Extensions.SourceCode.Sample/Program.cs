using System;

namespace Elmah.Io.Client.Extensions.SourceCode.Sample
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
                elmahIoClient.Messages.Error(new Guid("b4cb36a9-a272-45e8-8ca6-a48e1728a8d5"), e, e.Message);
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
