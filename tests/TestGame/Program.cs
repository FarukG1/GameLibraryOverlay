// A harmless process fixture. Never launches a real game during automated verification.
await Task.Delay(args.Length > 0 && int.TryParse(args[0], out var milliseconds) ? milliseconds : 2500);
