<#
.SYNOPSIS
    Prepare a bench VM to stand in for a hospital workstation.

.DESCRIPTION
    Run inside the VM, from the bench disc. It does three things and nothing else:

      1. names the machine (PC-RECEPTION, PC-LAB) -- the console shows the hostname, so the two
         workstations have to be told apart there;
      2. creates the non-administrator account `infirmier`, the everyday user of such a
         workstation. The agent runs as a service: what that user can do is not what the agent
         can do, and the bench shows that rather than asserting it;
      3. fills C:\Users\Public\Documents\Hospital-FICTIONAL\ with ordinary-looking hospital
         documents.

    Every document is invented. Each one is named FICTIONAL-... and its first line reads
    "FICTIONAL - invented data", so a file opened during a demonstration says what it is before
    anything is read into it. No real person's data is used, and nothing here comes from a
    patient record.

    This script does not touch SENTINEL, does not write to a canary, and simulates no attack. The
    only trigger on the bench is trigger-sentinel.cmd.

.PARAMETER ComputerName
    The name to give the workstation. The rename needs a restart, which this script does not do.

.PARAMETER NursePassword
    The password for the `infirmier` account. Required: the bench writes passwords down in
    deployment/bench/credentials.local.md on the host, it never invents one silently.

.PARAMETER DocumentCount
    How many documents to write (default 200).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File D:\bench-bootstrap.ps1 -ComputerName PC-LAB -NursePassword '...'
#>
[CmdletBinding()]
# The password arrives as text because the caller is a .cmd on the bench disc, which has no way
# to hand over a SecureString; it is converted on the next line it is used and never written to
# disk by this script. The account it sets is a local, non-administrator account on a throwaway
# bench VM, and its password is recorded in deployment/bench/credentials.local.md (gitignored).
# Taking a PSCredential here would only move the same text into the .cmd's prompt.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingPlainTextForPassword', 'NursePassword',
    Justification = 'Called from a .cmd on the bench disc, which cannot pass a SecureString.')]
param(
    [Parameter(Mandatory)] [string] $ComputerName,
    [Parameter(Mandatory)] [string] $NursePassword,
    [int] $DocumentCount = 200,
    [string] $DocumentRoot = 'C:\Users\Public\Documents\Hospital-FICTIONAL'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Step($n, $text) { Write-Host "[$n] $text" }

# -- 1. the workstation's name -----------------------------------------------
Step 1 "workstation name -> $ComputerName"
if ($env:COMPUTERNAME -eq $ComputerName) {
    Write-Host '      already named'
} else {
    Rename-Computer -NewName $ComputerName -Force -ErrorAction Stop
    Write-Host '      renamed; the name applies after a restart'
}

# -- 2. the everyday user ----------------------------------------------------
Step 2 'non-administrator user `infirmier`'
$secret = ConvertTo-SecureString $NursePassword -AsPlainText -Force
if (Get-LocalUser -Name 'infirmier' -ErrorAction SilentlyContinue) {
    Set-LocalUser -Name 'infirmier' -Password $secret
    Write-Host '      already exists, password updated'
} else {
    # -Description is limited to 48 characters by New-LocalUser; a longer one is not truncated,
    # it fails the whole call. Keep it short.
    New-LocalUser -Name 'infirmier' -Password $secret -FullName 'Nurse (FICTIONAL bench)' `
        -Description 'Everyday workstation account, no admin rights' `
        -PasswordNeverExpires -AccountNeverExpires | Out-Null
    Write-Host '      created'
}

# Members of Users only. Explicitly NOT Administrators: the point of the account is that it has
# no more rights than a nurse on a ward has.
Add-LocalGroupMember -Group 'Users' -Member 'infirmier' -ErrorAction SilentlyContinue
$isAdmin = (Get-LocalGroupMember -Group 'Administrators' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like '*\infirmier' })
if ($isAdmin) {
    Remove-LocalGroupMember -Group 'Administrators' -Member 'infirmier' -ErrorAction SilentlyContinue
    Write-Host '      removed from the Administrators group'
}
Write-Host ('      administrator : {0}' -f $(if ($isAdmin) { 'NO (removed)' } else { 'NO' }))

