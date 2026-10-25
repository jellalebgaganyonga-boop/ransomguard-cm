<#
.SYNOPSIS
    Prepare a bench VM to stand in for a hospital workstation.

.DESCRIPTION
    Run inside the VM, from the bench disc. It does three things and nothing else:

      1. names the machine (PC-ACCUEIL, PC-LABO) -- the console shows the hostname, so the two
         workstations have to be told apart there;
      2. creates the non-administrator account `infirmier`, the everyday user of such a
         workstation. The agent runs as a service: what the nurse can do is not what it can do,
         and the bench shows that;
      3. fills C:\Users\Public\Documents\Hopital-FICTIF\ with ordinary-looking hospital documents.

    Every document is invented. Each one is named FICTIF-... and its first line reads
    "FICTIF - donnees inventees", so a file opened during a demonstration says what it is before
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
    powershell -NoProfile -ExecutionPolicy Bypass -File D:\bench-bootstrap.ps1 -ComputerName PC-LABO -NursePassword '...'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ComputerName,
    [Parameter(Mandatory)] [string] $NursePassword,
    [int] $DocumentCount = 200,
    [string] $DocumentRoot = 'C:\Users\Public\Documents\Hopital-FICTIF'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Step($n, $text) { Write-Host "[$n] $text" }

# ── 1. the workstation's name ────────────────────────────────────────────────
Step 1 "nom du poste -> $ComputerName"
if ($env:COMPUTERNAME -eq $ComputerName) {
    Write-Host '      deja ce nom'
} else {
    Rename-Computer -NewName $ComputerName -Force -ErrorAction Stop
    Write-Host '      renomme; le nom prend effet au redemarrage'
}

# ── 2. the everyday user ─────────────────────────────────────────────────────
Step 2 'utilisateur non-administrateur `infirmier`'
$secret = ConvertTo-SecureString $NursePassword -AsPlainText -Force
if (Get-LocalUser -Name 'infirmier' -ErrorAction SilentlyContinue) {
    Set-LocalUser -Name 'infirmier' -Password $secret
    Write-Host '      existe deja, mot de passe mis a jour'
} else {
    # -Description is limited to 48 characters by New-LocalUser; a longer one is not truncated,
    # it fails the whole call. Keep it short.
    New-LocalUser -Name 'infirmier' -Password $secret -FullName 'Infirmier (banc FICTIF)' `
        -Description 'Compte quotidien, sans droits administrateur' `
        -PasswordNeverExpires -AccountNeverExpires | Out-Null
    Write-Host '      cree'
}

# Members of Users only. Explicitly NOT Administrators: the point of the account is that it has
# no more rights than a nurse on a ward has.
Add-LocalGroupMember -Group 'Users' -Member 'infirmier' -ErrorAction SilentlyContinue
$isAdmin = (Get-LocalGroupMember -Group 'Administrators' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like '*\infirmier' })
if ($isAdmin) {
    Remove-LocalGroupMember -Group 'Administrators' -Member 'infirmier' -ErrorAction SilentlyContinue
    Write-Host '      retire du groupe Administrateurs'
}
Write-Host ('      administrateur : {0}' -f $(if ($isAdmin) { 'NON (retire)' } else { 'NON' }))

# ── 3. the hospital's ordinary documents ─────────────────────────────────────
Step 3 "documents FICTIFS -> $DocumentRoot"

$services = @(
    @{ Dossier = 'Patients';  Prefixe = 'dossier-patient';     Extension = 'txt' },
    @{ Dossier = 'Laboratoire'; Prefixe = 'analyse-labo';      Extension = 'csv' },
    @{ Dossier = 'Pharmacie'; Prefixe = 'ordonnance';          Extension = 'txt' },
    @{ Dossier = 'Imagerie';  Prefixe = 'compte-rendu-imagerie'; Extension = 'txt' }
)

# Invented names. They are deliberately ordinary French-Cameroonian given names with no surname,
# so a document looks like a real one at a glance without naming anybody who exists.
$prenoms = @('Awa', 'Bekono', 'Chantal', 'Divine', 'Ebenezer', 'Fadimatou', 'Guy', 'Hortense',
    'Ibrahim', 'Jeanne', 'Kamga', 'Lucienne', 'Mbarga', 'Ngassa', 'Olivier', 'Perrine',
    'Quentin', 'Rodrigue', 'Solange', 'Thierry', 'Urbain', 'Valerie', 'Wilfried', 'Yvette')

$examens = @('hemogramme', 'glycemie a jeun', 'goutte epaisse', 'ionogramme sanguin',
    'bilan hepatique', 'creatininemie', 'test rapide paludisme', 'numeration plaquettaire')

$imagerie = @('radiographie thoracique', 'echographie abdominale', 'echographie obstetricale',
    'radiographie du genou', 'scanner cerebral')

$medicaments = @('paracetamol 500 mg', 'amoxicilline 500 mg', 'artemether-lumefantrine',
    'metformine 850 mg', 'sels de rehydratation orale', 'fer + acide folique')

$entete = 'FICTIF - donnees inventees'

foreach ($s in $services) {
    New-Item -ItemType Directory -Force (Join-Path $DocumentRoot $s.Dossier) | Out-Null
}

$random = New-Object System.Random 20261025   # same bench twice = same documents
$written = 0
for ($i = 1; $i -le $DocumentCount; $i++) {
    $s = $services[$i % $services.Count]
    $prenom = $prenoms[$random.Next(0, $prenoms.Count)]
    $numero = '{0:D4}' -f $i
    $jour = (Get-Date).AddDays(-$random.Next(1, 400)).ToString('yyyy-MM-dd')
    $name = 'FICTIF-{0}-{1}-{2}.{3}' -f $s.Prefixe, $numero, $prenom, $s.Extension
    $path = Join-Path (Join-Path $DocumentRoot $s.Dossier) $name

    $corps = switch ($s.Dossier) {
        'Laboratoire' {
            $lignes = @("$entete", 'examen;valeur;unite;reference')
            foreach ($e in ($examens | Get-Random -Count 4)) {
                $lignes += ('{0};{1};g/L;valeur inventee' -f $e, $random.Next(30, 160))
            }
            $lignes
        }
        'Pharmacie' {
            @("$entete", '', "Ordonnance $numero", "Patient (invente) : $prenom",
              "Date : $jour", '',
              ($medicaments | Get-Random -Count 3 | ForEach-Object { "- $_ , 1 comprime matin et soir" }),
              '', 'Document de demonstration. Aucune valeur medicale.')
        }
        'Imagerie' {
            @("$entete", '', "Compte rendu $numero",
              "Patient (invente) : $prenom", "Date : $jour",
              "Examen : $($imagerie[$random.Next(0, $imagerie.Count)])", '',
              'Conclusion : examen sans particularite. Texte invente pour la demonstration.')
        }
        default {
            @("$entete", '', "Dossier patient $numero",
              "Patient (invente) : $prenom", "Admis le : $jour",
              "Service : Medecine generale", '',
              'Antecedents : aucun (invente).',
              'Observations : donnees de demonstration, sans valeur medicale.')
        }
    }

    Set-Content -Path $path -Value $corps -Encoding UTF8
    $written++
}

$total = (Get-ChildItem $DocumentRoot -Recurse -File).Count
Write-Host "      $written ecrits, $total fichiers en tout"
foreach ($s in $services) {
    $n = (Get-ChildItem (Join-Path $DocumentRoot $s.Dossier) -File).Count
    Write-Host ('      {0,-12} {1}' -f $s.Dossier, $n)
}

Write-Host ''
Write-Host 'Termine. Redemarrez le poste pour que le nom soit applique.'
