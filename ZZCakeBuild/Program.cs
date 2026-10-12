using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace CakeBuild
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // CakeBuild.dll lives at <repo>/ZZCakeBuild/bin/<config>/.
            // Going three levels up gives us the repository root, regardless of
            // where build.ps1 was launched from.
            var assemblyDir = AppContext.BaseDirectory;
            var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", ".."));
            Directory.SetCurrentDirectory(repoRoot);

            return new CakeHost()
                .UseContext<BuildContext>()
                .Run(args);
        }
    }

    // Minimal shape of modinfo.json — only the fields we need.
    public class ModInfo
    {
        [JsonProperty("modid")] public string ModID { get; set; } = "";
        [JsonProperty("version")] public string Version { get; set; } = "";
    }

    public class BuildContext : FrostingContext
    {
        // Name of the .csproj file (without extension). Located at the repo root.
        public const string ProjectName = "warwthtreason";

        public string BuildConfiguration { get; }
        public string Version { get; }
        public string Name { get; }
        public bool SkipJsonValidation { get; }

        public BuildContext(ICakeContext context) : base(context)
        {
            BuildConfiguration = context.Argument("configuration", "Release");
            SkipJsonValidation = context.Argument("skipJsonValidation", false);

            var json = File.ReadAllText("modinfo.json");
            var modInfo = JsonConvert.DeserializeObject<ModInfo>(json)!;
            Version = modInfo.Version;
            Name = modInfo.ModID;
        }
    }

    [TaskName("ValidateJson")]
    public sealed class ValidateJsonTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            if (context.SkipJsonValidation) return;

            var jsonFiles = context.GetFiles("assets/**/*.json");
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(file.FullPath);
                    JToken.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new Exception(
                        $"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}",
                        ex);
                }
            }
        }
    }

    [TaskName("Build")]
    [IsDependentOn(typeof(ValidateJsonTask))]
    public sealed class BuildTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            context.DotNetClean($"{BuildContext.ProjectName}.csproj",
                new DotNetCleanSettings { Configuration = context.BuildConfiguration });

            context.DotNetPublish($"{BuildContext.ProjectName}.csproj",
                new DotNetPublishSettings { Configuration = context.BuildConfiguration });
        }
    }

    [TaskName("Package")]
    [IsDependentOn(typeof(BuildTask))]
    public sealed class PackageTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            // Ensure the Releases root exists, but do NOT wipe it — Debug and
            // Release builds produce different ZIPs that should coexist.
            context.EnsureDirectoryExists("Releases");

            // Wipe only the temporary staging folder for this mod.
            string stagingDir = $"Releases/{context.Name}";
            if (context.DirectoryExists(stagingDir))
                context.DeleteDirectory(stagingDir, new DeleteDirectorySettings { Recursive = true, Force = true });

            context.EnsureDirectoryExists(stagingDir);

            // Copy the compiled DLL + deps from the correct configuration folder.
            context.CopyFiles(
                $"bin/{context.BuildConfiguration}/Mods/mod/publish/*",
                stagingDir);

            // Copy lang files if they exist.
            if (context.DirectoryExists("assets"))
                context.CopyDirectory("assets", $"{stagingDir}/assets");

            // Copy the manifest.
            context.CopyFile("modinfo.json", $"{stagingDir}/modinfo.json");

            // Optional icon.
            if (context.FileExists("modicon.png"))
                context.CopyFile("modicon.png", $"{stagingDir}/modicon.png");

            // Zip with a configuration-specific name so Debug and Release
            // don't overwrite each other.
            context.Zip(
                stagingDir,
                $"Releases/{context.Name}_{context.Version}_{context.BuildConfiguration}.zip");
        }
    }

    [TaskName("Default")]
    [IsDependentOn(typeof(PackageTask))]
    public class DefaultTask : FrostingTask { }
}