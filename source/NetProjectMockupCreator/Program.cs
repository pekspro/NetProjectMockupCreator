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
        static Task<int> Main(string[] args)
        {
            //args = $"--name fakeproject{DateTime.Now:HHmmss} --level-sizes 1 3 4 --path e:\\temp --line-count 6 --file-count 10 --force".Split(' ');
            //args = $"--name fakeproject{DateTime.Now:HHmmss} --level-sizes 1 3 4 --path e:\\temp --line-count 6 --file-count 10".Split(' ');

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
                forceOption
            };

            rootCommand.Description = ".NET Project Mockup Creator";


            // Note that the parameters of the handler method are matched according to the names of the options
            rootCommand.Handler = CommandHandler.Create<string, FileInfo, int[], int, int, bool>(async (name, path, levelSizes, fileCount, lineCount, force) =>
            {
                Project main = CreateProjectStructure(true, "", 1, levelSizes.ToList(), fileCount, lineCount);

                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine($"Project name: {name}");
                Console.WriteLine($"Path: {path.FullName}");
                Console.WriteLine($"Total number of projects: {main.TotalNumberOfSubProjects}");
                Console.WriteLine($"Total number of files: {main.TotalNumberOfSubProjectSourceFiles}");
                Console.WriteLine($"Total number of lines of code: {main.TotalNumberOfLinesOfCode}");

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

        private static Project CreateProjectStructure(bool isMain, string prefix, int projectId, List<int> projectsPerLevel, int numberOfSourceFiles, int numberOfLinesOfCode)
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
                NumberOfLinesOfCode = isMain ? 0 : numberOfLinesOfCode
            };

            if(projectsPerLevel.Any())
            {
                for(int s = 0; s < projectsPerLevel.First(); s++)
                {
                    p.SubProject.Add(CreateProjectStructure(false, isMain ? "" : projectName, s, projectsPerLevel.Skip(1).ToList(), numberOfSourceFiles, numberOfLinesOfCode));
                }
            }

            return p;
        }

        private static async Task WriteAllAsync(Project main, string path, string projectname)
        {
            path = Path.Combine(path, projectname);

            Directory.CreateDirectory(path);

            await WriteSolutionFileAsync(main, path, projectname);

            await WriteProjectsAsync(main, path, projectname);

            await WriteBenchmarkFileAsync(path, main);
        }

        private static async Task WriteBenchmarkFileAsync(string path, Project main)
        {
            string CreateEditFileList(Project proj)
            {
                string s = "'./Main/MainClass.cs'";

                while(proj.SubProject.Any())
                {
                    proj = proj.SubProject[0];

                    s += $", './{proj.Name}/LibClass0001.cs'";
                }

                return s;
            }

            string benchmarkFilename = Path.Combine(path, "benchmark.ps1");

            string filecontent =
@$"Write-Output '{main.TotalNumberOfSubProjects} projects with {main.TotalNumberOfSubProjectSourceFiles} source files with {main.TotalNumberOfLinesOfCode} lines of code.'
Write-Output ''


for($i = 1; $i -le 3; $i++)
{{
    $sw = [System.Diagnostics.Stopwatch]::startNew()

    Write-Output 'Rebuilding... ($i)'
    dotnet build --no-incremental | Out-Null

    Write-Output 'Rebuilding time: $($sw.Elapsed.TotalSeconds) seconds'
}}


for($i = 1; $i -le 3; $i++)
{{
    $sw = [System.Diagnostics.Stopwatch]::startNew()

    Write-Output 'Building without changes... ($i)'
    dotnet run --project Main | Out-Null

    Write-Output 'Building time: $($sw.Elapsed.TotalSeconds) seconds'
}}


$filesToEdit = {CreateEditFileList(main)}
for($level = 0; $level -lt $filesToEdit.Count; $level++)
{{
    for($i = 1; $i -le 3; $i++)
    {{
        Add-Content -Path $filesToEdit[$level] -Value '// Added by benchmark.ps1'

        $sw = [System.Diagnostics.Stopwatch]::startNew()
    
        Write-Output 'Changing file on level ($level) and building... ($i)'
        dotnet build | Out-Null
    
        Write-Output 'Building time: $($sw.Elapsed.TotalSeconds) seconds'
    }} 
}}


for($i = 1; $i -le 3; $i++)
{{
    $sw = [System.Diagnostics.Stopwatch]::startNew()

    Write-Output 'Running... ($i)'
    dotnet run --project Main | Out-Null

    Write-Output 'Running time: $($sw.Elapsed.TotalSeconds) seconds'
}}

".Replace("'", "\"");

            await File.WriteAllTextAsync(benchmarkFilename, filecontent, Encoding.UTF8);
        }

        private static async Task WriteSolutionFileAsync(Project main, string path, string projectname)
        {
            Guid superId = Guid.NewGuid();

            void AddProjects(Project project, StringBuilder sb)
            {
                sb.Append(
@$"Project('{{{superId}}}') = '{project.Name}', '{project.Name}\{project.Name}.csproj', '{{{project.ID}}}'
EndProject
".Replace("'", "\"")
);
                foreach(var p in project.SubProject)
                {
                    AddProjects(p, sb);
                }

            }

            void AddProjectsBuildTypes(Project project, StringBuilder sb)
            {
                sb.Append(
@$"		{{{project.ID}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{{project.ID}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{{project.ID}}}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{{{project.ID}}}.Release|Any CPU.Build.0 = Release|Any CPU
".Replace("'", "\"")
);
                foreach (var p in project.SubProject)
                {
                    AddProjectsBuildTypes(p, sb);
                }

            }

            string solutionFilename = Path.Combine(path, projectname + ".sln");

            string filecontent = @"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 16
VisualStudioVersion = 16.0.30114.105
MinimumVisualStudioVersion = 10.0.40219.1
{0}    Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
{1}   EndGlobalSection

EndGlobal
";

            StringBuilder sbProjects = new StringBuilder();
            AddProjects(main, sbProjects);
            StringBuilder sbBuildTypes= new StringBuilder();
            AddProjectsBuildTypes(main, sbBuildTypes);
            filecontent = string.Format(filecontent, sbProjects.ToString(), sbBuildTypes.ToString());

            await File.WriteAllTextAsync(solutionFilename, filecontent, Encoding.UTF8);
        }


        private static async Task WriteProjectsAsync(Project project, string basePath, string namespacePrefix)
        {
            string projectPath = Path.Combine(basePath, project.Name);

            Directory.CreateDirectory(projectPath);

            string projectFileName = Path.Combine(projectPath, project.Name + ".csproj");

            await WriteProjectFileAsync(project, projectFileName);

            for (int i = 0; i < project.NumberOfFiles; i++)
            {
                await WriteSourceFileAsync(project, projectPath, namespacePrefix, i, project.NumberOfLinesOfCode, i >= project.NumberOfFiles - 1);
            }

            foreach (var p in project.SubProject)
            {
                await WriteProjectsAsync(p, basePath, namespacePrefix + "." + p.Name);
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
                sb.AppendLine("            sb.Append((char) (48 + random.Next(0, 10)));");
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

        private static async Task WriteProjectFileAsync(Project main, string projectFileName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<Project Sdk='Microsoft.NET.Sdk'>");
            sb.AppendLine("");
            sb.AppendLine("  <PropertyGroup>");

            if(main.IsMain)
            {
                sb.AppendLine("    <OutputType>Exe</OutputType>");
                sb.AppendLine("    <TargetFramework>netcoreapp2.1</TargetFramework>");
            }
            else
            {
                sb.AppendLine("    <TargetFramework>netstandard2.0</TargetFramework>");
            }

            sb.AppendLine("  </PropertyGroup>");

            if(main.SubProject.Any())
            {

                sb.AppendLine("");
                sb.AppendLine("  <ItemGroup>");
                foreach(var subProject in main.SubProject)
                {
                    sb.AppendLine($@"    <ProjectReference Include='..\{subProject.Name}\{subProject.Name}.csproj' />");
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
