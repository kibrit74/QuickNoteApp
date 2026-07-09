param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0.61",
    [switch]$Install
)

$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "QuickNoteApp.csproj"
$PackagingDir = Join-Path $ProjectDir "Packaging"
$ManifestTemplate = Join-Path $PackagingDir "Package.appxmanifest"
$AssetsDir = Join-Path $PackagingDir "Assets"
$ToolsDir = Join-Path $ProjectDir "Tools"
$PublishDir = Join-Path $ProjectDir "bin\$Configuration\net8.0-windows10.0.19041.0\$Runtime\publish"
$ArtifactsDir = Join-Path $ProjectDir "AppPackages"
$StageDir = Join-Path $ArtifactsDir "stage"
$PackagePath = Join-Path $ArtifactsDir "QuickNoteApp_$($Version)_$Runtime.msix"
$CertPath = Join-Path $ArtifactsDir "QuickNoteApp.cer"
$InstallerZipPath = Join-Path $ArtifactsDir "QuickNoteApp-Kurulum.zip"

function Find-Tool($toolName) {
    $fromPath = Get-Command $toolName -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }

    $kitRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (Test-Path $kitRoot) {
        $found = Get-ChildItem $kitRoot -Recurse -Filter $toolName -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\x64\$toolName" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    return $null
}

function Ensure-Directory($path) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}
function Write-InstallerFiles($artifactsDir, $packageFileName, $certFileName) {
    $installerPs1Path = Join-Path $artifactsDir "QuickNoteApp-Kur.ps1"
    $installerCmdPath = Join-Path $artifactsDir "QuickNoteApp-Kur.cmd"
    $readmePath = Join-Path $artifactsDir "BENI-OKU-KURULUM.txt"

    $installerPs1 = @"
`$ErrorActionPreference = "Stop"
`$root = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$packagePath = Join-Path `$root "$packageFileName"
`$certPath = Join-Path `$root "$certFileName"

function Pause-End {
    Write-Host ""
    Read-Host "Kapatmak icin Enter'a basin"
}

try {
    Write-Host "QuickNoteApp kurulumu basliyor..." -ForegroundColor Green

    if (-not (Test-Path `$packagePath)) {
        throw "MSIX dosyasi bulunamadi: `$packagePath"
    }

    if (Test-Path `$certPath) {
        Write-Host "Sertifika guvenilir kullanicilar alanina ekleniyor..."
        try {
            Import-Certificate -FilePath `$certPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
        } catch {
            Import-Certificate -FilePath `$certPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
        }
    }

    Write-Host "Eski QuickNoteApp kurulumu varsa kaldiriliyor..."
    Get-AppxPackage *QuickNoteApp* | ForEach-Object {
        Remove-AppxPackage -Package `$_.PackageFullName -ErrorAction SilentlyContinue
    }

    Write-Host "Yeni QuickNoteApp kuruluyor..."
    Add-AppxPackage -Path `$packagePath -ForceApplicationShutdown

    Write-Host ""
    Write-Host "Kurulum tamamlandi." -ForegroundColor Green
    Write-Host "Baslat menusunde QuickNoteApp diye aratip acabilirsiniz."
    Write-Host "Ilk acilista Gemini rehberi gelirse Kurulum Yardimcisini Ac dugmesine basin."
    Pause-End
}
catch {
    Write-Host ""
    Write-Host "Kurulum tamamlanamadi:" -ForegroundColor Red
    Write-Host `$_.Exception.Message
    Write-Host ""
    Write-Host "Cozum: Bu dosyaya sag tiklayip 'Yonetici olarak calistir' deneyin."
    Pause-End
    exit 1
}
"@

    $installerCmd = @"
@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0QuickNoteApp-Kur.ps1"
"@

    $readme = @"
QuickNoteApp Kurulum
====================

Normal kullanici icin tek adim:
1. QuickNoteApp-Kur.cmd dosyasina cift tikla.

Bu dosya sunlari otomatik yapar:
- Sertifikayi kullaniciya guvenilir olarak ekler.
- Eski QuickNoteApp kuruluysa kaldirir.
- Yeni QuickNoteApp MSIX paketini kurar.

