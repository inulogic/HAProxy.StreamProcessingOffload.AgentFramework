var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");
var versionNumber = Argument<string>("versionNumber", "0.0.0");

var solution = GetFiles("./HAProxy.StreamProcessingOffload.AgentFramework.sln").First();
var outDir = Directory("./artifacts");

Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("Package")
    ;

Task("Clean")
    .Does(_ =>
{
    Information("Cleaning {0}, bin and obj folders", outDir);

    CleanDirectory(outDir);
    CleanDirectories("./src/**/bin");
    CleanDirectories("./src/**/obj");
});

Task("Build")
    .Does(_ =>
{
    Information("Building solution {0} v{1}", solution.GetFilenameWithoutExtension(), versionNumber);

    DotNetBuild(solution.FullPath, new DotNetBuildSettings()
    {
        Configuration = configuration,
        MSBuildSettings = new DotNetMSBuildSettings()
            .SetVersion(versionNumber)
            .SetFileVersion(versionNumber)
            .WithProperty("AssemblyVersion", $"{Version.Parse(versionNumber).Major}.0.0")
    });
});

Task("Test")
    .Does(_ =>
{
    Information("Testing solution {0}", solution.GetFilenameWithoutExtension());

    DotNetTest(solution.FullPath, new DotNetTestSettings
    {
        Configuration = configuration,
        ResultsDirectory = outDir,
        Loggers = new []{"trx"},
        NoBuild = true,
        NoRestore = true
    });
});

Task("Package")
    .Does(_ =>
{
    Information("Packaging solution {0}", solution.GetFilenameWithoutExtension());

    DotNetPack(solution.FullPath, new DotNetPackSettings {
        Configuration = configuration,
        OutputDirectory = outDir,
        NoBuild = true,
        NoRestore = true,
        MSBuildSettings = new DotNetMSBuildSettings()
            .SetVersion(versionNumber)
    });
});

RunTarget(target);