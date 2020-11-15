using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetProjectMockupCreator
{
    class Program
    {
        const string ApplicationDirectoryName = "Application";
        const string NuGetLibrariesDirectoryName = "NuGetLibraries";
        const string NuGetPackagesDirectoryName = "NuGetPackages";

        static Task<int> Main(string[] args)
        {
            // args = $"--name fakeproject{DateTime.Now:HHmmss} --level-sizes 1 2 3 --path r:\\temp --line-count 6 --file-count 10 --nuget-level 3 --force".Split(' ');
            // args = $"--name fakeproject{DateTime.Now:HHmmss} --level-sizes 1 3 4 --path e:\\temp --line-count 6 --file-count 10".Split(' ');

            var nameOption = new Option<string>(
                "--name",
                description: "Name of project to be created.")
                {
                    Required = true,
                };

            var pathOption = new Option<FileInfo>(
                    "--path",
                    getDefaultValue: () => new FileInfo("./"),
                    description: "In which directory the project will be created.");

            var levelsOption = new Option<int[]>(
                "--level-sizes",
                description: "How many project to be created in each level (for instance 2 4 5).")
            {
                Required = true
            };
            levelsOption.Argument.AddValidator(i =>
            {
                var t = i.GetValueOrDefault<int[]>();

                if (t.Length <= 0)
                {
                    return "Not enough levels.";
                }

                if(t.Any(a => a <= 0))
                {
                    return "Levels size has to be a positive number.";
                }

                return null;
            });

            var linesOption = new Option<int>(
                "--line-count",
                getDefaultValue: () => 200,
                description: "Number of source code lines in each source file.");

            var fileCountOption = new Option<int>(
                "--file-count",
                getDefaultValue: () => 50,
                description: "Number of files in each project.");

            var nugetOption = new Option<int>(
                "--nuget-level",
                getDefaultValue: () => int.MaxValue,
                description: "From wich level projects will be nuget packages.");

            var forceOption = new Option<bool>(
                "--force",
                description: "Create without verification.");


            // Create a root command with some options
            var rootCommand = new RootCommand
            {
                nameOption,
                levelsOption,
                pathOption,
                linesOption,
                fileCountOption,
                nugetOption,
                forceOption
            };

            rootCommand.Description = ".NET Project Mockup Creator";


            // Note that the parameters of the handler method are matched according to the names of the options
            rootCommand.Handler = CommandHandler.Create<string, FileInfo, int[], int, int, int, bool>(async (name, path, levelSizes, fileCount, lineCount, nugetLevel, force) =>
            {
                Project main = CreateProjectStructure(true, "", 1, levelSizes.ToList(), fileCount, lineCount, nugetLevel);

                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine($"Project name: {name}");
                Console.WriteLine($"Path: {path.FullName}");
                Console.WriteLine($"Total number of projects: {main.TotalNumberOfSubProjects}");
                Console.WriteLine($"Total number of files: {main.TotalNumberOfSubProjectSourceFiles}");
                Console.WriteLine($"Total number of lines of code: {main.TotalNumberOfLinesOfCode}");
                if(nugetLevel >= 0 && nugetLevel < int.MaxValue)
                {
                    Console.WriteLine($"Projects from level {nugetLevel} will be nuget packages.");
                }

                if(!force)
                {
                    Console.Write($"Do you want to continue? ");

                    var key = Console.ReadKey();

                    if(key.KeyChar != 'y' && key.KeyChar != 'Y')
                    {
                        return;
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Creating projects...");
                await WriteAllAsync(main, path.FullName, name);
                Console.WriteLine("Done.");
            });

            return rootCommand.InvokeAsync(args);
        }

        private static Project CreateProjectStructure(bool isMain, string prefix, int projectId, List<int> projectsPerLevel, int numberOfSourceFiles, int numberOfLinesOfCode, int levelsLeftUntilNugetPackage)
        {
            string projectName = "Main";
            if(!isMain)
            {
                projectName = (prefix + "_Lib" + (projectId + 1).ToString("000")).Trim('_');
            }

            Project p = new Project()
            {
                ID = Guid.NewGuid(),
                IsMain = isMain,
                Name = projectName,
                NumberOfFiles = isMain ? 1 : numberOfSourceFiles,
                NumberOfLinesOfCode = isMain ? 0 : numberOfLinesOfCode,
                IsNugetPackage = levelsLeftUntilNugetPackage <= 0
            };

            if(projectsPerLevel.Any())
            {
                for(int s = 0; s < projectsPerLevel.First(); s++)
                {
                    p.SubProject.Add(CreateProjectStructure(false, isMain ? "" : projectName, s, projectsPerLevel.Skip(1).ToList(), numberOfSourceFiles, numberOfLinesOfCode, levelsLeftUntilNugetPackage - 1));
                }
            }

            return p;
        }

        private static async Task WriteAllAsync(Project main, string path, string solutionName)
        {
            path = Path.Combine(path, solutionName);

            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, ApplicationDirectoryName));

            await WriteSolutionFileAsync(main, Path.Combine(path, ApplicationDirectoryName), solutionName, false);

            if(main.SomeHasNugetPackage)
            {
                Directory.CreateDirectory(Path.Combine(path, NuGetLibrariesDirectoryName));
                await WriteSolutionFileAsync(main, Path.Combine(path, NuGetLibrariesDirectoryName), solutionName, true);
            }

            await WriteProjectsAsync(main, path, solutionName, solutionName);

            await WriteNugetPackageConfigurationAsync(main, path);

            await WriteBenchmarkFileAsync(path, main, solutionName);
        }

        private static async Task WriteBenchmarkFileAsync(string path, Project main, string solutionName)
        {
            string CreateEditFileList(Project proj)
            {
                string s = $"'{Path.Combine("./", ApplicationDirectoryName, "Main/MainClass.cs")}'";

                while(proj.SubProject.Any())
                {
                    proj = proj.SubProject[0];

                    if(proj.IsNugetPackage)
                    {
                        break;
                    }

                    //s += $", './{proj.Name}/LibClass0001.cs'";
                    s += $", '{Path.Combine("./", ApplicationDirectoryName, proj.Name, "LibClass0001.cs")}'";
                }

                return s;
            }

            string benchmarkFilename = Path.Combine(path, "benchmark.ps1");
            string buildProjectName = Path.Combine("./", ApplicationDirectoryName, $"{solutionName}.sln");
            string buildNugetProjectName = Path.Combine("./", NuGetLibrariesDirectoryName, $"{solutionName}-nuget.sln");

            string filecontent = @$"Write-Output '{main.TotalNumberOfSubProjects} projects with {main.TotalNumberOfSubProjectSourceFiles} source files with {main.TotalNumberOfLinesOfCode} lines of code.'";

            if(main.TotalNumberOfNugetProjects > 0)
            {
                filecontent += @$"
Write-Output '{main.TotalNumberOfNugetProjects} projects are NuGet packages and will not be benchmarked.'
Write-Output 'Building NuGet packages projects...'

dotnet build {buildNugetProjectName} | Out-Null
dotnet pack {buildNugetProjectName} --no-build --include-source --include-symbols --output {NuGetPackagesDirectoryName}
";
            }

filecontent += @$"

[double[]] $testBestValues = @()
[string[]] $testTitles = @()

[double[]] $temp = @()
Write-Output ''
for($i = 1; $i -le 3; $i++)
{{
    $sw = [System.Diagnostics.Stopwatch]::startNew()

    Write-Output 'Rebuilding... ($i)'
    dotnet build {buildProjectName} --no-incremental | Out-Null
    $sw.Stop()

    Write-Output 'Rebuilding time: $($sw.Elapsed.TotalSeconds) seconds'
    $temp += $sw.Elapsed.TotalSeconds
}}

$testTitles  += 'Rebuild time'
$testBestValues += ($temp | Measure-Object -Minimum).Minimum


[double[]] $temp = @()
Write-Output ''
for ($i = 1; $i -le 3; $i++)
{{
    $sw = [System.Diagnostics.Stopwatch]::startNew()

    Write-Output 'Building without changes... ($i)'
    dotnet build {buildProjectName} --project Main | Out-Null
    $sw.Stop()

    Write-Output 'Building time: $($sw.Elapsed.TotalSeconds) seconds'
    $temp += $sw.Elapsed.TotalSeconds
}}

$testTitles  += 'Build without changes'
$testBestValues += ($temp | Measure-Object -Minimum).Minimum


$filesToEdit = {CreateEditFileList(main)}
for($level = 0; $level -lt $filesToEdit.Count; $level++)
{{
    [double[]] $temp = @()
    Write-Output ''
    for($i = 1; $i -le 3; $i++)
    {{
        Add-Content -Path $filesToEdit[$level] -Value '// Added by benchmark.ps1'

        $sw = [System.Diagnostics.Stopwatch]::startNew()
    
        Write-Output 'Changing file on level ($level) and building... ($i)'
        dotnet build {buildProjectName} | Out-Null
        $sw.Stop()
    
        Write-Output 'Building time: $($sw.Elapsed.TotalSeconds) seconds'
        $temp += $sw.Elapsed.TotalSeconds
    }} 

    $testTitles  += 'Changing file on level ($level)'
    $testBestValues += ($temp | Measure-Object -Minimum).Minimum
}}


# [double[]] $temp = @()
# Write-Output ''
# for($i = 1; $i -le 3; $i++)
# {{
#     $sw = [System.Diagnostics.Stopwatch]::startNew()
# 
#     Write-Output 'Running... ($i)'
#     dotnet run {buildProjectName} --project Main | Out-Null
#     $sw.Stop()
# 
#     Write-Output 'Running time: $($sw.Elapsed.TotalSeconds) seconds'
# }}

Write-Output ''
Write-Output 'Best results:'

for ($i = 0; $i -lt $testTitles.Length; $i++) {{
    Write-Output '$($testTitles[$i]): $($testBestValues[$i])'
}}

".Replace("'", "\"");

            await File.WriteAllTextAsync(benchmarkFilename, filecontent, Encoding.UTF8);
        }

        private static async Task WriteNugetPackageConfigurationAsync(Project main, string path)
        {
            if(!main.SomeHasNugetPackage)
            {
                return;
            }

            string packageDir = Path.Combine(path, NuGetPackagesDirectoryName);
            Directory.CreateDirectory(packageDir);

            string solutionFilename = Path.Combine(path, "nuget.config");

            string filecontent = @$"<?xml version='1.0' encoding='utf-8'?>
  <configuration>
    <packageSources>
      <add key='LocalPackages' value='./{NuGetPackagesDirectoryName}' />
    </packageSources>
    <activePackageSource>
      <!-- this tells that all of them are active -->
      <add key='All' value='(Aggregate source)' />
    </activePackageSource>
 </configuration>
".Replace("'", "\"");

            await File.WriteAllTextAsync(solutionFilename, filecontent, Encoding.UTF8);
        }

        private static async Task WriteSolutionFileAsync(Project main, string path, string solutionname, bool nugetPackages)
        {
            Guid superId = Guid.NewGuid();

            void AddProjects(Project project, StringBuilder sb)
            {
                if(project.IsNugetPackage == nugetPackages)
                {
                    sb.AppendLine(@$"Project('{{{superId.ToString().ToUpper()}}}') = '{project.Name}', '{project.Name}\{project.Name}.csproj', '{{{project.ID.ToString().ToUpper()}}}'".Replace("'", "\""));
                    if(project.IsNugetPackage && project.SubProject.Any())
                    {
                        sb.AppendLine("\tProjectSection(ProjectDependencies) = postProject");
                        foreach(var subproject in project.SubProject)
                        {
                            sb.AppendLine($"\t\t{{{subproject.ID.ToString().ToUpper()}}} = {{{subproject.ID.ToString().ToUpper()}}}");
                        }
                        sb.AppendLine("\tEndProjectSection");
                    }

                    sb.AppendLine(@$"EndProject");
                }

                foreach(var p in project.SubProject)
                {
                    AddProjects(p, sb);
                }
            }

            void AddProjectsBuildTypes(Project project, StringBuilder sb)
            {
                if(project.IsNugetPackage == nugetPackages)
                {
                    sb.Append(
@$"		{{{project.ID.ToString().ToUpper()}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{{project.ID.ToString().ToUpper()}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{{project.ID.ToString().ToUpper()}}}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{{{project.ID.ToString().ToUpper()}}}.Release|Any CPU.Build.0 = Release|Any CPU
".Replace("'", "\"")
    );
                }

                foreach (var p in project.SubProject)
                {
                    AddProjectsBuildTypes(p, sb);
                }

            }

            string solutionFilename = Path.Combine(path, solutionname + (nugetPackages ? "-nuget" : "") +".sln");

            string filecontent = @"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 16
VisualStudioVersion = 16.0.30114.105
MinimumVisualStudioVersion = 10.0.40219.1
{0}Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
{1}    EndGlobalSection
EndGlobal
";

            StringBuilder sbProjects = new StringBuilder();
            AddProjects(main, sbProjects);
            StringBuilder sbBuildTypes= new StringBuilder();
            AddProjectsBuildTypes(main, sbBuildTypes);
            filecontent = string.Format(filecontent, sbProjects.ToString(), sbBuildTypes.ToString());

            await File.WriteAllTextAsync(solutionFilename, filecontent, Encoding.UTF8);
        }


        private static async Task WriteProjectsAsync(Project project, string basePath, string namespacePrefix, string solutionName)
        {
            string projectPath = Path.Combine(basePath, (project.IsNugetPackage ? NuGetLibrariesDirectoryName : ApplicationDirectoryName), project.Name);

            Directory.CreateDirectory(projectPath);

            string projectFileName = Path.Combine(projectPath, project.Name + ".csproj");

            await WriteProjectFileAsync(project, projectFileName, solutionName);

            for (int i = 0; i < project.NumberOfFiles; i++)
            {
                await WriteSourceFileAsync(project, projectPath, namespacePrefix, i, project.NumberOfLinesOfCode, i >= project.NumberOfFiles - 1);
            }

            foreach (var p in project.SubProject)
            {
                await WriteProjectsAsync(p, basePath, namespacePrefix + "." + p.Name, solutionName);
            }
        }

        private static string GetClassName(int id)
        {
            return "LibClass" + (id + 1).ToString("0000");
        }

        private static async Task WriteSourceFileAsync(Project project, string projectPath, string namespacePrefix, int id, int numberOfLinesOfCode, bool isLastProjectSourceFile)
        {
            string className = project.IsMain ? $"{project.Name}Class" : GetClassName(id);

            string sourceFileName = Path.Combine(projectPath, className + ".cs");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespacePrefix}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static class {className}");
            sb.AppendLine("    {");
            if(project.IsMain)
            {
                sb.AppendLine("        public static void Main(string[] args)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (args.Length <= 0)");
                sb.AppendLine("            {");
                sb.AppendLine("                Console.WriteLine($\"No arguments. Will not run.\");");
                sb.AppendLine("                return;");
                sb.AppendLine("            }");
                sb.AppendLine("            ");
                sb.AppendLine("            Console.WriteLine($\"Running application...\");");
                sb.AppendLine($"            StringBuilder sb = new StringBuilder({project.TotalNumberOfLinesOfCode});");
                sb.AppendLine("            Random random = new Random();");
            }
            else
            {
                sb.AppendLine("        public static void BuildString(StringBuilder sb, Random random)");
                sb.AppendLine("        {");
            }

            for(int i = 0; i < numberOfLinesOfCode; i++)
            {
                sb.AppendLine("            sb.Append((char) ('0' + random.Next(0, 10)));");
            }

            int minSubProjectId = id;
            int maxSubProjectId = id + 1;
            if(isLastProjectSourceFile)
            {
                maxSubProjectId = project.SubProject.Count;
            }

            for(int p = minSubProjectId; p < maxSubProjectId && p < project.SubProject.Count; p++)
            {
                sb.AppendLine();

                var subProject = project.SubProject[p];

                for(int s = 0; s < subProject.NumberOfFiles; s++)
                {
                    sb.AppendLine($"            {subProject.Name}.{GetClassName(s)}.BuildString(sb, random);");
                }
            }


            sb.AppendLine();
            if(project.IsMain)
            {
                sb.AppendLine($"            Console.WriteLine($'Lines of code executed: {{sb.Length}}');".Replace("'", "\""));
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            await File.WriteAllTextAsync(sourceFileName, sb.ToString(), Encoding.UTF8);
        }

        private static async Task WriteProjectFileAsync(Project project, string projectFileName, string solutionName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<Project Sdk='Microsoft.NET.Sdk'>");
            sb.AppendLine("");
            sb.AppendLine("  <PropertyGroup>");

            if(project.IsMain)
            {
                sb.AppendLine("    <OutputType>Exe</OutputType>");
                sb.AppendLine("    <TargetFramework>netcoreapp2.1</TargetFramework>");
            }
            else
            {
                sb.AppendLine("    <TargetFramework>netstandard2.0</TargetFramework>");
                if(project.IsNugetPackage)
                {
                    sb.AppendLine($"    <PackageId>{solutionName}.{project.Name}</PackageId>");
                    sb.AppendLine($"    <Version>1.0.0</Version>");
                }
            }

            sb.AppendLine("  </PropertyGroup>");
                        
            if(project.SubProject.Any())
            {

                sb.AppendLine("");
                sb.AppendLine("  <ItemGroup>");
                foreach(var subProject in project.SubProject)
                {
                    if(subProject.IsNugetPackage)
                    {
                        sb.AppendLine($@"    <PackageReference Include='{solutionName}.{subProject.Name}' Version='1.0.0' />");
                    }
                    else
                    {
                        sb.AppendLine($@"    <ProjectReference Include='..\{subProject.Name}\{subProject.Name}.csproj' />");
                    }
                }
                sb.AppendLine("  </ItemGroup>");
            }

            sb.AppendLine("");
            sb.AppendLine("</Project>");

            await File.WriteAllTextAsync(projectFileName, sb.ToString().Replace("'", "\""), Encoding.UTF8);
        }
    }

    public class Project
    {
        public Guid ID { get; set; } = Guid.NewGuid();

        public bool IsMain { get; set; }

        public int NumberOfFiles { get; set; }

        public int NumberOfLinesOfCode { get; set; }

        public string Name { get; set; }

        public List<Project> SubProject { get; set; } = new List<Project>();

        public bool IsNugetPackage { get; internal set; }

        public bool SomeHasNugetPackage
        {
            get
            {
                return IsNugetPackage || SubProject.Any(a => a.SomeHasNugetPackage);
            }
        }

        public int TotalNumberOfNugetProjects
        {
            get
            {
                return (IsNugetPackage ? 1 : 0) + SubProject.Sum(a => a.TotalNumberOfNugetProjects);
            }
        }

        public int TotalNumberOfSubProjects
        {
            get
            {
                return SubProject.Count + SubProject.Sum(a => a.TotalNumberOfSubProjects);
            }
        }

        public int TotalNumberOfSubProjectSourceFiles
        {
            get
            {
                return SubProject.Sum(a => a.NumberOfFiles) + SubProject.Sum(a => a.TotalNumberOfSubProjectSourceFiles);
            }
        }

        public int TotalNumberOfLinesOfCode
        {
            get
            {
                return SubProject.Sum(a => a.NumberOfLinesOfCode * a.NumberOfFiles) + SubProject.Sum(a => a.TotalNumberOfLinesOfCode);
            }
        }       
    }
}
