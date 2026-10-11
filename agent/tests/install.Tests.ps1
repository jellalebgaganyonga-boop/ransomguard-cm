# Pester 5 -- AGT-INST-001: install.ps1 never installs the LocalSystem executables in a folder users
# can modify. Runs the real script in a child process (it needs an elevated session: the CI runner).
# Every refused case stops before any download, extraction or service change.

BeforeAll {
    $script:Installer = Join-Path $PSScriptRoot '..\install.ps1'
    $script:ProgramFiles = [Environment]::GetFolderPath('ProgramFiles')

    function Invoke-Installer {
        param([string]$InstallPath, [string]$Environment = '')
        $previous = $env:DOTNET_ENVIRONMENT
        $env:DOTNET_ENVIRONMENT = $Environment
        try {
            # A local GRID that does not exist: an accepted path goes as far as the download, and fails there.
            $output = & pwsh -NoProfile -File $script:Installer -OTP 'test-otp-0123456789' -GridServer '127.0.0.1' -InstallPath $InstallPath 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        } finally {
            $env:DOTNET_ENVIRONMENT = $previous
        }
    }
}

Describe 'install.ps1 -InstallPath (AGT-INST-001)' {
    It 'refuses a folder outside Program Files' {
        $path = 'C:\RGAgentOutside-' + [guid]::NewGuid().ToString('N')
        $run = Invoke-Installer -InstallPath $path
        Write-Host $run.Output
        $run.ExitCode | Should -Be 1
        $run.Output | Should -Match 'is outside'
        $run.Output | Should -Not -Match 'Downloading agent package'
        Test-Path $path | Should -BeFalse
    }

    It 'refuses Program Files itself (the script deletes the installation folder)' {
        $run = Invoke-Installer -InstallPath $script:ProgramFiles
        $run.ExitCode | Should -Be 1
        $run.Output | Should -Match 'drive root or %ProgramFiles% itself'
        $run.Output | Should -Not -Match 'Downloading agent package'
    }

    It 'refuses a drive root, even in Development' {
        $run = Invoke-Installer -InstallPath 'C:\' -Environment 'Development'
        $run.ExitCode | Should -Be 1
        $run.Output | Should -Match 'drive root or %ProgramFiles% itself'
    }

    It 'accepts the default folder under Program Files (goes on to the download)' {
        $run = Invoke-Installer -InstallPath (Join-Path $script:ProgramFiles 'RansomGuard-CM-PesterTest')
        Write-Host $run.Output
        $run.Output | Should -Not -Match 'is outside'
        $run.Output | Should -Match 'Downloading agent package'
    }

    It 'accepts a folder outside Program Files in Development only, with a warning' {
        $run = Invoke-Installer -InstallPath ('C:\RGAgentDev-' + [guid]::NewGuid().ToString('N')) -Environment 'Development'
        $run.Output | Should -Match 'allowed in Development only'
        $run.Output | Should -Match 'Downloading agent package'
    }
}
