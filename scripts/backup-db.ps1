<#
.SYNOPSIS
  Backs up Coon.Meeting's LiteDB file safely for a production IIS deployment.

.DESCRIPTION
  LiteDB opens its data file in "Direct" mode by default, which holds an exclusive lock on it
  for as long as the app is running - a plain file copy taken while IIS is serving requests
  either fails on the lock outright or, worse, can grab a snapshot mid-write. This script briefly
  stops the app pool, copies the file, and restarts it - a few seconds of downtime, scheduled for
  a low-traffic window, in exchange for a guaranteed-consistent backup.

  Run this as a Windows Scheduled Task (see the registration snippet at the bottom of this file),
  not by hand - it needs to run as an account with permission to control the app pool (typically
  the same account IIS itself runs under, or a local admin).

.PARAMETER AppPoolName
  The IIS Application Pool this API runs under. Find it in IIS Manager, or:
  Get-WebApplication | Select-Object Path, ApplicationPool

.PARAMETER DbFilePath
  Absolute path to the LiteDB file - must match this app's own Database:FilePath / the
  Database__FilePath environment variable, NOT the "data/coon-meeting.db" default (that default
  is relative to the deployed app folder, which a redeploy can wipe - production should point
  Database__FilePath at a path outside the deployment folder, e.g. on a separate data volume).

.PARAMETER BackupDir
  Where dated backup copies are written. Should be a different physical location/drive than
  DbFilePath - a backup next to the file it's backing up doesn't survive a disk failure.

.PARAMETER RetentionDays
  Backups older than this are deleted after a successful new backup. Default 30.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AppPoolName,

    [Parameter(Mandatory = $true)]
    [string]$DbFilePath,

    [Parameter(Mandatory = $true)]
    [string]$BackupDir,

    [int]$RetentionDays = 30
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration

if (-not (Test-Path $DbFilePath)) {
    throw "Database file not found at '$DbFilePath' - check DbFilePath matches this app's actual Database__FilePath."
}

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$destination = Join-Path $BackupDir "coon-meeting-$timestamp.db"

Write-Host "Stopping app pool '$AppPoolName'..."
Stop-WebAppPool -Name $AppPoolName

try {
    # Stop-WebAppPool returns before the worker process has necessarily released its file
    # handle - poll briefly rather than assuming the lock is already gone.
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-WebAppPoolState -Name $AppPoolName).Value -ne "Stopped" -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }

    Copy-Item -Path $DbFilePath -Destination $destination -ErrorAction Stop
    Write-Host "Backed up to '$destination'."
}
finally {
    Write-Host "Starting app pool '$AppPoolName'..."
    Start-WebAppPool -Name $AppPoolName
}

$cutoff = (Get-Date).AddDays(-$RetentionDays)
Get-ChildItem -Path $BackupDir -Filter "coon-meeting-*.db" |
    Where-Object { $_.LastWriteTime -lt $cutoff } |
    Remove-Item -Force

<#
.REGISTER AS A SCHEDULED TASK (run once, as an administrator, to set this up)

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument `
    '-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\backup-db.ps1" -AppPoolName "CoonMeetingApiPool" -DbFilePath "D:\CoonMeetingData\coon-meeting.db" -BackupDir "D:\CoonMeetingBackups"'
$trigger = New-ScheduledTaskTrigger -Daily -At 3am
Register-ScheduledTask -TaskName "Coon.Meeting DB Backup" -Action $action -Trigger $trigger -RunLevel Highest -User "SYSTEM"
#>
