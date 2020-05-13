using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.AzurePipelines;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitHubActions.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Coverlet;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.Npm;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.CompressionTasks;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.Npm.NpmTasks;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
[GitHubActions("dotnetcore",
	GitHubActionsImage.Ubuntu1804,
	ImportSecrets = new[]{ "NUGET_API_KEY", "NETLIFY_PAT" },
	AutoGenerate = true,
	On = new [] { GitHubActionsTrigger.Push },
	InvokedTargets = new [] {"Test", "Push"}
	)]
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
	[Parameter("Personal authentication token to push CI website to Netlify")]
	readonly string NetlifyPat;
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
    AbsolutePath WebsiteDirectory => RootDirectory / "website";
    AbsolutePath TestResultDirectory => ArtifactsDirectory / "test-results";

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
        .Produces(TestResultDirectory / "*.trx")
        .Produces(TestResultDirectory / "*.xml")
        .Executes(() =>
        {
            DotNetTest(_ => _
                .SetConfiguration(Configuration)
                .SetNoBuild(InvokedTargets.Contains(Compile))
                .ResetVerbosity()
                .SetResultsDirectory(TestResultDirectory)
                .When(InvokedTargets.Contains(Coverage) || IsServerBuild, _ => _
                    .EnableCollectCoverage()
                    .SetCoverletOutputFormat(CoverletOutputFormat.cobertura)
                    .SetExcludeByFile("*.Generated.cs")
                    .When(IsServerBuild, _ => _
                        .EnableUseSourceLink()))
                .CombineWith(TestProjects, (_, v) => _
                    .SetProjectFile(v)
                    .SetLogger($"trx;LogFileName={v.Name}.trx")
                    .When(InvokedTargets.Contains(Coverage) || IsServerBuild, _ => _
                        .SetCoverletOutput(TestResultDirectory / $"{v.Name}.xml"))));

            // ArtifactsDirectory.GlobFiles("*.trx").ForEach(x =>
            //     AzurePipelines?.PublishTestResults(
            //         type: AzurePipelinesTestResultsType.VSTest,
            //         title: $"{Path.GetFileNameWithoutExtension(x)} ({AzurePipelines.StageDisplayName})",
            //         files: new string[] { x }));
        });

    string CoverageReportDirectory => ArtifactsDirectory / "coverage-report";
    // string CoverageReportArchive => ArtifactsDirectory / "coverage-report.zip";

    Target Coverage => _ => _
        .DependsOn(Test)
        .TriggeredBy(Test)
        .Consumes(Test)
        //.Produces(CoverageReportArchive)
        .Executes(() =>
        {
            ReportGenerator(_ => _
                .SetReports(TestResultDirectory / "*.xml")
                .SetReportTypes(ReportTypes.HtmlInline)
                .SetTargetDirectory(CoverageReportDirectory)
                .SetFramework("netcoreapp2.1"));

            // TestResultDirectory.GlobFiles("*.xml").ForEach(x =>
            //     AzurePipelines?.PublishCodeCoverage(
            //         AzurePipelinesCodeCoverageToolType.Cobertura,
            //         x,
            //         CoverageReportDirectory));
            //
            // CompressZip(
            //     directory: CoverageReportDirectory,
            //     archiveFile: CoverageReportArchive,
            //     fileMode: FileMode.Create);
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
				.SetSkipDuplicate(true)
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
