$ErrorActionPreference = "Stop"

Write-Host "Checking .NET SDK..."
dotnet --info

Write-Host "Restoring solution..."
dotnet restore .\WarungAutoBagAssistant.PC.sln

Write-Host "Building Release..."
dotnet build .\WarungAutoBagAssistant.PC.sln -c Release --no-restore

Write-Host "Build completed. Output:"
Write-Host ".\src\WABA.Desktop\bin\Release\net10.0-windows\WABA.Desktop.exe"
