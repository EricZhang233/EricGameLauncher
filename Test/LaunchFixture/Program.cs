if (args.Length != 1) return 2;
File.WriteAllText(args[0] + "." + Environment.ProcessId, "completed");
return 0;