# -- 3. the hospital's ordinary documents ------------------------------------
Step 3 "FICTIONAL documents -> $DocumentRoot"

$services = @(
    @{ Folder = 'Patients';   Prefix = 'patient-record';   Extension = 'txt' },
    @{ Folder = 'Laboratory'; Prefix = 'lab-result';       Extension = 'csv' },
    @{ Folder = 'Pharmacy';   Prefix = 'prescription';     Extension = 'txt' },
    @{ Folder = 'Imaging';    Prefix = 'imaging-report';   Extension = 'txt' }
)

# Invented names. Ordinary Cameroonian given names with no surname, so a document looks like a
# real one at a glance without naming anybody who exists.
$givenNames = @('Awa', 'Bekono', 'Chantal', 'Divine', 'Ebenezer', 'Fadimatou', 'Guy', 'Hortense',
    'Ibrahim', 'Jeanne', 'Kamga', 'Lucienne', 'Mbarga', 'Ngassa', 'Olivier', 'Perrine',
    'Quentin', 'Rodrigue', 'Solange', 'Thierry', 'Urbain', 'Valerie', 'Wilfried', 'Yvette')

$labTests = @('full blood count', 'fasting blood glucose', 'thick blood film', 'serum electrolytes',
    'liver function panel', 'serum creatinine', 'rapid malaria test', 'platelet count')

$imagingStudies = @('chest X-ray', 'abdominal ultrasound', 'obstetric ultrasound',
    'knee X-ray', 'head CT scan')

$medicines = @('paracetamol 500 mg', 'amoxicillin 500 mg', 'artemether-lumefantrine',
    'metformin 850 mg', 'oral rehydration salts', 'iron + folic acid')

$header = 'FICTIONAL - invented data'

foreach ($s in $services) {
    New-Item -ItemType Directory -Force (Join-Path $DocumentRoot $s.Folder) | Out-Null
}

$random = New-Object System.Random 20261025   # same bench twice = same documents
$written = 0
for ($i = 1; $i -le $DocumentCount; $i++) {
    $s = $services[$i % $services.Count]
    $name = $givenNames[$random.Next(0, $givenNames.Count)]
    $number = '{0:D4}' -f $i
    $day = (Get-Date).AddDays(-$random.Next(1, 400)).ToString('yyyy-MM-dd')
    $fileName = 'FICTIONAL-{0}-{1}-{2}.{3}' -f $s.Prefix, $number, $name, $s.Extension
    $path = Join-Path (Join-Path $DocumentRoot $s.Folder) $fileName

    $body = switch ($s.Folder) {
        'Laboratory' {
            $lines = @("$header", 'test;value;unit;reference')
            foreach ($t in ($labTests | Get-Random -Count 4)) {
                $lines += ('{0};{1};g/L;invented value' -f $t, $random.Next(30, 160))
            }
            $lines
        }
        'Pharmacy' {
            @("$header", '', "Prescription $number", "Patient (invented) : $name",
              "Date : $day", '',
              ($medicines | Get-Random -Count 3 | ForEach-Object { "- $_ , one tablet morning and evening" }),
              '', 'Demonstration document. No medical value.')
        }
        'Imaging' {
            @("$header", '', "Report $number",
              "Patient (invented) : $name", "Date : $day",
              "Study : $($imagingStudies[$random.Next(0, $imagingStudies.Count)])", '',
              'Conclusion : unremarkable study. Text invented for the demonstration.')
        }
        default {
            @("$header", '', "Patient record $number",
              "Patient (invented) : $name", "Admitted on : $day",
              'Ward : General medicine', '',
              'History : none (invented).',
              'Notes : demonstration data, of no medical value.')
        }
    }

    Set-Content -Path $path -Value $body -Encoding UTF8
    $written++
}

$total = (Get-ChildItem $DocumentRoot -Recurse -File).Count
Write-Host "      $written written, $total files in total"
foreach ($s in $services) {
    $n = (Get-ChildItem (Join-Path $DocumentRoot $s.Folder) -File).Count
    Write-Host ('      {0,-12} {1}' -f $s.Folder, $n)
}

Write-Host ''
Write-Host 'Done. Restart the workstation for the name to apply.'
