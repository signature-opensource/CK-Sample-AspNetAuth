// Run by CKli after the build of the repository (in this folder, the version as the first argument): builds the
// deployments of this folder (CK.AppDeployment), their published hosts in Assets/{deployment} (one release asset each).
#:package CK.AppDeployment@37.0.1-0.uc.1

return await CK.Setup.AppDeployment.RunAsync( args );
