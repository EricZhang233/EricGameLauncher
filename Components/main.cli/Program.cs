using EricGameLauncher;

var rawArgs = Environment.GetCommandLineArgs();
return await CliService.RunAsync(rawArgs);
