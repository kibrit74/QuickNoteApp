$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$packagePath = Join-Path $root "QuickNoteApp_1.0.0.94_win-x64.msix"
$certPath = Join-Path $root "QuickNoteApp.cer"

function Pause-End {
    Write-Host ""
    Read-Host "Kapatmak icin Enter'a basin"
}

try {
    Write-Host "QuickNoteApp kurulumu basliyor..." -ForegroundColor Green

    if (-not (Test-Path $packagePath)) {
        throw "MSIX dosyasi bulunamadi: $packagePath"
    }

    if (Test-Path $certPath) {
        Write-Host "Sertifika guvenilir kullanicilar alanina ekleniyor..."
        try {
            Import-Certificate -FilePath $certPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
        } catch {
            Import-Certificate -FilePath $certPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
        }
    }

    Write-Host "Eski QuickNoteApp kurulumu varsa kaldiriliyor..."
    Get-AppxPackage *QuickNoteApp* | ForEach-Object {
        Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue
    }

    Write-Host "Yeni QuickNoteApp kuruluyor..."
    Add-AppxPackage -Path $packagePath -ForceApplicationShutdown

    Write-Host ""
    Write-Host "Kurulum tamamlandi." -ForegroundColor Green
    Write-Host "Baslat menusunde QuickNoteApp diye aratip acabilirsiniz."
    Write-Host "Ilk acilista Gemini rehberi gelirse Kurulum Yardimcisini Ac dugmesine basin."
    Pause-End
}
catch {
    Write-Host ""
    Write-Host "Kurulum tamamlanamadi:" -ForegroundColor Red
    Write-Host $_.Exception.Message
    Write-Host ""
    Write-Host "Cozum: Bu dosyaya sag tiklayip 'Yonetici olarak calistir' deneyin."
    Pause-End
    exit 1
}