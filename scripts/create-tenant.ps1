<#
.SYNOPSIS
    Provisions a new Coon.Meeting tenant via the admin-gated POST /tenants endpoint.

.DESCRIPTION
    Ops-only: there is no self-serve tenant signup in v1. Prints the raw API key exactly once -
    only its hash is stored server-side, so a lost key means generating a new one, not recovering
    this one.

.EXAMPLE
    .\create-tenant.ps1 -ApiBaseUrl "https://meetings.example.com" -ProvisioningKey $env:ADMIN_KEY -Name "Acme Inc"

.EXAMPLE
    .\create-tenant.ps1 -ApiBaseUrl "http://localhost:5029" -ProvisioningKey "dev-admin-key" -Name "Test Tenant" `
        -WebhookUrl "https://acme.example.com/webhooks/coon-meeting" -WebhookSecret (New-Guid).Guid `
        -AllowedOrigins "https://app.acme.example.com" -Live
#>
param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [Parameter(Mandatory = $true)][string]$ProvisioningKey,
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$WebhookUrl,
    [string]$WebhookSecret,
    [string[]]$AllowedOrigins = @(),
    [switch]$Live
)

$body = @{
    name           = $Name
    webhookUrl     = $WebhookUrl
    webhookSecret  = $WebhookSecret
    allowedOrigins = $AllowedOrigins
    live           = [bool]$Live
} | ConvertTo-Json

$response = Invoke-RestMethod -Method Post -Uri "$($ApiBaseUrl.TrimEnd('/'))/api/v1/tenants" `
    -Headers @{ "X-Admin-Provisioning-Key" = $ProvisioningKey } `
    -ContentType "application/json" `
    -Body $body

Write-Host ""
Write-Host "Tenant provisioned:" -ForegroundColor Green
Write-Host "  Id:      $($response.id)"
Write-Host "  Name:    $($response.name)"
Write-Host "  API key: $($response.apiKey)" -ForegroundColor Yellow
Write-Host ""
Write-Host "Save the API key now - it is never shown again." -ForegroundColor Yellow
