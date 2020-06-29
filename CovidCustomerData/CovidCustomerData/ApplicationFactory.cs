using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CovidCustomerData
{
    public class ApplicationFactory : Micros.PosCore.Extensibility.IExtensibilityAssemblyFactory
    {

        public Micros.PosCore.Extensibility.ExtensibilityAssemblyBase Create(Micros.PosCore.Extensibility.IExecutionContext context)
        {
            return new Application(context);
        }

        public void Destroy(Micros.PosCore.Extensibility.ExtensibilityAssemblyBase app)
        {
            app.Destroy();
        }
    }
    
}
