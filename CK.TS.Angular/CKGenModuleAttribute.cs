using CK.StObj.TypeScript;

namespace CK.TS.Angular
{
    public class CKGenModuleAttribute : TypeScriptPackageAttribute
    {
        public CKGenModuleAttribute()
            : base( "CK.TS.Angular.Engine.CKGenModuleAttributeImpl, CK.TS.Angular.Engine" )
        {
        }
    }
}
