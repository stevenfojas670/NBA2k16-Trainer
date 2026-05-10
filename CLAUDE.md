# Working in this repo

## After making any code change

Always build **both Debug and Release** before reporting the task done:

```
dotnet build NBA2K16Trainer.csproj -c Debug -nologo -v quiet
dotnet build NBA2K16Trainer.csproj -c Release -nologo -v quiet
```

The trainer is launched from `bin/Release/net10.0-windows/NBA2K16Trainer.exe`,
so a Debug-only build leaves the user running stale code even after a commit.
Both must show `0 Warning(s) 0 Error(s)` before the change is considered shipped.

## Don't restart the trainer for the user

The user closes and relaunches the trainer themselves after a rebuild. Never
invoke the exe from a tool call — the trainer hooks the live `nba2k16.exe`
process and a Claude-spawned instance would race the user's instance for the
hook site.
