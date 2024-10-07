using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;

namespace CodeCake;


/// <summary>
/// Sample build "script".
/// Build scripts can be decorated with AddPath attributes that inject existing paths into the PATH environment variable. 
/// </summary>

public partial class Build : CodeCakeHost
{
    public Build()
    {
        Cake.Log.Verbosity = Verbosity.Diagnostic;

        StandardGlobalInfo globalInfo = CreateStandardGlobalInfo()
                                            .AddDotnet()
                                            .SetCIBuildTag();

        Task( "Check-Repository" )
            .Does( () =>
            {
                // We currently produce nothing so TerminateIfShouldStop
                // always stops the build script.
                // globalInfo.TerminateIfShouldStop();
            } );

        Task( "Clean" )
            .IsDependentOn( "Check-Repository" )
            .Does( () =>
             {
                 globalInfo.GetDotnetSolution().Clean();
                 Cake.CleanDirectories( globalInfo.ReleasesFolder.ToString() );

             } );

        Task( "Build" )
            .IsDependentOn( "Check-Repository" )
            .IsDependentOn( "Clean" )
            .Does( () =>
            {
                globalInfo.GetDotnetSolution().Build();
            } );

        Task( "Unit-Testing" )
            .IsDependentOn( "Build" )
            .WithCriteria( () => Cake.InteractiveMode() == InteractiveMode.NoInteraction
                                 || Cake.ReadInteractiveOption( "RunUnitTests", "Run Unit Tests?", 'Y', 'N' ) == 'Y' )
           .Does( () =>
           {

               globalInfo.GetDotnetSolution().SolutionTest();
           } );

        // The Default task for this script can be set here.
        Task( "Default" )
            .IsDependentOn( "Unit-Testing" );
    }
}
