using System.CommandLine;
using CodeAtlas.Traits.Commands;

var rootCommand = CommandFactory.CreateRootCommand();
return await rootCommand.InvokeAsync(args);
