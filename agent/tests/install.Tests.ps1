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

Describe 'install.ps1 reports the version of the package it installs' {
    # The version shown is read from the extracted agent assembly, never a constant: the script
    # said 0.3.0 for months after it stopped being true (LOT 2 demo). The package served here holds
    # the agent assembly the CI just built (Release) and no appsettings.json, so the script shows
    # the version after extraction, then stops at step 4: no service is created or started.
    BeforeAll {
        $script:Assembly = Join-Path $PSScriptRoot '..\src\RansomGuard.Agent.Service\bin\Release\net8.0-windows\RansomGuard.Agent.Service.dll'
        if (-not (Test-Path $script:Assembly)) { throw "build the agent (Release) first: $script:Assembly not found" }
        $script:Assembly = (Resolve-Path $script:Assembly).Path

        # The informational version, read by reflection in a separate process (not from the file
        # version resource the script reads): the two must agree.
        $script:Expected = (& pwsh -NoProfile -Command "[Reflection.Assembly]::LoadFrom('$script:Assembly').GetCustomAttributes([Reflection.AssemblyInformationalVersionAttribute], `$false)[0].InformationalVersion").Trim()

        $package = Join-Path $TestDrive 'RansomGuard-Agent-win-x64.zip'
        Compress-Archive -Path $script:Assembly -DestinationPath $package
        $body = [IO.File]::ReadAllBytes($package)

        # A one-shot HTTPS server on 127.0.0.1:8443, where install.ps1 looks for a local GRID
        # (certificate validation is bypassed for a local GRID only, as in development).
        $rsa = [Security.Cryptography.RSA]::Create(2048)
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=127.0.0.1', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $selfSigned = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddHours(1))
        $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($selfSigned.Export('Pfx'))
        $script:Listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 8443)
        $script:Listener.Start()
        $script:Server = Start-ThreadJob -ArgumentList $script:Listener, $certificate, $body -ScriptBlock {
            param($listener, $certificate, $body)
            $client = $listener.AcceptTcpClient()
            try {
                $tls = [Net.Security.SslStream]::new($client.GetStream(), $false)
                $tls.AuthenticateAsServer($certificate)
                $reader = [IO.StreamReader]::new($tls)
                $requestLine = $reader.ReadLine()
                while ($reader.ReadLine()) { }  # headers, up to the blank line
                $head = "HTTP/1.1 200 OK`r`nContent-Type: application/zip`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n"
                $tls.Write([Text.Encoding]::ASCII.GetBytes($head))
                $tls.Write($body)
                $tls.Flush()
                $requestLine
            } finally {
                $client.Dispose()
            }
        }

        # Windows PowerShell 5.1, as the console's install command runs it (powershell -File).
        $previous = $env:DOTNET_ENVIRONMENT
        $env:DOTNET_ENVIRONMENT = 'Development'
        try {
            $installPath = Join-Path $TestDrive 'install'
            $script:Output = & powershell -NoProfile -ExecutionPolicy Bypass -File $script:Installer -OTP 'test-otp-0123456789' -GridServer '127.0.0.1' -InstallPath $installPath 2>&1 | Out-String
            $script:ExitCode = $LASTEXITCODE
        } finally {
            $env:DOTNET_ENVIRONMENT = $previous
        }
        $script:RequestLine = $script:Server | Wait-Job -Timeout 30 | Receive-Job
        Write-Host $script:Output
    }

    AfterAll {
        if ($script:Listener) { $script:Listener.Stop() }
        if ($script:Server) { $script:Server | Remove-Job -Force }
    }

    It 'downloads the package under its stable name' {
        $script:RequestLine | Should -Be 'GET /agent/RansomGuard-Agent-win-x64.zip HTTP/1.1'
    }

    It 'shows the informational version of the agent assembly it extracted' {
        $script:Expected | Should -Match '^\d+\.\d+\.\d+'
        $script:Output | Should -Match ('Agent version: ' + [regex]::Escape($script:Expected) + '\r?\n')
        $script:Output | Should -Not -Match '0\.3\.0'
    }

    It 'stops before any service change when the package has no appsettings.json' {
        $script:ExitCode | Should -Be 1
        $script:Output | Should -Match 'appsettings.json not found in package'
        $script:Output | Should -Not -Match 'Installing Windows service'
    }
}
