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
    Write-Host "QuickNoteApp - Gemini CLI kurulumu" -ForegroundColor Green
    Write-Host "Resmi paket kurulacak: @google/gemini-cli"

    Write-Step "1) Node.js kontrol ediliyor..."
    if (-not (Test-NodeReady)) {
        Write-Host "Node.js veya npm bulunamadi. Otomatik Node.js LTS kurulumu deneniyor." -ForegroundColor Yellow
        if (-not (Install-NodeLts)) {
            Write-Host ""
            Write-Host "Node.js kurulmadan Gemini CLI kurulamaz." -ForegroundColor Red
            Write-Host "Daha stabil elle kurulum yolu:"
            Write-Host "1) https://nodejs.org/ adresinden Node.js LTS indirip kurun."
            Write-Host "2) Yeni terminal acip bu dosyayi tekrar calistirin."
            Pause-End
            exit 2
        }
    }

    Write-Host "Node.js hazir: $(& node --version)"
    Write-Host "npm hazir: $(& npm --version)"

    Write-Step "2) Gemini CLI kuruluyor veya guncelleniyor..."
    & npm install -g @google/gemini-cli@latest

    Write-Step "3) Kurulum kontrol ediliyor..."
    $gemini = Get-Command gemini -ErrorAction SilentlyContinue
    if (-not $gemini) {
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
        $gemini = Get-Command gemini -ErrorAction SilentlyContinue
    }

    if (-not $gemini) {
        Write-Host "Gemini CLI kuruldu ama PATH henuz yenilenmemis olabilir." -ForegroundColor Yellow
        Write-Host "Terminali kapatip acin ve su komutu deneyin:"
        Write-Host "gemini --version" -ForegroundColor White
        Pause-End
        exit 1
    }

    Write-Host "Gemini CLI hazir: $(& gemini --version)" -ForegroundColor Green

    Write-Step "4) Oturum acma"
    Write-Host "Yeni bir terminal acin ve su komutu yazin:"
    Write-Host "gemini" -ForegroundColor White
    Write-Host "Ekranda 'Sign in with Google' secenegini secin."
    Write-Host "Tarayicida Google hesabiniza giris yapin, sonra terminale geri donun."
    Write-Host ""
    Write-Host "QuickNoteApp icinde 'AI Analiz', 'Dikte' ve 'Plan' dugmeleri Gemini CLI hazir oldugunda calisir."
    Pause-End
}
catch {
    Write-Host ""
    Write-Host "Kurulum tamamlanamadi:" -ForegroundColor Red
    Write-Host $_.Exception.Message
    Write-Host ""
    Write-Host "Daha stabil yol: Node.js LTS kurulu oldugunu kontrol edin, sonra terminalde su komutu calistirin:"
    Write-Host "winget install --id OpenJS.NodeJS.LTS --source winget" -ForegroundColor White
    Write-Host "npm install -g @google/gemini-cli@latest" -ForegroundColor White
    Pause-End
    exit 1
}