Kurulum bitince Baslat menusunde QuickNoteApp diye aratip ac.
Ilk acilista Gemini rehberi gelirse Kurulum Yardimcisini Ac dugmesine bas.
"@

    [System.IO.File]::WriteAllText($installerPs1Path, $installerPs1, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($installerCmdPath, $installerCmd, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($readmePath, $readme, [System.Text.UTF8Encoding]::new($false))
}

$makeAppx = Find-Tool "makeappx.exe"
$signtool = Find-Tool "signtool.exe"

if (-not $makeAppx -or -not $signtool) {
    Write-Host "Windows SDK aracı eksik." -ForegroundColor Yellow
    Write-Host "Gerekli araçlar: makeappx.exe ve signtool.exe"
    Write-Host "Çözüm: Visual Studio Installer > Individual components > Windows 10/11 SDK kur."
    Write-Host "Sonra bu scripti tekrar çalıştır."
    exit 2
}

Ensure-Directory $ArtifactsDir
if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
Ensure-Directory $StageDir

Write-Host "Uygulama publish ediliyor..."
& "C:\Program Files\dotnet\dotnet.exe" publish $ProjectFile -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=false /p:PublishReadyToRun=false /p:IncludeNativeLibrariesForSelfExtract=false

Write-Host "MSIX stage hazırlanıyor..."
Copy-Item (Join-Path $PublishDir "*") $StageDir -Recurse -Force
Copy-Item $AssetsDir (Join-Path $StageDir "Assets") -Recurse -Force
if (Test-Path $ToolsDir) {
    $stageToolsDir = Join-Path $StageDir "Tools"
    Ensure-Directory $stageToolsDir
    Copy-Item (Join-Path $ToolsDir "*") $stageToolsDir -Recurse -Force
}

$manifest = [System.IO.File]::ReadAllText($ManifestTemplate, [System.Text.UTF8Encoding]::new($false))
$manifest = $manifest -replace 'Version="[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+"', ('Version="' + $Version + '"')
[System.IO.File]::WriteAllText((Join-Path $StageDir "AppxManifest.xml"), $manifest, [System.Text.UTF8Encoding]::new($false))

if (Test-Path $PackagePath) { Remove-Item $PackagePath -Force }
Write-Host "MSIX paketi oluşturuluyor..."
& $makeAppx pack /d $StageDir /p $PackagePath /nv

Write-Host "Kod imzalama sertifikası hazırlanıyor..."
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq "191C0EC5E0DBC9B62D4751EB2DF84E99F065A12D" } | Select-Object -First 1
if (-not $cert) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=Obuzhukuk" -and ($_.EnhancedKeyUsageList.FriendlyName -contains "Code Signing" -or $_.EnhancedKeyUsageList.FriendlyName -contains "Kod İmzalama" -or $_.EnhancedKeyUsageList.Oid.Value -contains "1.3.6.1.5.5.7.3.3") } | Select-Object -First 1
}
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Obuzhukuk" -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -KeyUsage DigitalSignature
}

Export-Certificate -Cert $cert -FilePath $CertPath | Out-Null
try {
    Import-Certificate -FilePath $CertPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
} catch {
    Import-Certificate -FilePath $CertPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
}

Write-Host "MSIX imzalanıyor..."
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $PackagePath

Write-InstallerFiles $ArtifactsDir (Split-Path -Leaf $PackagePath) (Split-Path -Leaf $CertPath)
if (Test-Path $InstallerZipPath) { Remove-Item $InstallerZipPath -Force }
$installerFiles = @(
    (Join-Path $ArtifactsDir "QuickNoteApp-Kur.cmd"),
    (Join-Path $ArtifactsDir "QuickNoteApp-Kur.ps1"),
    $PackagePath,
    $CertPath,
    (Join-Path $ArtifactsDir "BENI-OKU-KURULUM.txt")
)
Compress-Archive -Path $installerFiles -DestinationPath $InstallerZipPath -Force

Write-Host "Hazır: $PackagePath" -ForegroundColor Green
Write-Host "Sertifika: $CertPath"
Write-Host "Tek tık kurulum: $(Join-Path $ArtifactsDir 'QuickNoteApp-Kur.cmd')" -ForegroundColor Green
Write-Host "İndirme ZIP: $InstallerZipPath" -ForegroundColor Green

if ($Install) {
    Write-Host "MSIX yükleniyor..."
    Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown
    Write-Host "Yüklendi. Başlat menüsünde QuickNoteApp olarak arayabilirsin." -ForegroundColor Green
}