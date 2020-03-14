using System.Collections.Generic;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitHubActions.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
[GitHubActions("dotnetcore",
	GitHubActionsImage.Ubuntu1804,
	ImportSecrets = new[]{ "NUGET_API_KEY" },
	AutoGenerate = true,
	On = new [] { GitHubActionsTrigger.Push, GitHubActionsTrigger.PullRequest })]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    [Parameter("NuGet server URL.")]
	readonly string NugetSource = "https://api.nuget.org/v3/index.json";
    [Parameter("API Key for the NuGet server.")]
	readonly string NugetApiKey;
	[Parameter("Version to use for package.")]
	readonly string Version;

    [Solution]
	readonly Solution Solution;
    [GitRepository]
	readonly GitRepository GitRepository;
    //[GitVersion]
	//readonly GitVersion GitVersion;
	
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Project UtilityDisposablesProject => Solution.GetProject("UtilityDisposables");
    
    IEnumerable<Project> TestProjects => Solution.GetProjects("*.Tests");
    
    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution)
			);
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
	        var gitVersion = GetGitVersion();
	        
            DotNetBuild(s => s
                .EnableNoRestore()
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(gitVersion.AssemblySemVer)
                .SetFileVersion(Version ?? gitVersion.AssemblySemFileVer)
                .SetInformationalVersion(Version ?? gitVersion.InformationalVersion)
			);

            DotNetPublish(s => s
				.EnableNoRestore()
				.EnableNoBuild()
				.SetConfiguration(Configuration)
				.SetAssemblyVersion(gitVersion.AssemblySemVer)
				.SetFileVersion(Version ?? gitVersion.AssemblySemFileVer)
				.SetInformationalVersion(Version ?? gitVersion.InformationalVersion)
				.CombineWith(
					from project in new[] { UtilityDisposablesProject }
					from framework in project.GetTargetFrameworks()
                    select new { project, framework }, (cs, v) => cs
						.SetProject(v.project)
						.SetFramework(v.framework)
				)
			);
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(s => s
	            .SetConfiguration(Configuration)
	            .EnableNoRestore()
                .EnableNoBuild()
	            .CombineWith(
		            TestProjects, (cs, v) => cs
			            .SetProjectFile(v))
            );
        });

    Target Pack => _ => _
        .DependsOn(Clean, Test)
		.Requires(() => Configuration == Configuration.Release)
        .Executes(() =>
        {
	        var gitVersion = GetGitVersion();
	        
            DotNetPack(s => s
                .EnableNoRestore()
                .EnableNoBuild()
				.SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .SetVersion(Version ?? gitVersion.NuGetVersionV2)
				.SetIncludeSymbols(true)
				.SetSymbolPackageFormat(DotNetSymbolPackageFormat.snupkg)
            );
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Consumes(Pack)
        .Requires(() => Configuration == Configuration.Release)
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
				.SetSource(NugetSource)
				.SetApiKey(NugetApiKey)
				.CombineWith(ArtifactsDirectory.GlobFiles("*.nupkg"), (s, v) => s
					.SetTargetPath(v)
				)
            );
        });

    private GitVersion GetGitVersion()
    {
	    var package = NuGetPackageResolver.GetGlobalInstalledPackage("GitVersion.Tool", "5.2.3", null);
	    var settings = new GitVersionSettings().SetToolPath(package.Directory / "tools/netcoreapp3.1/any/gitversion.dll");
	    var gitVersion = GitVersionTasks.GitVersion(settings).Result;
	    return gitVersion;
    }
}
