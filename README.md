# .NET Project Mockup Creator
The purpose .NET Project Mockup Creator is to have a tool that test the build time on different solution architectures. If you have 100 000 source files, is it faster to have them in one huge project or in 100 projects with 1000 files each? Or is something else better? With this tool you could try different solutions. You will also get a PowerShell script that automatically builds the project and measure how long time it takes.

This project is a pure .NET Core console application that could run on Windows and most likely other platforms as well.

## Arguments

### --name
Name of project to be created.

### --path
In which directory the project will be created.

### --line-count
Number of source code lines in each source file.

### --file-count
Number of files in each project.

### --level-sizes
If you enter **-–level-sizes 2 3** there will be two libraries created that the main project is referencing to. Each of these libraries will themselves have three child libraries. You can have any number of levels and any number of projects in each sub level as you want. Be aware that the total number of projects is growing very quickly.

![Level size 2-3 sample](/images/structure-2-3.svg)

## Benchmarking

You can use the auto generated file `benchmark.ps1` to automatically test how long it takes to build and rebuild your project.
