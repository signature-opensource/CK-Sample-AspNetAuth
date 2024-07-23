using CK.Core;
using CK.Setup;
using CK.TypeScript.CodeGen;

namespace CK.TS.Angular.Engine
{
    public class AngularCodeGenerator : ITSCodeGenerator
    {
        public bool Initialize( IActivityMonitor monitor, ITypeScriptContextInitializer initializer )
        {
            return true;
        }

        bool ITSCodeGenerator.OnResolveObjectKey( IActivityMonitor monitor, TypeScriptContext context, RequireTSFromObjectEventArgs e ) => true;

        bool ITSCodeGenerator.OnResolveType( IActivityMonitor monitor, TypeScriptContext context, RequireTSFromTypeEventArgs builder ) => true;

        public bool StartCodeGeneration( IActivityMonitor monitor, TypeScriptContext context )
        {
            return true;
        }
    }
}
