# === deploy.ps1 ===

$serviceName   = "TheFirmDiscordBot"
$projectFolder = "C:\FirmDiscordBot"
$publishFolder = "$projectFolder\publish"

Write-Host "🔧 Stopping service..."
Stop-Service -Name $serviceName -Force

Write-Host "🧼 Cleaning old publish folder..."
if (Test-Path $publishFolder) {
    Remove-Item "$publishFolder\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $publishFolder
}

Write-Host "🛠️ Publishing to $publishFolder..."
dotnet publish "$projectFolder" -c Release -o $publishFolder

Write-Host "🚀 Restarting service..."
Start-Service -Name $serviceName

Write-Host "✅ Bot updated and restarted!"
