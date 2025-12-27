# =============================================================================
# Copernicus DEM Downloader Test Secrets Setup Script
# =============================================================================
# Cross-platform PowerShell script to configure user secrets for integration
# tests that require CDSE (Copernicus Data Space Ecosystem) S3 credentials.
#
# Usage:
#   pwsh -File ./scripts/setup-test-secrets.ps1
#
# This script will:
#   1. Prompt for each required secret value
#   2. Store the secrets securely using the .NET user-secrets tool
#
# The secrets are stored in the user's profile directory and are NOT
# committed to source control.
# =============================================================================

$ErrorActionPreference = "Stop"

# --- Configuration ---
# Must match the UserSecretsId in the test project csproj.
$SharedUserSecretsId = "copernicus-dem-downloader-tests"

# --- Secret Definitions ---
$secrets = [ordered]@{
    "CDSE:AccessKey" = @{
        Prompt = "Enter your CDSE S3 Access Key"
        Description = "Found in the CDSE portal under your account settings"
        IsSensitive = $false
    }
    "CDSE:SecretKey" = @{
        Prompt = "Enter your CDSE S3 Secret Key"
        Description = "Shown only once when creating S3 credentials in CDSE"
        IsSensitive = $true
    }
}

# --- Banner ---
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Copernicus DEM Downloader - Test Secrets Setup" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script configures the CDSE S3 credentials required for" -ForegroundColor White
Write-Host "running integration tests against the real Copernicus S3 API." -ForegroundColor White
Write-Host ""
Write-Host "Secrets are stored securely using the .NET user-secrets tool" -ForegroundColor White
Write-Host "in your user profile and are NOT committed to source control." -ForegroundColor White
Write-Host ""
Write-Host "You only need to run this script once per machine." -ForegroundColor Yellow
Write-Host ""

# --- Collect Secrets from User ---
Write-Host "--- Step 1: Collecting Secret Values ---" -ForegroundColor Yellow
Write-Host ""

$secretValues = @{}

foreach ($key in $secrets.Keys) {
    $config = $secrets[$key]

    Write-Host "[$key]" -ForegroundColor Magenta
    Write-Host "  $($config.Description)" -ForegroundColor DarkGray
    Write-Host ""

    if ($config.IsSensitive) {
        $value = Read-Host -Prompt "  $($config.Prompt)" -AsSecureString

        if ($value.Length -eq 0) {
            Write-Error "Value cannot be empty. Aborting."
            exit 1
        }
    } else {
        $value = Read-Host -Prompt "  $($config.Prompt)"

        if ([string]::IsNullOrWhiteSpace($value)) {
            Write-Error "Value cannot be empty. Aborting."
            exit 1
        }
    }

    $secretValues[$key] = $value
    Write-Host ""
}

Write-Host "All secrets collected successfully!" -ForegroundColor Green
Write-Host ""

# --- Apply Secrets ---
Write-Host "--- Step 2: Storing Secrets ---" -ForegroundColor Yellow
Write-Host ""
Write-Host "  UserSecretsId: $SharedUserSecretsId" -ForegroundColor Cyan
Write-Host ""

try {
    foreach ($key in $secretValues.Keys) {
        $value = $secretValues[$key]

        if ($value -is [System.Security.SecureString]) {
            $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($value)
            $plainTextValue = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
            [System.Runtime.InteropServices.Marshal]::FreeBSTR($bstr)

            dotnet user-secrets set "$key" "$plainTextValue" --id $SharedUserSecretsId 2>&1 | Out-Null

            Clear-Variable plainTextValue -ErrorAction SilentlyContinue
        } else {
            dotnet user-secrets set "$key" "$value" --id $SharedUserSecretsId 2>&1 | Out-Null
        }

        Write-Host "  [OK] $key" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "All $($secretValues.Count) secrets configured successfully!" -ForegroundColor Green
}
catch {
    Write-Host "  [ERROR] Failed to configure secrets: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# --- Summary ---
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Setup Complete" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "You can now run integration tests that require CDSE credentials." -ForegroundColor White
Write-Host ""
Write-Host "  UserSecretsId: $SharedUserSecretsId" -ForegroundColor Yellow
Write-Host ""
Write-Host "Secrets are stored in your user profile at:" -ForegroundColor White
Write-Host ""

if ($IsWindows -or $env:OS -eq "Windows_NT") {
    Write-Host "  $env:APPDATA\Microsoft\UserSecrets\$SharedUserSecretsId\secrets.json" -ForegroundColor DarkGray
} else {
    Write-Host "  ~/.microsoft/usersecrets/$SharedUserSecretsId/secrets.json" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "To run all tests:              dotnet test" -ForegroundColor White
Write-Host "To run only unit tests:        dotnet test --filter Category!=Integration" -ForegroundColor White
Write-Host "To run only integration tests: dotnet test --filter Category=Integration" -ForegroundColor White
Write-Host ""
Write-Host "To update secrets, simply run this script again." -ForegroundColor White
Write-Host "To view current secrets: dotnet user-secrets list --id $SharedUserSecretsId" -ForegroundColor DarkGray
Write-Host ""
