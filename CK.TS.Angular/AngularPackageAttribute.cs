using CK.StObj.TypeScript;

namespace CK.TS.Angular
{
    public class AngularPackageAttribute : TypeScriptPackageAttribute
    {
        public AngularPackageAttribute()
            : base( "CK.TS.Angular.Engine.AngularPackageAttributeImpl, CK.TS.Angular.Engine" )
        {
        }
    }
}
