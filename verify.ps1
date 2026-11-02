Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Running Automated Test Suite for DistributedRateLimiter   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

dotnet test (Join-Path $PSScriptRoot "DistributedRateLimiter.slnx") --verbosity normal

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nAll tests completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`nTest suite reported errors." -ForegroundColor Red
}
