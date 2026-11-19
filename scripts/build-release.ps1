$ErrorActionPreference = "Stop"

$solution = ".\WarungAutoBagAssistant.PC.sln"
$project = ".\src\WABA.Desktop\WABA.Desktop.csproj"
$output = ".\artifacts\publish"

Write-Host "Checking .NET SDK..."
dotnet --info

Write-Host "Restoring Windows x64..."
dotnet restore $solution -r win-x64

Write-Host "Publishing self-contained Windows x64..."
dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $output

$exe = Join-Path $output "WABA.Desktop.exe"
if (-not (Test-Path $exe)) {
    throw "WABA.Desktop.exe was not found at $exe"
}

Write-Host "Build completed. Application output:"
Get-ChildItem $output | Select-Object Name, Length
