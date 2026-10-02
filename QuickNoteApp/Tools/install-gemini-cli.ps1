param(
    [switch]$SkipPause
)

$ErrorActionPreference = "Stop"

function Write-Step($message) {
    Write-Host ""
    Write-Host $message -ForegroundColor Cyan
}

function Pause-End {
    if (-not $SkipPause) {
        Write-Host ""
        Read-Host "Kapatmak icin Enter'a basin"
    }
}

function Test-NodeReady {
    $node = Get-Command node -ErrorAction SilentlyContinue
    $npm = Get-Command npm -ErrorAction SilentlyContinue
    return ($null -ne $node -and $null -ne $npm)
}

function Install-NodeLts {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Host "winget bulunamadi. Node.js otomatik indirilemedi." -ForegroundColor Yellow
        Write-Host "Elle kurulum: https://nodejs.org/ adresinden LTS surumunu kurun."
        return $false
    }

    Write-Host "Node.js LTS winget ile kurulacak. Windows izin ekrani acilirsa onay verin." -ForegroundColor Yellow
    & winget install --id OpenJS.NodeJS.LTS --source winget --accept-package-agreements --accept-source-agreements

    if ($LASTEXITCODE -ne 0) {
        Write-Host "winget Node.js kurulumunu tamamlayamadi. Elle kurulum: https://nodejs.org/" -ForegroundColor Yellow
        return $false
    }

    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
    return Test-NodeReady
}

try {
    Write-Host "QuickNoteApp - Antigravity CLI (agy) Kurulumu ve Kontrolü" -ForegroundColor Green

    Write-Step "1) Antigravity CLI (agy) kontrol ediliyor..."
    $agy = Get-Command agy -ErrorAction SilentlyContinue
    if (-not $agy) {
        $localAgy = Join-Path $env:LOCALAPPDATA "agy\bin\agy.exe"
        if (Test-Path $localAgy) {
            $env:Path = "$env:Path;$env:LOCALAPPDATA\agy\bin"
            $agy = Get-Command agy -ErrorAction SilentlyContinue
        }
    }

    if ($agy) {
        Write-Host "Antigravity CLI (agy) hazır: $(& agy --version)" -ForegroundColor Green
    } else {
        Write-Host "Antigravity CLI (agy) henüz sistem yolunda bulunamadı." -ForegroundColor Yellow
        Write-Host "Lütfen 'agy' istemcisinin bilgisayarınıza yüklendiğinden emin olun."
    }

    Write-Step "2) Oturum açma işlemi"
    Write-Host "Yeni bir terminal açın ve şu komutu yazın:"
    Write-Host "agy" -ForegroundColor White
    Write-Host "Oturum açtıktan sonra QuickNoteApp otomatik olarak yetkileri kullanacaktır."
    Write-Host ""
    Write-Host "QuickNoteApp içinde 'AI Analiz', 'Dikte' ve 'Plan' düğmeleri agy hazır olduğunda çalışır."
    Pause-End
}
catch {
    Write-Host ""
    Write-Host "Kurulum/Kontrol tamamlanamadı:" -ForegroundColor Red
    Write-Host $_.Exception.Message
    Pause-End
    exit 1
}