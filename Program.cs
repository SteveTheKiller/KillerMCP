using KillerMCP.Services.Mcp;

if (await KillerMCP.ElevatedRepairBridge.TryRunAsync(args)) return;
await McpServer.RunAsync();
